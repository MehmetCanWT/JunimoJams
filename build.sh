#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")"
export PATH="$HOME/.dotnet:$PATH"

echo "==> Building Junimo Jams..."
dotnet build JunimoJams/JunimoJams.csproj -c Release

echo "✅ Junimo Jams build and deployment complete!"
