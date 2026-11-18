#!/usr/bin/env python3
"""Agente virtual do Ultima — joga via API (F5 do Marco 3).

Referência de como um agente se conecta, entende o mundo e age:
  1. cria sessão (POST /v1/game/sessions) → token
  2. lê estado (GET /v1/game/state) → mundo, personagens, jobs, mapa
  3. envia comandos (POST /v1/game/command): moveto/say/dig/stop_job...
  4. escuta eventos em long-poll (GET /v1/game/events) — percepção contínua

Uso:
  python3 agente.py [--api https://...run.sufficit.com.br] [--nome Agente] [--sala]

Sem --api, aponta para http://127.0.0.1:8123 (banca local).
Requer apenas Python 3.10+ (stdlib).
"""
from __future__ import annotations

import argparse
import json
import sys
import time
import urllib.error
import urllib.request


class UltimaAPI:
    def __init__(self, base: str) -> None:
        self.base = base.rstrip("/")
        self.token: str | None = None

    def _call(self, method: str, path: str, body: dict | None = None,
              timeout: float = 30.0) -> tuple[int, dict]:
        headers = {"Content-Type": "application/json"}
        if self.token:
            headers["X-Game-Token"] = self.token
        req = urllib.request.Request(
            self.base + path, method=method,
            data=json.dumps(body).encode() if body is not None else None,
            headers=headers)
        try:
            with urllib.request.urlopen(req, timeout=timeout) as res:
                raw = res.read().decode("utf-8")
                return res.status, (json.loads(raw) if raw else {})
        except urllib.error.HTTPError as exc:
            raw = exc.read().decode("utf-8")
            return exc.code, (json.loads(raw) if raw else {})

    # ---- contrato do jogo ----
    def join(self, name: str) -> dict:
        status, data = self._call("POST", "/v1/game/sessions", {"name": name})
        if status != 200:
            raise SystemExit(f"falha ao criar sessão: {status} {data}")
        self.token = data["token"]
        return data

    def state(self) -> dict:
        status, data = self._call("GET", "/v1/game/state")
        if status != 200:
            raise SystemExit(f"falha ao ler estado: {status} {data}")
        return data

    def command(self, action: str, **params) -> tuple[int, dict]:
        return self._call("POST", "/v1/game/command", {"action": action, **params})

    def events(self, since: int, wait: float = 15.0) -> dict:
        status, data = self._call(
            "GET", f"/v1/game/events?since={since}&wait={wait}")
        if status != 200:
            return {"events": [], "seq": since}
        return data

    def leave(self) -> None:
        if self.token:
            self._call("POST", "/v1/game/leave", {})


def wait_arrival(api: UltimaAPI, x: float, z: float, tol: float = 0.8,
                 timeout: float = 60.0) -> tuple[float, float]:
    """moveto + espera o tick mover o personagem até o destino."""
    status, _ = api.command("moveto", x=x, z=z)
    if status != 200:
        raise SystemExit(f"moveto recusado: {status}")
    deadline = time.time() + timeout
    while time.time() < deadline:
        me = api.state()["me"]
        if not me["moving"] and abs(me["x"] - x) < tol and abs(me["z"] - z) < tol:
            return me["x"], me["z"]
        time.sleep(0.7)
    me = api.state()["me"]
    return me["x"], me["z"]


def main() -> None:
    ap = argparse.ArgumentParser(description="Agente virtual do Ultima")
    ap.add_argument("--api", default="http://127.0.0.1:8123")
    ap.add_argument("--nome", default="Agente")
    args = ap.parse_args()

    api = UltimaAPI(args.api)
    session = api.join(args.nome)
    world = session["world"]
    me = session["character"]
    print(f"[agente] conectado como {me['name']} em ({me['x']},{me['z']}); "
          f"mundo half={world['half']} célula={world['cell']}")

    try:
        # 1. vai até a Guardiã e conversa
        npc = world["npc"]
        x, z = wait_arrival(api, npc["x"], npc["z"] + 2.5)
        print(f"[agente] chegou em ({x:.1f},{z:.1f})")
        status, r = api.command("say", text="Olá! Quem é você e o que guarda este jardim?")
        if r.get("npcReply"):
            print(f"[guardiã] {r['npcReply']}")

        # 2. enfileira escavações em série (automação: o personagem caminha
        #    sozinho até cada célula e inicia o trabalho, sem novos comandos)
        status, r = api.command("queue", cx=1, cz=-3)
        order = r.get("order")
        if order:
            print(f"[agente] ordem {order['id']} na célula {order['cell']} "
                  f"(fila: {len(r.get('queue', []))})")
            api.command("queue", cx=2, cz=-3)
            api.command("queue", cx=3, cz=-3)
            print("[agente] 3 ordens na fila; automação cuida do resto")
        else:
            print(f"[agente] queue recusado: {status} {r}")

        # 3. acompanha pelo fluxo de eventos (percepção do agente)
        since = session.get("eventsFrom", 0)
        print("[agente] escutando eventos (Ctrl+C para sair)...")
        while True:
            batch = api.events(since)
            for ev in batch.get("events", []):
                kind = ev["type"]
                if kind == "say" or kind == "npc":
                    print(f"[{ev['t']:.0f}s] {ev.get('name', '?')}: {ev.get('text', '')}")
                elif kind == "order_walking":
                    o = ev.get("order", {})
                    print(f"[{ev['t']:.0f}s] indo executar ordem {o.get('id')} "
                          f"na célula {o.get('cell')}")
                elif kind == "order_failed":
                    o = ev.get("order", {})
                    print(f"[{ev['t']:.0f}s] ordem {o.get('id')} falhou: {ev.get('reason')}")
                elif kind == "job_started":
                    j = ev.get("job", {})
                    print(f"[{ev['t']:.0f}s] trabalho iniciado na célula {j.get('cell')} "
                          f"(dono {j.get('ownerName')})")
                elif kind == "job_done":
                    print(f"[{ev['t']:.0f}s] trabalho concluído: célula {ev['cell']} "
                          f"virou {ev['tile']} (por {ev.get('by', '?')})")
                elif kind == "join":
                    print(f"[{ev['t']:.0f}s] {ev['character']['name']} entrou")
                elif kind == "leave":
                    print(f"[{ev['t']:.0f}s] {ev['character']['name']} saiu")
                since = max(since, ev["seq"])
    except KeyboardInterrupt:
        print("\n[agente] encerrando...")
    finally:
        api.leave()
        print("[agente] sessão encerrada")


if __name__ == "__main__":
    main()
