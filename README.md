# PictureIt
App for viewing and editing picture metadata, including bulk updates.

A Windows desktop app built with .NET 10 and WinUI 3. Metadata writes use [ExifTool](https://exiftool.org/), so edits are lossless and cover EXIF, IPTC and XMP.

## Requirements
- Windows 10 (19041) or later
- .NET 10 SDK

## Build and run
```bash
dotnet build src/PictureIt.sln
dotnet run --project src/PictureIt.App
dotnet test --solution src/PictureIt.sln
```
