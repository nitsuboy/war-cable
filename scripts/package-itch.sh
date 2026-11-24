#!/usr/bin/env bash
# Builds the static client and packs it for itch.io. The zip contains
# index.html at its root and uses relative asset paths.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

if [[ -z "${VITE_HUB_URL:-}" ]]; then
	echo "VITE_HUB_URL is required, e.g. https://203-0-113-7.sslip.io/hub" >&2
	exit 1
fi

echo "==> Building client with VITE_HUB_URL=$VITE_HUB_URL"
cd "$ROOT/web"
npm ci
VITE_HUB_URL="$VITE_HUB_URL" npm run build

echo "==> Packing itch.io zip"
rm -rf "$ROOT/artifacts/itch"
mkdir -p "$ROOT/artifacts/itch"
cp -r dist/. "$ROOT/artifacts/itch/"

rm -f "$ROOT/artifacts/duo-web.zip"
(
	cd "$ROOT/artifacts/itch"
	zip -qr "$ROOT/artifacts/duo-web.zip" .
)

echo "Created artifacts/duo-web.zip ($(du -h "$ROOT/artifacts/duo-web.zip" | cut -f1))"
echo "Upload it as an HTML game on itch.io. index.html is at the archive root."
