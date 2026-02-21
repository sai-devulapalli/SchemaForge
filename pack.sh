#!/usr/bin/env bash
# ============================================================
# SchemaForge - Build all NuGet packages into ./nupkg/
# Usage: ./pack.sh [--configuration Release|Debug]
# ============================================================
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
NUPKG_DIR="$SCRIPT_DIR/nupkg"
CONFIG="Release"

for arg in "$@"; do
    case $arg in
        --configuration) shift; CONFIG="$1" ;;
        Release|Debug) CONFIG="$arg" ;;
    esac
done

echo "Building SchemaForge NuGet packages (configuration: $CONFIG)"
echo "Output: $NUPKG_DIR"
echo ""

mkdir -p "$NUPKG_DIR"

PROJECTS=(
    "src/SchemaForge.Abstractions/SchemaForge.Abstractions.csproj"
    "src/SchemaForge/SchemaForge.csproj"
    "src/SchemaForge.Providers.SqlServer/SchemaForge.Providers.SqlServer.csproj"
    "src/SchemaForge.Providers.Postgres/SchemaForge.Providers.Postgres.csproj"
    "src/SchemaForge.Providers.MySql/SchemaForge.Providers.MySql.csproj"
    "src/SchemaForge.Providers.Oracle/SchemaForge.Providers.Oracle.csproj"
    "src/SchemaForge.Cli/SchemaForge.Cli.csproj"
)

for proj in "${PROJECTS[@]}"; do
    name=$(basename "$proj" .csproj)
    echo "  Packing $name..."
    if dotnet pack "$SCRIPT_DIR/$proj" \
        --configuration "$CONFIG" \
        --output "$NUPKG_DIR" \
        /p:ContinuousIntegrationBuild=false \
        -v:minimal 2>&1; then
        echo "    OK"
    else
        echo "    FAILED: $name"
        exit 1
    fi
done

echo ""
echo "Packages built:"
ls -lh "$NUPKG_DIR"/*.nupkg 2>/dev/null || echo "  (none found)"
