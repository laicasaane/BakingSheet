param(
    [Parameter(Mandatory = $true)]
    [string] $PackageRoot
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath $PackageRoot -PathType Container)) {
    throw "Package directory does not exist: '$PackageRoot'."
}

$packageJsonPath = Join-Path $PackageRoot "package.json"
if (-not (Test-Path -LiteralPath $packageJsonPath -PathType Leaf)) {
    throw "Package metadata does not exist: '$packageJsonPath'."
}

$packageVersion = [string] ((Get-Content -LiteralPath $packageJsonPath -Raw | ConvertFrom-Json).version)
if ($packageVersion -notmatch '^[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$') {
    throw "Package version is not semantic: '$packageVersion'."
}

$repositoryVersionUrl = "https://github.com/laicasaane/BakingSheet/blob/$packageVersion"
$rawRepositoryVersionUrl = "https://raw.githubusercontent.com/laicasaane/BakingSheet/$packageVersion"
$relativeLinkPattern = '(\]\(|\]:[ \t]*)(?:\./)?(docs/|\.github/images/)'
$utf8WithoutBom = [System.Text.UTF8Encoding]::new($false)

foreach ($documentName in @("CHANGELOG.md", "README.md")) {
    $documentPath = Join-Path $PackageRoot $documentName
    if (-not (Test-Path -LiteralPath $documentPath -PathType Leaf)) {
        continue
    }

    $content = [System.IO.File]::ReadAllText($documentPath)
    $rewrittenContent = [regex]::Replace(
        $content,
        $relativeLinkPattern,
        {
            param($match)

            $baseUrl = if ($match.Groups[2].Value -eq "docs/") {
                $repositoryVersionUrl
            }
            else {
                $rawRepositoryVersionUrl
            }

            return $match.Groups[1].Value + $baseUrl + "/" + $match.Groups[2].Value
        }
    )

    if ($rewrittenContent -cne $content) {
        [System.IO.File]::WriteAllText($documentPath, $rewrittenContent, $utf8WithoutBom)
    }
}
