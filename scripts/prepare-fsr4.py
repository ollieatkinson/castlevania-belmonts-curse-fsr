"""Fetch the unmodified AMD FSR SDK 2.3 upscaler runtime."""
import hashlib
from pathlib import Path
import urllib.request

url = "https://raw.githubusercontent.com/GPUOpen-LibrariesAndSDKs/FidelityFX-SDK/v2.3.0/Kits/FidelityFX/signedbin/amd_fidelityfx_upscaler_dx12.dll"
expected = "d0dcccc74a43c44ba435b7a369b456e0970d8a4464e4bd683119b374f2c9fb46"
path = Path(".build/fsr4/amd_fidelityfx_upscaler_dx12.dll")
path.parent.mkdir(parents=True, exist_ok=True)
if not path.exists() or hashlib.sha256(path.read_bytes()).hexdigest() != expected:
    with urllib.request.urlopen(url) as response:
        data = response.read()
    if hashlib.sha256(data).hexdigest() != expected:
        raise SystemExit("AMD SDK checksum mismatch")
    path.write_bytes(data)
print("AMD FSR SDK 2.3 runtime: checksum verified")
