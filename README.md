# FSR 4.1.1 Native AA

Experimental FSR 4.1.1 Native AA for Castlevania: Belmont’s Curse, using a DX11–DX12 bridge. Replaces this mod’s FSR 3.1 backend.

Requires a Radeon RX 7000 or RX 9000 desktop GPU and a working DX12 runtime. The game stays on DX11. Linux requires current Proton and Mesa with FSR 4 support.

## Install

Close the game and extract the whole plugin ZIP into the game folder. BepInEx 6.0.788 (Windows x64 / Unity IL2CPP) is required. The standalone ZIP includes it.

Select **Settings → Video → Anti-aliasing → FSR 4.1.1 Native AA**. Adjust sharpening below it. Select an original AA option to turn FSR off. Changes save automatically.

FSR renders at 100% resolution. This version has no frame generation or separate anti-ghosting mask. The SDK provides the temporal reconstruction.

The managed plugin stays at `BepInEx/plugins/BelmontsCurseGraphics.dll`. Native files live in `BepInEx/FSR4/`. To uninstall, remove the plugin and that native folder after closing the game.

## Build

```sh
bash scripts/build-native.sh
dotnet build -c Release -p:GamePath="/path/to/game"
dotnet run --project tests/Fsr4.Tests.csproj -c Release
GamePath="/path/to/game" bash scripts/package.sh
```

The native build uses CMake and Zig on Linux. AMD’s runtime is bundled and checksum-verified; building the bridge needs no downloads. Packages are written to `dist/`.

On Windows with the Visual C++ build tools:

```sh
cmake -S . -B .build/native -A x64
cmake --build .build/native --config Release
cmake --install .build/native --config Release --prefix .build/fsr4
```

See [development](docs/development.md), [testing](docs/validation.md) and [AMD SDK provenance](native/fsr4/PROVENANCE.md).

Project code is MIT. AMD’s runtime and other bundled dependencies retain their own licences. Unofficial community mod.
