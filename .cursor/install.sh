#!/usr/bin/env bash
#
# Cloud Agent bootstrap for 1Remote-plus.
#
# The whole solution targets net9.0-windows10.0.19041.0 (WPF). It cannot run on Linux, but it *builds*
# there with -p:EnableWindowsTargeting=true, and the windowless unit tests run through the throwaway
# net9.0 harness described in .agent_workspace/AUTO_ITERATION.md section 7. The full MSTest suite only
# executes in CI on windows-latest. This script prepares exactly the "build + windowless test" path.
#
# It is idempotent: every step is skipped or converges when the state it creates already exists, so it is
# safe to run again on a warm VM or on top of a prebuilt snapshot.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT"

DOTNET_DIR="$HOME/.dotnet"
DOTNET_CHANNEL="9.0"

echo "==> [1/4] .NET ${DOTNET_CHANNEL} SDK"
if "$DOTNET_DIR/dotnet" --list-sdks 2>/dev/null | grep -q '^9\.'; then
  echo "    already installed: $("$DOTNET_DIR/dotnet" --version)"
else
  curl -sSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
  bash /tmp/dotnet-install.sh --channel "$DOTNET_CHANNEL" --install-dir "$DOTNET_DIR"
fi
export DOTNET_ROOT="$DOTNET_DIR"
export PATH="$DOTNET_DIR:$PATH"

# Make `dotnet` resolvable from every shell the agent opens, without editing a shell profile.
if command -v sudo >/dev/null 2>&1 && sudo -n true 2>/dev/null; then
  sudo ln -sf "$DOTNET_DIR/dotnet" /usr/local/bin/dotnet
  printf 'export DOTNET_ROOT=%s\n' "$DOTNET_DIR" | sudo tee /etc/profile.d/dotnet.sh >/dev/null
fi

echo "==> [2/4] Git submodules (Shawn.Utils, Dragablz, VncSharpCore, PuTTY)"
git submodule update --init --recursive

echo "==> [3/4] Restore NuGet packages for the test project (pulls in Ui.csproj)"
dotnet restore Tests/Tests.csproj -p:EnableWindowsTargeting=true

echo "==> [4/4] Warm build of the test project so the first agent build is incremental"
# -c Debug matters: any other configuration runs Ui.csproj's PreBuild target, which shells out to
# powershell.exe and is not available here. EnableWindowsTargeting lets the net9.0-windows TFM build
# on Linux.
dotnet build Tests/Tests.csproj -c Debug -p:EnableWindowsTargeting=true

echo "install.sh: done — 'dotnet build Tests/Tests.csproj -c Debug -p:EnableWindowsTargeting=true' is ready"
