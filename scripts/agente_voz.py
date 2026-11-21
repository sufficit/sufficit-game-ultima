#!/usr/bin/env python3
"""Agente de voz do Ultima — peer WebRTC não-navegador (Marco 3, F5).

Usa a MESMA sinalização HTTP dos navegadores (/v1/net/{sala}/join|poll|signal)
e negocia um RTCPeerConnection via aiortc:
  - datachannel 'game' com as mensagens do protocolo (hi/pos/chat);
  - envia áudio sintético (tom calibrado) para os peers do navegador;
  - recebe o áudio dos navegadores e mede o nível (percepção) com audioop.

Uso:
  python3 scripts/agente_voz.py --api URL --sala vozt --nome "Agente (voz)" \
      --duracao 30 --fala "Olá do agente de voz!"

Requisitos: python3 + aiortc (pip install --user aiortc).
"""
import argparse
import asyncio
import audioop
import fractions
import json
import math
import secrets
import struct
import sys
import time
import urllib.error
import urllib.request

from aiortc import RTCConfiguration, RTCIceServer, RTCPeerConnection, RTCSessionDescription
from aiortc.mediastreams import AudioStreamTrack
from av import AudioFrame

STUN = RTCConfiguration(iceServers=[RTCIceServer(urls=[
    "stun:stun.l.google.com:19302", "stun:stun1.l.google.com:19302"])])


class TomSintetico(AudioStreamTrack):
    """Faixa de áudio contínua: senoide 440 Hz, mono s16 48 kHz (20 ms/quadro)."""
    kind = "audio"

    def __init__(self, freq: float = 440.0, volume: float = 0.12):
        super().__init__()
        self._freq, self._volume = freq, volume
        self._ts = 0
        self._start: float | None = None

    async def recv(self) -> AudioFrame:
        samples = 960  # 48 kHz * 20 ms
        if self._start is None:
            self._start = time.time()
        else:
            espera = self._start + (self._ts + samples) / 48000.0 - time.time()
            if espera > 0:
                await asyncio.sleep(espera)
        buf = bytearray()
        for i in range(samples):
            v = int(self._volume * 32767.0 * math.sin(2 * math.pi * self._freq * (self._ts + i) / 48000.0))
            buf += struct.pack("<h", max(-32767, min(32767, v)))
        frame = AudioFrame(format="s16", layout="mono", samples=samples)
        frame.planes[0].update(bytes(buf))
        frame.sample_rate = 48000
        frame.pts = self._ts
        frame.time_base = fractions.Fraction(1, 48000)
        self._ts += samples
        return frame


class Cliente:
    def __init__(self, api: str, sala: str, nome: str, x: float, z: float, fala: str, duracao: float):
        self.api, self.sala, self.nome = api.rstrip("/"), sala, nome[:40]
        self.x, self.z, self.fala, self.duracao = x, z, fala, duracao
        self.my_id = secrets.token_hex(4)  # 8 hex, como os ids do navegador
        self.peers: dict[str, dict] = {}
        self.audio = TomSintetico()
        self.stats = {"canais": 0, "quadros_recebidos": 0, "nivel_max": 0, "chat": 0}
        self._offer_timers: dict[str, asyncio.TimerHandle] = {}

    # ---------- sinalização HTTP (mesma do net.js) ----------
    def _http(self, path: str, body: dict | None = None, timeout: float = 25.0) -> dict:
        req = urllib.request.Request(self.api + path, method="POST" if body is not None else "GET")
        data = None
        if body is not None:
            data = json.dumps(body).encode()
            req.add_header("Content-Type", "application/json")
        with urllib.request.urlopen(req, data, timeout=timeout) as r:
            return json.loads(r.read())

    def join(self) -> list[str]:
        self._last_join = time.time()
        r = self._http(f"/v1/net/{self.sala}/join", {"peer": self.my_id, "name": self.nome}, 8)
        return [p for p in (r.get("peers") or []) if p != self.my_id]

    def signal(self, to: str, kind: str, data) -> None:
        self._http(f"/v1/net/{self.sala}/signal",
                   {"from": self.my_id, "to": to, "kind": kind, "data": data}, 8)

    async def poll_loop(self, stop_at: float) -> None:
        while time.time() < stop_at:
            try:
                r = await asyncio.to_thread(
                    self._http, f"/v1/net/{self.sala}/poll/{self.my_id}?wait=12")
                if r.get("expired"):
                    # servidor podou a presença (PRUNE_SECS=30): renova e segue
                    await asyncio.to_thread(self.join)
                    continue
            except Exception as e:
                print(f"[voz] poll falhou: {e}; reentrando")
                await asyncio.sleep(3)
                try:
                    await asyncio.to_thread(self.join)
                except Exception:
                    pass
                continue
            for pid in [p for p in (r.get("peers") or []) if p != self.my_id]:
                if pid not in self.peers:
                    self.conectar(pid)
            for m in r.get("messages") or []:
                await self.sinal(pid=m.get("from"), kind=m.get("kind"), data=m.get("data"))

    # ---------- WebRTC ----------
    def conectar(self, pid: str) -> None:
        print(f"[voz] peer descoberto: {pid} (polite={self.my_id < pid})")
        pc = RTCPeerConnection(STUN)
        peer = {"pc": pc, "polite": self.my_id < pid, "channel": None,
                "remote_pronto": False, "pendentes": [], "nome": None}
        self.peers[pid] = peer
        pc.addTrack(self.audio)  # áudio do agente para o navegador

        @pc.on("datachannel")
        def _(ch):
            self._ligar(pid, ch)

        @pc.on("connectionstatechange")
        def _():
            print(f"[voz] {pid[:6]} conexão: {pc.connectionState}")

        @pc.on("track")
        def _(track):
            if track.kind == "audio":
                asyncio.ensure_future(self._ouvir(track))

        # espelha o navegador: o lado "impaciente" cria o canal e insiste com a offer
        if not peer["polite"]:
            self._ligar(pid, pc.createDataChannel("game"))
            self._oferecer_de_novo(pid)

    def _ligar(self, pid: str, ch) -> None:
        peer = self.peers.get(pid)
        if not peer:
            return
        peer["channel"] = ch

        @ch.on("open")
        def _():
            self.stats["canais"] += 1
            ch.send(json.dumps({"t": "hi", "name": self.nome}))
            print(f"[voz] canal aberto com {pid[:6]}; hi enviado")

        @ch.on("message")
        def _(raw):
            try:
                msg = json.loads(raw)
            except (TypeError, ValueError):
                return
            if msg.get("t") == "hi":
                peer["nome"] = str(msg.get("name") or "")[:40]
                if not self.stats["canais"]:
                    # aiortc nem sempre dispara 'open' no lado que só recebe o canal
                    self.stats["canais"] = 1
                print(f"[voz] peer {pid[:6]} se chama {peer['nome']}")
            elif msg.get("t") == "chat":
                print(f"[voz] chat de {peer['nome'] or pid[:6]}: {msg.get('text','')[:80]}")

    def _oferecer_de_novo(self, pid: str, tentativa: int = 0) -> None:
        peer = self.peers.get(pid)
        if not peer or tentativa >= 24:
            return
        ch = peer["channel"]
        if ch is not None and ch.readyState == "open":
            return
        asyncio.ensure_future(self._enviar_offer(pid))
        loop = asyncio.get_event_loop()
        self._offer_timers[pid] = loop.call_later(
            2.5, self._oferecer_de_novo, pid, tentativa + 1)

    @staticmethod
    def _normalizar_transceivers(pc) -> None:
        # aiortc deixa direction=None em transceivers criados pela offer remota;
        # setLocalDescription quebraria com "None is not in list".
        for t in pc.getTransceivers():
            if t.direction is None:
                t.direction = "sendrecv" if t.kind == "audio" else "recvonly"

    async def _enviar_offer(self, pid: str) -> None:
        peer = self.peers.get(pid)
        if not peer or peer.get("fazendo_offer"):
            return
        try:
            peer["fazendo_offer"] = True
            self._normalizar_transceivers(peer["pc"])
            await peer["pc"].setLocalDescription()
            self.signal(pid, "offer", {"type": peer["pc"].localDescription.type,
                                       "sdp": peer["pc"].localDescription.sdp})
        except Exception as e:
            print(f"[voz] offer para {pid[:6]} falhou: {e}")
        finally:
            peer["fazendo_offer"] = False

    async def _add_ice(self, pc, data) -> None:
        # navegador envia candidate.toJSON(): {candidate, sdpMid, sdpMLineIndex, usernameFragment}
        from aiortc.sdp import candidate_from_sdp
        items = data if isinstance(data, list) else [data]
        for d in items:
            if not isinstance(d, dict):
                continue
            line = d.get("candidate") or ""
            if not line:
                continue
            if line.startswith("candidate:"):
                line = line[len("candidate:"):]
            try:
                c = candidate_from_sdp(line)
                mid = d.get("sdpMid")
                mline = d.get("sdpMLineIndex")
                c.sdpMid = str(mid) if mid is not None else "0"
                c.sdpMLineIndex = int(mline) if mline is not None else 0
                print(f"[voz] ICE recebido: mid={c.sdpMid!r} mline={c.sdpMLineIndex} ip={c.ip}:{c.port}")
                await pc.addIceCandidate(c)
            except Exception as e:
                print(f"[voz] ICE ignorado (payload={str(d)[:220]!r}): {type(e).__name__}: {e}")

    async def sinal(self, pid, kind, data) -> None:
        if not pid or pid == self.my_id or pid not in self.peers or not data:
            return
        peer = self.peers[pid]
        pc: RTCPeerConnection = peer["pc"]
        try:
            if kind == "offer":
                colisao = pc.signalingState == "have-local-offer" and not peer["polite"]
                if colisao:
                    return  # impaciente ignora offer durante a própria offer
                await pc.setRemoteDescription(RTCSessionDescription(data["sdp"], data["type"]))
                peer["remote_pronto"] = True
                for _, d in peer["pendentes"]:
                    await self._add_ice(pc, d)
                peer["pendentes"] = []
                self._normalizar_transceivers(pc)
                # offer sem m-line de áudio (navegador sem microfone): o transceiver local
                # não casou com nenhuma m-line e _offerDirection fica None -> aiortc quebra.
                for t in pc.getTransceivers():
                    if getattr(t, "_offerDirection", "x") is None:
                        t._offerDirection = "inactive"
                await pc.setLocalDescription()
                self.signal(pid, "answer", {"type": pc.localDescription.type,
                                            "sdp": pc.localDescription.sdp})
            elif kind == "answer":
                if pc.signalingState == "have-local-offer":
                    await pc.setRemoteDescription(RTCSessionDescription(data["sdp"], data["type"]))
                    peer["remote_pronto"] = True
                    for _, d in peer["pendentes"]:
                        await self._add_ice(pc, d)
                    peer["pendentes"] = []
            elif kind == "ice":
                if peer["remote_pronto"]:
                    await self._add_ice(pc, data)
                else:
                    peer["pendentes"].append(("ice", data))
            elif kind == "bye":
                await pc.close()
        except Exception:
            import traceback
            print(f"[voz] sinal {kind} de {pid[:6]} falhou:")
            traceback.print_exc()

    # ---------- percepção de áudio do navegador ----------
    async def _ouvir(self, track) -> None:
        ultima = time.time()
        primeiro = True
        while True:
            try:
                frame = await asyncio.wait_for(track.recv(), timeout=20)
            except Exception:
                return
            self.stats["quadros_recebidos"] += 1
            if primeiro:
                primeiro = False
                try:
                    print(f"[voz] primeiro quadro: format={getattr(frame, 'format', '?')} layout={getattr(frame, 'layout', '?')} samples={frame.samples} planes={len(frame.planes)}")
                except Exception as e:
                    print(f"[voz] falha ao descrever quadro: {e!r}")
            if time.time() - ultima >= 2.0:
                try:
                    pcm = bytes(frame.planes[0])
                    nivel = audioop.rms(pcm, 2)
                    self.stats["nivel_max"] = max(self.stats["nivel_max"], nivel)
                    print(f"[voz] nível de áudio recebido: RMS {nivel} ({len(pcm)} bytes)")
                except Exception as e:
                    print(f"[voz] erro RMS: {e!r}")
                ultima = time.time()

    # ---------- ciclo principal ----------
    async def rodar(self) -> None:
        print(f"[voz] agente {self.nome} (id {self.my_id}) entrando na sala {self.sala}")
        outros = await asyncio.to_thread(self.join)
        print(f"[voz] peers na sala: {len(outros)}")
        stop_at = time.time() + self.duracao
        tarefa_poll = asyncio.ensure_future(self.poll_loop(stop_at))
        enviados = 0
        try:
            while time.time() < stop_at:
                await asyncio.sleep(0.5)
                for pid, peer in self.peers.items():
                    ch = peer["channel"]
                    if ch and ch.readyState == "open":
                        ch.send(json.dumps({"t": "pos", "x": self.x, "z": self.z, "ry": 0}))
                        enviados += 1
                if self.fala and self.stats["canais"] and self.stats["chat"] == 0:
                    for pid, peer in self.peers.items():
                        ch = peer["channel"]
                        if ch and ch.readyState == "open":
                            ch.send(json.dumps({"t": "chat", "text": self.fala[:300]}))
                            self.stats["chat"] += 1
                            print(f"[voz] chat enviado para {pid[:6]}")
        finally:
            tarefa_poll.cancel()
            try:
                await asyncio.to_thread(self._http, f"/v1/net/{self.sala}/leave", {"peer": self.my_id}, 5)
            except Exception:
                pass
            for peer in self.peers.values():
                await peer["pc"].close()
        print(f"[voz] fim: {self.stats['canais']} canal(is), {enviados} posições, "
              f"{self.stats['chat']} chat(s), {self.stats['quadros_recebidos']} quadros recebidos, "
              f"RMS máx {self.stats['nivel_max']}")


def main() -> None:
    ap = argparse.ArgumentParser(description="Agente de voz do Ultima (peer WebRTC)")
    ap.add_argument("--api", default="https://w-5bee2eba74174628a92a487c6a938f9b.run.sufficit.com.br")
    ap.add_argument("--sala", default="jardim")
    ap.add_argument("--nome", default="Agente de Voz")
    ap.add_argument("--x", type=float, default=3.0)
    ap.add_argument("--z", type=float, default=5.0)
    ap.add_argument("--fala", default="Olá! Sou o agente de voz do Ultima.")
    ap.add_argument("--duracao", type=float, default=30.0)
    args = ap.parse_args()
    cliente = Cliente(args.api, args.sala, args.nome, args.x, args.z, args.fala, args.duracao)
    try:
        asyncio.run(cliente.rodar())
    except KeyboardInterrupt:
        print("\n[voz] interrompido")
        sys.exit(130)


if __name__ == "__main__":
    main()
