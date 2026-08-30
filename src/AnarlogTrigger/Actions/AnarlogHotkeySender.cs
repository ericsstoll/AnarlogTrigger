using System.Runtime.InteropServices;

namespace AnarlogTrigger.Actions;

/// <summary>
/// Activates anarlog.exe and sends Ctrl+Shift+N so the shortcut is handled by Anarlog, not the meeting app.
/// </summary>
public sealed class AnarlogHotkeySender
{
    private const ushort VkLControl = 0xA2;
    private const ushort VkLShift = 0xA0;
    private const ushort VkN = 0x4E;
    private const uint InputKeyboard = 1;
    private const uint KeyeventfKeyup = 0x0002;

    public void SendStartListeningHotkey()
    {
        var target = AnarlogWindowActivator.RequireAnarlogWindow();
        var previous = GetForegroundWindow();
        try
        {
            AnarlogWindowActivator.ForceForeground(target);
            Thread.Sleep(75);
            SendChord();
            Thread.Sleep(50);
        }
        finally
        {
            if (previous != IntPtr.Zero && previous != target && IsWindow(previous))
            {
                AnarlogWindowActivator.ForceForeground(previous);
            }
        }
    }

    private static void SendChord()
    {
        Input[] inputs =
        [
            KeyDown(VkLControl),
            KeyDown(VkLShift),
            KeyDown(VkN),
            KeyUp(VkN),
            KeyUp(VkLShift),
            KeyUp(VkLControl)
        ];

        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
        if (sent != inputs.Length)
        {
            throw new InvalidOperationException(
                $"SendInput sent {sent}/{inputs.Length} events (Win32 error {Marshal.GetLastWin32Error()}).");
        }
    }

    private static Input KeyDown(ushort virtualKey) => Build(virtualKey, 0);

    private static Input KeyUp(ushort virtualKey) => Build(virtualKey, KeyeventfKeyup);

    private static Input Build(ushort virtualKey, uint flags)
    {
        var scan = (ushort)MapVirtualKey(virtualKey, 0);
        return new Input
        {
            Type = InputKeyboard,
            Union = new InputUnion
            {
                Keyboard = new KeyboardInput
                {
                    Vk = virtualKey,
                    Scan = scan,
                    Flags = flags,
                    Time = 0,
                    ExtraInfo = IntPtr.Zero
                }
            }
        };
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, Input[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Union;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MouseInput Mouse;
        [FieldOffset(0)] public KeyboardInput Keyboard;
        [FieldOffset(0)] public HardwareInput Hardware;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort Vk;
        public ushort Scan;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HardwareInput
    {
        public uint Msg;
        public ushort ParamL;
        public ushort ParamH;
    }
}
