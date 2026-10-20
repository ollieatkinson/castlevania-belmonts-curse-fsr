# Testing

Version: 1.0.0.

Test machine: RX 7900 XTX, Proton Experimental, DXVK and vkd3d-proton 3.1.0. Game: Steam build 25975867 / 1.1.3 / Unity 2022.3.62f2.

- Managed Release build: no warnings or errors.
- Native tests select and query the FSR 4.1.1 provider, dispatch six frames at 1280×720 and 5120×2160, release/recreate the GPU context, reset history and read changed output back through DX11.
- The actual game loads an existing save in Notre Dame and dispatches FSR 4.1.1 at 5120×2160 with full-resolution depth. Captured gameplay frames render the scene, character and HUD correctly. Sustained dispatch has no reported FSR failure.
- A temporary gamepad test moves Rose from x=419.87 to x=420.73. Rendering and FSR dispatch continue after movement.
- The native Video menu displays FSR 4.1.1 Native AA and its sharpening control. Selecting SMAA disables FSR and releases its GPU context; selecting FSR again recreates the context and resumes dispatch after closing the menu.
- Game saves and player preferences match their pre-test hashes. The temporary test helper is excluded from the packages and removed after testing.
- Plugin and standalone packages pass layout validation. AMD's runtime checksum is verified before packaging.

Warm bridge calls took approximately 7–8 ms at 5120×2160 on this machine. These include synchronisation and are not total game frame times. Motion quality needs longer player comparison; the captured samples do not establish that all ghosting is gone. Windows and other GPUs have not been tested.
