# AMD FSR 4.1.1

The API headers are from AMD FidelityFX SDK v2.3.0:
https://github.com/GPUOpen-LibrariesAndSDKs/FidelityFX-SDK/tree/v2.3.0/Kits/FidelityFX
Their AMD MIT notices are retained in each file.

`scripts/prepare-fsr4.py` downloads the unmodified signed upscaler runtime from that tag and verifies SHA-256:
`d0dcccc74a43c44ba435b7a369b456e0970d8a4464e4bd683119b374f2c9fb46`.
The binary redistribution terms are included in LICENSE.md. No AMD binaries are committed to this repository.

The bridge and Unity input-packing shader are project code under the root MIT licence. OptiScaler was consulted as a reference for supported interop APIs; no OptiScaler code is included.

The runtime must enumerate and select a provider reporting 4.1.1. It refuses an FSR 3 provider. Original game AA is retained as the failure path.
