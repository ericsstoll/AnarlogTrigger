using System.Runtime.InteropServices;

namespace AnarlogTrigger;

internal static class NativeMessageBox
{
    private const uint MbOk = 0x00000000;
    private const uint MbIconError = 0x00000010;

    public static void Show(string text, string caption)
    {
        _ = MessageBoxW(IntPtr.Zero, text, caption, MbOk | MbIconError);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
}
