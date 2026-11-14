"""API pública e opcional de conteúdo do Ultima (stdlib only)."""
from __future__ import annotations

import hashlib
import json
import os
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import urlsplit

CONTENT_FILE = Path(__file__).with_name("content.json")
WORLD_ID = "ultima"
API_VERSION = "v1"


class Handler(BaseHTTPRequestHandler):
    server_version = "UltimaContentAPI"
    sys_version = ""

    def _headers(self, status: int, body: bytes = b"", etag: str | None = None) -> None:
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Access-Control-Allow-Origin", "*")
        self.send_header("Access-Control-Allow-Methods", "GET, OPTIONS")
        self.send_header("Access-Control-Allow-Headers", "If-None-Match")
        self.send_header("Access-Control-Expose-Headers", "ETag, Cache-Control")
        self.send_header("Cache-Control", "public, max-age=60, stale-while-revalidate=300")
        self.send_header("X-Content-Type-Options", "nosniff")
        if etag:
            self.send_header("ETag", etag)
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()

    def _json(self, payload: object, status: int = 200) -> None:
        body = json.dumps(payload, ensure_ascii=False, separators=(",", ":")).encode("utf-8")
        etag = '"' + hashlib.sha256(body).hexdigest() + '"'
        if self.headers.get("If-None-Match") == etag:
            self._headers(304, etag=etag)
            return
        self._headers(status, body, etag)
        self.wfile.write(body)

    def do_OPTIONS(self) -> None:
        self._headers(204)

    def do_GET(self) -> None:
        path = urlsplit(self.path).path.rstrip("/") or "/"
        if path == "/health":
            self._json({"status": "ok", "apiVersion": API_VERSION})
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
                "endpoints": [
                    "/v1/worlds/" + WORLD_ID,
                    "/v1/npcs/guardia",
                    "/v1/quests",
                    "/v1/announcements",
                ],
            })
            return
        if path == "/v1/worlds/" + WORLD_ID:
            self._json({"id": WORLD_ID, **content.get("world", {})})
            return
        if path == "/v1/npcs/guardia":
            npc = content.get("npcs", {}).get("guardia")
            if npc is None:
                self._json({"error": "not_found"}, 404)
            else:
                self._json({"id": "guardia", **npc})
            return
        if path == "/v1/quests":
            self._json({"items": content.get("quests", [])})
            return
        if path == "/v1/announcements":
            self._json({"items": content.get("announcements", [])})
            return
        self._json({"error": "not_found"}, 404)

    def do_POST(self) -> None:
        self._json({"error": "method_not_allowed"}, 405)

    def log_message(self, fmt: str, *args: object) -> None:
        print("%s - %s" % (self.address_string(), fmt % args), flush=True)


def main() -> None:
    port = int(os.environ.get("PORT", "8000"))
    ThreadingHTTPServer(("0.0.0.0", port), Handler).serve_forever()


if __name__ == "__main__":
    main()
