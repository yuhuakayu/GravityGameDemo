# JoyShockLibrary 3.0

Downloaded from the official [v3.0 release](https://github.com/JibbSmart/JoyShockLibrary/releases/tag/v3.0), published 2023-04-02.

- Archive: [JSL_3_0.zip](https://github.com/JibbSmart/JoyShockLibrary/releases/download/v3.0/JSL_3_0.zip)
- Archive SHA-256: `861bbc0a7805b7a57da80eb8ec8ee007865dfc03387d7a036adca8edc22de4d7`
- `x86_64/JoyShockLibrary.dll` SHA-256: `81a1095bc2e61ce61ced0c2d3f4ca00e37653437aaeeb266b46fca29b7b795ed`
- The DLL, `JoyShockLibrary.h`, `LICENSE.md`, and `README.md` are unmodified files from this archive.
- Only the Windows x64 binary is included. The Unity importer enables Windows x64 Editor and Windows x64 Standalone; other platforms gracefully use right-stick input.
- P/Invoke and button masks in `JslNative.cs` follow the bundled header. The archive's older `JoyShockLibrary.cs` example is deliberately not imported.
- Native gyro coordinates and fused gravity stay in JSL local space (`JslSetGyroSpace(..., 0)`); angular rates are degrees/second, acceleration is g-force, callback deltaTime is seconds.
- Automatic and continuous JSL zero-offset calibration are disabled so the managed one-second / ongoing / three-second calibration uses consistent raw rates. JSL still supplies fused gravity.
- v3.0 does not expose USB/Bluetooth or a HID path. Windows device ancestry supplies the bus when the matching Sony devices agree; mixed-bus ambiguity is displayed rather than guessed.
- No controller sample payloads, device identifiers, or recordings are sent over the network.

The upstream MIT license and third-party notice are retained in `LICENSE.md`.
