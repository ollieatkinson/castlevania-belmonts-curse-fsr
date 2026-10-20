#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
cmake_command="${CMAKE_COMMAND:-cmake}"
"$cmake_command" -S . -B .build/native \
  -DCMAKE_TOOLCHAIN_FILE="$PWD/native/zig-windows.cmake" -DCMAKE_BUILD_TYPE=Release
"$cmake_command" --build .build/native --config Release
"$cmake_command" --install .build/native --config Release --prefix "$PWD/.build/fsr4"
