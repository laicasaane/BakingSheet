[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [Parameter(Mandatory = $true)]
    [string] $Source,

    [Parameter(Mandatory = $true)]
    [string] $Destination,

    [Alias("Filter")]
    [string] $Include = "*",

    [switch] $Recurse
)

$ErrorActionPreference = "Stop"
$includePatterns = @($Include.Split("|") | Where-Object { $_.Length -gt 0 })

if ($includePatterns.Count -eq 0) {
    throw "At least one include pattern is required."
}

function Get-NormalizedRoot {
    param([string] $Path)

    $resolved = Resolve-Path -LiteralPath $Path
    return [System.IO.Path]::GetFullPath($resolved.Path).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar
    )
}

function Get-RelativePath {
    param(
        [string] $Root,
        [string] $Path
    )

    $prefix = $Root + [System.IO.Path]::DirectorySeparatorChar
    $fullPath = [System.IO.Path]::GetFullPath($Path)

    if (-not $fullPath.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Path '$fullPath' is outside synchronization root '$Root'."
    }

    return $fullPath.Substring($prefix.Length)
}

function Test-IsNestedPath {
    param(
        [string] $Parent,
        [string] $Child
    )

    $prefix = $Parent + [System.IO.Path]::DirectorySeparatorChar
    return $Child.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)
}

function Test-IncludedName {
    param([string] $Name)

    foreach ($pattern in $includePatterns) {
        if ($Name -like $pattern) {
            return $true
        }
    }

    return $false
}

function Remove-DestinationItem {
    param([string] $Path)

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    if (-not (Test-IsNestedPath -Parent $destinationRoot -Child $fullPath)) {
        throw "Refusing to remove path outside destination '$destinationRoot': '$fullPath'."
    }

    if ((Test-Path -LiteralPath $fullPath) -and $PSCmdlet.ShouldProcess($fullPath, "Remove stale Unity mirror item")) {
        Remove-Item -LiteralPath $fullPath -Recurse -Force
    }
}

if (-not (Test-Path -LiteralPath $Source -PathType Container)) {
    throw "Source directory does not exist: '$Source'."
}

if (-not (Test-Path -LiteralPath $Destination -PathType Container)) {
    throw "Destination directory does not exist: '$Destination'."
}

$sourceRoot = Get-NormalizedRoot $Source
$destinationRoot = Get-NormalizedRoot $Destination

if ($sourceRoot -eq $destinationRoot -or
    (Test-IsNestedPath -Parent $destinationRoot -Child $sourceRoot) -or
    ($Recurse -and (Test-IsNestedPath -Parent $sourceRoot -Child $destinationRoot))) {
    throw "Source and destination must be separate directory trees."
}

$sourceFileArguments = @{
    LiteralPath = $sourceRoot
    File = $true
    Force = $true
}
$destinationFileArguments = @{
    LiteralPath = $destinationRoot
    File = $true
    Force = $true
}

if ($Recurse) {
    $sourceFileArguments.Recurse = $true
    $destinationFileArguments.Recurse = $true
}

$sourceFiles = @(
    Get-ChildItem @sourceFileArguments |
        Where-Object { $_.Extension -ne ".meta" -and (Test-IncludedName $_.Name) }
)

if ($Recurse -and $includePatterns.Count -eq 1 -and $includePatterns[0] -eq "*") {
    $destinationDirectories = @(
        Get-ChildItem -LiteralPath $destinationRoot -Directory -Force -Recurse |
            Sort-Object { $_.FullName.Length } -Descending
    )

    foreach ($destinationDirectory in $destinationDirectories) {
        if (-not (Test-Path -LiteralPath $destinationDirectory.FullName -PathType Container)) {
            continue
        }

        $relativePath = Get-RelativePath -Root $destinationRoot -Path $destinationDirectory.FullName
        $sourcePath = Join-Path $sourceRoot $relativePath

        if (-not (Test-Path -LiteralPath $sourcePath -PathType Container)) {
            Remove-DestinationItem $destinationDirectory.FullName
            Remove-DestinationItem ($destinationDirectory.FullName + ".meta")
        }
    }
}

foreach ($destinationFile in @(
    Get-ChildItem @destinationFileArguments |
        Where-Object { $_.Extension -ne ".meta" -and (Test-IncludedName $_.Name) }
)) {
    $relativePath = Get-RelativePath -Root $destinationRoot -Path $destinationFile.FullName
    $sourcePath = Join-Path $sourceRoot $relativePath

    if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
        Remove-DestinationItem $destinationFile.FullName
        Remove-DestinationItem ($destinationFile.FullName + ".meta")
    }
}

$metadataArguments = @{
    LiteralPath = $destinationRoot
    File = $true
    Force = $true
    Filter = "*.meta"
}
if ($Recurse) {
    $metadataArguments.Recurse = $true
}

foreach ($metadataFile in @(Get-ChildItem @metadataArguments)) {
    $assetPath = $metadataFile.FullName.Substring(0, $metadataFile.FullName.Length - ".meta".Length)
    $assetName = [System.IO.Path]::GetFileName($assetPath)

    if (-not (Test-IncludedName $assetName)) {
        continue
    }

    $relativeAssetPath = Get-RelativePath -Root $destinationRoot -Path $assetPath
    $sourceAssetPath = Join-Path $sourceRoot $relativeAssetPath

    if (-not (Test-Path -LiteralPath $sourceAssetPath)) {
        Remove-DestinationItem $metadataFile.FullName
    }
}

if ($Recurse) {
    foreach ($sourceDirectory in @(Get-ChildItem -LiteralPath $sourceRoot -Directory -Force -Recurse)) {
        $relativePath = Get-RelativePath -Root $sourceRoot -Path $sourceDirectory.FullName
        $destinationPath = Join-Path $destinationRoot $relativePath

        if (-not (Test-Path -LiteralPath $destinationPath -PathType Container) -and
            $PSCmdlet.ShouldProcess($destinationPath, "Create Unity mirror directory")) {
            New-Item -ItemType Directory -Path $destinationPath -Force | Out-Null
        }
    }
}

foreach ($sourceFile in $sourceFiles) {
    $relativePath = Get-RelativePath -Root $sourceRoot -Path $sourceFile.FullName
    $destinationPath = Join-Path $destinationRoot $relativePath
    $destinationDirectory = Split-Path -Parent $destinationPath

    if (-not (Test-Path -LiteralPath $destinationDirectory -PathType Container) -and
        $PSCmdlet.ShouldProcess($destinationDirectory, "Create Unity mirror directory")) {
        New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null
    }

    if ($PSCmdlet.ShouldProcess($destinationPath, "Copy source-owned file")) {
        Copy-Item -LiteralPath $sourceFile.FullName -Destination $destinationPath -Force
    }
}
