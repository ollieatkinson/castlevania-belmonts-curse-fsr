"""Extract licensed FSR3 compute programs and retarget identical Unity schemas.

Requires UnityPy; input is the pinned FPSDoctor shader bundle. No game assets are
used. Only AMD/Nico de Poel's FSR3 compute programs survive in the output.
"""
from pathlib import Path
import hashlib,sys
import UnityPy
from UnityPy.helpers.Tpk import get_typetree_node
from UnityPy.helpers.UnityVersion import UnityVersion

source, output = map(Path, sys.argv[1:3])
env = UnityPy.load(str(source))
version = '2022.3.62f2'
asset = env.assets[0]
# Converting only headers is safe here because all retained serialized schemas
# are identical. Assert that fact rather than ignoring differences.
for cid in (72,142):
 def shape(v):
  return [(n.m_Type,n.m_Name,n.m_ByteSize,n.m_MetaFlag) for n in get_typetree_node(cid,UnityVersion.from_str(v)).traverse()]
 assert shape(asset.unity_version)==shape(version),f'Unsupported schema: {cid}'
kept={}
for obj in env.objects:
 if obj.type.name=='ComputeShader':
  data=obj.read_typetree()
  if data['m_Name'].startswith('ffx_fsr3upscaler_'):
   assert all(v['targetRenderer']==2 for v in data['variants'])
   kept[obj.path_id]=obj
bundle=next(o for o in env.objects if o.type.name=='AssetBundle')
data=bundle.read_typetree()
container=[(name,info) for name,info in data['m_Container'] if info['asset']['m_PathID'] in kept]
preload=[]
for _,info in container:
 info['preloadIndex']=len(preload);info['preloadSize']=1;preload.append(info['asset'])
data['m_PreloadTable']=preload;data['m_Container']=container
data['m_Name']=data['m_AssetBundleName']='belmont_fsr31_nativeaa'
bundle.save_typetree(data)
kept[bundle.path_id]=bundle
asset.objects=kept
used=[]
for obj in kept.values():
 typ=obj.serialized_type
 if typ not in used:used.append(typ)
 obj.type_id=used.index(typ)
asset.types=used
asset.set_version(version)
for item in env.files.values():item.version_engine=version
asset.mark_changed()
output.parent.mkdir(parents=True,exist_ok=True)
output.write_bytes(next(iter(env.files.values())).save(packer='lz4'))
check=UnityPy.load(str(output))
assert len(check.objects)==12
assert len(check.container)==11
assert all(o.type.name in ('ComputeShader','AssetBundle') for o in check.objects)
print(f'{output}: 11 FSR3 compute shaders, Unity {version}, SHA256 {hashlib.sha256(output.read_bytes()).hexdigest()}')
