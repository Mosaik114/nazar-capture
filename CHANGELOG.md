# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses
[Semantic Versioning](https://semver.org/).

## [Unreleased]

## [1.1.4] - 2026-11-16

A lighter look: the light mode becomes "Kalkweiß", as in Hams Dictate.

### Changed
- **Light mode "Kalkweiß":** the light palette is no longer cream paper but an almost neutral chalk white
  with a hint of warmth (window `#F5F4F1`, plates white, stones `#F0EEEA`, lighter ink edge and sides),
  the same values as Hams Dictate; the cobalt accent and the dark mode stay.
- The README pictures are drawn anew in the light look; the code that renders them with the app's own
  drawing code is now in the repository (`tools/readme/render.ps1`).

### Fixed
- The white marker colour in the editor toolbar has a fine ink edge, so it stays visible on the light toolbar.

## [1.1.3] - 2026-11-16

A redesign in the look of its sister app Hams Dictate, a new name, and updates from inside the app.

### Changed
- **New name: Nazar Capture** (was Mosaik Capture), with a new logo "Fokus" in cobalt, ink and
  watercolour. Programs `NazarCapture.exe` and `NazarCapture.Engine.exe`, installed to
  `%LOCALAPPDATA%\Programs\NazarCapture`; settings move once from `%LOCALAPPDATA%\MosaikCapture` to
  `%LOCALAPPDATA%\NazarCapture`. New default folders `Nazar-Screenshots` and `Nazar-Aufnahmen`; folders you
  chose stay. Autostart and Start menu point to the new program. The repository is now
  `Mosaik114/nazar-capture` (old links redirect).
- **One design language with Hams:** every window, card, bar and overlay is built from one kit
  (shared design tokens, painted "stone" buttons with an ink edge, Nunito for titles).
- **Main window and settings like Hams':** a small main window (Screenshot, Aufnahme, both hotkeys
  as key caps, gear) opens from the tray icon; the settings window has a painted head and four tabs
  (Screenshot, Aufnahme, Allgemein, Info & Hilfe), applies every change at once, shows problems in
  the row, can be resized and grows with the Windows text size.
- **Appearance:** light or dark, chosen in the settings and applied at once to every open window
  (new users start light; "like Windows" from 1.1.2 becomes the palette Windows shows at the first
  start).
- **Hotkeys** are recorded by pressing them; Print, Pause, Insert, the numpad and Win are possible.
  A hotkey taken by another program is retried every 30 seconds.
- **Keyboard and screen readers everywhere:** Tab, Space, Enter, menus with the arrow keys, the
  selection overlay with the arrow keys (Enter sets start and end); a card or the recording bar
  takes the keyboard with its hotkey plus Ctrl.
- **The start at logon (`--background`) is silent:** only the tray icon, no window, no card, no
  focus taken.

### Added
- **Updates:** once a day a read-only look at the latest GitHub release (can be switched off under
  *Allgemein*); *Jetzt aktualisieren* downloads the ZIP, checks its SHA-256 against the release and only
  then installs it. `NazarCapture.exe --install` installs or updates by hand.
- NVENC that fails in the first seconds of a recording falls back to the software encoder.

### Performance
- The engine is prepared in the background after logon, so the first screenshot opens in about
  70 ms instead of about 650 ms for a cold start (measuring run, `tools/measure.ps1`).
- Memory no longer grows with every capture: cards and pins keep small copies instead of the frozen
  screen. No file access on the UI thread when a recording starts.

### Fixed
- A failed card stays until it is closed; actions that wait for the file say so.
- Recordings need at least 2 GB of free space instead of failing late.
- Autostart removes an old entry only when it starts this app.
- A click on a closed selection list opens it again.

## [1.1.2] - 2026-11-13

### Changed
- The stop button in the recording bar is turquoise (#0B7F74, darker #096A61 while hovered)
  instead of salmon; its light square keeps 4.6:1 (6.1:1 hovered).
- The recording red is a clear red instead of an orange-looking salmon (#F0806A): #FF5A60 in
  the dark theme and #D42A37 in the light theme for the recording dot, "off" icons, the trim
  window's export error and the failure cards' status chip (at least 4.5:1 against the
  background in both themes; before, the light theme had only 2.5:1). The discard button
  waiting for confirmation is filled with #D42A37 (4.8:1 with its light icon). The settings
  window's error text uses #FF5A60 (5.0:1) instead of #F09080.

## [1.1.1] - 2026-11-13

### Changed
- The brand colour is now turquoise instead of terracotta: settings window, cards, editor,
  selection overlay, recording frame, countdown, click highlights, trim window, the first ink
  colour and the app and tray icon. Lines and icons use #0E8C80 in the light theme and
  #2AC8A6 in the dark theme; buttons and handles with light text use #0B7F74 (4.6:1), darker
  #096A61 while hovered or pressed. Hovering a primary button darkens it instead of fading it,
  so its label keeps its contrast. The recording red and the settings window's error text
  are unchanged.
- Failure cards (screenshot or recording failed) show their status chip in the recording red
  instead of the accent, so they read as a warning.

## [1.1.0] - 2026-11-13

### Fixed
- Quitting (tray or `--quit`) no longer closes the engine while the screenshot editor or
  the trim window is open, a screenshot is being saved or a trim/GIF export runs; the
  state query reports the same busy state.
- A recording that could not be saved is no longer lost: its raw segments and sound tracks
  move next to the target as `<name> (Rohdaten)`, the failure card offers to open them,
  and they are never cleaned up automatically.
- Long screenshot and recording folders (over 2,048 characters) are read whole.
- File names use the Gregorian date whatever the system language (e.g. Thai).
- The crash log says whether a restart really started, and why not.
- The dashed frame of pixelate, blur and spotlight stays visible on white content in the
  dark theme; editor notices are centered correctly the first time.
- A flood of rejected capture commands writes at most one log line per second.
- If Windows cannot report the Pictures or Videos folder, the default output folder is below
  the profile folder instead of the root of the drive.
- An audio toggle in the recording bar no longer overwrites unsaved check-box edits in the
  open settings window.
- Folder paths longer than the Windows limit (32,767 characters) are rejected when saving.

### Changed
- `--preview <ini>` is replaced by test isolation through the environment
  (`MOSAIK_CAPTURE_TEST_INSTANCE`, `MOSAIK_CAPTURE_TEST_CONFIG`): an isolated host and engine
  use their own mutexes, window names and data folder and never reach the installed app.
- Exit codes: failures that scripts cannot act on now share code 1; 3 (self-test), 5
  (`--probe` unhealthy, now also when the running instance does not answer) and 11 (`--quit`
  refused) are unchanged.
- The host reads numbers in the settings file like the engine does: anything that is not a
  number means the default (before, `system_audio=yes` read as off on one side and on on the
  other).

### Internal
- `contract/contract.json` is the single source of the host–engine contract; both test
  suites check against it, and a cross-process test runs the quit protocol against the
  built engine (its simulated work feeds the engine's own busy check, so the test meets the
  real refusal).
- `build.ps1 -Test` runs the self-tests of both programs under test-only names.
- The engine's identity is checked by its image path instead of file IDs; the settings
  window code is smaller (shared control creation, no policy tables), and doc comments that
  only repeated a name are gone (`missing_docs` is off).

## [1.0.0] - 2026-11-12

First public release as a standalone project.

### Added
- Settings window, icons and tray icon follow each monitor's scaling (per-monitor DPI).
- Modern folder picker that also accepts long paths.
- Keyboard support: focus and pressed states on buttons, Enter saves, Escape hides; the
  tray icon works with keyboard and pen.
- Messages when something goes wrong instead of silent failures: recording saved without
  sound, text recognition failed, copying to the clipboard failed, ffmpeg not responding.
- Closing the trim window cancels a running export.

### Changed
- Executables are now `MosaikCapture.exe` and `MosaikCapture.Engine.exe`. Settings moved to
  `%LOCALAPPDATA%\MosaikCapture` and are migrated automatically; the autostart entry is
  renamed and the old one removed; logs live in `%LOCALAPPDATA%\MosaikCapture\logs`.
- Custom shortcuts must include Ctrl or Alt, so they can't swallow normal typing.

### Fixed
- Pausing or resuming a recording no longer freezes the UI; the click display can no longer
  be dropped by Windows for responding too slowly.
- All parts of a paused recording use the same encoder, so joining them can't produce a
  broken video; a hanging ffmpeg can no longer leave the engine busy forever.
- Switching the default audio device during a recording (e.g. plugging in headphones) is
  followed within about a second instead of recording silence.
- Two quick trims or GIF exports can no longer overwrite each other.
- Recordings longer than about 93 minutes keep their sound (audio tracks no longer use
  size-limited WAV files).
- A screenshot on a locked or secure desktop shows a message instead of a black image; a
  monitor on a second graphics card is reported as not recordable instead of failing.
- Double-clicking a button in the settings window no longer runs its action twice.
- Unsaved edits in the settings window are kept when it is opened again.
- After a crash the engine restarts reliably (it waits for the old process first).
- `--quit` and forwarded commands report failures through their exit codes
  (see [docs/architecture.md](docs/architecture.md#inter-process-contract)).

### Internal
- Rust host rewritten from a `no_std` single file into an idiomatic Cargo project
  (`windows-sys`, `clippy::pedantic`, documented `unsafe`), unit tests and a window
  end-to-end test.
- C# engine moved to an SDK-style project with nullable types and analyzers; recording and
  screenshot flows extracted into tested state machines; one home each for P/Invoke, UI
  strings, metrics and timing; tests including real ffmpeg runs and pixel comparisons.
- `build.ps1`, GitHub Actions CI with packaging and release upload, secret scanning,
  Dependabot.

## 0.1.0 - 2026-11-08

- First standalone version (before this repository existed): screenshot and
  screen-recording tray app with its own hotkeys (Shift+Alt+4 / Shift+Alt+5), settings
  window and autostart. Its sources were imported unchanged as the first commit of this
  repository on 2026-11-12 (tag [`original-import`](https://github.com/Mosaik114/nazar-capture/tree/original-import)),
  so the whole 1.0.0 restructuring can be read as a before/after diff.

[Unreleased]: https://github.com/Mosaik114/nazar-capture/compare/v1.1.4...HEAD
[1.1.4]: https://github.com/Mosaik114/nazar-capture/releases/tag/v1.1.4
[1.1.3]: https://github.com/Mosaik114/nazar-capture/releases/tag/v1.1.3
[1.1.2]: https://github.com/Mosaik114/nazar-capture/releases/tag/v1.1.2
[1.1.1]: https://github.com/Mosaik114/nazar-capture/releases/tag/v1.1.1
[1.1.0]: https://github.com/Mosaik114/nazar-capture/releases/tag/v1.1.0
[1.0.0]: https://github.com/Mosaik114/nazar-capture/releases/tag/v1.0.0
