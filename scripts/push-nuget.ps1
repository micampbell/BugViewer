param(
    [Parameter(Mandatory=$true)]
    [string]$ApiKey,
    [string]$Source = "https://api.nuget.org/v3/index.json",
    [string]$Artifacts = "artifacts"
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

if (-not (Test-Path $Artifacts)) {
    throw "Artifacts folder '$Artifacts' not found. Run scripts/pack.ps1 first."
}

Get-ChildItem -Path $Artifacts -Filter *.nupkg | ForEach-Object {
    dotnet nuget push $_.FullName --api-key $ApiKey --source $Source --skip-duplicate
}

Get-ChildItem -Path $Artifacts -Filter *.snupkg | ForEach-Object {
    dotnet nuget push $_.FullName --api-key $ApiKey --source $Source --skip-duplicate
}
