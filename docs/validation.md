# Testing

Branch: fsr4-dx12-bridge. Version: 0.4.0-preview.1.

Test machine: RX 7900 XTX, Proton Experimental, DXVK and vkd3d-proton 3.1.0. Game: Steam build 25975867 / 1.1.3 / Unity 2022.3.62f2.

- Managed Release build: no warnings or errors.
- 28 regression checks pass for native AA selection, sharpening, prerequisites and jitter conversion.
- DX11/DX12 shared texture and shared fence creation succeeds on the same adapter.
- Native tests explicitly select and verify the FSR 4.1.1 provider. Six frames at 1280×720 and six at 5120×2160 complete successfully. DX11 readback confirms finite output and a response to changed colour after reset.
- At 5120×2160, warm bridge calls took approximately 6.8–7.1 ms in the synthetic test. The first dispatch also compiled shaders. These are bridge elapsed times, not total game frame times.
- Installed and launched: BepInEx loads the plugin, native runtime and persistent pack shader. Gameplay dispatch and visual acceptance are pending the save test.

Windows and other GPUs have not been tested.
