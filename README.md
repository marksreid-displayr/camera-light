# CameraLight

CameraLight detects when an application is using your webcam (by reading the Windows Capability
Access Manager consent store) and activates a smart bulb. This visual indicator informs observers
that you're in a meeting, preventing interruptions.

It runs as a system tray app: the icon shows what the lights are doing, and the menu behind it gives
you a status window, a history of everything that triggered the lights, settings, and a manual off
switch for when a light gets stuck.

## Installation

CameraLight is a dotnet10 tray app for Windows. To install:

1. Download the source code.
2. Build the application based on your Windows environment.
3. Ensure the `appsettings.json` file is configured to suit your needs.

### Starting CameraLight Automatically on Windows:

1. **Startup Folder**: Navigate to the startup folder (`shell:startup`) and place a shortcut to the CameraLight executable.
2. **Registry**: Add a new String Value under `HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run` pointing to the CameraLight executable.
3. **Task Scheduler**: Create a task to run CameraLight at logon or startup.
4. **Third-party software**: Consider tools like "Startup Delayer" for more controlled startup behaviors.

## The tray icon

| Icon | Meaning |
|---|---|
| Grey | The lights are off. |
| Red | The lights are on: something is holding the camera (or the microphone, if you have turned that on). |
| Amber | A light is unreachable. CameraLight keeps retrying on a widening interval. |
| Grey, crossed out | You have held the lights off from the menu. |

The tooltip names the app that triggered them. Left-clicking opens the status window.

The menu offers:

1. **Status** — what the lights are doing, what is holding the camera or microphone, and the reason
   for any failure.
2. **History** — every start, stop, light change and failure, newest first, filterable by device.
   Right-click a row to stop that app from ever turning the lights on again.
3. **Settings** — exceptions, microphone monitoring, and how often to check.
4. **Turn lights off now** — holds the lights off whatever the camera is doing, for when a light is
   stuck on. **Resume automatic** hands control back. This is deliberately forgotten on restart.
5. **Open settings folder** — opens `%APPDATA%\CameraLight`.
6. **Exit** — turns the lights off on the way out, so nothing is left on with nothing left to turn
   it off.

## Where your settings and history live

Both sit in `%APPDATA%\CameraLight`, not beside the executable, so deploying a new build over the
install folder never takes them with it:

- `settings.json` — everything the settings window owns. It is layered on top of the shipped
  `appsettings.json` and reloaded as soon as it changes, so nothing needs a restart.
- `history.jsonl` — one JSON object per event, capped at 2 MB with a single `.1` backup. The last
  500 events are reloaded at startup.

Light addresses and credentials stay in `appsettings.json` and are not exposed in the UI. The
diagnostic log is still Serilog's, at `c:\logs\cameralight\cameralight.log`.

## Detection

A device is considered in use when an app has an open session in the consent store:

```
HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\webcam
HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\webcam
```

Packaged apps appear directly under that key, keyed by package family name; desktop apps appear
under its `NonPackaged` subkey, keyed by executable path with backslashes escaped as `#`. Each app
records `LastUsedTimeStart` and `LastUsedTimeStop`; a start with no matching stop means the app is
holding the device right now.

The microphone records under the sibling `microphone` key and is read the same way, but only when
**Also turn the lights on for the microphone** is switched on — off by default, because far more
apps hold the microphone than hold the camera. When it is on, the camera and the microphone drive
the same lights: either one is enough to turn them on.

### Exceptions

Apps that should never turn the lights on are listed under `Detection:IgnoredApps`, matched as
case-insensitive substrings of the app's consent store identity. Windows Hello face unlock counts as
camera use, so it is the usual first entry — open **History**, find the row, and right-click it.
