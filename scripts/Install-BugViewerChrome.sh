#!/usr/bin/env bash
# Linux script for installing BugViewer in Google Chrome.
# Can also run on macOS via bash in Terminal.

set -euo pipefail

cd "$(dirname "$0")"

siteUrl="https://micampbell.github.io/BugViewer/"
checkOnly=0

# Parse arguments
while [[ $# -gt 0 ]]; do
  case "$1" in
    --check)
      checkOnly=1
      shift
      ;;
    --help)
      echo "Usage: ./Install-BugViewerChrome.sh [--check]"
      echo "  --check   Check Google Chrome and deployed manifest without opening windows."
      exit 0
      ;;
    *)
      echo "Unknown option: $1"
      echo "Usage: ./Install-BugViewerChrome.sh [--check]"
      exit 1
      ;;
  esac
done

# 1. Check for Google Chrome / Chromium
chromeExe=""
isMac=0
if [[ "$(uname -s)" == "Darwin" ]]; then
  isMac=1
  if [[ -d "/Applications/Google Chrome.app" || -d "$HOME/Applications/Google Chrome.app" ]] || open -Ra "Google Chrome" 2>/dev/null; then
    chromeExe="Google Chrome"
  fi
else
  for candidate in google-chrome google-chrome-stable chromium-browser chromium; do
    if command -v "$candidate" >/dev/null 2>&1; then
      chromeExe="$candidate"
      break
    fi
  done
fi

if [[ -z "$chromeExe" ]]; then
  echo "Google Chrome was not found. Please install Google Chrome from https://www.google.com/chrome/"
  if [[ "$checkOnly" -eq 0 && -t 0 ]]; then
    read -rp "Press Enter to exit..."
  fi
  exit 1
fi

# 2. Check for curl
if ! command -v curl >/dev/null 2>&1; then
  echo "curl was not found. Please install curl to run this script."
  if [[ "$checkOnly" -eq 0 && -t 0 ]]; then
    read -rp "Press Enter to exit..."
  fi
  exit 1
fi

# 3. Check deployed manifest
manifest=$(mktemp "${TMPDIR:-/tmp}/bugviewer-manifest.XXXXXX")
trap 'rm -f "$manifest"' EXIT

if ! curl --fail --silent --show-error --location --max-time 20 --output "$manifest" "${siteUrl}manifest.webmanifest"; then
  echo "Could not download the BugViewer PWA manifest. Deploy the updated viewer first."
  if [[ "$checkOnly" -eq 0 && -t 0 ]]; then
    read -rp "Press Enter to exit..."
  fi
  exit 1
fi

if ! grep -qi "file_handlers" "$manifest"; then
  echo "The deployed manifest does not contain file handlers. Deploy the updated viewer first."
  if [[ "$checkOnly" -eq 0 && -t 0 ]]; then
    read -rp "Press Enter to exit..."
  fi
  exit 1
fi

if ! grep -qi "\.bugscene" "$manifest"; then
  echo "The deployed manifest does not mention .bugscene. Deploy the updated viewer first."
  if [[ "$checkOnly" -eq 0 && -t 0 ]]; then
    read -rp "Press Enter to exit..."
  fi
  exit 1
fi

rm -f "$manifest"
trap - EXIT

echo "Found $chromeExe and a deployed manifest mentioning .bugscene file handling."
if [[ "$checkOnly" -eq 1 ]]; then
  exit 0
fi

echo ""
echo "Opening $siteUrl in $chromeExe."
echo "1. Install BugViewer using the install icon in the address bar."
echo "2. If prompted to let BugViewer open files, choose Allow."

if [[ "$isMac" -eq 1 ]]; then
  open -a "Google Chrome" "$siteUrl"
else
  "$chromeExe" "$siteUrl" >/dev/null 2>&1 &
fi

echo ""
if [[ -t 0 ]]; then
  read -rp "Press Enter after installing BugViewer in Chrome..."
  echo ""
  if [[ "$isMac" -eq 1 ]]; then
    echo "3. To associate .bugscene files with BugViewer on macOS:"
    echo "   - Right-click (or Control-click) any .bugscene file in Finder."
    echo "   - Choose 'Get Info' (Cmd+I)."
    echo "   - Expand the 'Open with:' section and select 'BugViewer'."
    echo "   - Click 'Change All...' to open all .bugscene files with BugViewer."
  else
    echo "3. On Linux, Chrome creates a desktop launcher in ~/.local/share/applications/"
    echo "   and registers .bugscene file associations automatically."
    echo "   If your file manager doesn't open .bugscene files in BugViewer automatically:"
    echo "   - Right-click a .bugscene file -> 'Open With Other Application' -> select BugViewer."
    echo "   - Or use: xdg-mime default <bugviewer-desktop-file> application/x-bugscene"
  fi
  echo ""
  read -rp "Press Enter to finish setup..."
fi
exit 0
