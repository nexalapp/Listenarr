#!/usr/bin/env bash

# ---------------------------------------------------------------------------
# Listenarr task runner (fork-local).
#
# Everything runs through the Docker dev environment rather than natively:
# macOS filesystem scanning is broken upstream (see the fstat$INODE64 and
# UnixOpenFlags arch-gate issues), so the container is the only place the
# library scan actually works on a Mac.
#
#   ./run.sh dev        start the stack
#   ./run.sh help       list every target
# ---------------------------------------------------------------------------

COMPOSE_FILE="docker-compose.dev.yml"
CONTAINER="listenarr-listenarr-dev-1"
API_URL="http://localhost:4545"
WEB_URL="http://localhost:5173"

# Folder bind-mounted at /audiobooks inside the container. Override per shell:
#   LISTENARR_DEV_LIBRARY=/path/to/books ./run.sh dev
export LISTENARR_DEV_LIBRARY="${LISTENARR_DEV_LIBRARY:-../audiobooks-test}"
# Where the download client's completed files are readable from this machine, and the
# path the client reports them under. Import resolves files by the client's own path,
# so the two must line up. Mounted read-only.
export LISTENARR_DEV_DOWNLOADS="${LISTENARR_DEV_DOWNLOADS:-./dev-downloads}"
export LISTENARR_DEV_DOWNLOADS_TARGET="${LISTENARR_DEV_DOWNLOADS_TARGET:-/downloads}"


# -- Dev environment --

dev() {
    if [ ! -d "$LISTENARR_DEV_LIBRARY" ]; then
        echo "Warning: library folder '$LISTENARR_DEV_LIBRARY' does not exist."
        echo "         Set LISTENARR_DEV_LIBRARY to point at your audiobooks."
    fi
    if [ "$LISTENARR_DEV_DOWNLOADS" != "./dev-downloads" ] && [ ! -d "$LISTENARR_DEV_DOWNLOADS" ]; then
        echo "Warning: downloads folder '$LISTENARR_DEV_DOWNLOADS' does not exist."
        echo "         Imports will find no files to import."
    fi
    echo "Starting Listenarr (library: $LISTENARR_DEV_LIBRARY)"
    docker compose -f "$COMPOSE_FILE" up -d
    wait_for_api
}

stop() {
    docker compose -f "$COMPOSE_FILE" down
}

restart() {
    # Needed after structural C# edits: hot reload cannot apply deleted fields or
    # types and dotnet watch halts with ENC0033 rather than rebuilding.
    #
    # "up -d" rather than "restart": restart reuses the existing container, so a
    # changed mount or environment variable is silently ignored and you debug a
    # container that never picked the change up. up recreates only when the config
    # actually changed, so it is no slower in the common case.
    docker compose -f "$COMPOSE_FILE" up -d
    wait_for_api
}

rebuild() {
    docker compose -f "$COMPOSE_FILE" up -d --build
    wait_for_api
}

logs() {
    docker compose -f "$COMPOSE_FILE" logs -f "$@"
}

api_logs() {
    docker logs -f "$CONTAINER" 2>&1 | grep --line-buffered '\[API\]'
}

shell() {
    docker exec -it "$CONTAINER" bash
}

status() {
    docker ps --filter "name=listenarr" --format '  {{.Names}}  {{.Status}}'
    printf '  API %s -> %s\n' "$API_URL" "$(curl -s -o /dev/null -w '%{http_code}' -m 5 "$API_URL/api/v1/system/ready" || echo down)"
    printf '  WEB %s -> %s\n' "$WEB_URL" "$(curl -s -o /dev/null -w '%{http_code}' -m 5 "$WEB_URL" || echo down)"
    printf '  ffprobe: %s\n' "$(docker exec "$CONTAINER" sh -c 'command -v ffprobe' 2>/dev/null || echo 'unavailable')"
}

wait_for_api() {
    printf 'Waiting for the API'
    for _ in $(seq 1 120); do
        if curl -sf -m 3 "$API_URL/api/v1/system/ready" >/dev/null 2>&1; then
            printf '\n  API  %s\n  WEB  %s\n' "$API_URL" "$WEB_URL"
            return 0
        fi
        printf '.'
        sleep 5
    done
    printf '\nStill not up. Check: ./run.sh logs\n'
    return 1
}


# -- Tests --

tests() {
    # Filter with: ./run.sh tests "FullyQualifiedName~PathMetadataParser"
    local filter="${1:-}"
    if [ -n "$filter" ]; then
        dotnet test --filter "$filter"
    else
        dotnet test
    fi
}

frontend_tests() {
    (cd fe && npm run test:unit)
}


# -- Upstream sync --

sync_upstream() {
    # Feature branches must be cut from upstream/canary, never from this fork's
    # canary, so fork-only files (this script included) cannot reach a PR.
    git fetch upstream
    echo "Behind upstream/canary by $(git rev-list --count HEAD..upstream/canary) commit(s)."
    echo "Start work with: git checkout -b <branch> upstream/canary"
}


help() {
    echo "A list of valid targets includes: "
    for func in $(declare -F | awk '{print $3}'); do
        echo "  - $func"
    done
}

# Check if an argument is provided
if [ $# -eq 0 ]; then
    echo "Please specify a target."
    exit 1
fi

# Check if the specified target is a function
if declare -F "$1" > /dev/null; then
    func="$1"
    shift
    "$func" "$@"
else
    echo ""
    echo "Error: '$1' is not a valid target"
    echo ""
    exit 1
fi
