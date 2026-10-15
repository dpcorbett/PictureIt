# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Overview

PictureIt is a Windows desktop app for viewing and editing photo metadata (EXIF/IPTC/XMP), with **bulk editing** as the core feature. It's built on .NET 10 with a WinUI 3 (Windows App SDK) UI. Development follows a phased plan (scaffold → metadata engine → app shell → bulk editing → organising → search → location → advanced → packaging). Phases 0–2 are done: you can browse a folder, preview photos and edit one photo's metadata. Bulk editing (Phase 3) is next.

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

Design rule: every metadata write goes through one pipeline. **Build an `EditSet` → show a `ChangePreview` → user confirms → write with backup → record in history (undoable).** Don't add write paths that skip the preview or backup steps. (History/undo UI arrives in Phase 3; backups already happen on every write.)

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
- `FileBackupStore` keeps one folder per batch with a `manifest.json`, under `%LocalAppData%\PictureIt\backups`. Settings are in `%LocalAppData%\PictureIt\settings.json`.
- ExifTool reads almost anything: a text file renamed `.jpg` reads as TXT, and a corrupt JPEG reads with an `ExifTool:Warning` tag. Only files like empty ones produce `ExifTool:Error`, which the reader turns into a per-file failure.

### App (WinUI) notes

- Thumbnails and the preview go through `ThumbnailCache`. Thumbnails come from the Windows shell (`GetThumbnailAsync` is capped at **1600 px**). The preview decodes the file at up to 2560 px and falls back to the shell thumbnail for formats Windows can't decode.
- The editor list is a grouped `ListView` over `MetadataEditorViewModel.Groups`, using `TagFieldTemplateSelector`. Per-field display state (e.g. `ShowKey`) lives on `TagFieldViewModel`, because item templates can't reach the editor.
- `GridView.SelectedItems.Add` is ignored until the grid has loaded. That's why `LibraryPage` defers selection requests, and why `LibraryViewModel` keeps a pending selection for a page that doesn't exist yet.
- Editor controls set `AutomationProperties.Name` (field name, "Save", etc.) for accessibility. UI Automation can also drive the app with these names.

### Tests

- `tests/TestData/sample.jpg` has known tags; the full list is in the doc comment on `TestFiles.SampleJpeg`. `sample.png` has none. Tests copy samples into a temp folder through `TestFiles` and never write to `TestData`.
- ExifTool tests share one process through `[Collection(ExifToolCollectionDefinition.Name)]`. `ExifToolFixture.ImageDataHashAsync` hashes the pixel data only, which is how tests prove writes are lossless.
- Presentation tests use NSubstitute fakes for the engine and UI services, plus `InMemorySettingsStore`.

## Build conventions

- `Directory.Build.props` enables nullable, `TreatWarningsAsErrors` and the `latest-recommended` analyzers with code style enforced at build. New warnings break the build.
- NuGet versions are managed centrally in `Directory.Packages.props`. Add `<PackageReference Include="X" />` without a version, and set the version there.
- `.editorconfig` sets file-scoped namespaces and `_camelCase` private fields. Under `tests/` it allows underscore test names (CA1707) and inline arrays (CA1861).
- CommunityToolkit.Mvvm uses **partial properties** (`[ObservableProperty] public partial T Name { get; set; }`), which have no `_name` backing field. Set them through the property in constructors, and guard `On…Changed` side effects (like saving settings) until construction finishes.
- The Visual Studio `.gitignore` rule `Backup*/` is anchored to the repo root (`/Backup*/`). Without that, it silently ignored the `Backup` source folders.

## Leftovers

- `src/PictureIt.UI/` (the abandoned WinForms scaffold) and `src/PictureIt.Core/Class1.cs` still exist on disk but are out of the build: the UI project was removed from the solution, and `Class1.cs` is excluded through `<Compile Remove>`. Both can be deleted.
