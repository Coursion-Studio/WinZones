# WinZones translations

Each file here translates WinZones into one language. English is written in the code itself, so there is no `en.json`.

| File | Language |
|---|---|
| `de.json` | Deutsch |
| `es.json` | Español |
| `fr.json` | Français |
| `it.json` | Italiano |
| `nl.json` | Nederlands |
| `pl.json` | Polski |
| `pt-BR.json` | Português (Brasil) |
| `tr.json` | Türkçe |
| `ru.json` | Русский |
| `ja.json` | 日本語 |
| `ko.json` | 한국어 |
| `zh-Hans.json` | 简体中文 |

The files are compiled into `WinZones.exe` when it is built, so users never see them. To switch languages in the app, use **Settings → General → Language**.

## How a file looks

Each file is a flat JSON object. The key is the exact English text and the value is the translation:

```json
{
  "Settings": "Einstellungen",
  "Snapped to zone {0}": "In Zone {0} eingerastet",
  "Open **Settings → About**": "Öffnen Sie **Einstellungen → Info**"
}
```

- **Keep the key exactly as it is.** Changing one letter in the English text makes it a different string, and the app falls back to English for it.
- **Keep placeholders.** `{0}`, `{1}` and so on are filled in by the app (a number, a monitor name, a shortcut). Every placeholder in the English text must appear in the translation. You may move them around, for example `"{0} von {1}"`.
- **Keep the markers.** In What's New text, `**bold**` and `` `code` `` are formatted. The translation needs the same number of `**` and `` ` `` marks.
- **Don't translate names**: WinZones, Windows, ClearType, Shift, Ctrl, Alt, Win, Enter, Esc, file names and command-line options stay as they are.
- **Notes for translators:** keys that start with `@` are ignored by the app, so `"@note": "..."` can hold a comment.
- Save the file as UTF-8.

Settings menu paths such as **Settings → General** should use the names your translation gives those pages, so the path matches what people see.

## Style

- Write the way Windows itself talks in your language: the same words for *window*, *monitor*, *tray* and *shortcut*, and the same polite form (for example *Sie* in German and *vous* in French). Keep to the form the existing file already uses.
- Keep it short. Buttons and menu items have little room; German and French in particular can overflow. If a translation is much longer than the English, look for a shorter word.
- Sentence case, like the English text: only the first word is capitalised.

## Fix a translation

1. Open the language's file and change the value (the text on the right).
2. Build and run the tests (see below).
3. Open a pull request. Mentioning where the string appears in the app helps the review.

## Add a language

1. Copy an existing file to `<code>.json`, using the Windows language code (`sv`, `uk`, `zh-Hant`, `pt-PT`…), and translate every value.
2. Add a line to `Languages` in [`Lang.cs`](../Lang.cs) with the code and the language's own name, for example `new[] { "sv", "Svenska" },`. This puts it in the Language dropdown.
3. If Windows reports regional codes that should use your file (for example `zh-TW` for `zh-Hant`), update `L.Resolve` in the same file. Simple cases (`sv-FI` → `sv`) already work.
4. Build and run the tests. The build picks up every `lang\*.json` by itself.

## Check your work

From the repository folder (nothing to install; the C# compiler that ships with Windows is enough):

```bat
build.bat
tests\run-tests.bat
```

For every language the tests check that:

- every string in the app has a translation;
- the file has no leftover strings the app no longer uses;
- placeholders (`{0}`) and `**` / `` ` `` markers match the English;
- the file is valid JSON and is compiled into the exe.

A failing test names the language and the string, so you know exactly what to fix.

To get the full list of English strings (for example to start a new language from scratch), run:

```bat
obj\tests\WinZones.Tests.exe --dump-keys keys.json
```

## Which strings are translated

Everything people see: Settings, the tray menu, notifications, dialogs, the layout editor hints, the welcome tour, and the newest section of [`CHANGELOG.md`](../CHANGELOG.md), which What's New shows after an update. Older changelog sections are not translated.

For developers: wrap user-facing text in `L.T("…")`, or `L.F("… {0}", value)` when it has placeholders. Use `L.N("…")` for text that is stored in English (for example in the settings file) and translated only when shown. The tests find these calls in the source, so a new string fails the tests until every language has it.
