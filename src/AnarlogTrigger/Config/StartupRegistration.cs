using Microsoft.Win32;

namespace AnarlogTrigger.Config;

/// <summary>
/// Registers AnarlogTrigger under the current user's Run key so it starts with Windows.
/// </summary>
public static class StartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "AnarlogTrigger";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(ValueName) is string s && !string.IsNullOrWhiteSpace(s);
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath)
            ?? throw new InvalidOperationException("Could not open the Windows Run registry key.");

        if (!enabled)
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
            return;
        }

        var exe = Environment.ProcessPath
            ?? throw new InvalidOperationException("Could not determine AnarlogTrigger.exe path.");

        key.SetValue(ValueName, QuoteIfNeeded(exe));
    }

    private static string QuoteIfNeeded(string path)
    {
        return path.Contains(' ', StringComparison.Ordinal) ? $"\"{path}\"" : path;
    }
}
