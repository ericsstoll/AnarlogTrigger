using System.Text.Json;
using System.Text.Json.Nodes;
using AnarlogTrigger.Config;
using AnarlogTrigger.Matching;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AnarlogTrigger.Config;

public sealed class SettingsStore
{
    private readonly string _configPath;
    private readonly ILogger<SettingsStore> _logger;
    private readonly object _gate = new();

    public AppSettings Settings { get; private set; }

    public SettingsStore(string configPath, ILogger<SettingsStore> logger)
    {
        _configPath = configPath;
        _logger = logger;
        Settings = Load();
    }

    public AppSettings Reload()
    {
        lock (_gate)
        {
            Settings = Load();
            return Settings;
        }
    }

    public void AddExtraProcess(string processName)
    {
        var normalized = processName.Trim();
        if (normalized.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^4];
        }

        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException("Process name is required.", nameof(processName));
        }

        lock (_gate)
        {
            if (Settings.ExtraProcessNames.Any(p =>
                    string.Equals(p, normalized, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            Settings.ExtraProcessNames.Add(normalized);
            Persist();
            _logger.LogInformation("Added ExtraProcessNames entry: {Process}", normalized);
        }
    }

    private AppSettings Load()
    {
        if (!File.Exists(_configPath))
        {
            var defaults = new AppSettings
            {
                BuiltInMeetingProcesses = BuiltInMeetingApps.DefaultProcessNames.ToList()
            };
            Directory.CreateDirectory(Path.GetDirectoryName(_configPath)!);
            File.WriteAllText(_configPath, JsonSerializer.Serialize(defaults, PrettyJson));
            return defaults;
        }

        var configuration = new ConfigurationBuilder()
            .AddJsonFile(_configPath, optional: false, reloadOnChange: false)
            .Build();

        var settings = new AppSettings();
        configuration.Bind(settings);

        if (settings.BuiltInMeetingProcesses.Count == 0)
        {
            settings.BuiltInMeetingProcesses = BuiltInMeetingApps.DefaultProcessNames.ToList();
        }

        return settings;
    }

    private void Persist()
    {
        var node = new JsonObject
        {
            ["DebounceSeconds"] = Settings.DebounceSeconds,
            ["StartCooldownSeconds"] = Settings.StartCooldownSeconds,
            ["PollIntervalMs"] = Settings.PollIntervalMs,
            ["BuiltInMeetingProcesses"] = ToJsonArray(Settings.BuiltInMeetingProcesses),
            ["ExtraProcessNames"] = ToJsonArray(Settings.ExtraProcessNames),
            ["ExcludedProcessNames"] = ToJsonArray(Settings.ExcludedProcessNames)
        };

        File.WriteAllText(_configPath, node.ToJsonString(PrettyJson));
    }

    private static JsonArray ToJsonArray(IEnumerable<string> values)
    {
        var array = new JsonArray();
        foreach (var value in values)
        {
            array.Add(value);
        }

        return array;
    }

    private static JsonSerializerOptions PrettyJson { get; } = new()
    {
        WriteIndented = true
    };

    public string ConfigPath => _configPath;
}
