#!/bin/sh
# Dev entrypoint. Lives in a file rather than compose's `command:` so the nested
# quoting concurrently needs is not mangled by YAML folding.
set -e

echo "[dev] installing root dependencies"
npm install --no-audit --no-fund --silent

echo "[dev] installing frontend dependencies"
(cd /src/fe && npm install --no-audit --no-fund --silent)

echo "[dev] ffprobe: $(command -v ffprobe || echo 'MISSING')"
echo "[dev] starting API + web"

# --no-hot-reload: always rebuild and restart, never patch a running process.
#
# Hot reload cannot apply the edits this codebase gets most -- adding a type,
# deleting one, changing a signature -- and on one it stops at "Do you want to
# restart your app? Yes (y) / No (n)". Nothing is attached to this container's
# stdin to answer that, so watch waits forever and the API keeps serving the code
# it started with. That presents as a 404 on a route you just added, or a fix that
# does not take, rather than as a stalled reload -- an expensive thing to debug
# twice. DOTNET_WATCH_RESTART_ON_RUDE_EDIT and --non-interactive were both tried
# here and neither suppressed the prompt on .NET 10.
#
# The cost is a rebuild on every C# edit. That is most edits anyway, and a slower
# loop beats a silently stale one.
exec npx concurrently --names API,WEB --prefix-colors blue,green \
  "cd /src/listenarr.api && dotnet watch run --no-hot-reload --no-launch-profile --urls http://0.0.0.0:4545" \
  "cd /src/fe && npm run dev -- --host 0.0.0.0 --port 5173"
