# Changelog

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
