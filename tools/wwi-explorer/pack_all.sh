#!/bin/bash
set -e
OUTPUT_DIR=$(pwd)/../../nupkg
mkdir -p $OUTPUT_DIR

projects=(
    "../../src/SchemaForge.Abstractions/SchemaForge.Abstractions.csproj"
    "../../src/SchemaForge/SchemaForge.csproj"
    "../../src/SchemaForge.Providers.SqlServer/SchemaForge.Providers.SqlServer.csproj"
    "../../src/SchemaForge.Providers.Postgres/SchemaForge.Providers.Postgres.csproj"
    "../../src/SchemaForge.Providers.MySql/SchemaForge.Providers.MySql.csproj"
    "../../src/SchemaForge.Providers.Oracle/SchemaForge.Providers.Oracle.csproj"
)

for proj in "${projects[@]}"; do
    echo "Packing $proj..."
    dotnet pack "$proj" -c Release -o "$OUTPUT_DIR"
done
