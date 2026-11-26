#!/usr/bin/env bash
# Power's package check for Windows (intent Constraints). Usage: scripts/windows-packages.sh [windows-dir]
exec python3 -I "$(cd "$(dirname "$0")" && pwd)/windows-packages.py" "$@"
