#!/usr/bin/env sh
# scripts/run.sh — Run an example script with auto-detected Docker socket.
#
# Usage:
#   sh scripts/run.sh run.mjs        → oxlint plugin demo
#   sh scripts/run.sh run_probe.mjs  → SQL analysis probe
#   sh scripts/run.sh run_eslint.mjs → eslint plugin demo
#
# Supported runtimes (checked in order):
#   1. $DOCKER_HOST already set  → use as-is
#   2. Colima                    → ~/.colima/default/docker.sock
#   3. Docker Desktop (macOS)    → ~/.docker/run/docker.sock
#   4. Standard Linux socket     → /var/run/docker.sock

if [ -z "$DOCKER_HOST" ]; then
  if [ -S "$HOME/.colima/default/docker.sock" ]; then
    export DOCKER_HOST="unix://$HOME/.colima/default/docker.sock"
  elif [ -S "$HOME/.docker/run/docker.sock" ]; then
    export DOCKER_HOST="unix://$HOME/.docker/run/docker.sock"
  elif [ -S "/var/run/docker.sock" ]; then
    export DOCKER_HOST="unix:///var/run/docker.sock"
  fi
fi

export TESTCONTAINERS_RYUK_DISABLED=true

SCRIPT="${1:-run.mjs}"
exec node "$(dirname "$0")/../$SCRIPT"
