# FSR 3.1 Native AA

FSR 3.1 anti-aliasing at native resolution for Castlevania: Belmont’s Curse, with sharpening and an anti-ghosting option. Requires DX11. No frame generation.

## Install

Download a ZIP from [Releases](https://github.com/ollieatkinson/castlevania-belmonts-curse-fsr/releases/latest):

- **Plugin:** for an existing BepInEx 6.0.788 Windows x64 / Unity IL2CPP installation.
- **Standalone:** includes BepInEx. On Windows, you can also extract it anywhere and run `Install.cmd`.

Close the game and extract the whole ZIP into the game folder, beside `Castlevania Belmont's Curse.exe`.

On Linux/Proton, set the Steam launch option:

```sh
WINEDLLOVERRIDES="winhttp=n,b" %command%
```

## Use

Open **Settings → Video → Anti-aliasing** and select **FSR 3.1 Native AA**. Adjust **FSR 3.1 sharpening** and **FSR 3.1 anti-ghosting** below it. Changes save automatically. Select an original AA option to turn FSR off.

FSR runs at 100% render scale. Sharpening defaults to 20%; anti-ghosting defaults to On. Some trails can remain around moving hair and transparent effects.

To uninstall, close the game and delete `BepInEx/plugins/BelmontsCurseGraphics.dll`. Settings are stored in `BepInEx/config/ollieatkinson.belmontscursegraphics.cfg`.

## Build

```sh
dotnet build -c Release -p:GamePath="/path/to/game"
dotnet run --project tests/Fsr31.Tests.csproj -c Release
GamePath="/path/to/game" bash scripts/package.sh
```

Packages are written to `dist/`. See [development](docs/development.md), [testing](docs/validation.md) and [shader provenance](vendor/fsr3/PROVENANCE.md).

MIT. Bundled dependencies retain their own licences. Unofficial community mod.
