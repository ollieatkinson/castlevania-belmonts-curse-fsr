# Development

CMake builds the native bridge and installs it alongside the bundled, checksum-verified AMD runtime. The Linux build wrapper uses the Zig Windows toolchain; Windows builds can use Visual C++.

The mod keeps the existing native Video menu integration and URP temporal resolve hook. URP generates geometry jitter, depth and motion vectors. FSR 4.1.1 replaces the resolve at 100% render scale with MSAA off.

The input-packing compute shader copies colour, the renderer’s actual depth plane and motion vectors into explicit RGBA16F, R32F and RG16F textures. A CommandBuffer plugin event runs the bridge on Unity’s render thread after those copies and before the output blit.

The native bridge creates a DX12 device on the same adapter as Unity’s DX11 device. Three shared NT-handle textures carry the inputs; another carries the output. A shared fence orders DX11 copies before DX12 dispatch. The bridge waits for the DX12 completion fence before copying output back and reusing its command allocator. This initial implementation prioritises correct ordering; it can stall the render thread.

Disabling FSR queues a render-thread cleanup event to release the GPU context. Retry uses the same ordering before clearing the fault state. Cleanup refuses to destroy resources while a timed-out GPU submission is still pending.

Provider enumeration and an explicit version override require FSR 4.1.1. The context is queried again after creation to confirm the provider. There is no FSR 3 backend in this branch. Unsupported inputs or a detected failure restore original game AA. The frame that fails in the render callback receives an unfiltered colour copy, so the queued Unity blit has valid input.

Jitter comes from the actual URP projection matrix, with DX11 Y inversion. Motion vectors are scaled by the negative input width/height. Frame time is supplied in milliseconds. Camera cuts, room loading and resolution changes reset history.

## Native test

```sh
bash scripts/build-native.sh
zig c++ -target x86_64-windows-gnu -O2 -Wno-nullability-completeness \
  native/Bridge.cpp native/TestBridge.cpp \
  -Inative/fsr4/api/include -Inative/fsr4/upscalers/include \
  -ld3d11 -ld3d12 -ldxgi -ldxguid -o .build/fsr4/bridge_test.exe
cd .build/fsr4
wine bridge_test.exe
wine bridge_test.exe full
```

Use the game’s Proton prefix with native DXVK/D3D12 libraries. The tests dispatch six frames, reset history, release and recreate the context, change the input colour and read the DX11 output back. `full` tests 5120×2160. Passing proves real provider execution and texture round trips; game integration needs a separate playtest.
