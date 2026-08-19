using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace FalconCanBridge.Simulators.Falcon4;

/// <summary>
/// Maps a logical command name (as referenced from a CanToSim <c>SignalMapping.CommandName</c>)
/// to a Windows virtual-key code plus modifiers, so it can be replayed into BMS as a simulated
/// keystroke matching whatever key the user has bound to that function under
/// Options -> Controls -> Keyboard in BMS.
///
/// This only covers discrete/momentary functions (gear handle, master arm, comm switches,
/// countermeasures, view keys, ...). Continuous analog inputs (throttle friction, trim wheels
/// modeled as an axis, etc.) cannot be driven this way - if your STM32 panel needs to feed an
/// analog axis into BMS, expose that axis as a USB HID joystick/throttle from the STM32 board
/// instead and let BMS read it natively; this app's CAN-to-keystroke path is only for buttons
/// and switches.
/// </summary>
public sealed class Falcon4KeyBinding
{
    public string CommandName { get; set; } = string.Empty;

    /// <summary>Windows virtual-key code, see winuser.h VK_* constants.</summary>
    public ushort VirtualKeyCode { get; set; }

    public bool Shift { get; set; }

    public bool Ctrl { get; set; }

    public bool Alt { get; set; }

    public static List<Falcon4KeyBinding> LoadFromFile(string path)
    {
        string json = File.ReadAllText(path);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        return JsonSerializer.Deserialize<List<Falcon4KeyBinding>>(json, options) ?? new List<Falcon4KeyBinding>();
    }

    public static void SaveToFile(string path, List<Falcon4KeyBinding> bindings)
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(path, JsonSerializer.Serialize(bindings, options));
    }
}
