"""Check the paths Windows needs when extracting a release into the game folder."""
import argparse
import configparser
from pathlib import PurePosixPath
import zipfile

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('archive')
parser.add_argument('plugin')
parser.add_argument('--standalone', action='store_true')
args = parser.parse_args()

with zipfile.ZipFile(args.archive) as package:
    names = [entry.filename for entry in package.infolist() if not entry.is_dir()]
    if len({name.casefold() for name in names}) != len(names):
        raise SystemExit('Duplicate Windows paths in package')
    for name in names:
        path = PurePosixPath(name)
        if '\\' in name or path.is_absolute() or '..' in path.parts:
            raise SystemExit(f'Invalid archive path: {name}')
        if 'bepinex/bepinex/' in name.casefold():
            raise SystemExit(f'Nested BepInEx folder: {name}')
    required = {f'BepInEx/plugins/{args.plugin}', 'README.md', 'LICENSE', 'INSTALL.txt',
                'BepInEx/FSR4/BelmontFsr4Bridge.dll', 'BepInEx/FSR4/amd_fidelityfx_upscaler_dx12.dll',
                'BelmontsCurseGraphics/licenses/FSR4/LICENSE.md'}
    for name in ['BepInEx/FSR4/BelmontFsr4Bridge.dll', 'BepInEx/FSR4/amd_fidelityfx_upscaler_dx12.dll']:
        image = package.read(name)
        pe = int.from_bytes(image[60:64], 'little')
        if image[:2] != b'MZ' or image[pe:pe+6] != b'PE\0\0\x64\x86':
            raise SystemExit(f'Expected Windows x64 native runtime: {name}')
    plugins = [name for name in names if PurePosixPath(name).name == args.plugin]
    if plugins != [f'BepInEx/plugins/{args.plugin}']:
        raise SystemExit(f'Plugin must appear exactly once under BepInEx/plugins: {plugins}')
    if args.standalone:
        required.update({'winhttp.dll', 'doorstop_config.ini', 'Install.cmd', 'Install.ps1',
                         'BepInEx/core/BepInEx.Core.dll', 'BepInEx/core/BepInEx.Unity.IL2CPP.dll',
                         'dotnet/coreclr.dll', 'dotnet/System.Private.CoreLib.dll'})
        config = configparser.ConfigParser()
        config.read_string(package.read('doorstop_config.ini').decode('utf-8-sig'))
        for section, key in [('General', 'target_assembly'), ('Il2Cpp', 'coreclr_path')]:
            required.add(config[section][key].replace('\\', '/'))
        if config['General'].getboolean('enabled') is not True:
            raise SystemExit('Doorstop is disabled')
        for name in ['winhttp.dll', 'dotnet/coreclr.dll']:
            image = package.read(name)
            pe = int.from_bytes(image[60:64], 'little')
            if image[:2] != b'MZ' or image[pe:pe+6] != b'PE\0\0\x64\x86':
                raise SystemExit(f'Expected Windows x64 loader: {name}')
    missing = required.difference(names)
    if missing:
        raise SystemExit(f'Missing package files: {sorted(missing)}')
    print(f'Package layout verified: {args.archive}')
