"""Wrap our compiled HLSL in the already validated Unity 2022.3 compute schema.

Usage: prepare_bridge_bundle.py template_bundle shader.dxbc output_bundle
The resulting bundle contains only our compute program and the Unity manifest.
"""
from pathlib import Path
import sys
import UnityPy

base, program, output = map(Path, sys.argv[1:])
env = UnityPy.load(str(base))
shader = next(o for o in env.objects if o.type.name == 'ComputeShader')
manifest = next(o for o in env.objects if o.type.name == 'AssetBundle')
data = shader.read_typetree()
data['m_Name'] = 'belmont_fsr4_bridge_pack'
variant = data['variants'][0]
kernel = variant['kernels'][0]
kernel.update(name='CS', variantIndices=[('', 0)], globalKeywords=[], localKeywords=[], dynamicKeywords=[])
compiled = kernel['uniqueVariants'][0]
compiled['code'] = list(program.read_bytes())
compiled['threadGroupSize'] = [8, 8, 1]
compiled['cbVariantIndices'] = []
def binding(name, slot, dimension=-1):
    return dict(name=name, generatedName='', bindPoint=slot, samplerBindPoint=-1, texDimension=dimension)
compiled['cbs'] = []
compiled['textures'] = [binding(name, slot, 2) for slot, name in enumerate(('r_color', 'r_depth', 'r_motion'))]
compiled['outBuffers'] = [binding(name, slot, 2) for slot, name in enumerate(('rw_color', 'rw_depth', 'rw_motion'))]
compiled['inBuffers'] = []
compiled['builtinSamplers'] = []
kernel['uniqueVariants'] = [compiled]
variant['kernels'] = [kernel]
variant['constantBuffers'] = []
shader.save_typetree(data)
data = manifest.read_typetree()
info = next(info for _, info in data['m_Container'] if info['asset']['m_PathID'] == shader.path_id)
info.update(preloadIndex=0, preloadSize=1)
data.update(m_Name='belmont_fsr4_bridge_pack', m_AssetBundleName='belmont_fsr4_bridge_pack', m_PreloadTable=[info['asset']], m_Container=[('assets/belmont_fsr4_bridge_pack.compute', info)])
manifest.save_typetree(data)
asset = env.assets[0]
asset.objects = {o.path_id: o for o in (shader, manifest)}
used = []
for o in asset.objects.values():
    if o.serialized_type not in used: used.append(o.serialized_type)
    o.type_id = used.index(o.serialized_type)
asset.types = used
asset.mark_changed()
output.write_bytes(next(iter(env.files.values())).save(packer='lz4'))
check = UnityPy.load(str(output))
assert len(check.objects) == 2 and len(check.container) == 1
print(f'{output}: one custom bridge_pack compute shader; {output.stat().st_size} bytes')
