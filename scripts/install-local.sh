#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")/.."

# Detect platform
if [[ "$(uname -s)" == "Darwin" ]]; then
    if [[ "$(uname -m)" == "arm64" ]]; then
        RID="osx-arm64"
    else
        RID="osx-x64"
    fi
elif [[ "$(uname -s)" == "Linux" ]]; then
    RID="linux-x64"
else
    RID="win-x64"
fi

echo "Building for $RID..."
dotnet publish src/Awk.Cli -c Release -r "$RID" --nologo -v q

BINARY="$(pwd)/src/Awk.Cli/bin/Release/net10.0/$RID/publish/awork"
INSTALL_DIR="${HOME}/.local/bin"
INSTALL_PATH="${INSTALL_DIR}/awork"

mkdir -p "$INSTALL_DIR"
echo "Installing to $INSTALL_PATH..."
ln -sf "$BINARY" "$INSTALL_PATH"

# Add to PATH hint if needed
if [[ ":$PATH:" != *":$INSTALL_DIR:"* ]]; then
    echo "Note: Add $INSTALL_DIR to your PATH if not already"
fi

echo "Done. Testing:"
"$INSTALL_PATH" doctor
