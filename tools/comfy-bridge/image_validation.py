"""Bounded PNG decoding with default alpha preservation and explicit replacement."""
import hashlib
import io
import warnings
from pathlib import Path
from PIL import Image

MAX_BYTES = 64 * 1024 * 1024
MAX_PIXELS = 16 * 1024 * 1024


class BridgeError(Exception):
    def __init__(self, code, message):
        self.code = code
        super().__init__(message)


def workspace_path(root, path):
    root = Path(root).resolve()
    candidate = Path(path)
    candidate = (candidate if candidate.is_absolute() else root / candidate).resolve()
    if not candidate.is_relative_to(root) or candidate == root:
        raise BridgeError("PATH_NOT_ALLOWED", "Path escapes workspace_root")
    return candidate


def decode(data, max_pixels=MAX_PIXELS):
    if len(data) > MAX_BYTES or not data.startswith(b'\x89PNG\r\n\x1a\n'):
        raise BridgeError("INVALID_PNG", "Expected PNG, maximum 64 MiB")
    try:
        with warnings.catch_warnings():
            warnings.simplefilter('error', Image.DecompressionBombWarning)
            with Image.open(io.BytesIO(data)) as im:
                if im.format != 'PNG' or im.width * im.height > min(max_pixels, MAX_PIXELS):
                    raise BridgeError("INVALID_PNG_DIMENSIONS", "Pixel limit exceeded")
                if getattr(im, 'n_frames', 1) != 1:
                    raise BridgeError("INVALID_PNG", "Animated PNG is unsupported")
                im.verify()
            with Image.open(io.BytesIO(data)) as im:
                im.load()
                return im.convert('RGBA')
    except BridgeError:
        raise
    except Exception as exc:
        raise BridgeError("INVALID_PNG", str(exc)) from exc


def read_png(path):
    with Path(path).open('rb') as stream:
        data = stream.read(MAX_BYTES + 1)
    decode(data)
    return data


def encode(image):
    out = io.BytesIO()
    image.save(out, format='PNG')
    return out.getvalue()


def prepare(source, candidate, restore_alpha=False, allow_alpha_change=False):
    if restore_alpha and allow_alpha_change:
        raise BridgeError("INVALID_ALPHA_MODE", "Alpha restoration and replacement are mutually exclusive")
    original, generated = decode(source), decode(candidate)
    if generated.size != original.size:
        raise BridgeError("INVALID_PNG_DIMENSIONS", f"Expected {original.size}, got {generated.size}")
    alpha = original.getchannel('A')
    if restore_alpha:
        generated.putalpha(alpha)
    elif not allow_alpha_change and generated.getchannel('A').tobytes() != alpha.tobytes():
        raise BridgeError("INVALID_PNG", "Alpha changed; choose --restore-source-alpha or --allow-alpha-change explicitly")
    return encode(generated)


def mock_edit(source):
    original = decode(source)
    r, g, b, a = original.split()
    return encode(Image.merge('RGBA', (r.point(lambda v: 255-v), b, g, a)))


def sha(data):
    return hashlib.sha256(data).hexdigest()
