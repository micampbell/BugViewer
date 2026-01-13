param(
    [string]$Configuration = "Release",
    [string]$Output = "artifacts"
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

if (-not (Test-Path $Output)) {
    New-Item -ItemType Directory -Path $Output | Out-Null
}

dotnet restore

dotnet build BugViewer/BugViewer.csproj -c $Configuration --no-restore

dotnet pack BugViewer/BugViewer.csproj -c $Configuration --no-build -o $Output

Write-Host "Packed to: $Output" -ForegroundColor Green
