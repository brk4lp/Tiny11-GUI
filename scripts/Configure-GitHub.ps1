[CmdletBinding()]
param(
    [string]$Repository = 'brk4lp/Tiny11-GUI'
)

$ErrorActionPreference = 'Stop'

if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
    throw 'GitHub CLI (gh) is required.'
}

gh auth status --hostname github.com
if ($LASTEXITCODE -ne 0) {
    throw 'GitHub CLI is not authenticated. Run: gh auth login --hostname github.com'
}

$description = 'Safe, multilingual WPF GUI for building lighter Windows 11 ISOs with configurable debloating and fail-fast DISM automation.'
$topics = @(
    'tiny11',
    'windows-11',
    'windows',
    'debloat',
    'windows-iso',
    'wpf',
    'dotnet',
    'powershell',
    'dism',
    'windows-customization'
)

$arguments = @('repo', 'edit', $Repository, '--description', $description, '--enable-issues', '--enable-discussions')
foreach ($topic in $topics) {
    $arguments += @('--add-topic', $topic)
}

& gh @arguments
if ($LASTEXITCODE -ne 0) {
    throw "GitHub repository configuration failed with exit code $LASTEXITCODE"
}

Write-Host "Updated description, topics, Issues, and Discussions for $Repository." -ForegroundColor Green
