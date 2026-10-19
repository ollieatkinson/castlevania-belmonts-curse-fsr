#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
python3 scripts/prepare-fsr4.py
mkdir -p .build/fsr4
"${ZIG_COMMAND:-zig}" c++ -target x86_64-windows-gnu -O2 -Wno-nullability-completeness \
  -shared native/Bridge.cpp -Ivendor/fsr4/api/include -Ivendor/fsr4/upscalers/include \
  -ld3d11 -ld3d12 -ldxgi -ldxguid -o .build/fsr4/BelmontFsr4Bridge.dll
