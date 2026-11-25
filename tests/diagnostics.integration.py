"""Exercita a gravação e os limites do relatório no servidor Release."""
import json
from pathlib import Path
import runpy
import tempfile

support = runpy.run_path(str(Path(__file__).with_name("world-store.integration.py")))
boot, request = support["boot"], support["request"]

with tempfile.TemporaryDirectory(prefix="ultima-diagnostics-") as tmp, boot(Path(tmp)) as url:
    route = "/v1/diagnostics/reports"
    report = {
        "build": "aabbccdd", "description": "GPU indisponível", "renderer": "WebGL2",
        "gpu": {"state": "unsupported", "progress": 0},
        "cpu": {"state": "ready", "model": "Gemma 3 270M", "progress": 1},
        "token": "segredo-fictício", "messages": ["conversa fictícia"],
    }
    request(url, route, {"build": "invalid"}, expect=400)
    request(url, route, {"build": "dev", "description": "x" * 5000}, expect=413)
    receipt = request(url, route, report, expect=201)
    log = Path(tmp) / "diagnostic-reports.jsonl"
    saved = json.loads(log.read_text())
    assert saved["id"] == receipt["id"] and len(saved["id"]) == 32
    assert saved["description"] == report["description"] and saved["cpu"]["state"] == "ready"
    assert "token" not in saved and "messages" not in saved
    request(url, route, expect=404)
    for _ in range(7):
        request(url, route, report, expect=201)
    request(url, route, report, expect=429)
    assert len(log.read_text().splitlines()) == 8
    print("OK: relato persistido, protocolo, campos permitidos e limites")
