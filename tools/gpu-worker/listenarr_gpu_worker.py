#!/usr/bin/env python3
"""Listenarr's audio-repair enhancer, run on a machine with a GPU.

Pulls repair chunks from Listenarr over HTTP, runs Resemble Enhance on the card with
the model kept loaded between chunks, and puts the result back. Listenarr does
everything else -- extraction, the music guard, reassembly, the encode -- and runs
the model on its own CPU whenever no worker is asking.

The card is shared: this box has a day job (KoboldCPP), so the worker asks for work
only while enough of the card is free, says it is busy otherwise, and gives the
memory back between chunks and entirely after a spell of idleness.

Settings come from the environment:
  LISTENARR_URL       e.g. http://10.10.10.10:4545
  LISTENARR_API_KEY   Listenarr's API key
  LISTENARR_WORKER_ID name shown in Listenarr's logs (default: the hostname)
  ENHANCER_RUN_DIR    the model directory (enhancer_stage2)
  ENHANCER_NEED_MB    free card memory needed to take a chunk (default 12500)
  ENHANCER_IDLE_UNLOAD_S  unload the model after this long without work (default 600)
"""

from __future__ import annotations

import gc
import http.client
import json
import logging
import os
import shutil
import socket
import sys
import tempfile
import time
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path

log = logging.getLogger("listenarr-gpu")

# Same settings as Listenarr's own CLI call: --nfe 16, the CLI's defaults otherwise.
NFE = 16
SOLVER = "midpoint"
LAMBD = 1.0
TAU = 0.5


class Api:
    def __init__(self, base: str, key: str, worker: str):
        self.base = base.rstrip("/") + "/api/v1/repair/worker"
        self.key = key
        self.worker = worker

    def _request(self, method: str, path: str, body: dict | None = None, timeout: float = 60):
        data = json.dumps(body).encode() if body is not None else None
        req = urllib.request.Request(self.base + path, data=data, method=method)
        req.add_header("X-Api-Key", self.key)
        if data is not None:
            req.add_header("Content-Type", "application/json")
        with urllib.request.urlopen(req, timeout=timeout) as resp:
            raw = resp.read()
            return resp.status, (json.loads(raw) if raw else None)

    def claim(self, ready: bool):
        status, task = self._request("POST", "/claim", {"workerId": self.worker, "ready": ready}, timeout=40)
        return task if status == 200 else None

    def download(self, task_id: str, dest: Path):
        q = urllib.parse.urlencode({"workerId": self.worker})
        req = urllib.request.Request(f"{self.base}/tasks/{task_id}/input?{q}")
        req.add_header("X-Api-Key", self.key)
        with urllib.request.urlopen(req, timeout=300) as resp, open(dest, "wb") as out:
            shutil.copyfileobj(resp, out, 1 << 20)

    def upload(self, task_id: str, src: Path) -> bool:
        # Streamed from disk with a length, so a 200 MB chunk never sits in memory twice.
        parts = urllib.parse.urlsplit(self.base)
        conn_type = http.client.HTTPSConnection if parts.scheme == "https" else http.client.HTTPConnection
        conn = conn_type(parts.netloc, timeout=300)
        q = urllib.parse.urlencode({"workerId": self.worker})
        try:
            with open(src, "rb") as body:
                conn.request(
                    "PUT",
                    f"{parts.path}/tasks/{task_id}/output?{q}",
                    body=body,
                    headers={
                        "X-Api-Key": self.key,
                        "Content-Type": "audio/wav",
                        "Content-Length": str(src.stat().st_size),
                    },
                )
                resp = conn.getresponse()
                resp.read()
                return resp.status == 204
        finally:
            conn.close()

    def fail(self, task_id: str, reason: str):
        try:
            self._request("POST", f"/tasks/{task_id}/fail", {"workerId": self.worker, "reason": reason[:500]})
        except Exception as exc:  # the chunk times out on the server side anyway
            log.warning("could not report the failure: %s", exc)


class Model:
    """Resemble Enhance, loaded on first use and dropped after idleness."""

    def __init__(self, run_dir: Path):
        self.run_dir = run_dir
        self.loaded = False
        import torch  # noqa: F401  (fail at start, not at the first chunk)

    def free_mb(self) -> float:
        import torch

        free, _ = torch.cuda.mem_get_info()
        # Memory this process holds cached counts as ours to use.
        return (free + torch.cuda.memory_reserved()) / 2**20

    def enhance(self, src: Path, dst: Path):
        import torch
        import torchaudio
        from resemble_enhance.enhancer.inference import enhance

        with torch.inference_mode():
            dwav, sr = torchaudio.load(str(src))
            dwav = dwav.mean(0)
            hwav, sr = enhance(
                dwav=dwav, sr=sr, device="cuda", nfe=NFE, solver=SOLVER,
                lambd=LAMBD, tau=TAU, run_dir=self.run_dir,
            )
            torchaudio.save(str(dst), hwav[None].cpu(), sr)
        self.loaded = True
        # Hand the activations back so the card's other tenant can load between chunks.
        torch.cuda.empty_cache()

    def unload(self):
        if not self.loaded:
            return
        import torch
        from resemble_enhance.enhancer.inference import load_enhancer

        load_enhancer.cache_clear()
        gc.collect()
        torch.cuda.empty_cache()
        self.loaded = False
        log.info("model unloaded after idling; the card is free")


def main() -> int:
    logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(message)s")
    base = os.environ["LISTENARR_URL"]
    key = os.environ["LISTENARR_API_KEY"]
    worker = os.environ.get("LISTENARR_WORKER_ID") or socket.gethostname()
    run_dir = Path(os.environ["ENHANCER_RUN_DIR"])
    need_mb = float(os.environ.get("ENHANCER_NEED_MB", "12500"))
    idle_unload = float(os.environ.get("ENHANCER_IDLE_UNLOAD_S", "600"))

    api = Api(base, key, worker)
    model = Model(run_dir)
    scratch = Path(tempfile.mkdtemp(prefix="listenarr-gpu-"))
    last_work = time.monotonic()
    was_ready = None
    log.info("worker %s polling %s", worker, base)

    while True:
        if model.loaded and time.monotonic() - last_work > idle_unload:
            model.unload()

        free = model.free_mb()
        ready = free >= need_mb
        if ready != was_ready:
            log.info("card %s (%.0f MB free, %.0f needed)", "free" if ready else "busy", free, need_mb)
            was_ready = ready

        try:
            task = api.claim(ready)
        except (urllib.error.URLError, OSError, http.client.HTTPException) as exc:
            log.warning("Listenarr unreachable: %s", exc)
            time.sleep(15)
            continue

        if task is None:
            if not ready:
                time.sleep(10)
            continue

        task_id = task["id"]
        src = scratch / "in.wav"
        dst = scratch / "out.wav"
        started = time.monotonic()
        try:
            api.download(task_id, src)
            model.enhance(src, dst)
            accepted = api.upload(task_id, dst)
            took = time.monotonic() - started
            log.info(
                "chunk %s: %.0fs of audio in %.1fs (%.1fx)%s",
                task_id, task["durationSeconds"], took, task["durationSeconds"] / took,
                "" if accepted else " -- Listenarr no longer wanted it",
            )
        except Exception as exc:  # report and keep serving; one bad chunk is not the worker
            log.exception("chunk %s failed", task_id)
            api.fail(task_id, f"{type(exc).__name__}: {exc}")
            try:
                import torch

                torch.cuda.empty_cache()
            except Exception:
                pass
            time.sleep(5)
        finally:
            for f in (src, dst):
                f.unlink(missing_ok=True)
            last_work = time.monotonic()


if __name__ == "__main__":
    sys.exit(main())
