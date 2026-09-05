[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$')]
    [string]$Version,

    [ValidateSet('win-x64')]
    [string]$Runtime = 'win-x64',

    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$artifactsRoot = Join-Path $repositoryRoot 'artifacts'
$packageName = "Tiny11-GUI-v$Version-$Runtime"
$publishDirectory = Join-Path $artifactsRoot $packageName
$archivePath = Join-Path $artifactsRoot "$packageName.zip"
$checksumPath = "$archivePath.sha256"

if (-not $artifactsRoot.StartsWith($repositoryRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Resolved artifacts directory is outside the repository.'
}

New-Item -ItemType Directory -Force -Path $artifactsRoot | Out-Null
Remove-Item -LiteralPath $publishDirectory -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $archivePath -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $checksumPath -Force -ErrorAction SilentlyContinue

Push-Location $repositoryRoot
try {
    dotnet restore tiny11-ui.sln --configfile NuGet.config -r $Runtime
    if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed with exit code $LASTEXITCODE" }

    dotnet build tiny11-ui.sln --configuration Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed with exit code $LASTEXITCODE" }

    if (-not $SkipTests) {
        dotnet test tests/Tiny11UI.Tests/Tiny11UI.Tests.csproj --configuration Release --no-build --no-restore
        if ($LASTEXITCODE -ne 0) { throw "dotnet test failed with exit code $LASTEXITCODE" }
    }

    dotnet publish tiny11-ui.csproj --configuration Release --runtime $Runtime --self-contained true --no-restore --output $publishDirectory -p:PublishProfile=win-x64 -p:Version=$Version
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

    Copy-Item README.md, SECURITY.md -Destination $publishDirectory
    Compress-Archive -Path (Join-Path $publishDirectory '*') -DestinationPath $archivePath -CompressionLevel Optimal

    $archive = Get-Item -LiteralPath $archivePath
    if ($archive.Length -le 0) { throw 'Release archive is empty.' }

    $hash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $($archive.Name)" | Set-Content -LiteralPath $checksumPath -Encoding ascii

    Write-Host "Created $archivePath" -ForegroundColor Green
    Write-Host "Created $checksumPath" -ForegroundColor Green
}
finally {
    Pop-Location
}
