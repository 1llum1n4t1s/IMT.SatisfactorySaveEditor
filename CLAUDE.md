# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & test

The solution uses `.slnx` format (Visual Studio 2026 +). Standard .NET CLI works for everything:

```bash
dotnet build SatisfactorySaveEditor.slnx                 # full build (6 projects)
dotnet test SatisfactorySaveEditor.slnx --no-restore     # run MSTest suite
dotnet test --filter "FullyQualifiedName~BinaryIO"       # single test class
dotnet build -c Release SatisfactorySaveEditor/SatisfactorySaveEditor.csproj
```

Target framework is **net10.0-windows** for the WPF editor and **net10.0** for the parser/tests. `ImplicitUsings` and `Nullable` are both **disabled** project-wide because the parser defines its own `Color` / `Vector` / `DateTime`-adjacent structs that would otherwise collide with `System.*` types — keep explicit `using` lists when editing.

`CA1416` is suppressed in the editor csproj because `gong-wpf-dragdrop` lacks `[SupportedOSPlatform]` attributes; don't unsuppress it.

There is an `azure-pipelines.yml` but it targets the legacy `.sln` / VSBuild and is **stale** — it will not work as-is after the SDK-style + slnx migration.

## Architecture

Four projects, layered:

- **SatisfactorySaveParser** — pure binary serialization of `.sav` files. Entry point: `SatisfactorySave(string path)` opens, decompresses (`System.IO.Compression.ZLibStream` from .NET 10, not P/Invoke), and parses into `FSaveHeader` + `List<SaveObject>`. Every property type (`IntProperty`, `ArrayProperty`, `StructProperty`, etc.) lives in `PropertyTypes/` and implements its own binary read/write against `BinaryReader`/`BinaryWriter`. **No UI dependencies.**
- **SatisfactorySaveEditor** — WPF app. MVVM with `CommunityToolkit.Mvvm` (8.x). `ViewModel/ViewModelLocator.cs` builds a `Microsoft.Extensions.DependencyInjection` container at static init: `MainViewModel` is singleton, dialog VMs are transient. XAML resolves them via `{Binding XxxViewModel, Source={StaticResource Locator}}`.
- **SatisfactorySaveParser.Tests** — MSTest 3.x, only covers the parser layer.
- **SourceCodeMessage** — tiny secondary WPF app shown when the user opens the source repo from the menu.

### The Model layer is the bridge

`SatisfactorySaveEditor/Model/SaveObjectModel.cs` (and `SaveEntityModel` / `SaveComponentModel` / `SaveRootModel`) wraps parser `SaveObject` instances with `INotifyPropertyChanged`, builds the TreeView hierarchy, and exposes `Items` + selection state. Whenever you mutate the editor side, you mutate **the Model**, never the raw parser object directly — the parser instance is still the truth that gets serialized back on save, but observable surface is the Model.

`Util/PropertyViewModelMapper.cs` maps each parser `SerializedProperty` subtype to a corresponding editor property VM (in `ViewModel/Property/` and `ViewModel/Struct/`). `View/PropertyTemplateDictionary.xaml` then renders each VM type with its own DataTemplate. Adding a new property type means: parser side in `PropertyTypes/`, VM side in `ViewModel/Property/`, mapping in `PropertyViewModelMapper`, template in `PropertyTemplateDictionary.xaml`.

### Cheats

Every entry under `Cheats/` implements `ICheat.Apply(SaveObjectModel, SatisfactorySave)`. They are self-registering by virtue of being instantiated in `MainViewModel`'s cheats menu wiring. Multi-step cheats (e.g. `MassDismantleCheat`) own their own dialog Window pair in the same folder.

### Logging

`SuperLightLogger` (NLog-compatible API). **Critical**: `App.xaml.cs` configures logging via a static field initializer guarded by an explicit `static App() { }` constructor — this defeats the `beforefieldinit` optimization. Without the empty static ctor, the CLR may delay static init until after `MainViewModel` calls `LogManager.GetCurrentClassLogger()`, which silently swaps in `NullLogger` for the rest of the process. Don't remove `static App() { }`.

### UI theming (macOS Tahoe Dark)

Two `ResourceDictionary`s merged in `App.xaml`:

- `Resources/Colors.Dark.xaml` — color tokens (Apple SF palette: `#0A84FF` accent, `#1C1C1E` window, etc.). Light variant doesn't exist yet; if you add one, swap the merge here.
- `Resources/Theme.xaml` (~1700 lines) — every WPF control style (Card / Button / TextBox / Menu / ComboBox / TreeView / DataGrid / ScrollBar / Tab / ContextMenu / ToolTip). All brushes use `{DynamicResource ...}` so a future light/dark swap reflows.

**Popup-borne controls (Menu submenu, ComboBox dropdown, ContextMenu, ToolTip) each carry `TextOptions.TextFormattingMode="Display"` + `TextRenderingMode="ClearType"` + `TextHintingMode="Fixed"` + `UseLayoutRounding="True"` on their root Border.** This is mandatory: Popups spin up their own `HwndSource` so the parent Window's `TextOptions` does **not** inherit, and without these the text falls back to `Ideal` mode and renders blurry. If you add a new Popup-hosted template, replicate this block.

`Util/WindowDarkMode.cs` calls `DwmSetWindowAttribute(DWMWA_USE_IMMERSIVE_DARK_MODE)` (attr 20, falling back to 19 for 1809) on every Window's `Loaded` event via `EventManager.RegisterClassHandler` — this dark-tints the OS-drawn titlebar. `App.OnStartup` registers the hook once.

### Localization

- `Util/LocalizationService.cs` — `ObservableObject` singleton (`LocalizationService.Instance`). `[ObservableProperty] CultureInfo currentCulture`; `OnCurrentCultureChanged` flips thread cultures + `Properties.Resources.Culture` and fires `OnPropertyChanged("Item[]")` to invalidate every binding.
- `Util/TrExtension.cs` — `MarkupExtension` that returns a `Binding` to `LocalizationService.Instance["<key>"]`. **All XAML strings go through `{loc:Tr Key=XXX}`**, not `x:Static`, because x:Static is evaluated once at parse and can't follow culture changes.
- `Properties/Resources.resx` (en, neutral) + `Properties/Resources.ja.resx`. The `Resources.Designer.cs` is hand-maintained as a `public static class` with one `public static string KeyName => ResourceManager.GetString("KeyName", resourceCulture);` per entry — keep en/ja/Designer triple in sync when adding keys.
- C# code reads strings via `Properties.Resources.XXX`. In `Window`-derived classes (e.g. `MassDismantleWindow.xaml.cs`) the inherited `Window.Resources` (`ResourceDictionary`) shadows `using SatisfactorySaveEditor.Properties` — use a `using Res = SatisfactorySaveEditor.Properties.Resources;` alias instead of the plain using.
- App startup calls `ApplyStoredCulture()` which reads `Properties.Settings.Default.Culture` or falls back to OS UI culture (ja → ja, else → en). PreferencesWindow exposes a ComboBox bound to `LocalizationService.Instance.SupportedCultures` for runtime switching.

## What's already in flight

The `master` branch has been heavily modernized off the upstream Goz3rr repo: WPF .NET Framework 4.7.2 → .NET 10, MvvmLight → CommunityToolkit.Mvvm, NLog → SuperLightLogger, Newtonsoft.Json → System.Text.Json, custom P/Invoke ZLib → `System.IO.Compression.ZLibStream`, custom IoC → `Microsoft.Extensions.DependencyInjection`, plus the UI redesign and ja localization above. The README is still upstream's and predates all of this — it says "Visual Studio 2017, .NET Framework 4.7.2", which is no longer accurate.

`Update 6/7` of Satisfactory itself is **not** supported (per the README banner and the in-app `MsgUnsupportedVersion` notice). The save format past `FSaveCustomVersion.SaveFileIsCompressed` may not round-trip cleanly on the newest game versions; treat regressions there as scope-bound rather than as bugs.
