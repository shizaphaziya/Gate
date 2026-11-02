#!/usr/bin/env bash
set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

echo "=== Building and starting Gate Test Linux Container (Ubuntu 24.04 with systemd + SSH) ==="
docker compose up -d --build

echo "=== Checking container status ==="
docker ps --filter "name=gate-test-linux"

echo ""
echo "Test Linux is ready!"
echo "  SSH Address: 127.0.0.1"
echo "  SSH Port:    2222"
echo "  User:        root"
echo "  Password:    root"
echo "  Gate Port:   5050"
