# Changelog
All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## 6.3.1-pre.1

- Added `VerticalDictionary<TKey, TValue>` for vertically arranged key-value data.
- Added raw-sheet import and export support for `VerticalDictionary<TKey, TValue>`.
- Added JSON import and export support for `VerticalDictionary<TKey, TValue>`.
- Added recursive nesting between vertical lists and vertical dictionaries.
- Added explicit markers for selecting nested vertical collection instances.
- Added `HeaderMode` with hybrid, split, and flat header layouts.
- Added property-level transposition with `[Transpose]` for CSV, Excel, and Google Sheet import/export.
- Added documentation for nested vertical collections.
- Added an advanced Unity sample for multi-level vertical collection layouts.
- Added the Unity Pipeline package to the development project.
- **Breaking Change**: `RawSheetConverter.SplitHeader` changed to `RawSheetConverter.HeaderMode`.
    - Use `HeaderMode.Flat` for the previous `false` behavior or `HeaderMode.Split` for the previous `true` behavior.
- **Breaking Change**: The protected `RawSheetConverter` constructor was changed by removing its `splitHeader` argument.
    - Set `HeaderMode` after construction instead.
- **Breaking Change**: The public `CsvSheetConverter` constructor was changed by removing its `splitHeader` argument.
    - Set `HeaderMode` after construction instead.
- **Breaking Change**: The default header layout was changed to `HeaderMode.Hybrid`.
    - Set `HeaderMode.Flat` to retain the previous default layout.
- Changed property mapping to traverse nested collection values consistently.
- Changed cross-sheet reference resolution to traverse nested collection values consistently.
- Changed value verification to traverse nested collection values consistently.
- Changed library projects to use C# 9.
- Fixed the Unity development project scripting symbol for the runtime CSV converter.
- Fixed the Unity development project scripting symbol for the runtime Excel converter.

## 6.3.0-pre.2

- Upgrade to Unity 6000.3.20f1
- Allow using Excel Converter in runtime
- Replace NReco.Csv.dll with OpenUPM distribution
- Update CI to copy `CHANGELOG.md` and `README.md` into package

## 6.3.0-pre.1

- Added an OpenUPM release workflow with Unity Cloud package signing.
- Added the Laicasaane fork license and retained the original BakingSheet license in the Unity package.
- Added a `.slnx` solution and shared build-output configuration for modern .NET development.
- Changed the Unity package identity to `com.laicasaane.bakingsheet` and updated its documentation, links, and project metadata.
- Changed the minimum Unity version to 6000.3 and upgraded the development project to Unity 6000.3.17f1, the Input System, and Asset Store Publishing Tools 12.0.5.
- Changed .NET and Unity dependencies to current releases, sourcing most Unity-side NuGet libraries from OpenUPM instead of bundled DLLs.
- Changed `GoogleSheetConverter` to use the current credential and modified-time APIs, with `FetchModifiedTime` now returning `DateTimeOffset`.
- Changed the development and publishing setup for `.slnx` and .NET 10, and refreshed the sample CSV data.
- Fixed Unity 6.3 compatibility for scripting defines and serialized sheet-reference selection.
- Fixed property mapping for custom `ISheet<,>` implementations.
- Fixed readonly-member and struct-layout warnings in sheet reference and enumeration types.
- Removed obsolete GitHub workflows, publishing tasks, and superseded prebuilt DLLs.
