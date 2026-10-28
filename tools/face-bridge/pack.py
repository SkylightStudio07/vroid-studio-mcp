"""Assemble Blender-baked RGB and alpha through ComfyUI into one embedded package."""
import argparse,base64,json,sys
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'comfy-bridge'))
from comfy_client import ComfyClient

parser=argparse.ArgumentParser();parser.add_argument('export');parser.add_argument('--comfy-url',default='http://127.0.0.1:8191');args=parser.parse_args()
path=Path(args.export).resolve();root=path.parent;manifest=json.loads(path.read_text());package=manifest['package'];cc=ComfyClient(args.comfy_url)
images={}
for texture in manifest['textures']:
    stem=texture['stem']
    if not stem.replace('-','').isalnum():raise ValueError('Invalid texture stem')
    color=cc.upload(root/(stem+'-color.png'));alpha=cc.upload(root/(stem+'-alpha.png'))
    graph={'1':{'class_type':'LoadImage','inputs':{'image':color}},'2':{'class_type':'LoadImage','inputs':{'image':alpha}},
           '3':{'class_type':'ImageToMask','inputs':{'image':['2',0],'channel':'red'}},'4':{'class_type':'InvertMask','inputs':{'mask':['3',0]}},
           '5':{'class_type':'JoinImageWithAlpha','inputs':{'image':['1',0],'alpha':['4',0]}},
           '6':{'class_type':'SaveImage','inputs':{'images':['5',0],'filename_prefix':'vroid-face-bridge-'+stem}}}
    png=cc.run(graph,'6');(root/(stem+'-rgba.png')).write_bytes(png);images[stem]=base64.b64encode(png).decode('ascii')
package['skin_png']=images['skin']
for addition in package['additions']:addition['rgba_png']=images[addition.pop('texture_stem')]
result=root/'face-package.json';result.write_text(json.dumps(package,separators=(',',':')))
print(json.dumps({'package':str(result),'bytes':result.stat().st_size,'additions':len(package['additions'])}),flush=True)
