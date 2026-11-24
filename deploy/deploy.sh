#!/usr/bin/env bash
# Publish the API as a self-contained linux-x64 build and install it on this
# machine (intended to be run on the VPS, or from the repo with sudo).
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SERVICE_USER="${SERVICE_USER:-duo}"
TARGET="${TARGET:-/opt/duo}"
STAGE="$ROOT/artifacts/api"

echo "==> Publishing self-contained build to $STAGE"
dotnet publish "$ROOT/server/Duo.Api/Duo.Api.csproj" \
	-c Release -r linux-x64 --self-contained true -o "$STAGE"

echo "==> Installing to $TARGET"
sudo install -d -o "$SERVICE_USER" -g "$SERVICE_USER" "$TARGET"
sudo rsync -a --delete --chown="$SERVICE_USER:$SERVICE_USER" "$STAGE/" "$TARGET/"

echo "==> Installing systemd unit"
sudo install -m 0644 "$ROOT/deploy/duo-api.service" /etc/systemd/system/duo-api.service
sudo systemctl daemon-reload
sudo systemctl enable duo-api
sudo systemctl restart duo-api

echo "==> Health check"
sleep 1
curl -fsS http://127.0.0.1:5000/health
echo
echo "Done. Point Caddy at $TARGET and set DUO_DOMAIN (see deploy/Caddyfile)."
