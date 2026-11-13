#!/usr/bin/env bash
# macOS script for installing BugViewer in Google Chrome.
# Can be double-clicked in Finder or executed from Terminal.

set -euo pipefail

# Ensure working directory is the script directory when double-clicked in Finder
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
      echo "Usage: Install-BugViewerChrome.command [--check]"
      echo "  --check   Check Google Chrome and deployed manifest without opening windows."
      exit 0
      ;;
    *)
      echo "Unknown option: $1"
      echo "Usage: Install-BugViewerChrome.command [--check]"
      exit 1
      ;;
  esac
done

# 1. Check for Google Chrome on macOS
chromeApp=""
if [[ -d "/Applications/Google Chrome.app" ]]; then
  chromeApp="/Applications/Google Chrome.app"
elif [[ -d "$HOME/Applications/Google Chrome.app" ]]; then
  chromeApp="$HOME/Applications/Google Chrome.app"
elif open -Ra "Google Chrome" 2>/dev/null; then
  chromeApp="Google Chrome"
fi

if [[ -z "$chromeApp" ]]; then
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

echo "Found Google Chrome and a deployed manifest mentioning .bugscene file handling."
if [[ "$checkOnly" -eq 1 ]]; then
  exit 0
fi

echo ""
echo "Opening $siteUrl in Google Chrome."
echo "1. Install BugViewer using the install button in the Chrome address bar."
echo "2. If prompted to let BugViewer open files, choose Allow."

if ! open -a "$chromeApp" "$siteUrl"; then
  echo "Could not launch Google Chrome."
  if [[ -t 0 ]]; then
    read -rp "Press Enter to exit..."
  fi
  exit 1
fi

echo ""
if [[ -t 0 ]]; then
  read -rp "Press Enter after installing BugViewer in Chrome..."
  echo ""
  echo "3. To associate .bugscene files with BugViewer on macOS:"
  echo "   - Right-click (or Control-click) any .bugscene file in Finder."
  echo "   - Choose 'Get Info' (Cmd+I)."
  echo "   - Expand the 'Open with:' section and select 'BugViewer'."
  echo "   - Click 'Change All...' to open all .bugscene files with BugViewer."
  echo ""
  read -rp "Press Enter to finish setup..."
fi
exit 0
