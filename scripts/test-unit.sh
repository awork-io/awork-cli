#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")/.."

dotnet test tests/Awk.CodeGen.Tests/Awk.CodeGen.Tests.csproj
