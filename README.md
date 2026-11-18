# PowerKeySwitch

PowerKeySwitch is a proposed Windows utility for correcting text typed with the wrong keyboard layout. It translates selected text by physical keyboard position and can be used as the shared engine for a lightweight standalone app and a future Microsoft PowerToys integration.

> **Development status:** A Windows tray app now supports explicit selection conversion with `Ctrl+Q`. This is not an official Microsoft PowerToys module. OCR, PowerToys IPC, and installer packaging are not implemented.

## Current capabilities

- .NET 8 / C# 12 Windows Core library.
- Dynamic HKL-based mapping using `VkKeyScanEx`, `MapVirtualKeyEx`, and `ToUnicodeEx`.
- Non-mutating `ToUnicodeEx` probes (`TO_UNICODE_NO_STATE_CHANGE`, `0x0004`).
- Deterministic fallback maps for Arabic 101/102, English US/UK QWERTY, French AZERTY, and Spanish QWERTY/ISO.
- Longest-match Arabic ligature handling for `لا`, `لأ`, and `لإ`.
- Preserves and reports unsupported characters.
- Windows layout enumeration and non-activating layout loading helpers.
- Tray application for user-triggered selected-text conversion, clipboard restoration, and optional per-user startup.
- Custom application and notification-area icon featuring English and Arabic characters with two-way arrows.

## Repository

```text
config/                         Settings example and JSON Schema
docs/                           Architecture and PowerToys RFC draft
src/PowerSwitch.Core/           Layout discovery and translation engine
src/PowerSwitch.App/            Windows tray app and explicit hotkey handling
tests/PowerSwitch.Core.Tests/   Deterministic translation tests
```

See [docs/architecture.md](docs/architecture.md) and [docs/powertoys-rfc.md](docs/powertoys-rfc.md).

## Build and test

Requirements: Windows 10 version 1607 or later, .NET 8 SDK, and the Windows targeting pack. From the repository root:

```powershell
dotnet test .\PowerKeySwitch.sln
```

The deterministic fallback tests do not require keyboard layouts to be installed. Native HKL tests should be run on Windows with the intended language packs installed.

## Run the Windows tray app

Requirements: Windows and the .NET 8 SDK. From the repository root:

```powershell
dotnet run --project .\src\PowerSwitch.App\PowerSwitch.App.csproj
```

The app stays in the notification area. Select text in another app and press `Ctrl+Q`; it copies the selection, translates to the next supported installed keyboard layout, pastes the result, and restores the previous clipboard contents unless another app changed the clipboard in the meantime. Right-click the tray icon to disable startup or exit. On first run, startup is enabled for the current Windows user using `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`; no administrator rights are needed. The startup preference is saved separately under `HKCU\Software\PowerKeySwitch`. Install at least two supported keyboard layouts in Windows. If `Ctrl+Q` is already registered, the tray app reports that it could not claim the shortcut.

To publish a Windows x64 build:

```powershell
dotnet publish .\src\PowerSwitch.App\PowerSwitch.App.csproj -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
```

For a per-user installation under `%LOCALAPPDATA%\Programs\PowerKeySwitch`, run `.\install.ps1` from PowerShell. It publishes the self-contained app and starts it; the app then registers itself for the current user's sign-in.

The app only reads selected text after the hotkey is pressed. It does not continuously inspect the screen, keep a text history, or send text elsewhere.

## Settings

`config/settings.example.json` shows planned settings such as the hotkey (`Ctrl+Q`), supported layouts, target policy, and optional last-word selection shortcut. The tray app currently uses its built-in `Ctrl+Q` shortcut and cycles layouts installed in Windows.

## Privacy and status

The translation core does not read the clipboard. The standalone app performs clipboard copy/paste only after the user presses the registered hotkey, works on the foreground selection, restores the previous clipboard data object, and never logs selected text. It does not install a keyboard hook or perform OCR. PowerToys integration requires adapting to the specific PowerToys source version and its internal module contracts; no stable third-party `IPowerToyModule` contract is assumed here.

## License

This project is not yet licensed. A license must be selected by the maintainers before distribution.
