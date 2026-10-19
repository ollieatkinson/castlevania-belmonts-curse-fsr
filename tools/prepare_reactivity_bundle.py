"""Wrap our compiled HLSL in the already validated Unity 2022.3 compute schema.

Usage: prepare_reactivity_bundle.py base_fsr_bundle shader.dxbc output_bundle
The resulting bundle contains only our compute program and the Unity manifest.
"""
from pathlib import Path
import sys
import UnityPy

base, program, output = map(Path, sys.argv[1:])
env = UnityPy.load(str(base))
shader = next(o for o in env.objects if o.type.name == 'ComputeShader' and o.read_typetree()['m_Name'] == 'ffx_fsr3upscaler_autogen_reactive_pass')
manifest = next(o for o in env.objects if o.type.name == 'AssetBundle')
data = shader.read_typetree()
data['m_Name'] = 'belmont_fsr31_reactivity'
variant = data['variants'][0]
kernel = variant['kernels'][0]
kernel.update(name='CS', variantIndices=[('', 0)], globalKeywords=[], localKeywords=[], dynamicKeywords=[])
compiled = kernel['uniqueVariants'][0]
compiled['code'] = list(program.read_bytes())
compiled['threadGroupSize'] = [8, 8, 1]
compiled['cbVariantIndices'] = [0]
def binding(name, slot, dimension=-1):
    return dict(name=name, generatedName='', bindPoint=slot, samplerBindPoint=-1, texDimension=dimension)
compiled['cbs'] = [binding('cbFsr31Reactivity', 0)]
compiled['textures'] = [binding(name, slot, 2) for slot, name in enumerate(('r_current_color', 'r_previous_color', 'r_motion'))]
compiled['outBuffers'] = [binding('rw_reactive', 0, 2)]
compiled['inBuffers'] = []
compiled['builtinSamplers'] = [dict(sampler=85, bindPoint=0)]
kernel['uniqueVariants'] = [compiled]
variant['kernels'] = [kernel]
variant['constantBuffers'] = [dict(name='cbFsr31Reactivity', byteSize=32, params=[dict(name=name, type=0, offset=offset, arraySize=0, rowCount=1, colCount=4) for name, offset in [('ReactivityParams', 0), ('HistoryParams', 16)]])]
shader.save_typetree(data)
data = manifest.read_typetree()
info = next(info for _, info in data['m_Container'] if info['asset']['m_PathID'] == shader.path_id)
info.update(preloadIndex=0, preloadSize=1)
data.update(m_Name='belmont_fsr31_reactivity', m_AssetBundleName='belmont_fsr31_reactivity', m_PreloadTable=[info['asset']], m_Container=[('assets/belmont_fsr31_reactivity.compute', info)])
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
print(f'{output}: one custom reactivity compute shader; {output.stat().st_size} bytes')
