#!/usr/bin/env bash
# Deploy a pull request to the NAS as soon as it is safe to.
#
#   deploy/unraid/deploy.sh <pr-number> [<pr-number> ...]
#
# For each PR, in order: wait for its fast checks and its deploy image (both run
# on the PR head, in parallel - .github/workflows/fast-check.yml and
# deploy-image.yml), merge it. Then pin the last PR's image
# - deploy-<version>-<sha7> - in docker-compose.yml, push it to the NAS and
# restart. Only then wait for canary's version bump and open the pin PR: the
# bump is off the critical path because the image already carries the version.
#
# Upstream's run-tests.yml (format, lint, Windows) still runs on every PR but is
# not waited for; review it after the deploy and fix forward.
#
# Requires: gh (logged in), ssh access to the NAS, a clean checkout on any branch.
set -euo pipefail

NAS=${NAS:-root@10.10.10.10}
NAS_PROJECT=${NAS_PROJECT:-/boot/config/plugins/compose.manager/projects/listenarr}
COMPOSE=deploy/unraid/docker-compose.yml
POLL=${POLL:-10}

[ $# -ge 1 ] || { echo "usage: $0 <pr-number> [<pr-number> ...]" >&2; exit 2; }

version() {
  git fetch -q origin canary
  git show origin/canary:listenarr.api/Listenarr.Api.csproj | grep -o "<Version>[^<]*" | cut -d'>' -f2
}

# The gate: the fast checks and the deploy image on the PR head must be green.
GATE='^(fast-backend|fast-frontend|image)$'
wait_green() {
  local pr=$1
  while true; do
    local buckets
    buckets=$(gh pr checks "$pr" --json name,bucket 2>/dev/null \
      | jq -r --arg g "$GATE" '[.[] | select(.name | test($g))] | if length < 3 then "pending" else .[].bucket end' \
      | sort -u | tr '\n' ' ')
    case "$buckets" in
      *fail*|*cancel*) echo "PR $pr: a gate check failed ($buckets)" >&2; gh pr checks "$pr" | grep -E "$GATE" | grep -v pass >&2; return 1 ;;
      *pending*|"")   sleep "$POLL" ;;
      *)              echo "PR $pr: gate green"; return 0 ;;
    esac
  done
}

image_tag() {
  # The deploy-image run for this PR head names its tag in the step summary.
  local pr=$1 sha
  sha=$(gh pr view "$pr" --json headRefOid -q .headRefOid)
  local run
  run=$(gh run list --workflow deploy-image.yml --commit "$sha" --status success --limit 1 --json databaseId -q '.[0].databaseId')
  [ -n "$run" ] || { echo "PR $pr: no successful deploy-image run for $sha" >&2; return 1; }
  # The tag is printed by the run's version step.
  gh run view "$run" --log 2>/dev/null | grep -oE "deploy-[0-9]+\.[0-9]+\.[0-9]+-[0-9a-f]{7}" | head -1
}

last_tag=""
before=$(version)
for pr in "$@"; do
  wait_green "$pr"
  tag=$(image_tag "$pr")
  gh pr merge "$pr" --merge --delete-branch >/dev/null
  echo "PR $pr: merged; image $tag"
  last_tag=$tag
done

[ -n "$last_tag" ] || exit 1
image="ghcr.io/nexalapp/listenarr:$last_tag"
docker manifest inspect "$image" >/dev/null 2>&1 || { echo "image $image not found" >&2; exit 1; }

# Restart the NAS first; the version bump and the pin PR can follow.
git checkout -q -- "$COMPOSE"
sed -i.bak "s|ghcr.io/nexalapp/listenarr:[^ ]*|$image|" "$COMPOSE" && rm -f "$COMPOSE.bak"
scp -q "$COMPOSE" "$NAS:$NAS_PROJECT/docker-compose.yml"
ssh "$NAS" "cd $NAS_PROJECT && docker compose up -d 2>&1 | tail -1; for i in \$(seq 1 60); do s=\$(curl -s -o /dev/null -w '%{http_code}' http://127.0.0.1:4545/api/v1/system/ready); [ \"\$s\" = 200 ] && break; sleep 5; done; echo ready=\$s; docker ps --filter name=listenarr --format '{{.Image}} {{.Status}}'"
echo "LIVE $image"

# canary.yml bumps the version once per merged PR; wait for the last one so the
# pin PR lands on the bumped canary.
bumps=$#
while :; do
  cur=$(version)
  IFS=. read -r a b c <<< "$cur"; IFS=. read -r x y z <<< "$before"
  [ $(( (a-x)*1000000 + (b-y)*1000 + (c-z) )) -ge "$bumps" ] && break
  sleep "$POLL"
done
ver=$cur
echo "canary now $ver"
pinned=$(mktemp); cp "$COMPOSE" "$pinned"
git checkout -q -- "$COMPOSE"
git checkout -q -b "chore/deploy-$ver" origin/canary
cp "$pinned" "$COMPOSE"; rm -f "$pinned"

git commit -q --no-verify -am "chore(deploy): pin $last_tag"
git push -q -u origin "chore/deploy-$ver" --no-verify
gh pr create --base canary --title "chore(deploy): pin $last_tag" --body "Deployed to the NAS." >/dev/null
gh pr edit "chore/deploy-$ver" --add-label patch >/dev/null
echo "PINNED $image"
