# Testing

Version 0.3.0 uses the rendering code tested in 0.3.0-preview.2. Tested with Steam build 25975867 (game 1.1.3), Unity 2022.3.62f2, Proton Experimental and an RX 7900 XTX at 5120×2160.

- Release build: no warnings or errors.
- 28 regression checks cover native AA selection, sharpening, prerequisites and jitter coordinates.
- Six D3D11/DXVK GPU checks cover the embedded anti-ghosting shader.
- Plugin and standalone packages pass layout validation; bundled dependency hashes are verified.
- Live logs confirm FSR dispatch with full-resolution depth, no missing-depth warnings and no FSR failures in the captured session.
- In-game AA switching, sharpening and anti-ghosting changes were observed in the logs and saved config. The player compared moving characters with FXAA and SMAA and accepted the result.

Some temporal trails can remain. Windows, other GPUs and other game builds have not been tested.
