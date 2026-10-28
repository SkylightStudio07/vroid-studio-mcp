import ipaddress
import json
import urllib.error
import urllib.parse
import urllib.request
from image_validation import BridgeError, MAX_BYTES


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        raise BridgeError('HTTP_REDIRECT_REJECTED', 'Local services must not redirect requests')


class LocalHttp:
    def __init__(self, url, timeout=30):
        parsed = urllib.parse.urlsplit(url)
        host = parsed.hostname
        try:
            local = host == 'localhost' or ipaddress.ip_address(host).is_loopback
        except ValueError:
            local = False
        if parsed.scheme != 'http' or not local or parsed.username or parsed.password or parsed.query or parsed.fragment:
            raise BridgeError('URL_NOT_ALLOWED', 'Use a loopback HTTP service URL')
        self.url = url.rstrip('/')
        self.timeout = timeout
        self.opener = urllib.request.build_opener(urllib.request.ProxyHandler({}), NoRedirect())

    def request(self, path='', payload=None, data=None, content_type=None, timeout=None, raw=False):
        headers = {'Accept': 'application/json, text/event-stream'}
        if payload is not None:
            data = json.dumps(payload).encode()
            content_type = 'application/json'
        if content_type:
            headers['Content-Type'] = content_type
        req = urllib.request.Request(self.url + path, data=data, headers=headers)
        try:
            with self.opener.open(req, timeout=timeout or self.timeout) as response:
                body = response.read(MAX_BYTES + 1)
                if len(body) > MAX_BYTES:
                    raise BridgeError('RESPONSE_TOO_LARGE', 'HTTP response exceeds 64 MiB')
                if raw:
                    return body
                return json.loads(body) if body else None
        except urllib.error.HTTPError as exc:
            detail = exc.read(8192).decode('utf-8', errors='replace')
            raise BridgeError('HTTP_ERROR', f'HTTP {exc.code}: {detail}') from exc
        except (urllib.error.URLError, TimeoutError, OSError) as exc:
            raise BridgeError('SERVICE_UNAVAILABLE', str(exc)) from exc
        except ValueError as exc:
            raise BridgeError('INVALID_RESPONSE', 'Service did not return JSON') from exc
