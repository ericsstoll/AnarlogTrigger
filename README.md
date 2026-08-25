# AnarlogTrigger

Windows tray app that watches microphone capture sessions. When a common meeting app (or a process you add) holds the mic, it sends **Ctrl+Shift+N** to start Anarlog recording. When that app releases the mic, it shows a sticky stop reminder (no auto-stop).

## Run (dev)

```powershell
dotnet run --project src/AnarlogTrigger
```

## Installer (x64 + arm64)

Build both self-contained MSIs (arm64 is cross-built from an x64 PC):

```powershell
dotnet build AnarlogTrigger.slnx -c Release
```

Outputs:

| MSI | Path | Target PC |
| --- | --- | --- |
| `AnarlogTrigger-x64.msi` | `src/AnarlogTrigger.Installer/bin/x64/Release/` | Intel/AMD |
| `AnarlogTrigger-arm64.msi` | `src/AnarlogTrigger.Installer/bin/arm64/Release/` | Windows on Arm |

Each MSI installs a self-contained app under Program Files and adds a Start Menu shortcut. No separate .NET runtime install is required. Copy the MSI that matches the **target** machine’s architecture.

Build one architecture only:

```powershell
dotnet build src/AnarlogTrigger.Installer/AnarlogTrigger.Installer.x64.wixproj -c Release
dotnet build src/AnarlogTrigger.Installer/AnarlogTrigger.Installer.arm64.wixproj -c Release
```

## Configuration (`appsettings.json`)

### Location

The config file is **`appsettings.json` next to `AnarlogTrigger.exe`**.

- Dev: `src/AnarlogTrigger/appsettings.json` (copied to the build output folder)
- Installed: typically `C:\Program Files\AnarlogTrigger\appsettings.json`

Open it from the tray menu (**Open config**), or edit it in any text editor. After saving, use **Reload config** (or restart the app). **Add process…** appends to `ExtraProcessNames` and reloads automatically.

Editing under Program Files may require running your editor as Administrator, or copying the file out, editing, and copying it back.

### Format

JSON object. Property names are case-sensitive as shown below. Process name matching is **case-insensitive**. You may include or omit the `.exe` suffix (`Zoom` and `Zoom.exe` are equivalent).

### Options reference

| Property | Type | Default | Description |
| --- | --- | --- | --- |
| `DebounceSeconds` | number | `5` | How long a watched app must keep the microphone **active** before AnarlogTrigger sends Ctrl+Shift+N. Brief mic grabs (mute toggles, device checks) under this duration do not start recording. Minimum effective value is `1`. |
| `StartCooldownSeconds` | number | `60` | After a successful start hotkey, ignore new mic-acquire events for this many seconds so one meeting does not fire repeatedly. |
| `PollIntervalMs` | number | `1000` | How often (milliseconds) to poll Windows capture (microphone) sessions. Lower = faster detection, slightly more CPU. Values below `250` are clamped to `250`. |
| `BuiltInMeetingProcesses` | string[] | see below | Process names treated as meeting apps. Remove entries to stop watching specific apps; replace the list to customize defaults. If the array is empty on load, built-in defaults are restored. |
| `ExtraProcessNames` | string[] | `[]` | Additional process names you want to watch (softphones, browsers, niche clients). Same matching rules as built-ins. Prefer this for custom apps instead of editing the built-in list. |
| `ExcludedProcessNames` | string[] | `["anarlog", "AnarlogTrigger"]` | Process names that are **never** treated as a meeting mic, even if they also appear in a watched list. Always include Anarlog and this app to avoid start loops. The running process name of AnarlogTrigger itself is always excluded automatically. |

### Watched vs excluded

A microphone session triggers AnarlogTrigger only when:

1. The session is **active** on a capture (microphone) device, and  
2. The owning process name is in `BuiltInMeetingProcesses` **or** `ExtraProcessNames`, and  
3. The process name is **not** in `ExcludedProcessNames`.

### Default `BuiltInMeetingProcesses`

| App | Process names in the default list |
| --- | --- |
| Microsoft Teams (new + classic) | `ms-teams`, `Teams` |
| Zoom | `Zoom` |
| Slack (huddles / calls) | `slack` |
| Discord | `Discord` |
| Webex | `webex`, `CiscoCollabHost`, `ciscowebexstart` |
| GoTo Meeting | `g2m`, `GoTo Meeting` |
| BlueJeans | `BlueJeans` |
| Skype | `Skype`, `SkypeApp` |
| Amazon Chime | `Chime` |

Browser-only apps (for example Google Meet in Chrome/Edge) are **not** in the defaults—watching `chrome` / `msedge` is too noisy. Add them under `ExtraProcessNames` only if you accept that risk.

### Example

```json
{
  "DebounceSeconds": 5,
  "StartCooldownSeconds": 60,
  "PollIntervalMs": 1000,
  "BuiltInMeetingProcesses": [
    "ms-teams",
    "Teams",
    "Zoom",
    "slack",
    "Discord",
    "webex",
    "CiscoCollabHost",
    "ciscowebexstart",
    "g2m",
    "GoTo Meeting",
    "BlueJeans",
    "Skype",
    "SkypeApp",
    "Chime"
  ],
  "ExtraProcessNames": [
    "MySoftphone",
    "firefox"
  ],
  "ExcludedProcessNames": [
    "anarlog",
    "AnarlogTrigger"
  ]
}
```

### Finding a process name

1. Start the meeting app / softphone.  
2. Open Task Manager → Details.  
3. Use the process name **without** `.exe` (for example `ms-teams`, not `ms-teams.exe`).  
4. Add it via tray **Add process…** or by editing `ExtraProcessNames`, then **Reload config**.

### Behavior notes tied to config

- **Start:** after debounce, sends **Ctrl+Shift+N** once (Anarlog must be running with that global shortcut).  
- **Stop:** does not auto-stop Anarlog; when the watched mic is released, a sticky Windows toast asks you to stop recording. The toast includes **Open Anarlog** (focuses `anarlog.exe`) and **Dismiss**.  
- Invalid JSON will prevent a clean reload; fix the file and use **Reload config** or restart.

## Tray menu

| Item | Action |
| --- | --- |
| Status | Shows monitoring on/off and current phase (idle, debouncing, etc.) |
| Start / Stop monitoring | Pause or resume mic watching without exiting |
| Test start hotkey | Focuses `anarlog.exe` and sends Ctrl+Shift+N (verifies delivery to Anarlog) |
| Add process… | Appends a name to `ExtraProcessNames` and reloads |
| Open config | Opens `appsettings.json` |
| Reload config | Re-reads `appsettings.json` from disk |
| Open log folder | Opens `%LocalAppData%\AnarlogTrigger\logs\` |
| Exit | Quits the tray app |

## Logs

Rolling log files: `%LocalAppData%\AnarlogTrigger\logs\` (kept for 14 days). Useful when a meeting app is not detecting—check whether the process name matched.

## Requirements

- Anarlog installed separately, with global shortcut **Ctrl+Shift+N** bound to start listening
- Windows 10/11
