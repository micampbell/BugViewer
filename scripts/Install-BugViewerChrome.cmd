@echo off
rem This file must retain CRLF line endings for cmd.exe label handling.
setlocal EnableExtensions DisableDelayedExpansion
set "siteUrl=https://micampbell.github.io/BugViewer/"
set "browser=Chrome"
set "exe=chrome.exe"
set "checkOnly="

:arguments
if "%~1"=="" goto argumentsDone
if /i "%~1"=="--chrome" (
    set "browser=Chrome"
    set "exe=chrome.exe"
) else if /i "%~1"=="--check" (
    set "checkOnly=1"
) else if /i "%~1"=="--help" (
    echo Usage: Install-BugViewerChrome.cmd [--check]
    echo   --check   Check the browser and deployed manifest without opening windows.
    exit /b 0
) else (
    echo Unknown option.
    echo Usage: Install-BugViewerChrome.cmd [--check]
    exit /b 1
)
shift
goto arguments

:argumentsDone
set "browserPath="
call :findBrowser "HKCU\Software\Microsoft\Windows\CurrentVersion\App Paths\%exe%"
if not defined browserPath call :findBrowser "HKLM\Software\Microsoft\Windows\CurrentVersion\App Paths\%exe%"
if not defined browserPath call :findBrowser "HKLM\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\App Paths\%exe%"
if not defined browserPath for /f "delims=" %%F in ('where "%exe%" 2^>nul') do if not defined browserPath set "browserPath=%%F"
if not defined browserPath (
    echo %browser% was not found. Please install Google Chrome.
    goto failure
)

where curl >nul 2>&1
if errorlevel 1 (
    echo curl was not found. This setup requires curl, included in current Windows versions.
    goto failure
)
set "manifest=%TEMP%\bugviewer-manifest-%RANDOM%-%RANDOM%.tmp"
call curl --fail --silent --show-error --location --max-time 20 --output "%manifest%" "%siteUrl%manifest.webmanifest"
if errorlevel 1 (
    echo Could not download the BugViewer PWA manifest. Deploy the updated viewer first.
    goto failure
)
findstr /i /l /c:"file_handlers" "%manifest%" >nul
if errorlevel 1 (
    echo The deployed manifest does not contain file handlers. Deploy the updated viewer first.
    goto failure
)
findstr /i /l /c:".bugscene" "%manifest%" >nul
if errorlevel 1 (
    echo The deployed manifest does not mention .bugscene. Deploy the updated viewer first.
    goto failure
)
del /q "%manifest%" >nul 2>&1
set "manifest="
echo Found %browser% and a deployed manifest mentioning .bugscene file handling.
if defined checkOnly exit /b 0

echo.
echo Opening %siteUrl% in %browser%.
echo 1. Install BugViewer using the install button in the browser address bar.
echo 2. If asked to let BugViewer open files, choose Allow.
start "" "%browserPath%" "%siteUrl%"
if errorlevel 1 (
    echo Could not launch %browser%.
    goto failure
)
echo.
pause
echo.
echo 3. In Windows Default apps, search for .bugscene and select BugViewer if needed.
echo 4. Double-click a .bugscene file and allow file access if prompted.
echo    Existing scene JSON files can be renamed to .bugscene.
start "" "ms-settings:defaultapps"
echo.
echo Complete the Windows and browser prompts to finish setup.
pause
exit /b 0

:findBrowser
for /f "tokens=2,*" %%A in ('reg query "%~1" /ve 2^>nul') do (
    if /i "%%A"=="REG_SZ" if exist "%%B" set "browserPath=%%B"
)
exit /b 0

:failure
if defined manifest if exist "%manifest%" del /q "%manifest%" >nul 2>&1
if not defined checkOnly pause
exit /b 1
