using System;
using FalconCanBridge.App.Mvvm;
using FalconCanBridge.Core.Models;

namespace FalconCanBridge.App.ViewModels;

/// <summary>Thin editable wrapper around a <see cref="SignalMapping"/> for DataGrid binding (adds a hex-friendly CAN ID property).</summary>
public sealed class MappingRowViewModel : ObservableObject
{
    public SignalMapping Model { get; }

    public MappingRowViewModel(SignalMapping model) => Model = model;

    public bool Enabled { get => Model.Enabled; set { Model.Enabled = value; OnPropertyChanged(); } }
    public string Name { get => Model.Name; set { Model.Name = value; OnPropertyChanged(); } }
    public MappingDirection Direction { get => Model.Direction; set { Model.Direction = value; OnPropertyChanged(); } }
    public SimulatorTarget Target { get => Model.Target; set { Model.Target = value; OnPropertyChanged(); } }
    public string SignalName { get => Model.SignalName; set { Model.SignalName = value; OnPropertyChanged(); } }
    public string CommandName { get => Model.CommandName; set { Model.CommandName = value; OnPropertyChanged(); } }

    public string CanIdHex
    {
        get => "0x" + Model.CanId.ToString("X");
        set
        {
            string s = value.Trim();
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s[2..];
            if (uint.TryParse(s, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out uint id))
            {
                Model.CanId = id;
                OnPropertyChanged();
            }
        }
    }

    public bool ExtendedId { get => Model.ExtendedId; set { Model.ExtendedId = value; OnPropertyChanged(); } }
    public int ByteOffset { get => Model.ByteOffset; set { Model.ByteOffset = value; OnPropertyChanged(); } }
    public int BitOffset { get => Model.BitOffset; set { Model.BitOffset = value; OnPropertyChanged(); } }
    public CanDataType DataType { get => Model.DataType; set { Model.DataType = value; OnPropertyChanged(); } }
    public bool LittleEndian { get => Model.LittleEndian; set { Model.LittleEndian = value; OnPropertyChanged(); } }
    public double Scale { get => Model.Scale; set { Model.Scale = value; OnPropertyChanged(); } }
    public double Offset { get => Model.Offset; set { Model.Offset = value; OnPropertyChanged(); } }
    public double MinValue { get => Model.MinValue; set { Model.MinValue = value; OnPropertyChanged(); } }
    public double MaxValue { get => Model.MaxValue; set { Model.MaxValue = value; OnPropertyChanged(); } }
    public int SendRateMs { get => Model.SendRateMs; set { Model.SendRateMs = value; OnPropertyChanged(); } }
    public double ChangeThreshold { get => Model.ChangeThreshold; set { Model.ChangeThreshold = value; OnPropertyChanged(); } }
    public string Notes { get => Model.Notes; set { Model.Notes = value; OnPropertyChanged(); } }
}
