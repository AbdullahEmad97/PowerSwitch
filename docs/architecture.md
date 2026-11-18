# PowerKeySwitch architecture

## Solution structure

```text
PowerKeySwitch.sln
config/
  settings.example.json
  settings.schema.json
docs/
  architecture.md
  powertoys-rfc.md
src/
  PowerSwitch.Core/
    Interop/                 Win32 declarations isolated from translation policy
    Layouts/                 HKL metadata, Windows layout enumeration/loading
    Translation/             dynamic translation and deterministic layout maps
  PowerSwitch.App/           standalone tray app, hotkey, clipboard transaction, input synthesis
  PowerSwitch.PowerToysBridge/ planned: PowerToys adapter and local IPC
tests/
  PowerSwitch.Core.Tests/
```

The current solution includes the Core library, its tests, and the standalone tray app. PowerToysBridge remains a documented future integration boundary.

## Responsibilities and dependencies

`PowerSwitch.Core` is a Windows-targeted class library. It contains no UI, clipboard manipulation, global hooks, or input injection. `KeyboardTranslationEngine` first uses the source HKL to resolve a character to a virtual key and modifier state, obtains the physical scan code, maps that scan code to the target HKL, and asks Windows to render the resulting character. It uses `ToUnicodeEx` with `0x0004` to keep the probe from changing Windows keyboard state. When native probing is unavailable or cannot resolve a character, the engine uses the deterministic built-in maps.

`PowerSwitch.App` depends on Core and owns explicit user-triggered hotkey handling, clipboard snapshot/copy/paste/restore, foreground-window layout selection, tray lifecycle, and per-user startup registry settings. Clipboard and input work runs on the WinForms STA message-pump thread. The app copies only a foreground selection after `Ctrl+Q`, uses the foreground thread's active layout as the source, and selects the next supported installed layout as the target. It does not continuously inspect the screen, retain selected text, or install a keyboard hook. `PowerSwitch.PowerToysBridge` will adapt the core feature to PowerToys module lifecycle and IPC APIs; it must be implemented against the version of PowerToys being integrated, not assumed to be a stable external plugin API.

The standalone app uses WinForms on .NET 8 to provide a small tray lifecycle and an STA message-pump for clipboard/input operations. The PowerToys bridge should reuse the same Core rather than duplicate translation logic.

## Translation behavior and constraints

- An HKL is passed into translation explicitly; the core does not infer a process's foreground thread or change its active layout.
- Layout recognition uses the low 16-bit language identifier in the HKL. Installed custom layouts with matching language IDs use native probing first.
- The deterministic maps model Arabic 101 and Arabic 102, US and UK English QWERTY, French AZERTY, and Spanish QWERTY/ISO. Windows language IDs do not reliably distinguish Arabic 101 from 102; callers selecting a known 102 variant must supply the `Arabic102` profile. They are known-layout fallbacks, not substitutes for OS layout packs or custom remappings.
- Arabic `لا`, `لأ`, and `لإ` are matched as longest physical-key outputs before per-character conversion. Unknown or unsupported characters are preserved and reported in `UnmappedCharacters`.
- A keystroke's shift/AltGr state is retained when translating a scan code. Dead keys that do not yield an independent character are not composed by the core; they remain unchanged unless the fallback table has a direct mapping.
- `TryLoadLayout` accepts an eight-character Windows KLID and loads that layout without requesting activation. Loading may fail if the layout is not installed.

## Settings and safety boundaries

`config/settings.schema.json` defines the planned settings format; `settings.example.json` provides an example. The current tray app uses `Ctrl+Q` and cycles through supported installed layouts. Clipboard waits time out, clipboard restoration avoids overwriting a newer clipboard change, and selected text is never logged.
