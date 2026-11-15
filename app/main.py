"""API pública e opcional do Ultima: conteúdo + sinalização WebRTC P2P (stdlib only).
A sinalização só apresenta navegadores; dados de jogo/chat/voz trafegam diretamente
entre peers via WebRTC. Salas são efêmeras e guardadas apenas na memória do processo.
"""
from __future__ import annotations
import hashlib
import json
import os
import threading
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import parse_qs, urlsplit

CONTENT_FILE = Path(__file__).with_name("content.json")
API_VERSION = "v1"
MAX_BODY = 64 * 1024
PRUNE_SECS = 30
QUEUE_CAP = 100
MAX_POLL_WAIT = 20.0
NET: dict[str, dict] = {}
NET_LOCK = threading.RLock()


def _room(room: str) -> dict:
    entry = NET.get(room)
    if entry is None:
        entry = {"peers": {}, "queues": {}, "cond": threading.Condition(NET_LOCK)}
        NET[room] = entry
    return entry


def _prune(entry: dict, now: float) -> None:
    stale = [pid for pid, info in entry["peers"].items() if now - info["last"] > PRUNE_SECS]
    for pid in stale:
        entry["peers"].pop(pid, None)
        entry["queues"].pop(pid, None)
    if stale:
        entry["cond"].notify_all()


def net_join(room: str, peer: str, name: str) -> list[str]:
    with NET_LOCK:
        entry = _room(room)
        _prune(entry, time.time())
        entry["peers"][peer] = {"name": name[:40], "last": time.time()}
        entry["queues"].setdefault(peer, [])
        entry["cond"].notify_all()
        return sorted(pid for pid in entry["peers"] if pid != peer)


def net_leave(room: str, peer: str) -> None:
    with NET_LOCK:
        entry = NET.get(room)
        if entry is not None:
            entry["peers"].pop(peer, None)
            entry["queues"].pop(peer, None)
            entry["cond"].notify_all()


def net_signal(room: str, sender: str, target: str | None, kind: str, data) -> int:
    with NET_LOCK:
        entry = _room(room)
        _prune(entry, time.time())
        if sender not in entry["peers"]:
            entry["peers"][sender] = {"name": "?", "last": time.time()}
            entry["queues"].setdefault(sender, [])
        message = {"from": sender, "kind": kind[:24], "data": data}
        targets = ([target] if target in entry["peers"] else []) if target else [p for p in entry["peers"] if p != sender]
        for pid in targets:
            queue = entry["queues"].setdefault(pid, [])
            queue.append(message)
            del queue[:-QUEUE_CAP]
        if targets:
            entry["cond"].notify_all()
        return len(targets)


def net_poll(room: str, peer: str, wait: float) -> dict:
    deadline = time.time() + max(0.0, min(wait, MAX_POLL_WAIT))
    with NET_LOCK:
        entry = _room(room)
        _prune(entry, time.time())
        if peer not in entry["peers"]:
            return {"expired": True, "peers": [], "messages": []}
        while peer in entry["peers"] and not entry["queues"].get(peer) and time.time() < deadline:
            entry["cond"].wait(max(0.05, deadline - time.time()))
            _prune(entry, time.time())
        if peer not in entry["peers"]:
            return {"expired": True, "peers": [], "messages": []}
        messages = entry["queues"].get(peer, [])
        entry["queues"][peer] = []
        return {"peers": sorted(p for p in entry["peers"] if p != peer), "messages": messages}


def net_rooms() -> dict:
    with NET_LOCK:
        now = time.time()
        out: dict[str, dict] = {}
        for room, entry in NET.items():
            _prune(entry, now)
            if entry["peers"]:
                out[room] = {pid: info["name"] for pid, info in entry["peers"].items()}
        return out


class Handler(BaseHTTPRequestHandler):
    server_version = "UltimaContentAPI"
    sys_version = ""

    def _headers(self, status: int, body: bytes = b"", etag: str | None = None,
                 cache: str = "public, max-age=60, stale-while-revalidate=300") -> None:
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Access-Control-Allow-Origin", "*")
        self.send_header("Access-Control-Allow-Methods", "GET, POST, OPTIONS")
        self.send_header("Access-Control-Allow-Headers", "Content-Type, If-None-Match")
        self.send_header("Access-Control-Expose-Headers", "ETag, Cache-Control")
        self.send_header("Cache-Control", cache)
        self.send_header("X-Content-Type-Options", "nosniff")
        if etag:
            self.send_header("ETag", etag)
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        # Importante: Content-Length/headers sem escrever o payload produzem HTTP 200 vazio.
        if body:
            self.wfile.write(body)

    def _json(self, payload: object, status: int = 200, cache: str | None = None) -> None:
        body = json.dumps(payload, ensure_ascii=False, separators=(",", ":")).encode("utf-8")
        if cache is None:
            etag = '"' + hashlib.sha256(body).hexdigest() + '"'
            if self.headers.get("If-None-Match") == etag:
                self._headers(304, etag=etag)
                return
            self._headers(status, body, etag=etag)
        else:
            self._headers(status, body, cache=cache)

    def _body(self) -> dict | None:
        try:
            length = int(self.headers.get("Content-Length") or 0)
        except ValueError:
            return None
        if length <= 0 or length > MAX_BODY:
            return None
        try:
            data = json.loads(self.rfile.read(length).decode("utf-8"))
        except (UnicodeDecodeError, json.JSONDecodeError):
            return None
        return data if isinstance(data, dict) else None

    def do_OPTIONS(self) -> None:
        self._headers(204)

    def do_GET(self) -> None:
        parsed = urlsplit(self.path)
        path = parsed.path.rstrip("/") or "/"
        query = parse_qs(parsed.query)
        if path == "/health":
            self._json({"status": "ok", "apiVersion": API_VERSION})
            return
        if path == "/v1/net/rooms":
            self._json({"rooms": net_rooms()}, cache="no-store")
            return
        parts = path.split("/")
        if len(parts) == 6 and parts[:3] == ["", "v1", "net"] and parts[4] == "poll":
            try:
                wait = float(query.get("wait", ["15"])[0])
            except ValueError:
                wait = 15.0
            self._json(net_poll(parts[3][:40], parts[5][:40], wait), cache="no-store")
            return

        try:
            content = json.loads(CONTENT_FILE.read_text(encoding="utf-8"))
        except (OSError, json.JSONDecodeError):
            self._json({"error": "content_unavailable"}, 503)
            return
        if path == "/v1/manifest":
            self._json({
                "apiVersion": API_VERSION,
                "schema": content.get("$schema"),
                "contentVersion": content.get("version"),
                "endpoints": ["/v1/worlds/ultima", "/v1/npcs/guardia", "/v1/quests", "/v1/announcements",
                              "/v1/net/rooms", "/v1/net/{room}/join", "/v1/net/{room}/signal", "/v1/net/{room}/poll/{peer}"]
            })
        elif path == "/v1/worlds/ultima":
            self._json({"id": "ultima", **content.get("world", {})})
        elif path == "/v1/npcs/guardia":
            npc = content.get("npcs", {}).get("guardia")
            self._json({"error": "not_found"}, 404) if npc is None else self._json({"id": "guardia", **npc})
        elif path == "/v1/quests":
            self._json({"items": content.get("quests", [])})
        elif path == "/v1/announcements":
            self._json({"items": content.get("announcements", [])})
        else:
            self._json({"error": "not_found"}, 404)

    def do_POST(self) -> None:
        path = urlsplit(self.path).path.rstrip("/") or "/"
        parts = path.split("/")
        if len(parts) == 5 and parts[:3] == ["", "v1", "net"]:
            room, action = parts[3][:40], parts[4]
            data = self._body()
            if data is None:
                self._json({"error": "bad_request"}, 400, cache="no-store")
                return
            peer = str(data.get("peer") or data.get("from") or "")[:40]
            if not peer:
                self._json({"error": "peer_required"}, 400, cache="no-store")
                return
            if action == "join":
                self._json({"peer": peer, "peers": net_join(room, peer, str(data.get("name") or "Jogador"))}, cache="no-store")
                return
            if action == "leave":
                net_leave(room, peer)
                self._json({"ok": True}, cache="no-store")
                return
            if action == "signal":
                kind = str(data.get("kind") or "")
                if kind not in ("offer", "answer", "ice", "bye"):
                    self._json({"error": "bad_kind"}, 400, cache="no-store")
                    return
                target = data.get("to")
                target = str(target)[:40] if isinstance(target, str) and target else None
                sent = net_signal(room, peer, target, kind, data.get("data"))
                self._json({"ok": True, "delivered": sent}, cache="no-store")
                return
        self._json({"error": "method_not_allowed"}, 405)

    def log_message(self, fmt: str, *args: object) -> None:
        print("%s - %s" % (self.address_string(), fmt % args), flush=True)


def main() -> None:
    port = int(os.environ.get("PORT", "8000"))
    ThreadingHTTPServer(("0.0.0.0", port), Handler).serve_forever()


if __name__ == "__main__":
    main()
