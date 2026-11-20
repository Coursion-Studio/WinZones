# Changelog

## 1.4.0 — languages, what's new and a welcome tour

_2026-11-21_

- **Languages:** WinZones now speaks Deutsch, Español, Français, Italiano, Nederlands, Polski, Português (Brasil), Türkçe, Русский, 日本語, 한국어 and 简体中文. It follows your Windows language, or pick one in **Settings → General → Language**. The change is instant.
- **What's New:** after an update, a short window shows what changed. Open it again any time from the tray menu or **Settings → About**.
- **Welcome tour:** the first time you start WinZones, a quick tour shows Shift+drag, auto-arrange and layouts, and lets you choose the most useful settings.
- **Fixes:** long text in Settings wraps instead of running under the buttons, and the Smart arrange page now says **Add category**, so it isn't mixed up with app rules.

## 1.3.0 — workspaces, app rules and game mode

- **Workspaces:** save where every window is and bring it all back with one click, a shortcut or `--workspace "Name"`. Missing apps can be opened for you, and a workspace can restore itself when its monitors are connected again (great for docking a laptop).
- **App rules:** always open an app in the same place, for example Spotify on monitor 2, zone 3. Rules apply when the app's window opens, and **Apply to open windows** tidies up right away.
- **Game mode:** while a game or any full-screen app is in front, WinZones pauses. Shortcuts go to the game, Shift+drag shows no zones and nothing pops up. Pause by hand any time with **Win+Alt+P**; the tray icon greys out while paused.
- **Command line:** `--workspace`, `--save-workspace`, `--arrange`, `--apply-rules`, `--pause`, `--resume` and `--settings` control the running app (Stream Deck, AutoHotkey, shortcuts).
- **Installer:** `WinZonesSetup.exe` installs per user without admin rights, adds a Start menu entry and an Installed apps entry with a clean uninstall, and supports silent installs (`/S`).
- **Scoop:** install from the Scoop manifest in this repo.
- Releases are now built, tested and published by GitHub Actions on a clean machine.

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
