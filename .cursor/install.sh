#!/usr/bin/env bash
# Cloud Agent install step for 1Remote-plus.
#
# The whole solution targets net9.0-windows10.0.19041.0, a Windows-only TFM, so the WPF app and the
# test host cannot *run* on the Linux VM a Cloud Agent uses. What Linux can do — and what CI does not,
# because CI runs on windows-latest — is compile the tree with the Windows targeting packs pulled from
# NuGet (-p:EnableWindowsTargeting=true). This script prepares exactly that: the .NET 9 SDK, the
# submodules Ui.csproj references, and a warmed package cache proven by a Debug build of the tests.
#
# It is idempotent: the SDK install is skipped when the right version is already present, and the
# submodule update and build converge on re-runs.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT"

DOTNET_DIR="$HOME/.dotnet"
DOTNET_CHANNEL="9.0"

# 1. .NET 9 SDK. Skip the download when a working SDK on the pinned channel is already installed.
if ! "$DOTNET_DIR/dotnet" --list-sdks 2>/dev/null | grep -q "^${DOTNET_CHANNEL}\."; then
  curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
  bash /tmp/dotnet-install.sh --channel "$DOTNET_CHANNEL" --install-dir "$DOTNET_DIR"
fi

# Make dotnet resolvable in every shell the agent opens, without editing shell profiles. The muxer
# resolves its own root through the symlink, so DOTNET_ROOT does not need to be exported.
if [ -w /usr/local/bin ] || command -v sudo >/dev/null 2>&1; then
  ${SUDO:-sudo} ln -sf "$DOTNET_DIR/dotnet" /usr/local/bin/dotnet 2>/dev/null \
    || ln -sf "$DOTNET_DIR/dotnet" /usr/local/bin/dotnet
fi
export PATH="$DOTNET_DIR:$PATH"

# 2. Submodules. Ui.csproj has project references into Shawn.Utils and Dragablz, and bundles the PuTTY
#    binaries, all of which are submodules; a plain clone leaves them empty and the build fails.
git submodule update --init --recursive

# 3. Restore + Debug build of the test project. -c Debug matters: any other configuration makes the
#    Ui PreBuild target shell out to powershell.exe, which does not exist here. This warms the NuGet
#    cache (including the Windows targeting packs) and proves the tree compiles.
dotnet build Tests/Tests.csproj -c Debug -p:EnableWindowsTargeting=true

echo "Cloud Agent environment ready: $(dotnet --version) SDK, submodules initialised, Tests build succeeded."
