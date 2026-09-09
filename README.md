# CameraLight

CameraLight detects when an application is using your webcam (by reading the Windows Capability Access Manager consent store) and activates a smart bulb. This visual indicator informs observers that you're in a meeting, preventing interruptions.

## Installation

CameraLight is developed as a dotnet10 console app for Windows. To install:

1. Download the source code.
2. Build the application based on your Windows environment.
3. Ensure the `appsettings.json` file is configured to suit your needs.

## Usage

After building CameraLight, you can have it monitor for camera-activating applications and turn on the smart bulb accordingly.

### Starting CameraLight Automatically on Windows:

1. **Startup Folder**: Navigate to the startup folder (`shell:startup`) and place a shortcut to the CameraLight executable.
2. **Registry**: Add a new String Value under `HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run` pointing to the CameraLight executable.
3. **Task Scheduler**: Create a task to run CameraLight at logon or startup.
4. **Third-party software**: Consider tools like "Startup Delayer" for more controlled startup behaviors.

## Features

- **Webcam Detection**: Reads the consent store that drives the Windows camera-in-use indicator, so any app holding the webcam counts, whatever it calls its windows.
- **URL Activation**: Calls a specified URL to activate the smart bulb.
- **Simplicity and Efficiency**: Lightweight and straightforward.

## Contributing

Contributions are welcome! Potential enhancements include:

- Ports to other platforms (considering the current implementation is Windows-specific).
- Additional methods for smart bulb activation.

Please ensure contributions maintain the project's simplicity and efficiency ethos.

## License

CameraLight is licensed under the MIT License. Refer to the LICENSE file for detailed information.

## Webcam detection

The camera is considered in use when an app has an open session in the consent store:

```
HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\webcam
HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\webcam
```

Packaged apps appear directly under that key, keyed by package family name; desktop apps appear
under its `NonPackaged` subkey, keyed by executable path with backslashes escaped as `#`. Each app
records `LastUsedTimeStart` and `LastUsedTimeStop`; a start with no matching stop means the app is
holding the camera right now.

Only the webcam counts, not the microphone, so audio-only calls leave the light off. Apps that
should never turn the light on can be listed in `WebcamDetection:IgnoredApps`, which matches
case-insensitive substrings of the app name.
