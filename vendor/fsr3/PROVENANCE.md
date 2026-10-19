# FSR 3.1 provenance

Runtime source: https://github.com/ndepoel/FSR3Unity at commit `71879e71aa72465a79bdf009d7d71f9f7ab83066` (MIT, Nico de Poel). Only FSR3/core runtime classes are included; the built-in-pipeline camera component is not used.

Compiled AMD/Nico de Poel FSR3 compute programs were obtained from MOXY1337's FPSDoctor 0.18.145 package, distributed with the AMD and FSR3Unity MIT licences:
https://gcdn.thunderstore.io/live/repository/packages/MOXY1337-FPSDoctor-0.18.145.zip

Package SHA256: `79a6fe95df15535652592cf07085c35d44433b29e23acacff7e48ae796033f20`
Original shader bundle SHA256: `a4582e10c566f20ced291df639723c1d58f22a3e6d2837221ce776dbf1c3f7a5`

`tools/prepare_fsr3_bundle.py` retains only the eleven FSR3 compute shader objects and bundle manifest, removes the unrelated spatial FSR1 and occlusion shaders, verifies that Unity 6000.0.75f1 and 2022.3.62f2 ComputeShader/AssetBundle schemas are identical, and retargets the manifest/version to the installed game. The compiled DXBC programs are preserved byte for byte. Only DX11 programs are present. The prepare-inputs program was disassembled and uses reversed-depth `atomic_umax` reconstruction.

Local runtime adaptations: nullable directives, Unity Object qualification, managed exception types, primitive IL2CPP texture-data upload, explicit blittable constant-buffer upload, and D3D11 constant-buffer alignment. Shader-holder fields are retained as a plain managed class. Owned shader clones and texture resources use HideAndDontSave and explicit cleanup. Accumulation sharpening is selected on the owned ComputeShader local keyword, not a global keyword. Camera integration uses a validated native-pointer detour because the BepInEx Harmony bridge cannot marshal CameraData by reference. Native URP jitter is preserved and converted from the actual GPU jitter matrix to texture pixel offsets, including DX11 Y inversion. See Unity 2022.3 CameraData.GetGPUProjectionMatrix in https://github.com/Unity-Technologies/Graphics/blob/2022.3/staging/Packages/com.unity.render-pipelines.universal/Runtime/UniversalRenderPipelineCore.cs . Camera integration, lifecycle and safety/fallback logic are local code in src/Fsr31Prototype.cs. No FPSDoctor gameplay or optimization code is included.

Licences: LICENSE.txt and LICENSE_AMD_FidelityFX.txt in this directory. No game assets are included.

Custom reactivity program: shaders/Fsr31Reactivity.hlsl is original project code under the root MIT licence, compiled to DXBC with tools/compile_compute.c via Wine d3dcompiler_47. tools/prepare_reactivity_bundle.py wraps that program in the same verified Unity compute schema, replacing all program/binding/constant metadata. assets/belmont_fsr31_reactivity contains only this custom program and a bundle manifest. It is separate from the eleven licensed FSR kernels. tools/test_reactivity.c exercises the exact embedded DXBC on DX11/DXVK.
