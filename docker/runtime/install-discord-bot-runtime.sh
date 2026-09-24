#!/bin/sh
set -eu

if [ ! -f /app/tools/discord-bot/package-lock.json ]; then
	echo "ERROR: /app/tools/discord-bot/package-lock.json is missing."
	exit 1
fi

# Ubuntu's security mirror publishes an index before the packages it names, and
# rebuilds it while a build is running. A fetch then fails with a 404 for a .deb that
# existed a minute ago, or an index whose size does not match its own listing. Three
# image builds died on that in one afternoon, each on a commit whose code was fine.
# Retrying with a fresh index is the whole fix; the failure is never sticky.
apt_retry() {
	attempt=1
	while true; do
		apt-get update && apt-get "$@" && return 0
		if [ "$attempt" -ge 4 ]; then
			echo "ERROR: apt-get $* failed after $attempt attempts." >&2
			return 1
		fi
		echo "apt-get $* failed (attempt $attempt); the mirror may be mid-sync, retrying." >&2
		attempt=$((attempt + 1))
		sleep $((attempt * 5))
	done
}

# libgomp1 is whisper.cpp's OpenMP runtime: Whisper.net's native libraries link it
# and the aspnet base image does not carry it.
apt_retry install -y --no-install-recommends ca-certificates curl gnupg libcap2 libgomp1
curl -fsSL https://deb.nodesource.com/setup_24.x | bash -
apt_retry install -y --no-install-recommends nodejs

cd /app/tools/discord-bot
npm ci --omit=dev --no-audit --no-fund
find node_modules -type f -name "*.map" -delete
npm cache clean --force
node --version

apt-get purge -y --auto-remove curl gnupg
rm -rf /usr/lib/node_modules/npm \
	/usr/bin/npm \
	/usr/bin/npx \
	/usr/bin/corepack \
	/usr/include/node \
	/root/.npm \
	/usr/share/doc \
	/usr/share/man \
	/usr/share/info \
	/var/lib/apt/lists/*
find /tmp -mindepth 1 -maxdepth 1 ! -name listenarr-runtime -exec rm -rf {} +
