$ErrorActionPreference = "Stop"

$repositoryRoot = Resolve-Path (Join-Path $PSScriptRoot "../..")
$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("BakingSheet.Copy." + [System.Guid]::NewGuid().ToString("N"))
$locationPushed = $false

function Write-FixtureFile {
    param(
        [string] $RelativePath,
        [string] $Content
    )

    $path = Join-Path $testRoot $RelativePath
    $directory = Split-Path -Parent $path
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
    Set-Content -LiteralPath $path -NoNewline -Value $Content
}

function Assert-Missing {
    param([string] $RelativePath)

    $path = Join-Path $testRoot $RelativePath
    if (Test-Path -LiteralPath $path) {
        throw "Expected '$path' to be absent."
    }
}

function Assert-Content {
    param(
        [string] $RelativePath,
        [string] $Expected
    )

    $path = Join-Path $testRoot $RelativePath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Expected '$path' to exist."
    }

    $actual = Get-Content -LiteralPath $path -Raw
    if ($actual -ne $Expected) {
        throw "Expected '$path' to contain '$Expected', but found '$actual'."
    }
}

try {
    New-Item -ItemType Directory -Path (Join-Path $testRoot ".vscode/scripts") -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $repositoryRoot "copy.ps1") -Destination (Join-Path $testRoot "copy.ps1")
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot "Sync-UnityFiles.ps1") -Destination (Join-Path $testRoot ".vscode/scripts/Sync-UnityFiles.ps1")
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot "Rewrite-PackageDocumentLinks.ps1") -Destination (Join-Path $testRoot ".vscode/scripts/Rewrite-PackageDocumentLinks.ps1")

    Write-FixtureFile "BakingSheet/Src/Current.cs" "new-core"
    Write-FixtureFile "UnityProject/Packages/com.laicasaane.bakingsheet/Runtime/Core/Current.cs" "old-core"
    Write-FixtureFile "UnityProject/Packages/com.laicasaane.bakingsheet/Runtime/Core/Current.cs.meta" "core-guid"
    Write-FixtureFile "UnityProject/Packages/com.laicasaane.bakingsheet/Runtime/Core/Stale.cs" "stale-core"
    Write-FixtureFile "UnityProject/Packages/com.laicasaane.bakingsheet/Runtime/Core/Stale.cs.meta" "stale-core-guid"

    $converters = @("Excel", "Google", "Csv", "Json")
    foreach ($converter in $converters) {
        Write-FixtureFile "BakingSheet.Converters.$converter/Current.cs" "new-$converter"
        Write-FixtureFile "UnityProject/Packages/com.laicasaane.bakingsheet/Runtime/Converters/$converter/Current.cs" "old-$converter"
        Write-FixtureFile "UnityProject/Packages/com.laicasaane.bakingsheet/Runtime/Converters/$converter/Current.cs.meta" "$converter-guid"
        Write-FixtureFile "UnityProject/Packages/com.laicasaane.bakingsheet/Runtime/Converters/$converter/Package.asmdef" "unity-only-$converter"
    }

    Write-FixtureFile "UnityProject/Packages/com.laicasaane.bakingsheet/Runtime/Converters/Excel/Stale.cs" "stale-excel"
    Write-FixtureFile "UnityProject/Packages/com.laicasaane.bakingsheet/Runtime/Converters/Excel/Stale.cs.meta" "stale-excel-guid"
    Write-FixtureFile "UnityProject/Packages/com.laicasaane.bakingsheet/Runtime/Converters/Google/link.xml" "unity-linker"
    $versionUrl = "https://github.com/laicasaane/BakingSheet/blob/6.3.1-pre.3+build.7"
    Write-FixtureFile "README.md" "[docs](docs/guide.md)`n![image](.github/images/sample.png)"
    Write-FixtureFile "CHANGELOG.md" "[image]: ./.github/images/change.png"
    Write-FixtureFile "Plan.md" "do-not-copy"
    Write-FixtureFile "UnityProject/Packages/com.laicasaane.bakingsheet/package.json" '{ "version": "6.3.1-pre.3+build.7" }'
    Write-FixtureFile "UnityProject/Packages/com.laicasaane.bakingsheet/README.md" "old-readme"
    Write-FixtureFile "UnityProject/Packages/com.laicasaane.bakingsheet/PackageOnly.md" "package-only"

    Push-Location $testRoot
    $locationPushed = $true
    & (Join-Path $testRoot "copy.ps1")
    Pop-Location
    $locationPushed = $false

    Assert-Content "UnityProject/Packages/com.laicasaane.bakingsheet/Runtime/Core/Current.cs" "new-core"
    Assert-Content "UnityProject/Packages/com.laicasaane.bakingsheet/Runtime/Core/Current.cs.meta" "core-guid"
    Assert-Missing "UnityProject/Packages/com.laicasaane.bakingsheet/Runtime/Core/Stale.cs"
    Assert-Missing "UnityProject/Packages/com.laicasaane.bakingsheet/Runtime/Core/Stale.cs.meta"

    foreach ($converter in $converters) {
        Assert-Content "UnityProject/Packages/com.laicasaane.bakingsheet/Runtime/Converters/$converter/Current.cs" "new-$converter"
        Assert-Content "UnityProject/Packages/com.laicasaane.bakingsheet/Runtime/Converters/$converter/Current.cs.meta" "$converter-guid"
        Assert-Content "UnityProject/Packages/com.laicasaane.bakingsheet/Runtime/Converters/$converter/Package.asmdef" "unity-only-$converter"
    }

    Assert-Missing "UnityProject/Packages/com.laicasaane.bakingsheet/Runtime/Converters/Excel/Stale.cs"
    Assert-Missing "UnityProject/Packages/com.laicasaane.bakingsheet/Runtime/Converters/Excel/Stale.cs.meta"
    Assert-Content "UnityProject/Packages/com.laicasaane.bakingsheet/Runtime/Converters/Google/link.xml" "unity-linker"
    Assert-Content "UnityProject/Packages/com.laicasaane.bakingsheet/README.md" "[docs]($versionUrl/docs/guide.md)`n![image]($versionUrl/.github/images/sample.png)"
    Assert-Content "UnityProject/Packages/com.laicasaane.bakingsheet/CHANGELOG.md" "[image]: $versionUrl/.github/images/change.png"
    Assert-Missing "UnityProject/Packages/com.laicasaane.bakingsheet/Plan.md"
    Assert-Content "UnityProject/Packages/com.laicasaane.bakingsheet/PackageOnly.md" "package-only"

    Write-Output "copy.ps1 tests passed."
}
finally {
    if ($locationPushed) {
        Pop-Location
    }

    if (Test-Path -LiteralPath $testRoot) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}
