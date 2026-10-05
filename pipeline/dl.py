#!/usr/bin/env python3
"""Tiny DreamLayer Agent API client for the jam.

Every call is logged to art/log.jsonl (prompt, operation, execution id, output file),
which is the source for the "how DreamLayer was used" section of the itch page.
Idempotency keys are derived from the request body, so re-running the same command
replays the original job instead of paying for it twice.

  python3 tools/dl.py balance
  python3 tools/dl.py caps
  python3 tools/dl.py gen    NAME "prompt" [--aspect 16:9]
  python3 tools/dl.py edit   NAME INPUT.png "prompt" [--aspect 16:9]
  python3 tools/dl.py cutout NAME INPUT.png
  python3 tools/dl.py sprite NAME INPUT.png (--action run | --anim "...") [--frames 12]
                             [--size 512] [--mode loop|once] [--keep-bg] --max-credits N
  python3 tools/dl.py status EXECUTION_ID [NAME]
"""
import argparse
import hashlib
import json
import mimetypes
import os
import random
import ssl
import sys
import time
import urllib.error
import urllib.request
import uuid
from pathlib import Path

API = "https://api.dreamlayer.io"


def _ssl_context() -> ssl.SSLContext:
    # python.org builds on macOS ship without CA certificates; fall back to certifi or the system bundle.
    try:
        import certifi

        return ssl.create_default_context(cafile=certifi.where())
    except ImportError:
        return ssl.create_default_context(cafile="/etc/ssl/cert.pem")


SSL_CTX = _ssl_context()
ROOT = Path(__file__).resolve().parent.parent
RAW = ROOT / "art" / "raw"
LOG = ROOT / "art" / "log.jsonl"


def api_key() -> str:
    key = os.environ.get("DREAMLAYER_API_KEY", "").strip()
    if not key:
        p = Path.home() / ".dreamlayer_key"
        if p.exists():
            key = p.read_text().strip()
    if not key:
        sys.exit("No API key: set DREAMLAYER_API_KEY or save it to ~/.dreamlayer_key")
    return key


def request(method, path, body=None, headers=None, raw=False, timeout=120):
    h = {"Authorization": f"Bearer {api_key()}", "DreamLayer-Version": "1"}
    h.update(headers or {})
    data = None
    if body is not None and not isinstance(body, (bytes, bytearray)):
        data = json.dumps(body).encode()
        h["Content-Type"] = "application/json"
    elif body is not None:
        data = body
    url = path if path.startswith("http") else API + path
    for attempt in range(10):
        req = urllib.request.Request(url, data=data, headers=h, method=method)
        try:
            with urllib.request.urlopen(req, timeout=timeout, context=SSL_CTX) as r:
                payload = r.read()
                return payload if raw else json.loads(payload or b"{}")
        except urllib.error.HTTPError as e:
            detail = e.read().decode(errors="replace")[:800]
            # Rate limits and server hiccups are retryable; back off with jitter.
            if e.code in (429, 500, 502, 503, 504) and attempt < 9:
                time.sleep(min(60, 2 ** attempt) + random.random() * 2)
                continue
            sys.exit(f"HTTP {e.code} on {method} {path}: {detail}")
        except (urllib.error.URLError, TimeoutError) as e:
            if attempt < 9:
                time.sleep(min(60, 2 ** attempt))
                continue
            sys.exit(f"network error on {method} {path}: {e}")


def log(entry):
    LOG.parent.mkdir(parents=True, exist_ok=True)
    entry["ts"] = time.strftime("%Y-%m-%dT%H:%M:%S")
    with LOG.open("a") as f:
        f.write(json.dumps(entry) + "\n")


def upload(path: Path) -> str:
    boundary = uuid.uuid4().hex
    ctype = mimetypes.guess_type(path.name)[0] or "image/png"
    body = (
        f"--{boundary}\r\nContent-Disposition: form-data; name=\"file\"; filename=\"{path.name}\"\r\n"
        f"Content-Type: {ctype}\r\n\r\n"
    ).encode() + path.read_bytes() + f"\r\n--{boundary}--\r\n".encode()
    res = request("POST", "/v1/input-assets", body, {"Content-Type": f"multipart/form-data; boundary={boundary}"})
    return res["input_asset_id"]


def idem_key(name: str, body: dict, salt: str = "") -> str:
    # The uploaded asset id is fresh on every upload, so it is left out of the key;
    # callers pass a hash of the input file as the salt instead.
    stable = {k: v for k, v in body.items() if k != "input_asset_id"}
    digest = hashlib.sha256((json.dumps(stable, sort_keys=True) + salt).encode()).hexdigest()[:16]
    return f"lamplighter-{name}-{digest}"[:120]


def execute(name: str, body: dict, salt: str = "") -> dict:
    # DL_SALT forces a fresh job when an earlier one with the same request was cancelled.
    salt += os.environ.get("DL_SALT", "")
    res = request("POST", "/v1/execute", body, {"Idempotency-Key": idem_key(name, body, salt), "Accept": "application/json"})
    exec_id = res["execution_id"]
    print(f"[{name}] execution {exec_id} ({res.get('status')})", flush=True)
    return wait(exec_id, name, body)


def wait(exec_id: str, name: str, body: dict | None = None, timeout_s: int = 900) -> dict:
    start = time.time()
    last = None
    while time.time() - start < timeout_s:
        st = request("GET", f"/v1/executions/{exec_id}")
        status = st["status"]
        if status != last:
            print(f"[{name}] {status}", flush=True)
            last = status
        if status == "completed":
            return finish(exec_id, name, st, body)
        if status in ("failed", "cancelled"):
            err = st.get("error") or (st.get("image_job") or {}).get("sanitized_error")
            log({"name": name, "execution_id": exec_id, "status": status, "error": err, "request": body})
            sys.exit(f"[{name}] {status}: {json.dumps(err)}")
        if status == "needs_input":
            q = st.get("question")
            log({"name": name, "execution_id": exec_id, "status": status, "question": q, "request": body})
            sys.exit(f"[{name}] DreamLayer asked a question: {json.dumps(q)}")
        time.sleep(6 + random.random() * 2)
    sys.exit(f"[{name}] still running after {timeout_s}s; resume with: python3 tools/dl.py status {exec_id} {name}")


def finish(exec_id: str, name: str, st: dict, body: dict | None) -> dict:
    asset = st["image_job"]["finished_assets"][0]
    ext = ".zip" if asset.get("content_type") == "application/zip" else ".png"
    RAW.mkdir(parents=True, exist_ok=True)
    out = RAW / f"{name}{ext}"
    out.write_bytes(request("GET", asset["download_url"], raw=True, timeout=300))
    print(f"[{name}] saved {out.relative_to(ROOT)} ({asset.get('width')}x{asset.get('height')})", flush=True)
    log({"name": name, "execution_id": exec_id, "status": "completed", "out": str(out.relative_to(ROOT)), "request": body,
         "width": asset.get("width"), "height": asset.get("height")})
    return {"out": out, "status": st}


def main():
    ap = argparse.ArgumentParser()
    sub = ap.add_subparsers(dest="cmd", required=True)
    sub.add_parser("balance")
    sub.add_parser("caps")
    g = sub.add_parser("gen")
    g.add_argument("name")
    g.add_argument("prompt")
    g.add_argument("--aspect", default="1:1")
    g.add_argument("--salt", default="")
    e = sub.add_parser("edit")
    e.add_argument("name")
    e.add_argument("input")
    e.add_argument("prompt")
    e.add_argument("--aspect", default=None)
    e.add_argument("--salt", default="")
    c = sub.add_parser("cutout")
    c.add_argument("name")
    c.add_argument("input")
    s = sub.add_parser("sprite")
    s.add_argument("name")
    s.add_argument("input")
    s.add_argument("--action", choices=["walk", "run", "idle"])
    s.add_argument("--anim")
    s.add_argument("--frames", type=int, default=12)
    s.add_argument("--size", type=int, default=512)
    s.add_argument("--mode", choices=["loop", "once"])
    s.add_argument("--keep-bg", action="store_true")
    s.add_argument("--max-credits", type=float, required=True)
    t = sub.add_parser("status")
    t.add_argument("execution_id")
    t.add_argument("name", nargs="?", default="resumed")
    a = ap.parse_args()

    if a.cmd == "balance":
        print(json.dumps(request("GET", "/v1/balance?pricing=current"), indent=2))
    elif a.cmd == "caps":
        print(json.dumps(request("GET", "/v1/capabilities"), indent=2))
    elif a.cmd == "gen":
        execute(a.name, {"prompt": a.prompt, "operation": "text_to_image", "aspect_ratio": a.aspect, "max_credits": 1}, a.salt)
    elif a.cmd == "edit":
        asset = upload(Path(a.input))
        body = {"prompt": a.prompt, "operation": "image_to_image", "input_asset_id": asset, "max_credits": 1}
        if a.aspect:
            body["aspect_ratio"] = a.aspect
        # The asset id changes on every upload, so key on the file contents instead.
        salt = hashlib.sha256(Path(a.input).read_bytes()).hexdigest() + a.salt
        execute(a.name, body, salt)
    elif a.cmd == "cutout":
        asset = upload(Path(a.input))
        execute(a.name, {"prompt": "remove the background", "operation": "background_remove", "input_asset_id": asset, "max_credits": 1},
                hashlib.sha256(Path(a.input).read_bytes()).hexdigest())
    elif a.cmd == "sprite":
        if bool(a.action) == bool(a.anim):
            sys.exit("pass exactly one of --action or --anim")
        opts = {"frame_count": a.frames, "frame_size": a.size}
        if a.action:
            opts["action"] = a.action
        else:
            opts["animation_prompt"] = a.anim
        if a.mode:
            opts["animation_mode"] = a.mode
        if a.keep_bg:
            opts["background"] = "keep"
        asset = upload(Path(a.input))
        execute(a.name, {"operation": "sprite_sheet", "input_asset_id": asset, "options": opts, "max_credits": a.max_credits},
                hashlib.sha256(Path(a.input).read_bytes()).hexdigest())
    elif a.cmd == "status":
        wait(a.execution_id, a.name)


if __name__ == "__main__":
    main()
