# Changelog

## 1.2.0 — automatic updates

- **Over-the-air updates:** WinZones checks GitHub Releases a few seconds after start and then every 6 hours. When a new version is out, a toast appears, and an **Update** item shows up at the top of the tray menu and in **Settings → General → Updates**.
- **Install automatically** (optional): download, verify and install in the background, then restart. Your settings are kept.
- **Verified downloads:** the zip's SHA-256 must match both the release's `.sha256` file and GitHub's own checksum, and the exe inside must report the new version. Anything else is discarded.
- The app swaps its own exe safely, and rolls back if the copy fails. If the folder isn't writable (for example Program Files), it opens the download page instead.
- After updating, a toast confirms the new version.

## 1.1.0 — custom UI

- **New tray menu:** a Raycast-style command palette. Type to filter layouts and commands, then use the arrow keys and Enter. It has layout thumbnails, shortcut keycaps and inline toggles.
- **Redesigned settings window:** icon sidebar, rounded cards, and custom toggles, sliders, steppers and dropdowns. Shortcuts now show as keycaps.
- **Toasts** replace Windows balloon tips, and **custom dialogs** replace message boxes.
- **WinKit:** the UI is now a reusable kit in `kit/` (theme tokens, animation, popups and controls) for other WinForms apps.

## 1.0.0 — first release

- Zone snapping with **Shift + drag**: frosted-glass overlay, a gliding highlight with a live preview of the dragged window, and **Ctrl** to span zones
- **Smart auto-arrange** of all windows by app type (Win+Alt+A, the drop pill, or a tray double-click)
- Custom layouts per monitor with a visual editor, plus ultrawide, 16:9 and grid presets
- Global, rebindable shortcuts with conflict detection
- Smooth window animations and multi-level undo
- Dark settings window: General, Layouts, Shortcuts, Smart arrange and About
- Single ~280 KB exe, per-monitor DPI aware, portable mode, MIT licensed
