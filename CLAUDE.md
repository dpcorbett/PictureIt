# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Overview

PictureIt is a Windows desktop app for viewing and editing photo metadata (EXIF/IPTC/XMP), with **bulk editing** as the core feature. It's built on .NET 10 with a WinUI 3 (Windows App SDK) UI. Development follows a phased plan (scaffold → metadata engine → app shell → bulk editing → organising → search → location → advanced → packaging). Phases 0 (scaffold) and 1 (metadata engine) are done. The UI is still a placeholder window.

## Commands

Run from the repo root. The SDK version is pinned by `global.json`.

```bash
dotnet build src/PictureIt.sln                          # build everything
dotnet run --project src/PictureIt.App                  # launch the app
dotnet test --solution src/PictureIt.sln                # run all tests
dotnet test --project tests/PictureIt.Core.Tests --filter-method "*TagKeyTests*"   # run a subset
```

Tests use xUnit v3 on the **Microsoft Testing Platform** (opted in via `global.json`). With the .NET 10 SDK this means `dotnet test` needs `--solution`/`--project`, and test filters use `--filter-method`/`--filter-class` rather than VSTest's `--filter`.

## Architecture

- **`src/PictureIt.Core`** (`net10.0`): the domain model and pure operations. No UI or I/O dependencies. All edit logic belongs here so it can be unit-tested and shared with the future CLI.
- **`src/PictureIt.Infrastructure`** (`net10.0`): adapters behind Core's interfaces. ExifTool (all writes; long-lived `-stay_open` process) and MetadataExtractor (fast reads).
- **`src/PictureIt.App`** (WinUI 3, unpackaged, MVVM with CommunityToolkit.Mvvm): references both. When no runtime identifier is given, the csproj chooses `win-x64` or `win-arm64`, so plain `dotnet build`/`run` works. The output exe is `PictureIt.exe` under `bin\<platform>\...`.
- **`tests/`**: xUnit projects mirroring the `src/` projects.

Design rule: every metadata write goes through one pipeline. **Build an `EditSet` → show a `ChangePreview` → user confirms → write with backup → record in history (undoable).** Don't add write paths that skip the preview or backup steps.

Tags are identified by `TagKey` (`Core/Model/TagKey.cs`), using ExifTool's group-1 `Group:Name` notation (`IFD0:Model`, `ExifIFD:ExposureTime`, `XMP-dc:Subject`) and compared case-insensitively.

### Metadata engine (Infrastructure)

- **ExifTool is downloaded at build time** by `build/ExifTool.targets`. It's pinned to one version, checked by SHA-256, cached in `tools/exiftool/` (gitignored) and copied to `exiftool\` in every output that references Infrastructure. The first build needs network access. To upgrade, change the version and hash in that file.
- `ExifToolProcess` is one long-lived `-stay_open` process shared by everything. Commands run one at a time and the process restarts after a crash or a cancelled command. Each argument is one line, so **arguments can't contain line breaks**. `ExifToolArguments` sends multi-line values, and values with leading or trailing whitespace, through a temp file (`-TAG<=FILE`).
- `ExifToolReader` is the authoritative reader for editing, because its keys match what the writer writes. `MetadataExtractorSummaryReader` is a fast, read-only reader for thumbnails and the status bar. Its directory names differ from ExifTool's groups, so never use its output to key edits.
- `ExifToolWriter` backs up the whole batch through `IBackupStore` before changing anything. It then writes one file per ExifTool command with `-overwrite_original_in_place`, and with `-P` by default to keep the modified date. Failures are reported per file. "Unchanged" with a warning (e.g. an unknown tag) counts as a failure.
- `FileBackupStore` keeps one folder per batch with a `manifest.json`, under `%LocalAppData%\PictureIt\backups` by default.
- ExifTool reads almost anything: a text file renamed `.jpg` reads as TXT, and a corrupt JPEG reads with an `ExifTool:Warning` tag. Only files like empty ones produce `ExifTool:Error`, which the reader turns into a per-file failure.

### Tests

- `tests/TestData/sample.jpg` has known tags; the full list is in the doc comment on `TestFiles.SampleJpeg`. `sample.png` has none. Tests copy samples into a temp folder through `TestFiles` and never write to `TestData`.
- ExifTool tests share one process through `[Collection(ExifToolCollectionDefinition.Name)]`. `ExifToolFixture.ImageDataHashAsync` hashes the pixel data only, which is how tests prove writes are lossless.

## Build conventions

- `Directory.Build.props` enables nullable, `TreatWarningsAsErrors` and the `latest-recommended` analyzers with code style enforced at build. New warnings break the build.
- NuGet versions are managed centrally in `Directory.Packages.props`. Add `<PackageReference Include="X" />` without a version, and set the version there.
- `.editorconfig` sets file-scoped namespaces and `_camelCase` private fields, and allows underscore test names under `tests/`.

## Leftovers

- `src/PictureIt.UI/` (the abandoned WinForms scaffold) and `src/PictureIt.Core/Class1.cs` still exist on disk but are out of the build: the UI project was removed from the solution, and `Class1.cs` is excluded through `<Compile Remove>`. Both can be deleted.
- The original .NET Framework app at the repo root is deleted in the working tree but not committed. `git show HEAD:MainForm.cs` shows the old EXIF-reading approach (`System.Drawing.PropertyItems`), which is being replaced, not ported.
