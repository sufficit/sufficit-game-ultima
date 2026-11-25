"""Valida famílias, IA e replay com um provedor HTTP isolado."""
import contextlib
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import json
import hashlib
from pathlib import Path
import runpy
import tempfile
import threading

support = runpy.run_path(str(Path(__file__).with_name("world-store.integration.py")))
boot, request = support["boot"], support["request"]
GARDEN = "/v1/game/garden"


class Provider(BaseHTTPRequestHandler):
    def do_POST(self):
        body = json.loads(self.rfile.read(int(self.headers["Content-Length"])))
        messages = body["messages"]
        text = messages[-1]["content"]
        assert "prop.lantern" in messages[0]["content"]
        if text == "provedor fora":
            self.send_error(503)
            return
        actions = [
            {"action": "path", "mat": "dirt", "r": 1, "points": [{"x": 0, "z": -12}, {"x": 0, "z": -8}]},
            {"action": "place", "asset": "prop.lantern", "x": 2, "z": -12},
            {"action": "place", "asset": "flora.flowers", "x": -2, "z": -12},
        ]
        if text == "e o que eu pedi?":
            assert any(message["content"] == "Luminária, flores e trilha" for message in messages)
            actions = []
        content = "JSON truncado" if text == "resposta ruim" else json.dumps({"reply": "Trilha, luz e flores.", "actions": actions})
        data = json.dumps({"choices": [{"message": {"content": content}}]}).encode()
        self.send_response(200)
        self.send_header("Content-Type", "application/json")
        self.end_headers()
        self.wfile.write(data)

    def log_message(self, *_):
        pass


@contextlib.contextmanager
def provider():
    with ThreadingHTTPServer(("127.0.0.1", 0), Provider) as server:
        runner = threading.Thread(target=server.serve_forever, daemon=True)
        runner.start()
        try:
            yield {"NPC_SERVER_AI": "on", "NPC_LLM_URL": f"http://127.0.0.1:{server.server_port}/chat",
                   "NPC_LLM_KEY": "isolado", "NPC_LLM_MODEL": "teste"}
        finally:
            server.shutdown()
            runner.join()


def test():
    with tempfile.TemporaryDirectory(prefix="ultima-garden-test-") as tmp, provider() as settings:
        path = Path(tmp)
        glb = (Path(__file__).resolve().parents[1] / "assets/glb/prop.stump.glb").read_bytes()
        approved = hashlib.sha256(glb).hexdigest()
        with boot(path, ULTIMA_UPLOAD_SHA256_ALLOWLIST=approved, **settings) as url:
            catalog = request(url, GARDEN)
            assert set(catalog["families"]) == {"terrain", "plants", "decor"}
            assert catalog["models"]["*"]["parameters"]["scaleY"]["inherits"] == "scale"
            assert "customize" in catalog["families"]["plants"]["actions"]
            assert catalog["models"]["prop.lantern"]["parameters"]["poleHeight"]["default"] == 1.4
            session = request(url, "/v1/game/sessions", {"name": "Jardineiro"})
            token = session["token"]
            observer = request(url, "/v1/game/sessions", {"name": "Observador"})
            initial = request(url, "/v1/game/map")
            place = {"action": "place", "asset": "prop.lantern", "x": 2, "z": -12}
            assert request(url, GARDEN + "/edits", {"actions": [place]}, expect=401)["error"] == "invalid_token"
            assert request(url, GARDEN + "/plants", {"actions": [place]}, token, expect=400)["error"] == "wrong_family"
            assert request(url, GARDEN + "/edits", {"actions": [place, {**place, "x": 70}]}, token, expect=400)["error"] == "outside_garden"
            assert request(url, "/v1/game/map") == initial
            assert request(url, GARDEN + "/dialog", {"message": "Trilha"}, token, expect=409)["error"] == "guardian_out_of_reach"
            request(url, "/v1/game/command", {"action": "pose", "x": 0, "z": -12}, token)
            assert request(url, GARDEN + "/dialog", {"message": "resposta ruim"}, token, expect=422)["error"] == "invalid_ai_response"
            assert request(url, GARDEN + "/dialog", {"message": "provedor fora"}, token, expect=503)["error"] == "ai_unavailable"
            assert request(url, "/v1/game/map") == initial
            edited = request(url, GARDEN + "/dialog", {"message": "Luminária, flores e trilha"}, token)
            assert edited["source"] == "llm" and edited["applied"] == 7
            assert edited["map"] == request(url, "/v1/game/map")
            events = request(url, f'/v1/game/events?since={observer["eventsFrom"]}&wait=0', token=observer["token"])
            assert any(event["type"] == "world_edited" and event["revision"] == edited["revision"] for event in events["events"])
            assert request(url, GARDEN + "/dialog", {"message": "e o que eu pedi?"}, token)["applied"] == 0
            flower = next(op for op in edited["operations"] if op.get("asset") == "flora.flowers")
            result = request(url, GARDEN + "/plants", {"actions": [
                {"action": "remove", "uid": flower["uid"]},
                {"action": "place", "asset": "flora.fern", "x": flower["x"], "z": flower["z"]},
            ]}, token)
            assert result["applied"] == 2
            before = request(url, "/v1/game/map")
            assert {p["asset"] for p in before["placements"]} == {"prop.lantern", "flora.fern"}
            lamp = next(p for p in before["placements"] if p["asset"] == "prop.lantern")
            custom = {"action": "customize", "uid": lamp["uid"], "parameters": {"lightIntensity": 1.2}}
            bad = {**custom, "parameters": {"poleHeight": 12}}
            assert request(url, GARDEN + "/decor", {"actions": [custom, bad | {"uid": "missing"}]}, token, expect=400)["error"] == "invalid_placement"
            assert request(url, GARDEN + "/decor", {"actions": [bad]}, token, expect=400)["error"] == "invalid_model_parameters"
            assert request(url, "/v1/game/map") == before
            made = request(url, GARDEN + "/decor", {"actions": [{**place, "x": -2, "parameters":
                {"poleHeight": 2.4}}]}, token)
            new_lamp = made["operations"][0]
            request(url, GARDEN + "/decor", {"actions": [{**custom, "uid": new_lamp["uid"]}]}, token)
            adjusted = request(url, GARDEN + "/dialog", {"message":
                "essas luminarias possuem o meu tamanho, quero luminarias mais altas e um pouco mais fortes"}, token)
            assert adjusted["source"] == "garden" and adjusted["applied"] == 2
            final = request(url, "/v1/game/map")
            first, second = [next(p for p in final["placements"] if p["uid"] == uid) for uid in [lamp["uid"], new_lamp["uid"]]]
            assert (first["x"], first["z"]) == (lamp["x"], lamp["z"])
            assert first["parameters"] == {"poleHeight": 2, "lightIntensity": 1.31}
            assert second["parameters"] == {"poleHeight": 3, "lightIntensity": 1.5}
            events = request(url, f'/v1/game/events?since={observer["eventsFrom"]}&wait=0', token=observer["token"])
            assert any(event["type"] == "world_edited" and event["revision"] == final["revision"] for event in events["events"])
            fern = next(p for p in final["placements"] if p["asset"] == "flora.fern")
            request(url, GARDEN + "/plants", {"actions": [{"action": "customize", "uid": fern["uid"],
                "parameters": {"poleHeight": 2}}]}, token, expect=400)
            scaled = request(url, GARDEN + "/plants", {"actions": [{"action": "customize", "uid": fern["uid"],
                "parameters": {"scaleY": 2, "rotationY": 1.57}}]}, token)["map"]
            updated = next(p for p in scaled["placements"] if p["uid"] == fern["uid"])
            assert updated == fern | {"parameters": {"scaleY": 2, "rotationY": 1.57}}
            imported = request(url, "/v1/assets/upload?name=tronco", glb, token)["asset"]["id"]
            assert imported in request(url, GARDEN + "/context", token=token)["prompt"]
            placed = request(url, GARDEN + "/decor", {"actions": [{"action": "place", "asset": imported,
                "x": 6, "z": -12, "scale": 1.25, "parameters": {"scaleX": 0.8, "scaleZ": 1.6}}]}, token)
            uid = placed["operations"][0]["uid"]
            final = request(url, GARDEN + "/decor", {"actions": [{"action": "customize", "uid": uid,
                "parameters": {"scaleY": 2.25}}]}, token)["map"]
            variant = next(p for p in final["placements"] if p["uid"] == uid)
            assert variant["scale"] == 1.25 and variant["parameters"] == {"scaleX": 0.8, "scaleY": 2.25, "scaleZ": 1.6}
        with boot(path, NPC_SERVER_AI="off") as url:
            assert request(url, "/v1/game/map") == final
            token = request(url, "/v1/game/sessions", {"name": "Jardineiro"})["token"]
            request(url, "/v1/game/command", {"action": "pose", "x": 0, "z": -12}, token)
            assert request(url, GARDEN + "/dialog", {"message": "adicione flores"}, token, expect=503)["error"] == "ai_unavailable"
            assert request(url, "/v1/game/map") == final
        print("OK: famílias, escopo, lote atômico, diálogo, eventos, rejeição, substituição e replay")


if __name__ == "__main__":
    test()
