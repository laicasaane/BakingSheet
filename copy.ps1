$ErrorActionPreference = "Stop"

$syncScript = Join-Path $PSScriptRoot ".vscode/scripts/Sync-UnityFiles.ps1"
$rewriteDocumentLinksScript = Join-Path $PSScriptRoot ".vscode/scripts/Rewrite-PackageDocumentLinks.ps1"
$packageRoot = Join-Path $PSScriptRoot "UnityProject/Packages/com.laicasaane.bakingsheet"

& $syncScript `
    -Source (Join-Path $PSScriptRoot "BakingSheet/Src") `
    -Destination (Join-Path $packageRoot "Runtime/Core") `
    -Recurse

& $syncScript `
    -Source (Join-Path $PSScriptRoot "BakingSheet.Converters.Excel") `
    -Destination (Join-Path $packageRoot "Runtime/Converters/Excel") `
    -Include "*.cs"

& $syncScript `
    -Source (Join-Path $PSScriptRoot "BakingSheet.Converters.Google") `
    -Destination (Join-Path $packageRoot "Runtime/Converters/Google") `
    -Include "*.cs"

& $syncScript `
    -Source (Join-Path $PSScriptRoot "BakingSheet.Converters.Csv") `
    -Destination (Join-Path $packageRoot "Runtime/Converters/Csv") `
    -Include "*.cs"

& $syncScript `
    -Source (Join-Path $PSScriptRoot "BakingSheet.Converters.Json") `
    -Destination (Join-Path $packageRoot "Runtime/Converters/Json") `
    -Include "*.cs"

& $syncScript `
    -Source $PSScriptRoot `
    -Destination $packageRoot `
    -Include "CHANGELOG.md|LICENSE.md|LICENSE.Original.md|README.md|Third Party Notices.md"

& $rewriteDocumentLinksScript -PackageRoot $packageRoot
