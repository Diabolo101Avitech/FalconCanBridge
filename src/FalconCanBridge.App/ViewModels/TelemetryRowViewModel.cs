using FalconCanBridge.App.Mvvm;

namespace FalconCanBridge.App.ViewModels;

public sealed class TelemetryRowViewModel : ObservableObject
{
    private double _value;
    private string _textValue = string.Empty;

    public string Name { get; }

    public double Value
    {
        get => _value;
        set => SetField(ref _value, value);
    }

    /// <summary>Set for string-valued signals (e.g. DCS-BIOS PFL text); empty for numeric-only signals.</summary>
    public string TextValue
    {
        get => _textValue;
        set => SetField(ref _textValue, value);
    }

    public TelemetryRowViewModel(string name) => Name = name;
}
