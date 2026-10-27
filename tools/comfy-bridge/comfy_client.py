import copy
import json
import time
import uuid
from urllib.parse import urlencode
from local_http import LocalHttp
from image_validation import BridgeError, read_png, decode


class ComfyClient:
    def __init__(self, url, timeout=900):
        self.http = LocalHttp(url)
        self.timeout = timeout
        self.prompt_id = None

    def health(self):
        try:
            return self.http.request('/system_stats')
        except BridgeError as exc:
            raise BridgeError('COMFYUI_UNAVAILABLE', str(exc)) from exc

    def upload(self, path):
        data = read_png(path)
        boundary = uuid.uuid4().hex
        filename = 'vroid-' + uuid.uuid4().hex + '.png'
        body = (f'--{boundary}\r\nContent-Disposition: form-data; name="image"; filename="{filename}"\r\n'
                'Content-Type: image/png\r\n\r\n').encode() + data + f'\r\n--{boundary}--\r\n'.encode()
        result = self.http.request('/upload/image', data=body, content_type='multipart/form-data; boundary='+boundary)
        name = result['name']
        subfolder = result.get('subfolder','')
        return subfolder + '/' + name if subfolder else name

    @staticmethod
    def bind(workflow, bindings, values):
        graph = copy.deepcopy(workflow)
        if not isinstance(graph, dict) or not graph or any(not isinstance(n,dict) or 'class_type' not in n or 'inputs' not in n for n in graph.values()):
            raise BridgeError('COMFYUI_WORKFLOW_INVALID', 'Expected API format workflow, not UI format')
        for name, value in values.items():
            if value is None:
                continue
            binding = bindings.get(name)
            if not binding or binding.get('node_id') not in graph or binding.get('input') not in graph[binding['node_id']]['inputs']:
                raise BridgeError('COMFYUI_WORKFLOW_INVALID', f'Missing/invalid binding: {name}')
            graph[binding['node_id']]['inputs'][binding['input']] = value
        return graph

    def run(self, workflow, output_node, cancelled=lambda:False, submitted=lambda _:None):
        nodes = self.http.request('/object_info')
        missing = sorted({n['class_type'] for n in workflow.values()} - nodes.keys())
        if missing or output_node not in workflow:
            raise BridgeError('COMFYUI_WORKFLOW_INVALID', f'Missing nodes {missing} or invalid output_node')
        if cancelled():
            raise BridgeError('CANCELLED', 'Cancelled before submission')
        try:
            response = self.http.request('/prompt', payload={'prompt':workflow,'client_id':uuid.uuid4().hex})
        except BridgeError as exc:
            if exc.code == 'HTTP_ERROR':
                raise BridgeError('COMFYUI_WORKFLOW_INVALID', str(exc)) from exc
            raise
        if not response.get('prompt_id') or response.get('node_errors'):
            raise BridgeError('COMFYUI_WORKFLOW_INVALID', json.dumps(response))
        self.prompt_id = response['prompt_id']
        submitted(self.prompt_id)
        end = time.monotonic()+self.timeout
        while time.monotonic() < end:
            if cancelled():
                self.cancel_pending()
                raise BridgeError('CANCELLED', 'Import skipped; an already running job may finish on ComfyUI')
            history = self.http.request('/history/'+self.prompt_id, timeout=min(30,max(.1,end-time.monotonic())))
            job = history.get(self.prompt_id)
            if job:
                status = job.get('status',{})
                if status.get('status_str') == 'error' or any(m[0] in ('execution_error','execution_interrupted') for m in status.get('messages',[])):
                    raise BridgeError('COMFYUI_JOB_FAILED', json.dumps(status))
                if status.get('completed'):
                    images = job.get('outputs',{}).get(output_node,{}).get('images',[])
                    if len(images) != 1:
                        raise BridgeError('COMFYUI_JOB_FAILED', 'Configured output must produce exactly one image')
                    image = images[0]
                    if image.get('type') not in ('output','temp'):
                        raise BridgeError('COMFYUI_JOB_FAILED', 'Unexpected output storage type')
                    data = self.http.request('/view?'+urlencode({k:image.get(k,'') for k in ('filename','subfolder','type')}), raw=True)
                    decode(data)
                    return data
            time.sleep(min(.5,max(0,end-time.monotonic())))
        self.cancel_pending()
        raise BridgeError('TIMEOUT', 'ComfyUI deadline exceeded; import skipped')

    def cancel_pending(self):
        # Never interrupt the shared server globally or delete somebody else's queue entries.
        if self.prompt_id:
            try:
                self.http.request('/queue', payload={'delete':[self.prompt_id]})
            except BridgeError:
                pass
