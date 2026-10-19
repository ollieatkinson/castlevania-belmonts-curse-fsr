#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")/.."
: "${GamePath:?Set GamePath to the game directory after running BepInEx once.}"
dotnet_command="${DOTNET_COMMAND:-dotnet}"
if [[ -z "${DOTNET_COMMAND:-}" ]] && ! command -v dotnet >/dev/null 2>&1 && [[ -x .build/tools/dotnet/dotnet ]]; then
  dotnet_command="$PWD/.build/tools/dotnet/dotnet"
fi
version="${1:-}"
if [[ -z "$version" ]]; then
  version="$("$dotnet_command" msbuild BelmontsCurseGraphics.csproj -getProperty:Version -nologo)"
fi
if [[ ! "$version" =~ ^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-[0-9A-Za-z]+(\.[0-9A-Za-z]+)*)?$ ]]; then
  echo "Invalid version: $version (expected X.Y.Z or X.Y.Z-preview.N)" >&2
  exit 1
fi

bash scripts/build-native.sh

"$dotnet_command" build BelmontsCurseGraphics.csproj -c Release \
  -p:GamePath="$GamePath" -p:Version="$version"

python3 - "$version" <<'PY'
import hashlib
from pathlib import Path
import sys
import zipfile

version = sys.argv[1]
dist = Path("dist")
dist.mkdir(exist_ok=True)
archive = dist / f"BelmontsCurseGraphics-v{version}.zip"
with zipfile.ZipFile(archive, "w", zipfile.ZIP_DEFLATED) as package:
    package.write("bin/Release/net6.0/BelmontsCurseGraphics.dll", "BepInEx/plugins/BelmontsCurseGraphics.dll")
    package.write("packaging/INSTALL.txt", "INSTALL.txt")
    package.write("docs/validation.md", "docs/validation.md")
    for document in ("README.md", "LICENSE"):
        package.write(document, document)
    for binary in ("BelmontFsr4Bridge.dll", "amd_fidelityfx_upscaler_dx12.dll"):
        package.write(".build/fsr4/" + binary, "BepInEx/FSR4/" + binary)
    for document in ("LICENSE.md", "PROVENANCE.md"):
        package.write("vendor/fsr4/" + document, "BelmontsCurseGraphics/licenses/FSR4/" + document)
    package.write("docs/development.md", "docs/development.md")
checksum = hashlib.sha256(archive.read_bytes()).hexdigest()
archive.with_suffix(".zip.sha256").write_text(f"{checksum}  {archive.name}\n")
print(f"Created {archive}")
PY

"$dotnet_command" msbuild BelmontsCurseGraphics.csproj -t:PackageStandalone -p:Version="$version" -nologo

python3 packaging/validate_package.py "dist/BelmontsCurseGraphics-v$version.zip" BelmontsCurseGraphics.dll
python3 packaging/validate_package.py "dist/BelmontsCurseGraphics-v$version.standalone.zip" BelmontsCurseGraphics.dll --standalone
