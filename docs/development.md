# Development

The native Video menu appends FSR to the original AA choices and adds sharpening and anti-ghosting controls. The FSR choice is saved in the mod config; original choices keep the game’s enum values and translated labels.

For the world camera, the controller captures the required settings, selects 100% render scale with MSAA off, and uses URP’s temporal inputs. A validated native detour replaces the temporal resolve with FSR 3.1. Disabling FSR, leaving the world camera or a managed failure restores the captured settings.

The DX11 backend uses FP32, LDR colour, reversed depth and URP motion vectors. Depth comes from `UniversalRenderer.m_DepthTexture.rt`. Jitter is converted from the URP GPU projection matrix to texture pixels with DX11 Y inversion. Camera cuts, resolution changes and native history resets clear FSR history.

Eleven licensed FSR compute shaders and one project-owned reactivity shader are embedded as bundles. Persistent shader copies survive scene loading; owned textures and shaders are released on shutdown. The reactivity shader compares the current colour neighbourhood with motion-reprojected previous input to reduce stale history. It is a heuristic and can leave trails.

## Shader development

The runtime ships compiled shaders; building the C# plugin does not need Unity Editor. The original reactivity shader can be rebuilt with Zig, Wine, d3dcompiler_47 and UnityPy:

```sh
zig cc -target x86_64-windows-gnu -O2 tools/compile_compute.c -o compile_compute.exe
wine compile_compute.exe shaders/Fsr31Reactivity.hlsl reactivity.dxbc
python tools/prepare_reactivity_bundle.py assets/belmont_fsr31_nativeaa reactivity.dxbc assets/belmont_fsr31_reactivity
zig cc -target x86_64-windows-gnu -O2 tools/test_reactivity.c -ld3d11 -ldxgi -ldxguid -o test_reactivity.exe
WINEDLLOVERRIDES='d3d11=n;dxgi=n' wine test_reactivity.exe reactivity.dxbc
```

Run the GPU checks with the game’s Proton prefix and DXVK libraries. A plain WineD3D/OpenGL run encountered a translation failure; it is not the validated test backend. The six cases check static history, disocclusion, reset, matching motion, locating stale reprojected history and ignoring history outside the reprojected footprint. These checks exercise the exact embedded DXBC; Unity integration remains a separate live test.
