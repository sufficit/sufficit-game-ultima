#!/usr/bin/env python3
"""Run the C# Guardian tool qualification against pinned, local GGUFs."""

import argparse
from contextlib import contextmanager
from functools import partial
import hashlib
import http.server
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tempfile
import threading
import time
from urllib.request import urlopen


HERE = Path(__file__).resolve().parent
REPO = HERE.parents[1]
RUNTIME = {
    "wllama.mjs": ("esm/index.min.js", "e7f3745ee508c111c22d178d23c52cc74016dd8cec39e4600d87e28d287d7981"),
    "wllama.wasm": ("esm/wasm/wllama.wasm", "79ac5be917b71d50571a4f584436a9f6baf6b6e57719d094ed8bd950e9a65780"),
}
VERSION = "3.13.0"


class QuietFiles(http.server.SimpleHTTPRequestHandler):
    def log_message(self, *args):
        pass

    def end_headers(self):
        self.send_header("Access-Control-Allow-Origin", "*")
        super().end_headers()


@contextmanager
def serve(directory):
    with http.server.ThreadingHTTPServer(("127.0.0.1", 0), partial(QuietFiles, directory=directory)) as server:
        worker = threading.Thread(target=server.serve_forever, daemon=True)
        worker.start()
        try:
            yield f"http://127.0.0.1:{server.server_port}"
        finally:
            server.shutdown()
            worker.join()


def sha256(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def fetch(directory, name, url, digest):
    if not url.startswith("https://") or not re.fullmatch("[a-f0-9]{64}", digest):
        raise ValueError(f"{name} needs an HTTPS URL and a lowercase SHA-256 digest.")
    path = directory / name
    if path.is_file() and sha256(path) == digest:
        return path
    # Never serve an unverified partial download.
    with tempfile.NamedTemporaryFile(dir=directory, delete=False) as stream:
        pending = Path(stream.name)
        try:
            with urlopen(url, timeout=40) as response:
                shutil.copyfileobj(response, stream, 1024 * 1024)
            stream.flush()
            if sha256(pending) != digest:
                raise ValueError(f"SHA-256 mismatch for {name}.")
            stream.close()
            pending.replace(path)
        finally:
            pending.unlink(missing_ok=True)
    return path


@contextmanager
def disposable_api(server_dll, log_path):
    with tempfile.TemporaryDirectory(prefix="ultima-cpu-garden-") as data, log_path.open("wb") as log:
        env = {
            **os.environ,
            "ASPNETCORE_URLS": "http://127.0.0.1:0",
            "ULTIMA_DATA_DIR": data,
            "NPC_SERVER_AI": "off",
            "MESHY_API_KEY": "",
        }
        running = subprocess.Popen(["dotnet", str(server_dll)], cwd=server_dll.parent,
                                   env=env, stdout=log, stderr=subprocess.STDOUT)
        try:
            for _ in range(150):
                if running.poll() is not None:
                    break
                match = re.search(r"Now listening on: (http://127\.0\.0\.1:\d+)",
                                  log_path.read_text(errors="replace"))
                if match:
                    yield match[1]
                    return
                time.sleep(0.2)
            raise RuntimeError(f"Disposable API did not start. See {log_path}.")
        finally:
            if running.poll() is None:
                running.terminate()
                try:
                    running.wait(timeout=10)
                except subprocess.TimeoutExpired:
                    running.kill()
                    running.wait()


def records(log):
    for line in log.read_text(errors="replace").splitlines():
        if line.startswith('{"trial":'):
            yield json.loads(line)


def compare(options):
    models = json.loads(options.manifest.read_text())["models"]
    selection = options.models or list(models)
    if not selection or any(not re.fullmatch("[a-z0-9][a-z0-9-]{0,63}", key) or key not in models
                            for key in selection) or len(set(selection)) != len(selection):
        raise ValueError("Select distinct model IDs present in the manifest.")
    for key in selection:
        entry = models[key]
        if (not isinstance(entry, dict) or not isinstance(entry.get("url"), str) or
                not isinstance(entry.get("sha256"), str)):
            raise ValueError(f"{key} needs a url and sha256 in the manifest.")
    if not (options.site / "index.html").is_file():
        raise ValueError("Publish the client first or pass --site with its wwwroot.")
    if not options.server.is_file():
        raise ValueError("Build the standalone API first or pass --server with its DLL.")

    options.cache.mkdir(parents=True, exist_ok=True)
    options.output.mkdir(parents=True, exist_ok=True)
    for name, (path, digest) in RUNTIME.items():
        fetch(options.cache, name, f"https://cdn.jsdelivr.net/npm/@wllama/wllama@{VERSION}/{path}", digest)

    results = []
    with serve(options.site) as site, serve(options.cache) as cache:
        for key in selection:
            print(f"Running {key}", flush=True)
            spec = models[key]
            entry = {"model": key, "url": spec["url"], "sha256": spec["sha256"]}
            log_path = options.output / f"{key}.log"
            try:
                artifact = fetch(options.cache, f"{key}.gguf", spec["url"], spec["sha256"])
                entry["bytes"] = artifact.stat().st_size
                with disposable_api(options.server.resolve(), options.output / f"{key}.server.log") as api:
                    env = {
                        **os.environ, "ULTIMA_SITE": site, "ULTIMA_TOOL_CACHE": cache,
                        "ULTIMA_GARDEN_API": api, "ULTIMA_CPU_TOOL_TRIAL": key,
                        "ULTIMA_MODE": options.mode, "ULTIMA_CHROMIUM_ARGS": options.chromium_args,
                        "ULTIMA_BUILD": "",
                    }
                    with log_path.open("wb") as log:
                        process = subprocess.run(
                            ["dotnet", "run", "--project", str(HERE), "-c", "Release", "--no-build",
                             "--", "guardian-cpu-tools"], cwd=REPO, env=env,
                            stdout=log, stderr=subprocess.STDOUT, timeout=1200,
                        )
                    events = list(records(log_path))
                    entry.update(exitCode=process.returncode, events=events)
                    verdict = next((event["qualified"] for event in events if "qualified" in event), None)
                    loaded = next((event["ready"] for event in events if "ready" in event), None)
                    entry["status"] = ("qualified" if verdict and process.returncode == 0 else
                                       "rejected" if verdict is False else
                                       "load-failed" if loaded is False else "error")
            except (OSError, ValueError, RuntimeError, subprocess.TimeoutExpired) as exc:
                entry.update(status="error", error=str(exc))
            results.append(entry)
            (options.output / "results.json").write_text(
                json.dumps({"runtime": VERSION, "renderer": options.mode, "models": results},
                           ensure_ascii=False, indent=2) + "\n")
            print(f"{key}: {entry['status']}", flush=True)
    return 2 if any(r["status"] == "error" for r in results) else int(
        any(r["status"] != "qualified" for r in results))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--manifest", type=Path, default=HERE / "cpu-models.json")
    parser.add_argument("--cache", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--site", type=Path, default=REPO / "src/bin/Release/net10.0/publish/wwwroot")
    parser.add_argument("--server", type=Path, default=REPO / "app/bin/Release/net10.0/UltimaServer.dll")
    parser.add_argument("--mode", choices=("main", "worker"), default="worker")
    parser.add_argument("--chromium-args", default="--use-angle=swiftshader --enable-unsafe-swiftshader")
    parser.add_argument("models", nargs="*")
    try:
        raise SystemExit(compare(parser.parse_args()))
    except (OSError, ValueError, TypeError, KeyError) as error:
        print(error, file=sys.stderr)
        raise SystemExit(2) from None
