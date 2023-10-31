# CameraLight

CameraLight detects when you're using an application that utilizes the camera (by monitoring window titles on Windows) and activates a smart bulb. This visual indicator informs observers that you're in a meeting, preventing interruptions.

## Installation

CameraLight is developed as a dotnet8 console app for Windows. To install:

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

- **Window Title Detection**: Monitors window titles on Windows to determine camera use.
- **URL Activation**: Calls a specified URL to activate the smart bulb.
- **Simplicity and Efficiency**: Lightweight and straightforward.

## Contributing

Contributions are welcome! Potential enhancements include:

- Improved camera detection mechanisms.
- Ports to other platforms (considering the current implementation is Windows-specific).
- Additional methods for smart bulb activation.

Please ensure contributions maintain the project's simplicity and efficiency ethos.

## License

CameraLight is licensed under the MIT License. Refer to the LICENSE file for detailed information.
