import base64
import json
import time
from local_http import LocalHttp
from image_validation import BridgeError, sha, decode


class VroidClient:
    def __init__(self, url):
        self.http = LocalHttp(url, timeout=100)
        self.sequence = 0

    def rpc(self, method, params=None):
        self.sequence += 1
        payload = {'jsonrpc':'2.0', 'id':self.sequence, 'method':method}
        if params is not None:
            payload['params'] = params
        result = self.http.request(payload=payload)
        if not result or 'error' in result or result.get('id') != self.sequence:
            raise BridgeError('VROID_RPC_ERROR', str(result))
        return result['result']

    def initialize(self):
        return self.rpc('initialize', {'protocolVersion':'2025-06-18', 'capabilities':{},
                                      'clientInfo':{'name':'vroid-comfy-bridge','version':'0.1.0'}})

    def call(self, name, **arguments):
        result = self.rpc('tools/call', {'name':name,'arguments':arguments})
        text = '\n'.join(c.get('text','') for c in result.get('content',[]) if c.get('type') == 'text')
        if result.get('isError'):
            raise BridgeError('VROID_TOOL_ERROR', text)
        try:
            return json.loads(text)
        except ValueError:
            return text

    def stable_preview(self, timeout=20):
        # Only call after a blocking mutation or camera command has settled.
        end, previous, equal = time.monotonic()+timeout, None, 0
        while time.monotonic() < end:
            result = self.rpc('tools/call', {'name':'screenshot','arguments':{'mode':'inline'}})
            if result.get('isError'):
                raise BridgeError('VROID_TOOL_ERROR', str(result))
            images = [c for c in result.get('content',[]) if c.get('type') == 'image']
            if not images:
                raise BridgeError('VROID_TOOL_ERROR', 'No preview image returned')
            data = base64.b64decode(images[0]['data'], validate=True)
            if all(lo == hi for lo, hi in decode(data).convert('RGB').getextrema()):
                raise BridgeError('EMPTY_PREVIEW', 'Preview is a solid frame; ensure the app has rendered its viewport')
            digest = sha(data)
            equal = equal+1 if digest == previous else 1
            previous = digest
            if equal >= 3:
                return data
        raise BridgeError('TIMEOUT', 'Preview did not stabilize in three consecutive frames')
