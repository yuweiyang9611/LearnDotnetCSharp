#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repository_root"

python3 -m venv .venv
.venv/bin/python -c 'import sys; assert sys.version_info[:2] == (3, 11), sys.version'

dotnet --version
dotnet restore LearnDotnetCSharp.slnx -p:RestoreIgnoreFailedSources=false

echo "Workspace ready: .NET restored and Python .venv created."
