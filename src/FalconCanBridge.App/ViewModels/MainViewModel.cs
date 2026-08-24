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
    }

    private async Task DisconnectCanAsync()
    {
        if (_activeCanAdapter is null) return;
        await _activeCanAdapter.CloseAsync();
        _activeCanAdapter.Dispose();
        _activeCanAdapter = null;
        IsCanOpen = false;
        CanStatus = "Closed";
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
