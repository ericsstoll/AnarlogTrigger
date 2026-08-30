using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace AnarlogTrigger.Actions;

/// <summary>
/// Finds and focuses the Anarlog desktop window (anarlog.exe).
/// </summary>
public static class AnarlogWindowActivator
{
    private const uint SwRestore = 9;

    public static void FocusAnarlog()
    {
        var hwnd = FindAnarlogWindow()
            ?? throw new InvalidOperationException(
                "anarlog.exe is not running or has no usable window. Open Anarlog, then try again.");

        ForceForeground(hwnd);
    }

    public static IntPtr RequireAnarlogWindow()
    {
        return FindAnarlogWindow()
            ?? throw new InvalidOperationException(
                "anarlog.exe is not running or has no usable window. Open Anarlog, then try again.");
    }

    public static IntPtr? FindAnarlogWindow()
    {
        var anarlog = Process.GetProcessesByName("anarlog").FirstOrDefault(p => !p.HasExited);
        return anarlog is null ? null : FindBestWindow(anarlog.Id);
    }

    public static void ForceForeground(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        if (IsIconic(hwnd))
        {
            ShowWindow(hwnd, SwRestore);
        }

        var foreground = GetForegroundWindow();
        var targetThread = GetWindowThreadProcessId(hwnd, out _);
        var foregroundThread = GetWindowThreadProcessId(foreground, out _);
        var currentThread = GetCurrentThreadId();

        var attachedForeground = false;
        var attachedTarget = false;
        try
        {
            if (foregroundThread != 0 && foregroundThread != currentThread)
            {
                attachedForeground = AttachThreadInput(currentThread, foregroundThread, true);
            }

            if (targetThread != 0 && targetThread != currentThread && targetThread != foregroundThread)
            {
                attachedTarget = AttachThreadInput(currentThread, targetThread, true);
            }

            BringWindowToTop(hwnd);
            SetForegroundWindow(hwnd);
        }
        finally
        {
            if (attachedTarget)
            {
                AttachThreadInput(currentThread, targetThread, false);
            }

            if (attachedForeground)
            {
                AttachThreadInput(currentThread, foregroundThread, false);
            }
        }
    }

    private static IntPtr? FindBestWindow(int processId)
    {
        IntPtr best = IntPtr.Zero;
        var bestScore = -1;

        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out var windowPid);
            if (windowPid != (uint)processId || !IsWindow(hwnd))
            {
                return true;
            }

            var score = ScoreWindow(hwnd);
            if (score > bestScore)
            {
                bestScore = score;
                best = hwnd;
            }

            return true;
        }, IntPtr.Zero);

        return best == IntPtr.Zero ? null : best;
    }

    private static int ScoreWindow(IntPtr hwnd)
    {
        var score = 0;
        if (GetWindow(hwnd, 4 /* GW_OWNER */) != IntPtr.Zero)
        {
            score -= 50;
        }

        if (IsWindowVisible(hwnd))
        {
            score += 100;
        }

        var length = GetWindowTextLength(hwnd);
        if (length > 0)
        {
            score += 40 + Math.Min(length, 40);
            var sb = new StringBuilder(length + 1);
            _ = GetWindowText(hwnd, sb, sb.Capacity);
            if (sb.ToString().Contains("anarlog", StringComparison.OrdinalIgnoreCase))
            {
                score += 80;
            }
        }

        if (IsIconic(hwnd))
        {
            score += 10;
        }

        return score;
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, uint nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
}
