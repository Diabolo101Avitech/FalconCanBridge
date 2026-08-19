using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using FalconCanBridge.Core.Logging;

namespace FalconCanBridge.Simulators.Falcon4;

/// <summary>
/// Replays configured commands as global Windows keystrokes via SendInput, so an incoming
/// CAN button/switch can trigger a BMS keybinding. Because SendInput is a *global* keyboard
/// event (there is no publicly documented, version-stable way to post synthetic input directly
/// to a specific DirectInput-reading game window), BMS must be the foreground/focused window
/// for keystrokes to reach it - the sender logs a warning instead of sending when that isn't
/// the case, so a panel switch flip can't accidentally type into whatever else has focus.
/// </summary>
public sealed class Falcon4KeyboardCommandSender
{
    private const string LogSource = "Falcon4Input";

    private readonly Dictionary<string, Falcon4KeyBinding> _bindings;

    public Falcon4KeyboardCommandSender(IEnumerable<Falcon4KeyBinding> bindings)
    {
        _bindings = bindings.ToDictionary(b => b.CommandName, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>value != 0 => key down, value == 0 => key up. Callers driving a momentary button typically send 1 then 0.</summary>
    public void Send(string commandName, double value)
    {
        if (!_bindings.TryGetValue(commandName, out var binding))
        {
            AppLog.Warning(LogSource, $"No key binding configured for command '{commandName}'.");
            return;
        }

        if (!IsBmsForeground())
        {
            AppLog.Warning(LogSource, $"Falcon BMS is not the foreground window - dropped command '{commandName}' to avoid sending keystrokes elsewhere.");
            return;
        }

        bool keyDown = value != 0;
        SendKey(binding, keyDown);
    }

    private static bool IsBmsForeground()
    {
        IntPtr hWnd = GetForegroundWindow();
        if (hWnd == IntPtr.Zero) return false;

        var sb = new System.Text.StringBuilder(256);
        GetWindowText(hWnd, sb, sb.Capacity);
        string title = sb.ToString();

        return title.Contains("Falcon BMS", StringComparison.OrdinalIgnoreCase);
    }

    private static void SendKey(Falcon4KeyBinding binding, bool keyDown)
    {
        var inputs = new List<INPUT>();

        if (keyDown)
        {
            if (binding.Ctrl) inputs.Add(MakeKeyInput(VK_CONTROL, down: true));
            if (binding.Shift) inputs.Add(MakeKeyInput(VK_SHIFT, down: true));
            if (binding.Alt) inputs.Add(MakeKeyInput(VK_MENU, down: true));
            inputs.Add(MakeKeyInput(binding.VirtualKeyCode, down: true));
        }
        else
        {
            inputs.Add(MakeKeyInput(binding.VirtualKeyCode, down: false));
            if (binding.Alt) inputs.Add(MakeKeyInput(VK_MENU, down: false));
            if (binding.Shift) inputs.Add(MakeKeyInput(VK_SHIFT, down: false));
            if (binding.Ctrl) inputs.Add(MakeKeyInput(VK_CONTROL, down: false));
        }

        INPUT[] arr = inputs.ToArray();
        SendInput((uint)arr.Length, arr, Marshal.SizeOf<INPUT>());
    }

    private static INPUT MakeKeyInput(ushort vk, bool down) => new()
    {
        type = INPUT_KEYBOARD,
        U = new InputUnion
        {
            ki = new KEYBDINPUT
            {
                wVk = vk,
                wScan = 0,
                dwFlags = down ? 0u : KEYEVENTF_KEYUP,
                time = 0,
                dwExtraInfo = IntPtr.Zero
            }
        }
    };

    // ---- Win32 interop -------------------------------------------------------------

    private const int INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const ushort VK_SHIFT = 0x10;
    private const ushort VK_CONTROL = 0x11;
    private const ushort VK_MENU = 0x12;

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    // MOUSEINPUT and HARDWAREINPUT are unused by this sender but MUST stay part of the union:
    // Win32's native INPUT struct sizes its union to fit the largest member (MOUSEINPUT, which
    // is larger than KEYBDINPUT on x64). SendInput validates cbSize against that native size
    // exactly - a union sized to KEYBDINPUT alone reports a smaller Marshal.SizeOf<INPUT>()
    // than Windows expects and SendInput then silently rejects every event.
    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);
}
