# Localization guide

PSO2 Shape Studio loads its interface languages from [`locales/*.json`](locales/).
Adding a language does not require a C# change.

## Add a language

1. Copy [`locales/en.json`](locales/en.json) to a BCP 47 filename such as
   `fr.json`, `de.json`, or `pt-BR.json`.
2. Change the locale metadata at the top of the file:
   - `code`: canonical BCP 47 code saved in user settings.
   - `name`: native name shown in the language selector.
   - `order`: selector order. Community languages should normally start at `100`.
3. Translate values in `strings`, `shapeNames`, and `objectTypes`. Do not rename keys.
4. Keep placeholders such as `{0}`, `{0:N0}`, and `{1:F1}` unchanged. Their positions and
   format specifiers are validated by the test suite.
5. Save the file as UTF-8 and open a pull request.

The `$schema` entry enables validation and editor completion with
[`locales/schema.json`](locales/schema.json). Repository tests also require every submitted locale
to contain all current English keys. Missing keys in a locally installed, unfinished translation
fall back to English at runtime.

## Test without rebuilding

Place the JSON file in the `locales` directory beside `Pso2ShapeStudio.App.exe`, then restart the
application. It appears automatically in the language selector. A file with the same `code` as a
built-in locale overrides that locale, which makes translation review possible without compiling.

For a source checkout, run:

```powershell
dotnet test .\Pso2ShapeStudio.sln -c Release -p:Platform=x64 --nologo
```

English remains the fallback language. PSO2 item names are sourced separately from the Global and
Japanese item-name tables, so this locale file translates interface and category text but does not
add a new item-name database.

The BCP 47 `code` also controls number formatting and automatic Windows language detection. The app
follows the culture hierarchy, so `en-US` selects `en`, `ja-JP` selects `ja`, `zh-TW`, `zh-HK`, or
`zh-MO` select `zh-Hant`, and `zh-CN` or `zh-SG` select `zh-Hans` without extra metadata.
