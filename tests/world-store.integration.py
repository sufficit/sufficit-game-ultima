"""Exercita o servidor Release em um dataDir temporário, inclusive dois boots."""
import contextlib
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import tempfile
import time
from urllib.error import HTTPError
from urllib.request import Request, urlopen

SERVER = Path(__file__).resolve().parents[1] / "app/bin/Release/net10.0/UltimaServer.dll"


@contextlib.contextmanager
def boot(data_dir, **settings):
    env = os.environ | {
        "ASPNETCORE_URLS": "http://127.0.0.1:0",
        "ULTIMA_DATA_DIR": str(data_dir),
        "MESHY_API_KEY": "",
        **settings,
    }
    with tempfile.TemporaryFile(mode="w+") as output:
        process = subprocess.Popen(["dotnet", str(SERVER)], env=env, stdout=output, stderr=output)
        try:
            for _ in range(200):
                output.seek(0)
                match = re.search(r"Now listening on: (http://127\.0\.0\.1:\d+)", output.read())
                if match:
                    yield match[1]
                    return
                if process.poll() is not None:
                    break
                time.sleep(0.025)
            output.seek(0)
            raise RuntimeError(output.read())
        finally:
            process.terminate()
            process.wait(timeout=10)


def request(url, path, payload=None, token=None, expect=200):
    headers = {"Content-Type": "application/json"}
    if token:
        headers["X-Game-Token"] = token
    if isinstance(payload, bytes):
        headers["Content-Type"] = "model/gltf-binary"
        body = payload
    else:
        body = None if payload is None else json.dumps(payload).encode()
    try:
        response = urlopen(Request(url + path, data=body, headers=headers), timeout=5)
    except HTTPError as error:
        response = error
    with response:
        result = json.load(response)
        assert response.status == expect, (path, response.status, result)
        return result


def journal(path):
    return [json.loads(line) for line in (path / "world/world.journal.jsonl").read_text().splitlines()]


def main(path):
    now = time.time()
    legacy = {
        "revision": 17,
        "tiles": {"1,1": {"type": "rock", "by": "legado", "at": now, "rev": 17}},
        "jobs": [{"id": "jlegacy", "type": "dig", "cell": "2,2", "cx": 2, "cz": 2,
                  "owner": "legacy", "ownerName": "legado", "startedAt": now - 7, "doneAt": now + 23}],
    }
    (path / "map.json").write_text(json.dumps(legacy))
    with boot(path) as url:
        initial = request(url, "/v1/game/map")
        assert initial["tiles"] == legacy["tiles"]
        assert initial["jobs"][0]["id"] == "jlegacy" and initial["jobs"][0]["progress"] > 0.20
        assert (path / "map.json.migrated").exists()
        assert (path / "world/world.snapshot.json").exists()

        alice = request(url, "/v1/game/sessions", {"name": "Alice"})
        bob = request(url, "/v1/game/sessions", {"name": "Bob"})
        a, b = alice["token"], bob["token"]
        command = "/v1/game/command"
        tile = request(url, "/v1/game/map/edits", {"edits": [{"cx": 3, "cz": 3, "type": "path"}]}, a)
        sculpt = request(url, command, {"action": "sculpt", "x": 7, "z": 7, "r": 2, "dh": 1}, a)
        paint = request(url, command, {"action": "paint", "x": 7, "z": 7, "r": 2, "mat": "water"}, b)
        passage = request(url, command, {"action": "passage", "x1": 4, "z1": 5, "x2": 9, "z2": 5, "r": 2}, a)
        carves = request(url, command, {"action": "carve", "x": 10, "z": 5, "r": 2, "y0": -3}, a)
        for key in [sculpt["edit"], passage["edit"], carves["edit"]]:
            refused = request(url, command, {"action": "undo_surface", "edit": key}, b, expect=404)
            assert refused["error"] == "edit_not_found"
        for key in [sculpt["edit"], passage["edit"], carves["edit"]]:
            result = request(url, command, {"action": "undo_surface", "edit": key}, a)
            assert result["ok"]
            request(url, command, {"action": "undo_surface", "edit": key}, a, expect=404)

        path_points = [{"x": 0, "y": 0, "z": 4}, {"x": 4, "y": 0.5, "z": 10}, {"x": 10, "y": 0.4, "z": 18}]
        invalid = request(url, command, {"action": "tunnel", "points": path_points[:2]}, a, expect=400)
        assert invalid["error"] == "tunnel_path_3_to_8"
        removed_tunnel = request(url, command, {"action": "tunnel", "points": path_points, "r": 2.4}, a)
        request(url, command, {"action": "undo_surface", "edit": removed_tunnel["edit"]}, a)
        tunnel = request(url, command, {"action": "tunnel", "points": path_points, "r": 2.4}, b)
        final = request(url, "/v1/game/map")
        assert final["volumeTunnels"]
        assert final["edits"][-1]["kind"] == "tunnel" and final["edits"][-1]["points"][1]["y"] < -2.5
        assert final["tiles"]["3,3"]["rev"] == tile["revision"]
        assert [op["edit"] for op in final["edits"]] == [paint["edit"], tunnel["edit"]]
        for client in [alice, bob]:
            events = request(url, f"/v1/game/events?since={client['eventsFrom']}&wait=0", token=client["token"])
            assert sum(event.get("op", {}).get("kind") == "undo_surface" for event in events["events"]) == 4
            assert request(url, "/v1/game/state", token=client["token"])["map"]["edits"] == final["edits"]
        ops = journal(path)
        assert len([op for op in ops if op["kind"] == "surface_batch"]) == 6
        assert any(op["kind"] == "tile" and op["cell"] == "3,3" for op in ops)
        assert [op["op"] for op in ops] == list(range(1, len(ops) + 1))

    with boot(path) as url:
        replay = request(url, "/v1/game/map")
        for key in ["revision", "tiles", "edits", "placements"]:
            assert replay[key] == final[key], key
        assert replay["jobs"][0]["id"] == "jlegacy"
        assert replay["jobs"][0]["progress"] >= initial["jobs"][0]["progress"]
        token = request(url, "/v1/game/sessions", {"name": "Alice"})["token"]
        command = request(url, "/v1/game/command", {"action": "sculpt", "x": 8, "z": 7, "dh": 1}, token)
        assert command["revision"] > replay["revision"]
        assert journal(path)[-1]["op"] > ops[-1]["op"]

    print("OK: migração legada, job retomado, lote atômico, dois clientes, undo e replay")


def test_assets(path):
    source = SERVER.parents[4] / "assets/glb"
    approved = (source / "prop.stump.glb").read_bytes()
    rejected = (source / "prop.log.glb").read_bytes()
    digest = hashlib.sha256(approved).hexdigest()
    settings = {"ULTIMA_WORLD_ID": "bosque", "ULTIMA_UPLOAD_SHA256_ALLOWLIST": digest}
    with boot(path, **settings) as url:
        token = request(url, "/v1/game/sessions", {"name": "Alice"})["token"]
        endpoint = "/v1/assets/upload?name=modelo"
        assert request(url, endpoint, rejected, token, expect=403)["error"] == "asset_not_approved"
        asset = request(url, endpoint, approved, token)["asset"]
        assert asset["sha256"] == digest
        assert request(url, "/v1/assets")["assets"] == [asset]
    with boot(path, ULTIMA_WORLD_ID="lago") as url:
        assert request(url, "/v1/assets")["assets"] == []
        assert request(url, "/v1/game/map")["tiles"] == {}
    assets = path / "worlds/bosque/assets"
    live = assets / asset["file"].split("/")[-1]
    stale = assets / "player.modelo-deadbeef.glb"
    stale.write_bytes(rejected)
    old = time.time() - 32 * 86400
    for item in [live, stale]:
        os.utime(item, (old, old))
    with boot(path, **settings, ULTIMA_ASSET_GC_DAYS="30") as url:
        assert request(url, "/v1/assets")["assets"] == [asset]
        assert live.exists() and not stale.exists()
    print("OK: catálogo segregado, whitelist de SHA-256 e limpeza de órfãos")


if __name__ == "__main__":
    with tempfile.TemporaryDirectory(prefix="ultima-world-test-") as directory:
        path = Path(directory)
        main(path)
        test_assets(path)
