#!/usr/bin/env bash
# Deploy a pull request to the NAS as soon as it is safe to.
#
#   deploy/unraid/deploy.sh <pr-number> [<pr-number> ...]
#
# For each PR, in order: wait for its fast checks and its deploy image (both run
# on the PR head, in parallel - .github/workflows/fast-check.yml and
# deploy-image.yml), merge it. Then pin the last PR's image
# - deploy-<version>-<sha7> - in docker-compose.yml, push that file to the NAS
# and restart.
#
# The pin is not committed. The NAS holds the compose file it runs, so that copy
# is the record of what is deployed; a commit of the same line to canary would
# only be a second, staler copy of it, and the PR it needed was one more thing to
# merge after every deploy. The working tree is left as it was found.
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
    # gh exits non-zero while any check is failing - including a previous run's,
    # in the moment after a push before the new runs exist - and set -e would
    # end the script on that, silently. The buckets are what matter.
    buckets=$( (gh pr checks "$pr" --json name,bucket 2>/dev/null || true) \
      | jq -r --arg g "$GATE" '[.[] | select(.name | test($g))] | (.[].bucket), (if length < 3 then "pending" else empty end)' \
      | sort -u | tr '\n' ' ')
    case "$buckets" in
      *fail*|*cancel*) echo "PR $pr: a gate check failed ($buckets)" >&2; gh pr checks "$pr" | grep -E "$GATE" | grep -v pass >&2; return 1 ;;
      *pending*|"")   sleep "$POLL" ;;
      *)              echo "PR $pr: gate green"; return 0 ;;
    esac
  done
}

# The tag of the deploy-image run for a commit, once that run has succeeded.
# Labelling a PR starts a second deploy-image run on the same commit, and the
# workflow's concurrency group cancels one of the pair. Reading only the newest
# run therefore reports a perfectly good build as a failure about half the time,
# so ask for every run on the commit: a success anywhere wins, and it is a real
# failure only when nothing is still running and nothing succeeded.
image_tag_for() {
  local sha=$1 runs run
  while true; do
    runs=$(gh run list --workflow deploy-image.yml --commit "$sha" --limit 20 \
      --json databaseId,status,conclusion \
      -q '.[] | "\(.databaseId) \(.status) \(.conclusion)"')
    run=$(echo "$runs" | grep " completed success$" | head -1 || true)
    [ -n "$run" ] && break
    if [ -n "$runs" ] && ! echo "$runs" | grep -qv " completed "; then
      echo "deploy-image for $sha failed:" >&2
      echo "$runs" >&2
      return 1
    fi
    sleep "$POLL"
  done
  # The tag is printed by the run's version step.
  gh run view "${run%% *}" --log 2>/dev/null | grep -oE "deploy-[0-9]+\.[0-9]+\.[0-9]+-[0-9a-f]{7}" | head -1
}

# A PR's image is the merge result only when its head already contains canary's tip.
# Otherwise the merge commit is what runs, and its own image (built on the push to
# canary) is the one to deploy.
head_is_current() {
  local pr=$1 head
  head=$(gh pr view "$pr" --json headRefOid -q .headRefOid)
  git fetch -q origin canary "$head"
  git merge-base --is-ancestor origin/canary "$head"
}

before=$(version)
last_tag=""
for pr in "$@"; do
  wait_green "$pr"
  head=$(gh pr view "$pr" --json headRefOid -q .headRefOid)
  current=false; head_is_current "$pr" && current=true
  # Not --delete-branch: that also checks out canary locally, which fails when
  # another worktree holds it, and the failure lands after the merge has gone
  # through. The remote branch is deleted separately once the merge is confirmed.
  gh pr merge "$pr" --merge >/dev/null
  merge_sha=$(gh pr view "$pr" --json mergeCommit -q .mergeCommit.oid)
  head_ref=$(gh pr view "$pr" --json headRefName -q .headRefName)
  gh api -X DELETE "repos/{owner}/{repo}/git/refs/heads/$head_ref" >/dev/null 2>&1 || true
  echo "PR $pr: merged as ${merge_sha:0:7} ($([ $current = true ] && echo "head was current" || echo "head was behind canary"))"
  last_head=$head; last_current=$current; last_merge=$merge_sha
done

# Only the last merge's result needs an image. A PR whose head was already on
# canary's tip has one from its PR run; otherwise the push to canary builds one.
if [ "$last_current" = true ]; then
  last_tag=$(image_tag_for "$last_head")
else
  echo "waiting for the merge commit's image"
  last_tag=$(image_tag_for "$last_merge")
fi
echo "image $last_tag"

[ -n "$last_tag" ] || exit 1
image="ghcr.io/nexalapp/listenarr:$last_tag"
docker manifest inspect "$image" >/dev/null 2>&1 || { echo "image $image not found" >&2; exit 1; }

# The pin is written into the working copy only to send it; it is put back below.
git checkout -q -- "$COMPOSE"
sed -i.bak "s|ghcr.io/nexalapp/listenarr:[^ ]*|$image|" "$COMPOSE" && rm -f "$COMPOSE.bak"
scp -q "$COMPOSE" "$NAS:$NAS_PROJECT/docker-compose.yml"
ssh "$NAS" "cd $NAS_PROJECT && docker compose up -d 2>&1 | tail -1; for i in \$(seq 1 60); do s=\$(curl -s -o /dev/null -w '%{http_code}' http://127.0.0.1:4545/api/v1/system/ready); [ \"\$s\" = 200 ] && break; sleep 5; done; echo ready=\$s; docker ps --filter name=listenarr --format '{{.Image}} {{.Status}}'"
echo "LIVE $image"

# The pin travelled to the NAS with the file above; the working copy goes back to
# what canary says, so the next deploy starts from a clean tree.
git checkout -q -- "$COMPOSE"
echo "PINNED $image (on the NAS; not committed)"

