#!/usr/bin/env bash
# Build and deploy the game server using Docker Compose on Linux.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT/server"

echo "==> Building and starting Docker container..."
docker compose down --remove-orphans
docker compose up --build -d

echo "==> Health check..."
sleep 2
curl -fsS http://127.0.0.1:5000/health
echo
echo "Done. Container running on port 5000 (proxied via Caddy)."
