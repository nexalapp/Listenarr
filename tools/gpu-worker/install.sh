#!/usr/bin/env bash
# Builds the GPU worker's environment and installs it as a systemd user service.
#
#   LISTENARR_URL=http://10.10.10.10:4545 LISTENARR_API_KEY=... ./install.sh [dir]
#
# Needs uv, an Nvidia driver, and the model weights copied from Listenarr's own install
# (config/enhancer/current/models) into <dir>/models. Measured on an RTX 5080.
set -euo pipefail

DIR="${1:-$HOME/listenarr-gpu}"
UV="${UV:-$(command -v uv || echo "$HOME/.local/bin/uv")}"
HERE="$(cd "$(dirname "$0")" && pwd)"
mkdir -p "$DIR"
cd "$DIR"

[ -d models/enhancer_stage2 ] || { echo "copy the model weights to $DIR/models first" >&2; exit 1; }

if [ ! -x venv/bin/python ]; then
  "$UV" venv -q --python 3.11 venv
fi

# torch 2.1.1, which Listenarr's CPU install pins, predates Blackwell (sm_120).
"$UV" pip install -q --python venv --index-url https://download.pytorch.org/whl/cu128 \
  torch==2.8.0 torchaudio==2.8.0
# resemble-enhance pins torch 2.1.1, so it goes in without its dependencies and they
# follow at its own pins, torch excepted.
"$UV" pip install -q --python venv --no-deps resemble-enhance==0.0.1
"$UV" pip install -q --python venv \
  celluloid==0.2.0 librosa==0.10.1 matplotlib==3.8.1 numpy==1.26.2 omegaconf==2.3.0 \
  pandas==2.1.3 ptflops==0.7.1.2 rich==13.7.0 scipy==1.11.4 soundfile==0.12.1 \
  tqdm==4.66.1 resampy==0.4.2 tabulate==0.8.10 \
  setuptools wheel ninja py-cpuinfo hjson pydantic psutil packaging einops msgpack

# deepspeed: imported at module load, never called at inference. 0.12.4 breaks on
# torch 2.8, and every version insists on a CUDA toolkit it will not use -- at install
# and on import. A stand-in nvcc that only reports a version satisfies it.
mkdir -p fakecuda/bin
printf '#!/bin/sh\necho "Cuda compilation tools, release 12.8, V12.8.93"\n' > fakecuda/bin/nvcc
chmod +x fakecuda/bin/nvcc
CUDA_HOME="$DIR/fakecuda" DS_BUILD_OPS=0 "$UV" pip install -q --python venv \
  --no-build-isolation --no-deps deepspeed==0.17.6

install -m 0755 "$HERE/listenarr_gpu_worker.py" "$DIR/listenarr_gpu_worker.py"

if [ ! -f worker.env ]; then
  umask 077
  cat > worker.env <<ENV
LISTENARR_URL=${LISTENARR_URL:?set LISTENARR_URL}
LISTENARR_API_KEY=${LISTENARR_API_KEY:?set LISTENARR_API_KEY}
LISTENARR_WORKER_ID=$(hostname)
ENHANCER_RUN_DIR=$DIR/models/enhancer_stage2
CUDA_HOME=$DIR/fakecuda
HF_HUB_OFFLINE=1
PYTHONUNBUFFERED=1
ENV
fi

mkdir -p "$HOME/.config/systemd/user"
sed "s#@DIR@#$DIR#g" "$HERE/listenarr-gpu-worker.service" > "$HOME/.config/systemd/user/listenarr-gpu-worker.service"
systemctl --user daemon-reload
systemctl --user enable --now listenarr-gpu-worker
# Without linger a user service stops at logout and never starts at boot.
loginctl enable-linger "$USER" || echo "run: sudo loginctl enable-linger $USER" >&2
systemctl --user --no-pager status listenarr-gpu-worker | head -5
