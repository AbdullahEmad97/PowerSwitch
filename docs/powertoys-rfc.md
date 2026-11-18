# RFC: PowerKeySwitch — keyboard-layout typo correction

- **Status:** Draft for discussion
- **Authors:** PowerKeySwitch contributors
- **Target:** Microsoft PowerToys Keyboard Manager / a future standalone utility module
- **Scope:** Initial design proposal; not an approved PowerToys specification

## Summary

PowerKeySwitch corrects text entered while a different keyboard layout was active. A user selects text (or enables a last-word fallback) and invokes a configurable hotkey. The utility maps the selected text through physical key positions from the source layout to the target layout, replaces the selection, and requests that the foreground window use the target layout.

## Motivation

Multilingual users often notice a layout mismatch only after typing. Existing operating-system layout switching changes the layout for future input but does not repair already-entered text. PowerKeySwitch addresses this workflow while leaving the user in control of when text is read or modified.

## Proposed user experience

1. The user enables supported layouts and selects a target policy (cycle to the next layout or choose the most recent different layout).
2. The user selects text and presses the configured hotkey, default `Ctrl+Q`.
3. If there is no selection, the configured fallback may select the last word using `Ctrl+Shift+Left`; this behavior can be disabled.
4. The selected text is translated using physical key positions, pasted in place, and the foreground window is asked to use the target layout.
5. Unsupported characters are preserved. Failures are surfaced without discarding the user's existing clipboard contents.

## Proposed architecture

- **Core:** platform-specific HKL/scan-code translation plus deterministic fallback maps. No clipboard access or global input hooks.
- **InputEngine:** global hotkey, foreground-window validation, short-lived clipboard transaction, simulated input, and input-language request.
- **Standalone App:** tray lifecycle, configuration UI, and settings persistence.
- **PowerToysBridge:** adapter using PowerToys' supported module lifecycle and IPC interfaces for the selected integration release.
- **Tests:** layout conversion tables, native interop tests on Windows, clipboard lifecycle tests, and packaging checks.

The initial repository setup implements Core. The other components are subsequent phases and should be built against the target PowerToys source revision. PowerToys module interfaces are repository-internal and may change; a bridge must follow the chosen revision's contribution guidelines, settings model, telemetry policy, accessibility requirements, and localization process.

## Translation model

For each input character, `VkKeyScanEx` against the source HKL returns the source virtual key and modifier bits. `MapVirtualKeyEx` resolves the physical scan code; the same scan code is mapped to a virtual key in the target HKL. `ToUnicodeEx` then renders the target character. The probe uses `0x0004` (`TO_UNICODE_NO_STATE_CHANGE`) so dead-key state is not changed by translation. Because one physical key can produce multiple characters and some characters require dead-key composition, known fallback strings use longest-match key tables and unsupported text is kept intact.

## Clipboard and input safety requirements

- Register an explicit user-configurable hotkey; do not capture or retain arbitrary typing.
- Operate only on a user-initiated selection in the foreground window.
- Capture and restore clipboard data formats on a best-effort, bounded transaction; do not silently replace data if copy/paste fails.
- Use clipboard update notifications with a finite timeout. Avoid assuming that a zero-length clipboard means no selection until copy completion is known.
- Release only modifiers that the feature itself simulated; do not release the user's physically held modifiers.
- Request a target layout only after successful replacement, and surface failures.
- Do not transmit selected text, retain correction history, or include text in diagnostics/telemetry.

The initial implementation deliberately keeps these behaviors out of Core, where they can be independently reviewed and tested before enabling system-wide input.

## Configuration and compatibility

The checked-in JSON Schema is the normative initial settings shape. Defaults are `Ctrl+Q`, Arabic 101, English QWERTY, French AZERTY, and Spanish QWERTY/ISO enabled, cycle-to-next target policy, and last-word selection fallback enabled. A future settings UI must validate that at least two layouts are enabled and must handle registration conflicts.

The initial supported layout set is a practical subset, not a guarantee for customized layouts or every locale variant. Native mapping requires the target layout to be loaded. Windows language IDs do not consistently distinguish Arabic 101 from 102, so a selected Arabic 102 profile must be carried explicitly to the fallback mapper. The fallback handles common keys without requiring every language pack; it preserves unknown characters instead of guessing.

## Rollout and open design decisions

1. Implement and validate the translation engine and fallback tables.
2. Implement the standalone input/clipboard transaction with explicit failure reporting.
3. Build the tray/settings experience and persistence.
4. Port the adapter to the selected PowerToys revision and obtain maintainers' guidance before proposing an upstream merge.
5. Expand OS-level integration, packaging, and accessibility tests.

Questions for a PowerToys design review include whether this belongs in Keyboard Manager or a dedicated utility, what supported extension boundary is available, how selection fallback should be enabled by default, and how layout-cycle preferences should interact with per-window Windows input-language state.
