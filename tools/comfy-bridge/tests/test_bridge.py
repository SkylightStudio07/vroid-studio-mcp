import argparse
import io
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from PIL import Image
from image_validation import BridgeError,decode,encode,prepare,mock_edit,workspace_path
from comfy_client import ComfyClient
from local_http import LocalHttp
from bridge import roundtrip


def png(color=(20,40,60,128),size=(8,8)):
    return encode(Image.new('RGBA',size,color))


class ValidationTests(unittest.TestCase):
    def test_roundtrip_pixels(self):
        data=png()
        self.assertEqual(decode(prepare(data,data)).tobytes(),decode(data).tobytes())

    def test_mock_preserves_alpha_and_changes_rgb(self):
        data=png();result=mock_edit(data)
        self.assertNotEqual(decode(result).tobytes(),decode(data).tobytes())
        self.assertEqual(decode(result).getchannel('A').tobytes(),decode(data).getchannel('A').tobytes())

    def test_invalid_png(self):
        for data in (b'not png',png()[:30],png()[:-20]):
            with self.subTest(data=data[:8]),self.assertRaises(BridgeError): decode(data)

    def test_wrong_dimensions(self):
        with self.assertRaisesRegex(BridgeError,'Expected'): prepare(png(),png(size=(4,4)))

    def test_alpha_requires_explicit_option(self):
        with self.assertRaisesRegex(BridgeError,'Alpha changed'): prepare(png(),png((1,2,3,255)))
        result=prepare(png(),png((1,2,3,255)),True)
        self.assertEqual(decode(result).getchannel('A').getextrema(),(128,128))

    def test_pixel_limit(self):
        with self.assertRaises(BridgeError): decode(png(),max_pixels=10)

    def test_alpha_replacement_preserves_candidate_rgba(self):
        candidate=png((10,20,30,0))
        self.assertEqual(decode(prepare(png(),candidate,allow_alpha_change=True)).tobytes(),decode(candidate).tobytes())

    def test_alpha_options_conflict(self):
        with self.assertRaises(BridgeError): prepare(png(),png(),True,True)

    def test_alpha_replacement_still_checks_dimensions(self):
        with self.assertRaises(BridgeError): prepare(png(),png(size=(4,4)),allow_alpha_change=True)

    def test_workspace_escape(self):
        with tempfile.TemporaryDirectory() as root:
            with self.assertRaises(BridgeError): workspace_path(root,'../outside.png')
            self.assertEqual(workspace_path(root,'exports/a.png'),Path(root).resolve()/'exports/a.png')

    def test_url_policy(self):
        for url in ('https://example.com','http://192.168.1.2:8188','http://127.0.0.1@evil.test','file:///etc/passwd'):
            with self.subTest(url=url),self.assertRaises(BridgeError): LocalHttp(url)
        LocalHttp('http://127.0.0.1:8188')


GRAPH={'source':{'class_type':'LoadImage','inputs':{'image':'old.png'}},
       'out':{'class_type':'SaveImage','inputs':{'images':['source',0]}}}


class FakeHttp:
    def __init__(self,job=None): self.job=job; self.calls=[]
    def request(self,path='',**kw):
        self.calls.append((path,kw))
        if path=='/object_info': return {'LoadImage':{},'SaveImage':{}}
        if path=='/prompt': return {'prompt_id':'ours','node_errors':{}}
        if path=='/history/ours':
            return {'somebody-else':{'outputs':{'out':{'images':[{'filename':'wrong.png'}]}}},**({'ours':self.job} if self.job else {})}
        if path.startswith('/view?'): return png()
        if path=='/queue': return {}
        raise AssertionError(path)


class ComfyTests(unittest.TestCase):
    def test_bind_does_not_mutate_workflow(self):
        graph=ComfyClient.bind(GRAPH,{'source_texture':{'node_id':'source','input':'image'}},{'source_texture':'new.png'})
        self.assertEqual(graph['source']['inputs']['image'],'new.png')
        self.assertEqual(GRAPH['source']['inputs']['image'],'old.png')

    def test_bind_rejects_missing_slot_and_ui_workflow(self):
        with self.assertRaises(BridgeError): ComfyClient.bind(GRAPH,{}, {'source_texture':'x'})
        with self.assertRaises(BridgeError): ComfyClient.bind({'nodes':[]},{},{})

    def client(self,job=None,timeout=1):
        c=ComfyClient('http://127.0.0.1:8188',timeout); c.http=FakeHttp(job);return c

    def test_selects_only_submitted_prompt_and_output_node(self):
        c=self.client({'status':{'completed':True},'outputs':{'out':{'images':[{'filename':'ours.png','type':'output','subfolder':''}]}}})
        self.assertEqual(c.run(GRAPH,'out'),png())
        self.assertTrue(any('ours.png' in path for path,_ in c.http.calls))
        self.assertFalse(any('wrong.png' in path for path,_ in c.http.calls))

    def test_failure_is_not_success(self):
        c=self.client({'status':{'status_str':'error','messages':[['execution_error',{'error':'missing model'}]]}})
        with self.assertRaises(BridgeError) as e:c.run(GRAPH,'out')
        self.assertEqual(e.exception.code,'COMFYUI_JOB_FAILED')

    def test_timeout_deletes_only_owned_pending_job(self):
        c=self.client(timeout=0)
        with self.assertRaises(BridgeError) as e:c.run(GRAPH,'out')
        self.assertEqual(e.exception.code,'TIMEOUT')
        self.assertEqual(c.http.calls[-1],('/queue',{'payload':{'delete':['ours']}}))

    def test_cancel_before_submission(self):
        c=self.client()
        with self.assertRaises(BridgeError) as e:c.run(GRAPH,'out',lambda:True)
        self.assertEqual(e.exception.code,'CANCELLED')
        self.assertFalse(any(p=='/prompt' for p,_ in c.http.calls))

    def test_missing_output_rejected(self):
        c=self.client({'status':{'completed':True},'outputs':{}})
        with self.assertRaises(BridgeError):c.run(GRAPH,'out')


class FakeVroid:
    root=None
    instances=[]
    def __init__(self,*_): self.calls=[];self.import_args=None;self.__class__.instances.append(self)
    def initialize(self): return {}
    def stable_preview(self): return png()
    def call(self,name,**kwargs):
        self.calls.append(name)
        if name=='texture_targets':return {'workspace_root':str(self.root)}
        if name=='texture_export':
            p=Path(kwargs['output_path']);p.write_bytes(png())
            return {'texture_path':str(p),'revision':1}
        if name=='texture_import':
            self.import_args=kwargs
            return {'backup_id':'b','revision':2}
        if name=='texture_layer_info':return {'revision':2}
        return {}


class PipelineTests(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory();self.addCleanup(self.temp.cleanup)
        FakeVroid.root=Path(self.temp.name);FakeVroid.instances=[]
        self.config={'workspace_root':self.temp.name,'comfyui_url':'http://127.0.0.1:8188','vroid_mcp_url':'http://127.0.0.1:17891/mcp'}
        self.args=argparse.Namespace(target_id='target',layer_id='layer',mock_comfy=True,apply=False,
            cancel_file=None,region='face',restore_source_alpha=False,save_as=None)

    def test_preview_only_never_imports(self):
        with patch('bridge.VroidClient',FakeVroid): result=roundtrip(self.config,self.args)
        self.assertEqual(result['status'],'preview_only')
        self.assertNotIn('texture_import',FakeVroid.instances[-1].calls)
        self.assertTrue(Path(result['manifest_path']).is_file())

    def test_invalid_result_never_imports(self):
        self.args.apply=True
        with patch('bridge.VroidClient',FakeVroid),patch('bridge.mock_edit',return_value=png(size=(2,2))):
            with self.assertRaises(BridgeError):roundtrip(self.config,self.args)
        self.assertNotIn('texture_import',FakeVroid.instances[-1].calls)
        manifest=json.loads(next(Path(self.temp.name).rglob('manifest.json')).read_text())
        self.assertEqual(manifest['status'],'failed')

    def test_apply_records_backup_and_three_views(self):
        self.args.apply=True
        with patch('bridge.VroidClient',FakeVroid):result=roundtrip(self.config,self.args)
        self.assertTrue(result['applied'])
        self.assertEqual(result['import_result']['backup_id'],'b')
        self.assertTrue((Path(result['manifest_path']).parent/'after-back.png').is_file())
        self.assertEqual(FakeVroid.instances[-1].import_args['alpha_mode'],'preserve')

    def test_explicit_alpha_change_is_forwarded(self):
        self.args.apply=True;self.args.allow_alpha_change=True
        with patch('bridge.VroidClient',FakeVroid),patch('bridge.mock_edit',return_value=png((1,2,3,0))):
            result=roundtrip(self.config,self.args)
        self.assertEqual(result['alpha_mode'],'replace')
        self.assertEqual(FakeVroid.instances[-1].import_args['alpha_mode'],'replace')

    def test_alpha_change_without_option_never_imports(self):
        self.args.apply=True
        with patch('bridge.VroidClient',FakeVroid),patch('bridge.mock_edit',return_value=png((1,2,3,0))):
            with self.assertRaises(BridgeError):roundtrip(self.config,self.args)
        self.assertNotIn('texture_import',FakeVroid.instances[-1].calls)

    def test_post_import_failure_restores_confirmed_revision(self):
        self.args.apply=True
        class FailingVroid(FakeVroid):
            def stable_preview(self):
                if 'texture_import' in self.calls: raise BridgeError('EMPTY_PREVIEW','lost rendering')
                return png()
        with patch('bridge.VroidClient',FailingVroid):
            with self.assertRaises(BridgeError):roundtrip(self.config,self.args)
        self.assertIn('texture_restore',FakeVroid.instances[-1].calls)
        manifest=json.loads(next(Path(self.temp.name).rglob('manifest.json')).read_text())
        self.assertFalse(manifest['applied'])

    def test_lost_import_response_is_explicitly_unconfirmed(self):
        self.args.apply=True
        class LostVroid(FakeVroid):
            def call(self,name,**kwargs):
                if name=='texture_import': raise BridgeError('SERVICE_UNAVAILABLE','connection lost')
                return super().call(name,**kwargs)
        with patch('bridge.VroidClient',LostVroid):
            with self.assertRaises(BridgeError):roundtrip(self.config,self.args)
        manifest=json.loads(next(Path(self.temp.name).rglob('manifest.json')).read_text())
        self.assertTrue(manifest['import_attempted'])
        self.assertIn('unconfirmed',manifest['application_state'])

    def test_offline_comfy_never_imports(self):
        self.args.apply=True;self.args.mock_comfy=False
        self.args.workflow=str(Path(self.temp.name)/'workflow.json')
        self.args.prompt=self.args.reference_image=self.args.mask=None
        Path(self.args.workflow).write_text(json.dumps(GRAPH))
        self.config['bindings']={'source_texture':{'node_id':'source','input':'image'}}
        with patch('bridge.VroidClient',FakeVroid),patch('bridge.ComfyClient.health',side_effect=BridgeError('COMFYUI_UNAVAILABLE','offline')):
            with self.assertRaises(BridgeError):roundtrip(self.config,self.args)
        self.assertNotIn('texture_import',FakeVroid.instances[-1].calls)


if __name__=='__main__':unittest.main()
