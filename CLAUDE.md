# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Overview

PictureIt is a Windows desktop app for viewing and editing photo metadata (EXIF/IPTC/XMP), with **bulk editing** as the core feature. It's built on .NET 10 with a WinUI 3 (Windows App SDK) UI. Development follows a phased plan (scaffold → metadata engine → app shell → bulk editing → organising → search → location → advanced → packaging). Phases 0–3 are done: browse a folder, edit one or many photos, apply bulk tools (templates, copy/paste, find and replace, shift dates, remove metadata) and undo any batch from History. Organising tools (Phase 4: rename, sort, export/import, duplicates) are next.

## Commands

Run from the repo root. The SDK version is pinned by `global.json`.

```bash
dotnet build src/PictureIt.sln                          # build everything
dotnet run --project src/PictureIt.App                  # launch the app
dotnet run --project src/PictureIt.App -- "C:\path\photo.jpg"   # open a folder, or a photo's folder with it selected
dotnet test --solution src/PictureIt.sln                # run all tests
dotnet test --project tests/PictureIt.Core.Tests --filter-method "*TagKeyTests*"   # run a subset
```

Tests use xUnit v3 on the **Microsoft Testing Platform** (opted in via `global.json`). With the .NET 10 SDK this means `dotnet test` needs `--solution`/`--project`, and test filters use `--filter-method`/`--filter-class` rather than VSTest's `--filter`.

The app's exe is `src\PictureIt.App\bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64\PictureIt.exe` (ARM64 machines: `bin\ARM64\...`). `src/PictureIt.App/Directory.Build.props` defaults the platform so building the project and building the solution use the same folder. Close a running instance before rebuilding, because the exe stays locked.

## Architecture

- **`src/PictureIt.Core`** (`net10.0`): domain model, the tag catalog and pure operations. No UI or I/O dependencies.
- **`src/PictureIt.Infrastructure`** (`net10.0`): adapters behind Core's interfaces. ExifTool (all writes; long-lived `-stay_open` process), MetadataExtractor (fast summaries), backups and JSON settings.
- **`src/PictureIt.Presentation`** (`net10.0`, CommunityToolkit.Mvvm): view-models and the UI service interfaces (`IFolderPicker`, `IDialogService`, `IThemeService`). **No WinUI types**, so all UI behaviour is unit-tested here.
- **`src/PictureIt.App`** (WinUI 3, unpackaged): views and Windows implementations of the UI services. Services are registered in `App.xaml.cs` (Microsoft.Extensions.DependencyInjection). Pages get their view-model from `App.Services`. Keep code-behind to view concerns: thumbnails, preview, selection plumbing, drag and drop.
- **`tests/`**: one xUnit project per `src/` project except the App.

Design rule: every metadata write goes through **`BatchRunner`** (`Presentation/Services`): build an `EditSet` → `ChangePreview` → user confirms → write with progress and backup → record a `BatchRecord` in history → send `FilesChangedMessage` → report failures. Don't add write paths that skip it. Views that show files listen for `FilesChangedMessage` (CommunityToolkit `IMessenger`) and reload. Undo restores from the batch's backup and sends the same message.

**The preview must match the write.** `PreviewMatchesWriteTests` (Infrastructure.Tests) predicts each bulk operation with `ChangePreview`, writes it with real ExifTool, re-reads the file, and fails if anything changed that the preview didn't show. Add a case there for every new operation.

### Bulk operations (Core/Operations)

- Each operation is a pure function from a photo's `ImageMetadata` to `MetadataEdit`s: `DateShift`, `FindReplace`, `CopyMetadata`, `PrivacyStrip`, and `MetadataTemplate.ToEdits()`. The tool view-models in `Presentation/ViewModels/Tools` just collect options; `ToolsViewModel` runs them over the selection.
- **Patterns:** a `Remove` edit's key can be a pattern: `*:gps*`, `MakerNotes:all`, or `*:all` (everything). `TagPattern` expands it for the preview the way ExifTool does: derived groups (MWG, Composite, File…) are skipped, and `*:all` keeps the ICC colour profile (the writer adds `--ICC_Profile:all` to match).
- **Raw values:** `MetadataEdit.SetRaw` writes ExifTool's numeric form (`-TAG#=`, e.g. GPS `51.5007`) and is compared with `MetadataTag.RawValue` in previews. Use it for anything whose display form isn't writable.
- **MWG side effects:** an MWG write also changes its component tags (`TagCatalog.MwgComponents`). Previews list only the MWG field, so the preview-matches-write test treats components as covered. In this ExifTool version the MWG location fields also write `XMP-iptcExt:LocationShown*`.
- **EXIF dates have no time zone.** `DateShift` adds zones only to XMP values (or values that already have one) and sets `ExifIFD:OffsetTime*` for EXIF. IPTC keeps the date and the time in separate tags, which are shifted as a pair.

### Multi-photo editing

`MetadataEditorViewModel` merges the selected photos. A field whose values differ is **mixed** (`TagFieldViewModel.IsMixed`). A mixed text field starts empty and is left alone unless it's edited, or cleared with `ClearAllCommand`. A mixed list shows the items every photo shares, and edits become add/remove edits so each photo keeps its own other items. Reads are cached per path and invalidated by `FilesChangedMessage` or a changed write time. ExifTool `-P` keeps the write time, which is why the message matters. Multi-photo selections are read after a short `SelectionDelay`.

Tags are identified by `TagKey` (`Core/Model/TagKey.cs`), using ExifTool's group-1 `Group:Name` notation (`IFD0:Model`, `ExifIFD:ExposureTime`, `XMP-dc:Subject`) and compared case-insensitively.

### Tag catalog and edits (Core)

- `TagCatalog` (`Core/Catalog`) decides each tag's editor section, editor type (text, multi-line, date, rating, list), list-ness and validation. It looks tags up **by name regardless of group**; only file-system, computed and maker-note groups are decided by group.
- **Common fields** (`TagCatalog.CommonFields`) are always shown at the top, even when empty. Most are **MWG composite tags** (`MWG:Keywords`, `MWG:Description`, `MWG:Creator`…): one write updates EXIF, IPTC and XMP together, so copies don't drift. Prefer MWG tags for user-facing fields.
- ExifTool reports a one-item list as a plain value, so list-ness comes from the catalog (`IsListTag`), not from the JSON.
- `ListEdits` is the single definition of what an edit does to a tag's values. `ChangePreview` uses it to predict results. `ExifToolWriter` uses it to turn add/remove-from-list edits into one complete `Set`, after reading the current values. ExifTool's own `+=`/`-=` is deliberately avoided: `+=` duplicates existing items, and `-=` combined with `+=` reorders the list. This keeps the preview and the file identical.

### Metadata engine (Infrastructure)

- **ExifTool is downloaded at build time** by `build/ExifTool.targets`. It's pinned to one version, checked by SHA-256, cached in `tools/exiftool/` (gitignored) and copied to `exiftool\` in every output that references Infrastructure. The first build needs network access. To upgrade, change the version and hash in that file.
- `ExifToolProcess` is one long-lived `-stay_open` process shared by everything. Commands run one at a time and the process restarts after a crash or a cancelled command, which costs a restart. View-models therefore ignore stale results instead of cancelling reads. Each argument is one line, so **arguments can't contain line breaks**. `ExifToolArguments` sends multi-line values, and values with leading or trailing whitespace, through a temp file (`-TAG<=FILE`).
- `ExifToolReader` (reads with `-use MWG`) is the authoritative reader for editing, because its keys match what the writer writes. `MetadataExtractorSummaryReader` is a fast, read-only reader for the status bar. Its directory names differ from ExifTool's groups, so never use its output to key edits.
- `ExifToolWriter` backs up the whole batch through `IBackupStore` before changing anything. It then writes one file per ExifTool command with `-overwrite_original_in_place`, and with `-P` by default to keep the modified date. Failures are reported per file. "Unchanged" with a warning (e.g. an unknown tag) counts as a failure.
- `FileBackupStore` keeps one folder per batch with a `manifest.json`, under `%LocalAppData%\PictureIt\backups`. `BackupCleanup` deletes backups older than the retention setting at startup and marks those batches as no longer undoable. Settings, history and templates are JSON files in `%LocalAppData%\PictureIt\` (`Infrastructure/Settings/JsonFile.cs` writes them atomically).
- ExifTool reads almost anything: a text file renamed `.jpg` reads as TXT, and a corrupt JPEG reads with an `ExifTool:Warning` tag. Only files like empty ones produce `ExifTool:Error`, which the reader turns into a per-file failure.

### App (WinUI) notes

- Thumbnails and the preview go through `ThumbnailCache`. Thumbnails come from the Windows shell (`GetThumbnailAsync` is capped at **1600 px**). The preview decodes the file at up to 2560 px and falls back to the shell thumbnail for formats Windows can't decode.
- The editor list is a grouped `ListView` over `MetadataEditorViewModel.Groups`, using `TagFieldTemplateSelector`. Per-field display state (e.g. `ShowKey`) lives on `TagFieldViewModel`, because item templates can't reach the editor.
- `GridView.SelectedItems.Add` is ignored until the grid has loaded. That's why `LibraryPage` defers selection requests, and why `LibraryViewModel` keeps a pending selection for a page that doesn't exist yet.
- Editor controls set `AutomationProperties.Name` (field name, "Save", etc.) for accessibility. UI Automation can also drive the app with these names.
- Tool dialogs are user controls in `App/Dialogs`. `DialogService.ShowToolAsync` maps each tool view-model type to its view, so add a case there for a new tool. The change preview is `ChangePreviewView`. `ContentDialogMaxWidth` is raised to 960 in `App.xaml`, because the WinUI default (548) clips the table.
- `NumberBox` values are `double` (NaN when cleared), so tool view-models bound to them use `double`.

### Tests

- `tests/TestData/sample.jpg` has known tags; the full list is in the doc comment on `TestFiles.SampleJpeg`. `sample.png` has none. Tests copy samples into a temp folder through `TestFiles` and never write to `TestData`.
- ExifTool tests share one process through `[Collection(ExifToolCollectionDefinition.Name)]`. `ExifToolFixture.ImageDataHashAsync` hashes the pixel data only, which is how tests prove writes are lossless.
- Presentation tests build everything through `Harness` (`tests/PictureIt.Presentation.Tests/Fakes.cs`). It wires a real `BatchRunner`, messenger and view-models to a `FakeMetadataReader`, in-memory stores and NSubstitute writer/dialogs. `Harness.Written` lists the edit sets that reached the writer.

## Build conventions

- `Directory.Build.props` enables nullable, `TreatWarningsAsErrors` and the `latest-recommended` analyzers with code style enforced at build. New warnings break the build.
- NuGet versions are managed centrally in `Directory.Packages.props`. Add `<PackageReference Include="X" />` without a version, and set the version there.
- `.editorconfig` sets file-scoped namespaces and `_camelCase` private fields. Under `tests/` it allows underscore test names (CA1707) and inline arrays (CA1861).
- CommunityToolkit.Mvvm uses **partial properties** (`[ObservableProperty] public partial T Name { get; set; }`), which have no `_name` backing field. Set them through the property in constructors, and guard `On…Changed` side effects (like saving settings) until construction finishes.
- The Visual Studio `.gitignore` rule `Backup*/` is anchored to the repo root (`/Backup*/`). Without that, it silently ignored the `Backup` source folders.

## Leftovers

- `src/PictureIt.UI/` (the abandoned WinForms scaffold) and `src/PictureIt.Core/Class1.cs` still exist on disk but are out of the build: the UI project was removed from the solution, and `Class1.cs` is excluded through `<Compile Remove>`. Both can be deleted.
