$ErrorActionPreference = "Stop"

$scriptPath = Join-Path $PSScriptRoot "Sync-UnityFiles.ps1"
$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("BakingSheet.SyncUnityFiles." + [System.Guid]::NewGuid().ToString("N"))

function Assert-True {
    param(
        [bool] $Condition,
        [string] $Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Assert-Content {
    param(
        [string] $Path,
        [string] $Expected
    )

    Assert-True (Test-Path -LiteralPath $Path -PathType Leaf) "Expected file '$Path' to exist."
    $actual = Get-Content -LiteralPath $Path -Raw
    Assert-True ($actual -eq $Expected) "Expected '$Path' to contain '$Expected', but found '$actual'."
}

try {
    $treeSource = Join-Path $testRoot "tree-source"
    $treeDestination = Join-Path $testRoot "tree-destination"
    New-Item -ItemType Directory -Path (Join-Path $treeSource "CurrentFolder") -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $treeDestination "CurrentFolder") -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $treeDestination "StaleFolder") -Force | Out-Null

    Set-Content -LiteralPath (Join-Path $treeSource "Current.cs") -NoNewline -Value "new-current"
    Set-Content -LiteralPath (Join-Path $treeSource "CurrentFolder/Nested.cs") -NoNewline -Value "new-nested"
    Set-Content -LiteralPath (Join-Path $treeDestination "Current.cs") -NoNewline -Value "old-current"
    Set-Content -LiteralPath (Join-Path $treeDestination "Current.cs.meta") -NoNewline -Value "current-guid"
    Set-Content -LiteralPath (Join-Path $treeDestination "CurrentFolder.meta") -NoNewline -Value "folder-guid"
    Set-Content -LiteralPath (Join-Path $treeDestination "Stale.cs") -NoNewline -Value "stale"
    Set-Content -LiteralPath (Join-Path $treeDestination "Stale.cs.meta") -NoNewline -Value "stale-guid"
    Set-Content -LiteralPath (Join-Path $treeDestination "Orphan.cs.meta") -NoNewline -Value "orphan-guid"
    Set-Content -LiteralPath (Join-Path $treeDestination "StaleFolder/Gone.cs") -NoNewline -Value "gone"
    Set-Content -LiteralPath (Join-Path $treeDestination "StaleFolder/Gone.cs.meta") -NoNewline -Value "gone-guid"
    Set-Content -LiteralPath (Join-Path $treeDestination "StaleFolder.meta") -NoNewline -Value "stale-folder-guid"

    & $scriptPath -Source $treeSource -Destination $treeDestination -Recurse

    Assert-Content (Join-Path $treeDestination "Current.cs") "new-current"
    Assert-Content (Join-Path $treeDestination "CurrentFolder/Nested.cs") "new-nested"
    Assert-Content (Join-Path $treeDestination "Current.cs.meta") "current-guid"
    Assert-Content (Join-Path $treeDestination "CurrentFolder.meta") "folder-guid"
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $treeDestination "Stale.cs"))) "Expected stale tree file to be deleted."
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $treeDestination "Stale.cs.meta"))) "Expected stale tree file metadata to be deleted."
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $treeDestination "Orphan.cs.meta"))) "Expected orphaned tree metadata to be deleted."
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $treeDestination "StaleFolder"))) "Expected stale tree folder to be deleted."
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $treeDestination "StaleFolder.meta"))) "Expected stale tree folder metadata to be deleted."

    $fileSource = Join-Path $testRoot "file-source"
    $fileDestination = Join-Path $testRoot "file-destination"
    New-Item -ItemType Directory -Path $fileSource, $fileDestination -Force | Out-Null

    Set-Content -LiteralPath (Join-Path $fileSource "Current.cs") -NoNewline -Value "new-current"
    Set-Content -LiteralPath (Join-Path $fileSource "New.cs") -NoNewline -Value "new-file"
    Set-Content -LiteralPath (Join-Path $fileSource "Project.csproj") -NoNewline -Value "not-owned"
    Set-Content -LiteralPath (Join-Path $fileDestination "Current.cs") -NoNewline -Value "old-current"
    Set-Content -LiteralPath (Join-Path $fileDestination "Current.cs.meta") -NoNewline -Value "current-guid"
    Set-Content -LiteralPath (Join-Path $fileDestination "Stale.cs") -NoNewline -Value "stale"
    Set-Content -LiteralPath (Join-Path $fileDestination "Stale.cs.meta") -NoNewline -Value "stale-guid"
    Set-Content -LiteralPath (Join-Path $fileDestination "Package.asmdef") -NoNewline -Value "unity-only"
    Set-Content -LiteralPath (Join-Path $fileDestination "Package.asmdef.meta") -NoNewline -Value "asmdef-guid"
    Set-Content -LiteralPath (Join-Path $fileDestination "link.xml") -NoNewline -Value "unity-linker"
    Set-Content -LiteralPath (Join-Path $fileDestination "link.xml.meta") -NoNewline -Value "link-guid"

    & $scriptPath -Source $fileSource -Destination $fileDestination -Filter "*.cs"

    Assert-Content (Join-Path $fileDestination "Current.cs") "new-current"
    Assert-Content (Join-Path $fileDestination "New.cs") "new-file"
    Assert-Content (Join-Path $fileDestination "Current.cs.meta") "current-guid"
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $fileDestination "Stale.cs"))) "Expected stale filtered file to be deleted."
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $fileDestination "Stale.cs.meta"))) "Expected stale filtered file metadata to be deleted."
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $fileDestination "Project.csproj"))) "Expected unmatched source file not to be copied."
    Assert-Content (Join-Path $fileDestination "Package.asmdef") "unity-only"
    Assert-Content (Join-Path $fileDestination "Package.asmdef.meta") "asmdef-guid"
    Assert-Content (Join-Path $fileDestination "link.xml") "unity-linker"
    Assert-Content (Join-Path $fileDestination "link.xml.meta") "link-guid"

    $listSource = Join-Path $testRoot "list-source"
    $listDestination = Join-Path $listSource "Package"
    New-Item -ItemType Directory -Path $listSource -Force | Out-Null
    New-Item -ItemType Directory -Path $listDestination -Force | Out-Null

    Set-Content -LiteralPath (Join-Path $listSource "Approved.md") -NoNewline -Value "new-approved"
    Set-Content -LiteralPath (Join-Path $listSource "Plan.md") -NoNewline -Value "do-not-copy"
    Set-Content -LiteralPath (Join-Path $listDestination "Approved.md") -NoNewline -Value "old-approved"
    Set-Content -LiteralPath (Join-Path $listDestination "Removed.md") -NoNewline -Value "stale-approved"
    Set-Content -LiteralPath (Join-Path $listDestination "Removed.md.meta") -NoNewline -Value "stale-approved-guid"
    Set-Content -LiteralPath (Join-Path $listDestination "PackageOnly.md") -NoNewline -Value "package-only"

    & $scriptPath -Source $listSource -Destination $listDestination -Include "Approved.md|Removed.md"

    Assert-Content (Join-Path $listDestination "Approved.md") "new-approved"
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $listDestination "Removed.md"))) "Expected missing allowlisted file to be deleted."
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $listDestination "Removed.md.meta"))) "Expected missing allowlisted file metadata to be deleted."
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $listDestination "Plan.md"))) "Expected non-allowlisted source file not to be copied."
    Assert-Content (Join-Path $listDestination "PackageOnly.md") "package-only"

    Write-Output "Sync-UnityFiles tests passed."
}
finally {
    if (Test-Path -LiteralPath $testRoot) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}
