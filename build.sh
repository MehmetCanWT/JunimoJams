#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")"
export PATH="$HOME/.dotnet:$PATH"

echo "==> Building Junimo Jams (RustedUI Edition)..."
dotnet build JunimoJamsRusted/JunimoJamsRusted.csproj -c Release

echo "✅ Junimo Jams (RustedUI Edition) build and deployment complete!"
