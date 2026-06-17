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

The editor references **`HelixToolkit.Wpf.SharpDX`** (DX11 3D view) and the Tests project references **`System.Drawing.Common`** (PNG rendering). Both are Windows-only — the `CA1416` platform warnings they raise are expected; don't chase them.

**Analysis/diagnostic harnesses** live in the Tests project but are **not hermetic CI tests**: `MapExport` / `MapRender` (factory placement map → JSON / PNG in `%TEMP%`) and `V2FramingAnalysis` (1.0 property-tag framing sweep) load a **real on-disk sample save** discovered under the user's Documents folder. Run them explicitly, e.g. `dotnet test --filter "FullyQualifiedName~MapRender" --no-build`. **Caveat:** `V2FramingAnalysis` brute-forces tag offsets by calling the real `PropertiesListV2.Parse` at trial positions; a wrong offset feeds a bogus FString length into `ReadLengthPrefixedString` and can OOM/crash the test host (bypassing `finally`). Bounds-check the scan (validate lengths/counts before allocating) before re-running it across the full save.

## Architecture

Four projects, layered:

- **SatisfactorySaveParser** — pure binary serialization of `.sav` files. Entry point: `SatisfactorySave(string path)` opens, decompresses (`System.IO.Compression.ZLibStream` from .NET 10, not P/Invoke), and parses into `FSaveHeader` + `List<SaveObject>` (`Entries`). Every legacy property type (`IntProperty`, `ArrayProperty`, `StructProperty`, etc.) lives in `PropertyTypes/` and implements its own binary read/write against `BinaryReader`/`BinaryWriter`. **No UI dependencies.** The parser handles **two on-disk formats** — see the save-format section below.
- **SatisfactorySaveEditor** — WPF app. MVVM with `CommunityToolkit.Mvvm` (8.x). `ViewModel/ViewModelLocator.cs` builds a `Microsoft.Extensions.DependencyInjection` container at static init: `MainViewModel` is singleton, dialog VMs are transient. XAML resolves them via `{Binding XxxViewModel, Source={StaticResource Locator}}`.
- **SatisfactorySaveParser.Tests** — MSTest 3.x, only covers the parser layer.
- **SourceCodeMessage** — tiny secondary WPF app shown when the user opens the source repo from the menu.

### Save format: legacy (≤U5) vs 1.0+ partitioned worlds

`SatisfactorySave` chooses a path on `FSaveHeader.IsNewFormat` (`HeaderVersion >= AddedWorldPartitionAndHash`, i.e. game 1.0+). **The full byte-level spec is [`docs/SAVE_FORMAT_1.0.md`](docs/SAVE_FORMAT_1.0.md) — read it before touching either path.** It was reverse-engineered from a real 1.2 save and is the source of truth.

- **Legacy (≤ Update 5):** flat body `[objectCount][headers][dataCount][data][collected]`, decompressed via the old 6×int64 chunk header. Handled by `LoadData`/`SaveData` on `SatisfactorySave` exactly as upstream. Unchanged; don't regress it.
- **1.0+ (header v14, save v60):** completely different. Header gains `SaveName`/`SaveIdentifier`/`IsPartitionedWorld`/`SaveDataHash`/`IsCreativeMode`; compression chunks gain the `0x22222222` marker + algorithm byte + int64 body length (see `ChunkInfo` constants + `SatisfactorySave.Decompress`/`Compress`). The body is a per-level partitioned structure parsed into **`Save/SaveBodyV2.cs`**: version data, validation grids, ~3366 streaming sublevels, the persistent/runtime level, and unresolved refs. **`SaveBodyV2` keeps every blob byte-exact for round-trip** — sublevel TOC/DATA stay opaque `byte[]`; only the persistent level's objects are expanded into `SaveObject`s and surfaced via `SatisfactorySave.Entries`.

Round-trip is byte-exact (verified: open → `Save` → reopen yields an identical decompressed body) **because unparsed data is preserved verbatim, not reconstructed.** This is the load-bearing design rule: each `SaveObject` carries its raw inner property bytes in `RawData`, and `SaveBodyV2.BuildData` re-emits `RawData` rather than re-serializing properties. Editing transforms/names/object-deletion already round-trips; **per-property editing of 1.0 objects is not done yet** (see below).

1.0+ object headers (`SaveEntity`/`SaveComponent`) use the `(BinaryReader, FSaveCustomVersion)` constructors + `SerializeNewHeader` (ClassName/Reference/`ObjectFlags`), distinct from the legacy ctors. New 1.0 property tag format lives under `PropertyTypes/V2/` (`FPropertyTagV2`, `FPropertyTagNodeV2`, `PropertiesListV2`) — the tag layout changed entirely in UE5 (flag-driven, type info in a recursive `FPropertyTagNode`); `SaveBodyV2.TryParseProperties` is the hook where V2 parsing will populate `DataFields`, falling back to opaque `RawData` on any failure so round-trip is never broken.

### The Model layer is the bridge

`SatisfactorySaveEditor/Model/SaveObjectModel.cs` (and `SaveEntityModel` / `SaveComponentModel` / `SaveRootModel`) wraps parser `SaveObject` instances with `INotifyPropertyChanged`, builds the TreeView hierarchy, and exposes `Items` + selection state. Whenever you mutate the editor side, you mutate **the Model**, never the raw parser object directly — the parser instance is still the truth that gets serialized back on save, but observable surface is the Model.

`Util/PropertyViewModelMapper.cs` maps each parser `SerializedProperty` subtype to a corresponding editor property VM (in `ViewModel/Property/` and `ViewModel/Struct/`). `View/PropertyTemplateDictionary.xaml` then renders each VM type with its own DataTemplate. Adding a new property type means: parser side in `PropertyTypes/`, VM side in `ViewModel/Property/`, mapping in `PropertyViewModelMapper`, template in `PropertyTemplateDictionary.xaml`.

### Tree display names (friendly names)

The tree shows a **display-only** human/Japanese name, never the raw class path. `Util/FriendlyName.Pretty(string)` strips the trailing instance id, then — **only when the UI culture is `ja`** (`LocalizationService.Instance.CurrentCulture`) — looks the class segment up in `Util/FriendlyNameMap.Ja` (a `~140`-entry `class-segment → 日本語名` dictionary), falling back to a `_C`-strip + word-split English heuristic. `Converter/NodeTooltipConverter.cs` surfaces the raw path on hover. This is **cosmetic**: `SaveObjectModel.Title` (used for search, rename, and serialization) stays the raw class path — only the tree's `DisplayName` binding is prettified. The name is computed once when the model is built, so a runtime language switch only re-localizes the tree after the file is reopened. To localize more classes, add entries to `FriendlyNameMap.Ja`; no other wiring needed.

### 3D world view (HelixToolkit SharpDX)

`View/World3DWindow.xaml(.cs)` is a **read-only** DX11 fly-through of every placed actor, opened **non-modally** from the menu (`Menu3DView` → `MainViewModel.Open3DViewCommand` → `new World3DWindow(saveGame.Entries).Show()`). It uses **`HelixToolkit.Wpf.SharpDX` 3.x** (DX11; renders fine over RDP). Three gotchas, all load-bearing:

- **Coordinate transform:** Satisfactory is **Z-up, centimetres**; HelixToolkit is **Y-up, and the view works in metres** → every position maps `new System.Numerics.Vector3(p.X/100, p.Z/100, p.Y/100)`. HelixToolkit 3.x geometry uses `System.Numerics`, not `Media3D` — keep the `Num = System.Numerics` alias and fully-qualify `HelixToolkit.Wpf.SharpDX.PerspectiveCamera` (else it collides with `Media3D.PerspectiveCamera`).
- **Outlier filter is mandatory:** a handful of in-flight vehicles/projectiles sit at ±950 km height. Without `if (Math.Abs(p.X) > 2e7f || Math.Abs(p.Y) > 2e7f || Math.Abs(p.Z) > 5e5f) continue;` the bounding box explodes and the from-bounds camera ends up ~2000 km away, framing the whole factory as a single speck (looks "black" / "can't pan"). Don't drop this clamp.
- Camera is **placed explicitly from the scene bounds** (not `ZoomExtents`); points are drawn as one `PointGeometryModel3D` per category (colors mirror `MapRender`). This is M1 of a larger epic — picking/selection-sync, an XYZ move gizmo, and delete/duplicate/add are not built yet, so the window currently mutates nothing.

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

**Satisfactory 1.0+ save support is mid-rollout** (the upstream README/`MsgUnsupportedVersion` "Update 6/7 unsupported" wording is now stale). Current state:

- **Done & verified:** open a 1.0/1.2 save, browse all persistent objects in the tree, edit transforms/names, delete objects, and save with a **byte-exact round-trip** (game-loadable). Legacy ≤U5 saves are fully unaffected (40 parser tests green).
- **In progress (Stage 3):** per-property editing of 1.0 objects. The V2 tag reader (`PropertyTypes/V2/`) is proven on real bytes (components 100%, parentless actors 100% in sampling); remaining work is per-type property bodies, write-back, and the parent-bearing-actor data prefix. Track via `docs/SAVE_FORMAT_1.0.md` §6–7.

The Model layer already tolerates 1.0 objects whose `DataFields` are null (opaque `RawData`): `SaveObjectModel` guards the null and shows an empty property list rather than throwing. Don't remove that guard.

Also new on `master` beyond the upstream baseline: the **friendly-name tree localization** and the **3D world view** (both above), plus the **non-hermetic analysis harnesses** in the Tests project (above). These read a real 1.0 sample save from the user's Documents folder; the editor right-pane stays empty for 1.0 objects until Stage 3 lands, which is expected — not a regression.
