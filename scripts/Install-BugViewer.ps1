<#
.SYNOPSIS
Guides a Windows user through installing BugViewer and selecting it for .bugscene files.
.DESCRIPTION
The deployed viewer's PWA registers the file handler when installed. Windows and the
browser may require the user to approve file handling and choose the default app.
This script does not change the default app or modify the registry.
.PARAMETER SiteUrl
The URL of the deployed viewer, including its trailing slash.
.PARAMETER Browser
The browser in which to install the app: Edge (default) or Chrome.
.PARAMETER CheckOnly
Check the browser and deployed manifest without opening any windows.
#>
[CmdletBinding()]
param(
    [string] $SiteUrl = 'https://micampbell.github.io/BugViewer/',
    [ValidateSet('Edge', 'Chrome')]
    [string] $Browser = 'Edge',
    [switch] $CheckOnly
)

$ErrorActionPreference = 'Stop'

try {
    $site = [Uri] $SiteUrl
    if (-not $site.IsAbsoluteUri -or ($site.Scheme -ne 'https' -and
        -not ($site.Scheme -eq 'http' -and $site.IsLoopback))) {
        throw 'SiteUrl must be an HTTPS address (HTTP is allowed for localhost testing).'
    }
    if (-not $site.AbsoluteUri.EndsWith('/')) {
        throw 'SiteUrl must end with a slash, for example https://micampbell.github.io/BugViewer/.'
    }

    $executable = if ($Browser -eq 'Edge') { 'msedge.exe' } else { 'chrome.exe' }
    $appPaths = @(
        "HKCU:\Software\Microsoft\Windows\CurrentVersion\App Paths\$executable",
        "HKLM:\Software\Microsoft\Windows\CurrentVersion\App Paths\$executable",
        "HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\App Paths\$executable"
    )
    $browserPath = $null
    foreach ($key in $appPaths) {
        $candidate = (Get-ItemProperty -LiteralPath $key -ErrorAction SilentlyContinue).'(default)'
        if ($candidate -and (Test-Path -LiteralPath $candidate -PathType Leaf)) {
            $browserPath = $candidate
            break
        }
    }
    if (-not $browserPath) {
        $command = Get-Command $executable -ErrorAction SilentlyContinue
        if ($command) { $browserPath = $command.Source }
    }
    if (-not $browserPath) { throw "$Browser was not found. Install $Browser or choose the other browser with -Browser." }

    $manifestUrl = [Uri]::new($site, 'manifest.webmanifest').AbsoluteUri
    try {
        $response = Invoke-WebRequest -Uri $manifestUrl -UseBasicParsing -TimeoutSec 20
        $content = $response.Content
        if ($content -is [byte[]]) { $content = [Text.Encoding]::UTF8.GetString($content) }
        $manifest = $content | ConvertFrom-Json
    }
    catch {
        throw "Could not read the viewer's PWA manifest at $manifestUrl. Deploy the PWA version of BugViewer first. $($_.Exception.Message)"
    }
    $handlesScene = $false
    foreach ($handler in $manifest.file_handlers) {
        if ($null -eq $handler.accept) { continue }
        foreach ($entry in $handler.accept.PSObject.Properties) {
            if (@($entry.Value) -contains '.bugscene') { $handlesScene = $true }
        }
    }
    if (-not $handlesScene) {
        throw "The manifest at $manifestUrl does not advertise .bugscene file handling. Deploy the updated viewer first."
    }

    Write-Host "Found $Browser and a BugViewer manifest with .bugscene support."
    if ($CheckOnly) { return }

    Write-Host ''
    Write-Host "Opening $SiteUrl in $Browser."
    Write-Host '1. Use the Install app button in the address bar to install BugViewer.'
    Write-Host '2. If the browser asks to let BugViewer open files, choose Allow.'
    Start-Process -FilePath $browserPath -ArgumentList $site.AbsoluteUri
    Write-Host ''
    Read-Host 'After completing installation, press Enter to open Windows Default apps' | Out-Null

    Write-Host ''
    Write-Host '3. In Default apps, search for .bugscene and select BugViewer as its default.'
    Write-Host '   If BugViewer is already selected, no change is needed.'
    Write-Host '4. Double-click a .bugscene file. Existing scene JSON can be renamed to .bugscene.'
    Write-Host '   If the browser asks for file access on first open, choose Allow.'
    Start-Process 'ms-settings:defaultapps'
    Write-Host ''
    Write-Host 'Complete the Windows and browser prompts to finish setup.'
}
catch {
    Write-Error $_.Exception.Message -ErrorAction Continue
    exit 1
}
