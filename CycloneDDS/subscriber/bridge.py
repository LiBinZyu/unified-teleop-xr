"""Read-only Cyclone DDS gateway. Serves the viewer and its HTTP event stream."""

import argparse
import ctypes as C
from dataclasses import dataclass
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import ipaddress
import json
import math
import os
from pathlib import Path
import shutil
import sys
from threading import Event, Lock, Thread
import time


ROOT = Path(__file__).resolve().parent
BUNDLE = ROOT / ".web-build" / "viewer.js"

UINT32, ARRAY, POSE, TWIST, JOY = 14, 17, 29, 33, 50
SENSOR_DATA, RELIABLE, TRANSIENT = 0, 1, 2


def channels():
    yield "head/pose", POSE, SENSOR_DATA
    yield "head/velocity", TWIST, SENSOR_DATA
    yield "head/tracking_state", UINT32, TRANSIENT
    yield "eye/pose", POSE, SENSOR_DATA
    yield "eye/tracking_state", UINT32, TRANSIENT
    for side in ("left", "right"):
        for kind in ("controller", "hand"):
            label = f"{side}/{kind}"
            if kind == "controller":
                yield f"{label}_pose", POSE, SENSOR_DATA
            else:
                yield f"{label}_joints", ARRAY, SENSOR_DATA
            yield f"{label}_velocity", TWIST, SENSOR_DATA
            yield f"{label}_tracking_state", UINT32, TRANSIENT
    yield "controller/input", JOY, RELIABLE


class Header(C.Structure):
    _fields_ = [("sec", C.c_int32), ("nanosec", C.c_uint32), ("frame", C.c_void_p)]


class Sequence(C.Structure):
    _fields_ = [
        ("maximum", C.c_uint32), ("length", C.c_uint32),
        ("buffer", C.c_void_p), ("release", C.c_bool),
    ]

    def values(self, item, limit):
        count = min(self.length, limit)
        if not count or not self.buffer:
            return []
        return list(C.cast(self.buffer, C.POINTER(item * count)).contents)


class Layout(C.Structure):
    _fields_ = [("dim", Sequence), ("data_offset", C.c_uint32)]


class HandArray(C.Structure):
    _fields_ = [("layout", Layout), ("data", Sequence)]


class Joy(C.Structure):
    _fields_ = [("header", Header), ("axes", Sequence), ("buttons", Sequence)]


def finite(values):
    return [value if math.isfinite(value) else None for value in values]


def check(result, operation):
    if result < 0:
        raise RuntimeError(f"{operation}: DDS error {result}")
    return result


def library_path(override):
    if override:
        return override.expanduser().resolve()
    runtime = ROOT / "runtime"
    if sys.platform == "win32":
        filename = "cdds_plugin.dll"
        unity = ROOT.parents[1] / "Assets/CycloneDDS/Plugins/Windows/x86_64"
        if not (runtime / filename).exists() and (unity / filename).exists():
            runtime.mkdir(exist_ok=True)
            for name in ("ddsc.dll", filename):
                shutil.copy2(unity / name, runtime / name)
    elif sys.platform == "darwin":
        filename = "libcdds_plugin.dylib"
    else:
        filename = "libcdds_plugin.so"
    return runtime / filename


class Native:
    def __init__(self, path):
        if not path.is_file():
            raise RuntimeError(f"DDS plugin not found: {path}. See README.md or use --library.")
        self.search_dir = os.add_dll_directory(str(path.parent)) if sys.platform == "win32" else None
        try:
            self.dll = C.CDLL(str(path))
            self.bind("init", C.c_int, C.c_int, C.c_char_p)
            self.bind("shutdown", None)
            for kind in ("subscriber", "publisher"):
                self.bind(f"create_{kind}", C.c_int, C.c_char_p, C.c_int, C.c_int)
            self.bind("destroy_entity", None, C.c_int)
            self.bind("sample_size", C.c_size_t, C.c_int)
            self.bind("sample_alloc", C.c_void_p, C.c_int)
            self.bind("sample_free", None, C.c_int, C.c_void_p)
            self.bind("take_raw", C.c_int, C.c_int, C.c_void_p, C.c_size_t)
            self.bind("take_uint32", C.c_int, C.c_int, C.POINTER(C.c_uint32))
            self.bind("get_matched_publications", C.c_int, C.c_int)
            for kind in ("pose", "twist"):
                self.bind(f"take_{kind}_stamped", C.c_int, C.c_int, C.POINTER(C.c_int64),
                          C.c_char_p, C.c_int, C.POINTER(C.c_double), C.POINTER(C.c_double))
        except (OSError, AttributeError):
            self.close()
            raise

    def bind(self, name, returns, *arguments):
        function = getattr(self.dll, f"cdds_{name}")
        function.restype = returns
        function.argtypes = arguments
        setattr(self, name, function)

    def close(self):
        if self.search_dir:
            self.search_dir.close()


class Reader:
    def __init__(self, native, name, kind, qos):
        self.native = native
        self.name, self.kind = name, kind
        self.buffer = None
        self.last_sample = None
        self.serial = 0
        self.stamp = C.c_int64()
        self.frame = C.create_string_buffer(256)
        self.first = (C.c_double * 3)()
        self.second = (C.c_double * (4 if kind == POSE else 3))()
        self.scalar = C.c_uint32()
        self.handle = check(native.create_subscriber(f"rt/xr/{name}".encode(), kind, qos), name)
        try:
            if kind in (JOY, ARRAY):
                self.struct = Joy if kind == JOY else HandArray
                self.size = native.sample_size(kind)
                if self.size != C.sizeof(self.struct):
                    raise RuntimeError(f"{name}: plugin and bridge disagree on native message layout")
                self.buffer = native.sample_alloc(kind)
                if not self.buffer:
                    raise RuntimeError(f"{name}: sample allocation failed")
        except Exception:
            self.close()
            raise

    def close(self):
        if self.buffer:
            self.native.sample_free(self.kind, self.buffer)
        self.native.destroy_entity(self.handle)

    def take(self):
        if self.buffer:
            if not check(self.native.take_raw(self.handle, self.buffer, self.size), self.name):
                return None
            sample = self.struct.from_address(self.buffer)
            if self.kind == JOY:
                return {"axes": finite(sample.axes.values(C.c_float, 8)),
                        "buttons": sample.buttons.values(C.c_int32, 6)}
            return finite(sample.data.values(C.c_float, 26 * 8 + 1))
        if self.kind == UINT32:
            return (self.scalar.value if check(
                self.native.take_uint32(self.handle, C.byref(self.scalar)), self.name
            ) else None)
        function = self.native.take_pose_stamped if self.kind == POSE else self.native.take_twist_stamped
        if check(function(self.handle, C.byref(self.stamp), self.frame, len(self.frame),
                          self.first, self.second), self.name):
            return finite([*self.first, *self.second])
        return None

    def update(self, now, stale):
        # Reliable Joy can queue up to ten samples. Bound work per tick and send the latest.
        for _ in range(12 if self.kind == JOY else 1):
            value = self.take()
            if value is None:
                break
            self.serial += 1
            self.last_sample = {"seq": self.serial, "data": value}
            self.received_at = now
        if self.last_sample is not None:
            if now - self.received_at > stale or not self.native.get_matched_publications(self.handle):
                self.last_sample = None
        return self.last_sample


@dataclass
class Feed:
    domain: int

    def __post_init__(self):
        self.lock = Lock()
        self.stop = Event()
        self.payload = b'{"streams":{}}'

    def set(self, streams, error=None):
        payload = json.dumps({"domain": self.domain, "streams": streams, "error": error},
                             separators=(",", ":"), allow_nan=False).encode()
        with self.lock:
            self.payload = payload

    def poll(self, readers, stale):
        try:
            while not self.stop.is_set():
                now = time.monotonic()
                active = {}
                for reader in readers:
                    sample = reader.update(now, stale)
                    if sample is not None:
                        active[reader.name] = sample
                self.set(active)
                self.stop.wait(max(0, 1 / 30 - (time.monotonic() - now)))
        except Exception as error:
            print(error, file=sys.stderr)
            self.set({}, str(error))


def load_web_assets():
    try:
        bundle = BUNDLE.read_bytes()
    except FileNotFoundError as error:
        raise RuntimeError("Viewer bundle missing. Run npm ci and npm run build in subscriber first.") from error
    return {
        "/": ("text/html; charset=utf-8", (ROOT / "page.html.in").read_bytes()),
        "/style.css": ("text/css; charset=utf-8", (ROOT / "style.css").read_bytes()),
        "/viewer.js": ("text/javascript; charset=utf-8", bundle),
    }


class Handler(BaseHTTPRequestHandler):
    def do_GET(self):
        host = self.headers.get("Host")
        if host not in self.server.allowed_hosts:
            self.send_error(403)
            return
        if self.headers.get("Origin") not in (None, f"http://{host}"):
            self.send_error(403)
            return
        if self.path == "/events":
            self.send_events()
            return
        asset = self.server.web_assets.get(self.path)
        if asset is None:
            self.send_error(404)
            return
        content_type, content = asset
        self.send_response(200)
        self.send_header("Content-Type", content_type)
        self.send_header("Content-Length", str(len(content)))
        self.end_headers()
        self.wfile.write(content)

    def send_events(self):
        self.connection.settimeout(3)
        self.send_response(200)
        self.send_header("Content-Type", "text/event-stream")
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        last = None
        sent = 0
        try:
            while not self.server.feed.stop.is_set():
                with self.server.feed.lock:
                    payload = self.server.feed.payload
                now = time.monotonic()
                if payload != last or now - sent >= 0.5:
                    self.wfile.write(b"data: " + payload + b"\n\n")
                    last, sent = payload, now
                self.server.feed.stop.wait(1 / 30)
        except (ConnectionError, TimeoutError):
            pass

    def log_message(self, _format, *_args):
        pass


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--domain", type=int, default=0)
    parser.add_argument("--bind", type=ipaddress.IPv4Address, default="127.0.0.1",
                        help="HTTP bind address. Use the gateway's LAN IP to share the viewer.")
    parser.add_argument("--port", type=int, default=8765)
    parser.add_argument("--stale-ms", type=int, default=1000)
    parser.add_argument("--library", type=Path)
    discovery = parser.add_mutually_exclusive_group()
    discovery.add_argument("--config", type=Path, help="Cyclone DDS XML configuration file")
    discovery.add_argument("--peer", type=ipaddress.ip_address, help="XR publisher IP for unicast discovery")
    args = parser.parse_args()
    if not 0 <= args.domain <= 232 or not 0 <= args.port <= 65535 or args.stale_ms < 100:
        parser.error("expected domain 0–232, port 0–65535 and stale-ms ≥100")
    if args.bind.is_unspecified or args.bind.is_multicast:
        parser.error("--bind requires a specific local IPv4 address")
    if args.config:
        try:
            os.environ["CYCLONEDDS_URI"] = args.config.read_text(encoding="utf-8-sig").strip()
        except (OSError, UnicodeError, ValueError) as error:
            parser.error(f"cannot read DDS configuration: {error}")

    native, worker, readers = None, None, []
    initialized = False
    feed = Feed(args.domain)
    try:
        web_assets = load_web_assets()
        native = Native(library_path(args.library))
        peer = str(args.peer).encode() if args.peer else None
        check(native.init(args.domain, peer), "create DDS participant")
        initialized = True
        for name, kind, qos in channels():
            readers.append(Reader(native, name, kind, qos))
        address = str(args.bind)
        with ThreadingHTTPServer((address, args.port), Handler) as server:
            server.web_assets, server.feed = web_assets, feed
            addresses = (address, "localhost") if address == "127.0.0.1" else (address,)
            server.allowed_hosts = {f"{host}:{server.server_port}" for host in addresses}
            if server.server_port == 80:
                server.allowed_hosts.update(addresses)
            worker = Thread(target=feed.poll, args=(readers, args.stale_ms / 1000), daemon=True)
            worker.start()
            print(f"http://{address}:{server.server_port}/  ·  DDS domain {args.domain}", flush=True)
            server.serve_forever()
    except KeyboardInterrupt:
        return 0
    except (OSError, RuntimeError, AttributeError) as error:
        print(f"Subscriber: {error}", file=sys.stderr)
        return 1
    finally:
        feed.stop.set()
        if worker:
            worker.join()
        for reader in readers:
            reader.close()
        if initialized:
            native.shutdown()
        if native:
            native.close()
    return 0


if __name__ == "__main__":
    sys.exit(main())
