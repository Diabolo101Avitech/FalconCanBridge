using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using FalconCanBridge.App.Mvvm;
using FalconCanBridge.CanBus.Adapters;
using FalconCanBridge.Core.CanOpen;
using FalconCanBridge.Core.Interfaces;
using FalconCanBridge.Core.Logging;
using FalconCanBridge.Core.Mapping;
using FalconCanBridge.Core.Models;
using FalconCanBridge.Simulators.Dcs;
using FalconCanBridge.Simulators.Falcon4;
using Microsoft.Win32;

namespace FalconCanBridge.App.ViewModels;

public enum CanAdapterKind
{
    Slcan,
    Pcan
}

/// <summary>
/// Composition root / UI-facing view model. Owns the active simulator connector, the active CAN
/// adapter, and the <see cref="MappingEngine"/> wiring one to the other. All UI collections are
/// updated back on the WPF dispatcher thread since the connectors/adapters raise their events
/// from background polling/IO threads.
/// </summary>
public sealed class MainViewModel : ObservableObject
{
    private const int MaxLogLines = 2000;
    private const int MaxTrafficRows = 1000;

    private readonly Dispatcher _dispatcher;
    private readonly MappingEngine _mappingEngine = new();
    private readonly Dictionary<string, TelemetryRowViewModel> _telemetryIndex = new();

    private ISimulatorConnector? _activeConnector;
    private ICanBusAdapter? _activeCanAdapter;

    private CanOpenNmtMaster? _canOpenNmtMaster;
    private CanOpenHeartbeatMonitor? _canOpenHeartbeatMonitor;
    private CanOpenSdoClient? _canOpenSdoClient;

    public ObservableCollection<TelemetryRowViewModel> Telemetry { get; } = new();
    public ObservableCollection<CanTrafficRowViewModel> CanTraffic { get; } = new();
    public ObservableCollection<string> LogLines { get; } = new();
    public ObservableCollection<MappingRowViewModel> Mappings { get; } = new();

    public Array SimulatorChoices { get; } = new[] { SimulatorTarget.Falcon4Bms, SimulatorTarget.Dcs };
    public Array CanAdapterChoices { get; } = Enum.GetValues(typeof(CanAdapterKind));

    private SimulatorTarget _selectedSimulator = SimulatorTarget.Falcon4Bms;
    public SimulatorTarget SelectedSimulator
    {
        get => _selectedSimulator;
        set => SetField(ref _selectedSimulator, value);
    }

    private CanAdapterKind _selectedCanAdapter = CanAdapterKind.Slcan;
    public CanAdapterKind SelectedCanAdapter
    {
        get => _selectedCanAdapter;
        set => SetField(ref _selectedCanAdapter, value);
    }

    private string _simulatorStatus = "Disconnected";
    public string SimulatorStatus { get => _simulatorStatus; set => SetField(ref _simulatorStatus, value); }

    private bool _isSimulatorConnected;
    public bool IsSimulatorConnected { get => _isSimulatorConnected; set => SetField(ref _isSimulatorConnected, value); }

    private string _canStatus = "Closed";
    public string CanStatus { get => _canStatus; set => SetField(ref _canStatus, value); }

    private bool _isCanOpen;
    public bool IsCanOpen { get => _isCanOpen; set => SetField(ref _isCanOpen, value); }

    // SLCAN connection settings
    private string _serialPortName = "COM5";
    public string SerialPortName { get => _serialPortName; set => SetField(ref _serialPortName, value); }

    private int _canBitrate = 500000;
    public int CanBitrate { get => _canBitrate; set => SetField(ref _canBitrate, value); }

    private int _serialBaud = 115200;
    public int SerialBaud { get => _serialBaud; set => SetField(ref _serialBaud, value); }

    // PCAN connection settings
    private string _pcanChannel = "USB1";
    public string PcanChannel { get => _pcanChannel; set => SetField(ref _pcanChannel, value); }

    // CANopen settings - layered on top of whichever CAN adapter is open (SLCAN or PCAN); CANopen
    // is a payload-level protocol, indifferent to the physical transport underneath it.
    private bool _enableCanOpen;
    /// <summary>
    /// Toggling this while the CAN adapter is already open takes effect immediately (sets up or
    /// tears down the NMT master/heartbeat monitor/SDO client there and then, and auto-starts the
    /// node per <see cref="CanOpenAutoStart"/>) rather than only on the next "Open" - the adapter
    /// connection itself isn't affected either way.
    /// </summary>
    public bool EnableCanOpen
    {
        get => _enableCanOpen;
        set
        {
            if (!SetField(ref _enableCanOpen, value)) return;
            if (!IsCanOpen || _activeCanAdapter is null) return;

            if (value)
            {
                SetupCanOpen(_activeCanAdapter);
                if (CanOpenAutoStart)
                {
                    _ = AutoStartNodeAsync();
                }
            }
            else
            {
                TeardownCanOpen();
            }
        }
    }

    private int _canOpenNodeId = 1;
    /// <summary>
    /// Target node ID for NMT commands, the node-status readout, and the SDO test panel. Takes
    /// effect on the next "Open" of the CAN adapter - the heartbeat monitor tracks every node it
    /// sees regardless, but the SDO client and manual NMT buttons are bound to this one node.
    /// </summary>
    public int CanOpenNodeId { get => _canOpenNodeId; set => SetField(ref _canOpenNodeId, value); }

    private bool _canOpenAutoStart = true;
    /// <summary>If set, sends NMT Start to <see cref="CanOpenNodeId"/> automatically right after the CAN adapter opens.</summary>
    public bool CanOpenAutoStart { get => _canOpenAutoStart; set => SetField(ref _canOpenAutoStart, value); }

    private string _canOpenNodeStatus = "n/a";
    public string CanOpenNodeStatus { get => _canOpenNodeStatus; set => SetField(ref _canOpenNodeStatus, value); }

    private bool _isCanOpenNodeOperational;
    public bool IsCanOpenNodeOperational { get => _isCanOpenNodeOperational; set => SetField(ref _isCanOpenNodeOperational, value); }

    // SDO test panel - reads/writes one object dictionary entry on CanOpenNodeId, for configuration
    // registers that aren't exchanged cyclically via PDO (e.g. calibration constants, thresholds).
    private string _sdoIndexHex = "2000";
    public string SdoIndexHex { get => _sdoIndexHex; set => SetField(ref _sdoIndexHex, value); }

    private int _sdoSubIndex;
    public int SdoSubIndex { get => _sdoSubIndex; set => SetField(ref _sdoSubIndex, value); }

    public Array SdoDataTypeChoices { get; } =
        new[] { CanDataType.UInt8, CanDataType.Int8, CanDataType.UInt16, CanDataType.Int16, CanDataType.UInt32, CanDataType.Int32, CanDataType.Float32 };

    private CanDataType _sdoDataType = CanDataType.UInt16;
    public CanDataType SdoDataType { get => _sdoDataType; set => SetField(ref _sdoDataType, value); }

    private string _sdoValueText = "0";
    public string SdoValueText { get => _sdoValueText; set => SetField(ref _sdoValueText, value); }

    private string _sdoResult = string.Empty;
    public string SdoResult { get => _sdoResult; set => SetField(ref _sdoResult, value); }

    private string _mappingProfileName = "Default Profile";
    public string MappingProfileName { get => _mappingProfileName; set => SetField(ref _mappingProfileName, value); }

    public RelayCommand AddMappingCommand { get; }
    public RelayCommand RemoveMappingCommand { get; }
    public RelayCommand ClearLogCommand { get; }
    public RelayCommand ClearCanTrafficCommand { get; }
    public AsyncRelayCommand ConnectSimulatorCommand { get; }
    public AsyncRelayCommand DisconnectSimulatorCommand { get; }
    public AsyncRelayCommand ConnectCanCommand { get; }
    public AsyncRelayCommand DisconnectCanCommand { get; }
    public RelayCommand LoadMappingCommand { get; }
    public RelayCommand SaveMappingCommand { get; }

    public AsyncRelayCommand StartNodeCommand { get; }
    public AsyncRelayCommand StopNodeCommand { get; }
    public AsyncRelayCommand ResetNodeCommand { get; }
    public AsyncRelayCommand SdoReadCommand { get; }
    public AsyncRelayCommand SdoWriteCommand { get; }

    public MainViewModel()
    {
        _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

        AppLog.EntryLogged += OnAppLogEntry;
        _mappingEngine.CanFrameReady += OnCanFrameReady;
        _mappingEngine.SimCommandRequested += OnSimCommandRequested;

        AddMappingCommand = new RelayCommand(AddMapping);
        RemoveMappingCommand = new RelayCommand(p => RemoveMapping(p as MappingRowViewModel), p => p is MappingRowViewModel);
        ClearLogCommand = new RelayCommand(() => LogLines.Clear());
        ClearCanTrafficCommand = new RelayCommand(() => CanTraffic.Clear());

        ConnectSimulatorCommand = new AsyncRelayCommand(ConnectSimulatorAsync, () => !IsSimulatorConnected, ReportError);
        DisconnectSimulatorCommand = new AsyncRelayCommand(DisconnectSimulatorAsync, () => IsSimulatorConnected, ReportError);
        ConnectCanCommand = new AsyncRelayCommand(ConnectCanAsync, () => !IsCanOpen, ReportError);
        DisconnectCanCommand = new AsyncRelayCommand(DisconnectCanAsync, () => IsCanOpen, ReportError);

        LoadMappingCommand = new RelayCommand(LoadMapping);
        SaveMappingCommand = new RelayCommand(SaveMapping);

        StartNodeCommand = new AsyncRelayCommand(() => SendNmtCommandAsync(NmtCommand.Start), () => EnableCanOpen && IsCanOpen, ReportError);
        StopNodeCommand = new AsyncRelayCommand(() => SendNmtCommandAsync(NmtCommand.Stop), () => EnableCanOpen && IsCanOpen, ReportError);
        ResetNodeCommand = new AsyncRelayCommand(() => SendNmtCommandAsync(NmtCommand.ResetNode), () => EnableCanOpen && IsCanOpen, ReportError);
        SdoReadCommand = new AsyncRelayCommand(SdoReadAsync, () => EnableCanOpen && IsCanOpen, ReportError);
        SdoWriteCommand = new AsyncRelayCommand(SdoWriteAsync, () => EnableCanOpen && IsCanOpen, ReportError);

        LoadDefaultMappingIfPresent();
    }

    // ---- Mapping table ---------------------------------------------------------------

    private void AddMapping(object? _ = null)
    {
        var row = new MappingRowViewModel(new SignalMapping { Name = "NewSignal" });
        Mappings.Add(row);
        PushMappingsToEngine();
    }

    private void RemoveMapping(MappingRowViewModel? row)
    {
        if (row is null) return;
        Mappings.Remove(row);
        PushMappingsToEngine();
    }

    public void PushMappingsToEngine()
    {
        _mappingEngine.LoadMappings(Mappings.Select(r => r.Model));
    }

    private void LoadDefaultMappingIfPresent()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "config", "mapping.sample.json");
        if (File.Exists(path))
        {
            LoadMappingFrom(path);
        }
    }

    private void LoadMapping(object? _ = null)
    {
        var dlg = new OpenFileDialog { Filter = "Mapping profile (*.json)|*.json", Title = "Load mapping profile" };
        if (dlg.ShowDialog() == true)
        {
            LoadMappingFrom(dlg.FileName);
        }
    }

    private void LoadMappingFrom(string path)
    {
        try
        {
            var doc = MappingConfig.Load(path);
            MappingProfileName = doc.ProfileName;
            Mappings.Clear();
            foreach (var m in doc.Mappings) Mappings.Add(new MappingRowViewModel(m));
            PushMappingsToEngine();
            AppLog.Info("App", $"Loaded mapping profile '{doc.ProfileName}' ({doc.Mappings.Count} entries) from {path}.");
        }
        catch (Exception ex)
        {
            AppLog.Error("App", $"Failed to load mapping profile: {ex.Message}");
        }
    }

    private void SaveMapping(object? _ = null)
    {
        var dlg = new SaveFileDialog { Filter = "Mapping profile (*.json)|*.json", Title = "Save mapping profile", FileName = "mapping.json" };
        if (dlg.ShowDialog() != true) return;

        try
        {
            var doc = new MappingDocument { ProfileName = MappingProfileName, Mappings = Mappings.Select(r => r.Model).ToList() };
            MappingConfig.Save(dlg.FileName, doc);
            AppLog.Info("App", $"Saved mapping profile to {dlg.FileName}.");
        }
        catch (Exception ex)
        {
            AppLog.Error("App", $"Failed to save mapping profile: {ex.Message}");
        }
    }

    // ---- Simulator connection ---------------------------------------------------------

    private async Task ConnectSimulatorAsync()
    {
        PushMappingsToEngine();

        _activeConnector?.Dispose();

        _activeConnector = SelectedSimulator switch
        {
            SimulatorTarget.Falcon4Bms => CreateFalcon4Connector(),
            SimulatorTarget.Dcs => new DcsBiosConnector(new DcsBiosAddressMap { Fields = LoadDcsFieldsIfPresent() }),
            _ => throw new InvalidOperationException("Unsupported simulator selection.")
        };

        var connectorRef = _activeConnector;
        connectorRef.TelemetryUpdated += OnTelemetryUpdated;
        connectorRef.ConnectionStateChanged += OnSimulatorConnectionStateChanged;
        connectorRef.LogMessage += (_, msg) => AppLog.Info(connectorRef.Name, msg);

        await _activeConnector.StartAsync();
        IsSimulatorConnected = true;
        SimulatorStatus = $"{_activeConnector.Name}: starting...";
    }

    private async Task DisconnectSimulatorAsync()
    {
        if (_activeConnector is null) return;
        await _activeConnector.StopAsync();
        _activeConnector.Dispose();
        _activeConnector = null;
        IsSimulatorConnected = false;
        SimulatorStatus = "Disconnected";
    }

    private List<DcsBiosSignalDefinition> LoadDcsFieldsIfPresent()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "config", "dcs-bios-fields.sample.json");
        return File.Exists(path) ? DcsBiosSignalDefinition.LoadFromFile(path) : new List<DcsBiosSignalDefinition>();
    }

    private Falcon4Connector CreateFalcon4Connector()
    {
        // Falcon4FieldMap() now loads the comprehensive built-in field table (every scalar/RWR/
        // light-bit signal from BMS's primary shared-memory block - see Falcon4FieldMap remarks).
        // The optional JSON file next to the exe ADDS to or OVERRIDES individual entries of that
        // table by Name rather than replacing it wholesale, so a stock install still gets full
        // telemetry coverage even without a custom fields file.
        var fieldMap = new Falcon4FieldMap();
        string fieldsPath = Path.Combine(AppContext.BaseDirectory, "config", "falcon4-fields.sample.json");
        if (File.Exists(fieldsPath))
        {
            fieldMap.MergeFromFile(fieldsPath);
        }

        var connector = new Falcon4Connector(fieldMap);

        string bindingsPath = Path.Combine(AppContext.BaseDirectory, "config", "falcon4-keybindings.sample.json");
        if (File.Exists(bindingsPath))
        {
            connector.ConfigureKeyBindings(Falcon4KeyBinding.LoadFromFile(bindingsPath));
        }

        return connector;
    }

    private void OnSimulatorConnectionStateChanged(object? sender, EventArgs e)
    {
        RunOnUi(() =>
        {
            bool connected = _activeConnector?.IsConnected ?? false;
            SimulatorStatus = _activeConnector is null ? "Disconnected" : connected ? $"{_activeConnector.Name}: connected" : $"{_activeConnector.Name}: waiting for sim...";
        });
    }

    private void OnTelemetryUpdated(object? sender, TelemetryUpdatedEventArgs e)
    {
        _mappingEngine.OnTelemetry(e.Snapshot);

        RunOnUi(() =>
        {
            foreach (var kvp in e.Snapshot.Values)
            {
                if (!_telemetryIndex.TryGetValue(kvp.Key, out var row))
                {
                    row = new TelemetryRowViewModel(kvp.Key);
                    _telemetryIndex[kvp.Key] = row;
                    Telemetry.Add(row);
                }
                row.Value = kvp.Value;
            }

            if (e.Snapshot.TextValues is not null)
            {
                foreach (var kvp in e.Snapshot.TextValues)
                {
                    string key = kvp.Key;
                    if (!_telemetryIndex.TryGetValue(key, out var row))
                    {
                        row = new TelemetryRowViewModel(key);
                        _telemetryIndex[key] = row;
                        Telemetry.Add(row);
                    }
                    row.TextValue = kvp.Value;
                }
            }
        });
    }

    // ---- CAN adapter connection ---------------------------------------------------------

    private async Task ConnectCanAsync()
    {
        TeardownCanOpen();
        _activeCanAdapter?.Dispose();

        _activeCanAdapter = SelectedCanAdapter switch
        {
            CanAdapterKind.Slcan => new SlcanSerialAdapter(),
            CanAdapterKind.Pcan => new PcanBasicAdapter(),
            _ => throw new InvalidOperationException("Unsupported CAN adapter selection.")
        };

        var adapterRef = _activeCanAdapter;
        adapterRef.FrameReceived += OnCanFrameReceived;
        adapterRef.LogMessage += (_, msg) => AppLog.Info(adapterRef.Name, msg);

        string connectionString = SelectedCanAdapter == CanAdapterKind.Slcan
            ? $"{SerialPortName};{CanBitrate};{SerialBaud}"
            : $"{PcanChannel};{CanBitrate}";

        await _activeCanAdapter.OpenAsync(connectionString);
        IsCanOpen = true;
        CanStatus = $"{_activeCanAdapter.Name}: open ({connectionString})";

        if (EnableCanOpen)
        {
            SetupCanOpen(adapterRef);

            if (CanOpenAutoStart)
            {
                await AutoStartNodeAsync();
            }
        }
    }

    private async Task AutoStartNodeAsync()
    {
        if (_canOpenNmtMaster is null) return;

        try
        {
            await _canOpenNmtMaster.StartNodeAsync(CanOpenNodeId);
            AppLog.Info("CANopen", $"Sent NMT Start to node {CanOpenNodeId}.");
        }
        catch (Exception ex)
        {
            AppLog.Warning("CANopen", $"Failed to send NMT Start to node {CanOpenNodeId}: {ex.Message}");
        }
    }

    private async Task DisconnectCanAsync()
    {
        if (_activeCanAdapter is null) return;
        TeardownCanOpen();
        await _activeCanAdapter.CloseAsync();
        _activeCanAdapter.Dispose();
        _activeCanAdapter = null;
        IsCanOpen = false;
        CanStatus = "Closed";
    }

    // ---- CANopen -----------------------------------------------------------------------

    private void SetupCanOpen(ICanBusAdapter adapter)
    {
        _canOpenNmtMaster = new CanOpenNmtMaster(adapter);
        _canOpenSdoClient = new CanOpenSdoClient(adapter, CanOpenNodeId);

        _canOpenHeartbeatMonitor = new CanOpenHeartbeatMonitor();
        _canOpenHeartbeatMonitor.NodeStateChanged += OnCanOpenNodeStateChanged;
        _canOpenHeartbeatMonitor.NodeTimedOut += OnCanOpenNodeTimedOut;

        RunOnUi(() =>
        {
            CanOpenNodeStatus = "waiting for heartbeat...";
            IsCanOpenNodeOperational = false;
        });
    }

    private void TeardownCanOpen()
    {
        if (_canOpenHeartbeatMonitor is not null)
        {
            _canOpenHeartbeatMonitor.NodeStateChanged -= OnCanOpenNodeStateChanged;
            _canOpenHeartbeatMonitor.NodeTimedOut -= OnCanOpenNodeTimedOut;
            _canOpenHeartbeatMonitor.Dispose();
            _canOpenHeartbeatMonitor = null;
        }

        _canOpenSdoClient?.Dispose();
        _canOpenSdoClient = null;
        _canOpenNmtMaster = null;

        RunOnUi(() =>
        {
            CanOpenNodeStatus = "n/a";
            IsCanOpenNodeOperational = false;
        });
    }

    private Task SendNmtCommandAsync(NmtCommand command)
    {
        if (_canOpenNmtMaster is null)
        {
            AppLog.Warning("CANopen", "Cannot send NMT command: enable CANopen and open the CAN adapter first.");
            return Task.CompletedTask;
        }

        return _canOpenNmtMaster.SendAsync(command, CanOpenNodeId);
    }

    private void OnCanOpenNodeStateChanged(object? sender, NodeStateChangedEventArgs e)
    {
        AppLog.Info("CANopen", $"Node {e.NodeId} NMT state: {e.PreviousState} -> {e.State}.");
        if (e.NodeId != CanOpenNodeId) return;

        RunOnUi(() =>
        {
            CanOpenNodeStatus = $"Node {e.NodeId}: {e.State}";
            IsCanOpenNodeOperational = e.State == NmtState.Operational;
        });
    }

    private void OnCanOpenNodeTimedOut(object? sender, NodeTimedOutEventArgs e)
    {
        AppLog.Warning("CANopen", $"Node {e.NodeId} heartbeat timed out.");
        if (e.NodeId != CanOpenNodeId) return;

        RunOnUi(() =>
        {
            CanOpenNodeStatus = $"Node {e.NodeId}: heartbeat lost";
            IsCanOpenNodeOperational = false;
        });
    }

    private async Task SdoReadAsync()
    {
        if (_canOpenSdoClient is null)
        {
            AppLog.Warning("CANopen", "Cannot read SDO: enable CANopen and open the CAN adapter first.");
            return;
        }

        if (!TryParseSdoIndex(out ushort index)) return;
        byte subIndex = (byte)Math.Clamp(SdoSubIndex, 0, 255);

        try
        {
            double value = SdoDataType switch
            {
                CanDataType.UInt8 => await _canOpenSdoClient.UploadUInt8Async(index, subIndex),
                CanDataType.Int8 => await _canOpenSdoClient.UploadInt8Async(index, subIndex),
                CanDataType.UInt16 => await _canOpenSdoClient.UploadUInt16Async(index, subIndex),
                CanDataType.Int16 => await _canOpenSdoClient.UploadInt16Async(index, subIndex),
                CanDataType.UInt32 => await _canOpenSdoClient.UploadUInt32Async(index, subIndex),
                CanDataType.Int32 => await _canOpenSdoClient.UploadInt32Async(index, subIndex),
                CanDataType.Float32 => await _canOpenSdoClient.UploadFloat32Async(index, subIndex),
                _ => throw new InvalidOperationException("Unsupported SDO data type.")
            };

            SdoValueText = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            SdoResult = $"OK: 0x{index:X4}:{subIndex} = {value}";
            AppLog.Info("CANopen", $"SDO read 0x{index:X4}:{subIndex} = {value}.");
        }
        catch (Exception ex)
        {
            SdoResult = $"Error: {ex.Message}";
            AppLog.Error("CANopen", $"SDO read of 0x{index:X4}:{subIndex} failed: {ex.Message}");
        }
    }

    private async Task SdoWriteAsync()
    {
        if (_canOpenSdoClient is null)
        {
            AppLog.Warning("CANopen", "Cannot write SDO: enable CANopen and open the CAN adapter first.");
            return;
        }

        if (!TryParseSdoIndex(out ushort index)) return;
        byte subIndex = (byte)Math.Clamp(SdoSubIndex, 0, 255);

        if (!double.TryParse(SdoValueText, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double value))
        {
            SdoResult = $"Error: '{SdoValueText}' is not a number.";
            return;
        }

        try
        {
            switch (SdoDataType)
            {
                case CanDataType.UInt8: await _canOpenSdoClient.DownloadUInt8Async(index, subIndex, (byte)Math.Clamp(value, 0, 255)); break;
                case CanDataType.Int8: await _canOpenSdoClient.DownloadInt8Async(index, subIndex, (sbyte)Math.Clamp(value, sbyte.MinValue, sbyte.MaxValue)); break;
                case CanDataType.UInt16: await _canOpenSdoClient.DownloadUInt16Async(index, subIndex, (ushort)Math.Clamp(value, 0, ushort.MaxValue)); break;
                case CanDataType.Int16: await _canOpenSdoClient.DownloadInt16Async(index, subIndex, (short)Math.Clamp(value, short.MinValue, short.MaxValue)); break;
                case CanDataType.UInt32: await _canOpenSdoClient.DownloadUInt32Async(index, subIndex, (uint)Math.Clamp(value, 0, uint.MaxValue)); break;
                case CanDataType.Int32: await _canOpenSdoClient.DownloadInt32Async(index, subIndex, (int)Math.Clamp(value, int.MinValue, int.MaxValue)); break;
                case CanDataType.Float32: await _canOpenSdoClient.DownloadFloat32Async(index, subIndex, (float)value); break;
                default: throw new InvalidOperationException("Unsupported SDO data type.");
            }

            SdoResult = $"OK: wrote {value} to 0x{index:X4}:{subIndex}";
            AppLog.Info("CANopen", $"SDO write 0x{index:X4}:{subIndex} = {value}.");
        }
        catch (Exception ex)
        {
            SdoResult = $"Error: {ex.Message}";
            AppLog.Error("CANopen", $"SDO write of 0x{index:X4}:{subIndex} failed: {ex.Message}");
        }
    }

    private bool TryParseSdoIndex(out ushort index)
    {
        string s = SdoIndexHex.Trim();
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s[2..];

        if (ushort.TryParse(s, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out index))
        {
            return true;
        }

        SdoResult = $"Error: '{SdoIndexHex}' is not a valid hex object index.";
        index = 0;
        return false;
    }

    private void OnCanFrameReady(object? sender, CanFrame frame)
    {
        if (_activeCanAdapter is { IsOpen: true })
        {
            _ = _activeCanAdapter.SendAsync(frame);
        }
        AddTrafficRow(frame);
    }

    private void OnCanFrameReceived(object? sender, CanFrameReceivedEventArgs e)
    {
        _mappingEngine.OnCanFrameReceived(e.Frame);
        _canOpenHeartbeatMonitor?.OnCanFrameReceived(e.Frame);
        AddTrafficRow(e.Frame);
    }

    private void OnSimCommandRequested(object? sender, SimCommandRequestedEventArgs e)
    {
        if (_activeConnector is null) return;
        if (e.Target != SimulatorTarget.Any && e.Target != _activeConnector.Target) return;
        _activeConnector.SendCommand(e.CommandName, e.Value);
    }

    private void AddTrafficRow(CanFrame frame)
    {
        RunOnUi(() =>
        {
            CanTraffic.Insert(0, new CanTrafficRowViewModel(frame));
            while (CanTraffic.Count > MaxTrafficRows) CanTraffic.RemoveAt(CanTraffic.Count - 1);
        });
    }

    // ---- Logging -------------------------------------------------------------------

    private void OnAppLogEntry(LogEntry entry)
    {
        RunOnUi(() =>
        {
            LogLines.Add(entry.ToString());
            while (LogLines.Count > MaxLogLines) LogLines.RemoveAt(0);
        });
    }

    private void ReportError(Exception ex) => AppLog.Error("App", ex.Message);

    private void RunOnUi(Action action)
    {
        if (_dispatcher.CheckAccess()) action();
        else _dispatcher.BeginInvoke(action);
    }
}
