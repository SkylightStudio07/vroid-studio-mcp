"""Verify native hair outline guards, rendering, isolation and saved persistence."""
import argparse
import json
from pathlib import Path
import sys
import uuid

sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'comfy-bridge'))
from vroid_client import VroidClient


def main():
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('--model',required=True)
    p.add_argument('--output',default='run/qa/hair-appearance')
    args=p.parse_args()
    root=Path(args.output).resolve()/uuid.uuid4().hex
    root.mkdir(parents=True)
    c=VroidClient('http://127.0.0.1:17891/mcp')
    checks=[]

    def check(name,condition):
        checks.append({'name':name,'passed':bool(condition)})
        (root/'result.json').write_text(json.dumps(checks,indent=2))
        assert condition,name
        print('PASS',name,flush=True)

    def rejected(name,**kwargs):
        return bool(c.rpc('tools/call',{'name':name,'arguments':kwargs}).get('isError'))

    def hair():
        listing=c.call('hair_list')
        return {'groups':{g['group_id']:{'parameters':g['native_parameters'],'visible':g['visible'],
                     'strands':{s['engine_strand_id']:s['visible'] for s in g['strands']}} for g in listing['groups']},
                'strands':{s['engine_strand_id']:{k:v for k,v in c.call('hair_get',strand_id=s['strand_id']).items()
                          if k in ('points','native_parameters')} for g in listing['groups'] for s in g['strands']}}

    if c.call('state')['in_edit']:
        raise RuntimeError('Dedicated app must be at model selection; refusing to discard a session')
    for name,kwargs in [('hair_appearance',{}),('hair_outline',{'width':0.04,'expected_revision':0}),
                        ('hair_outline_color',{'material_id':'invalid','color':'#7A7268','expected_revision':0})]:
        check(name+'_no_model_guard',rejected(name,**kwargs))
    c.call('open',path=str(Path(args.model).resolve()))
    before=c.call('hair_appearance')
    native=hair();face=c.call('face_bridge_status')
    material=before['materials'][0]['material_id'];revision=before['revision']
    for value in ('NaN','Infinity',-0.01,1.01):
        check('invalid_width_'+str(value),rejected('hair_outline',width=value,expected_revision=revision))
    check('stale_width_revision',rejected('hair_outline',width=0.04,expected_revision=-1))
    check('invalid_material',rejected('hair_outline_color',material_id='invalid',color='#7A7268',expected_revision=revision))
    check('invalid_color',rejected('hair_outline_color',material_id=material,color='#FFFFFFFF',expected_revision=revision))
    check('stale_color_revision',rejected('hair_outline_color',material_id=material,color='#7A7268',expected_revision=-1))
    check('guards_preserve_appearance_and_revision',c.call('hair_appearance')==before)
    c.call('camera',action='frame',region='head',angle='front')
    preview=c.stable_preview()
    (root/'before.png').write_bytes(preview)
    operations=[]
    operations.append(c.call('hair_outline_color',material_id=material,color='#7A7268',expected_revision=revision))
    operations.append(c.call('hair_outline',width=0.04,expected_revision=c.call('hair_appearance')['revision']))
    after=c.call('hair_appearance')
    (root/'operations.json').write_text(json.dumps(operations,indent=2))
    check('full_model_checkpoints',all(Path(op['checkpoint_path']).is_file() for op in operations))
    check('native_width_readback',abs(after['outline_width']-0.04)<1e-6)
    expected=[int('7A7268'[i:i+2],16)/255 for i in (0,2,4)]+[1]
    actual=next(x for x in after['materials'] if x['material_id']==material)
    actual=next(x['value'] for x in actual['parameters'] if x['key']=='OutlineColor')
    check('native_color_readback',max(abs(x-y) for x,y in zip(expected,actual))<1e-5)
    def other_colors(state):
        return {m['material_id']:{p['key']:p for p in m['parameters'] if p['key']!='OutlineColor'} for m in state['materials']}
    check('other_material_parameters_preserved',other_colors(before)==other_colors(after))
    check('native_geometry_profiles_and_visibility_preserved',native==hair())
    current_face=c.call('face_bridge_status')
    check('face_package_preserved',face['sha256']==current_face['sha256']==current_face['active_sha256'] and
          current_face['active'] and not current_face['runtime_error'])
    for name,kwargs in [('hair_outline',{'width':0.04}),('hair_outline_color',{'material_id':material,'color':'#7A7268'})]:
        revision=c.call('hair_appearance')['revision']
        result=c.call(name,**kwargs,expected_revision=revision)
        check(name+'_same_value_noop',result['changed'] is False and result['revision']==revision)
    c.call('camera',action='frame',region='head',angle='front')
    modified=c.stable_preview();(root/'after.png').write_bytes(modified)
    check('stable_render_changed',modified!=preview)
    model=root/'outlined.vroid'
    c.call('save',path=str(model));c.call('main_menu');c.call('open',path=str(model))
    reopened=c.call('hair_appearance')
    check('save_reopen_native_appearance_exact',{k:v for k,v in reopened.items() if k!='revision'}=={k:v for k,v in after.items() if k!='revision'})
    check('save_reopen_native_hair_exact',native==hair())
    current_face=c.call('face_bridge_status')
    check('save_reopen_face_package_active',current_face['active'] and current_face['sha256']==face['sha256']==current_face['active_sha256'] and not current_face['runtime_error'])
    c.call('camera',action='frame',region='head',angle='front')
    check('save_reopen_preview_exact',modified==c.stable_preview())
    print(root,flush=True)


if __name__=='__main__':
    main()
