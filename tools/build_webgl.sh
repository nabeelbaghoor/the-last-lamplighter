#!/bin/bash
# Builds Builds/WebGL with the windowed editor (batchmode licensing does not work on this Mac).
set -u
P="$(cd "$(dirname "$0")/.." && pwd)"
U="${UNITY:-/Applications/Unity/Hub/Editor/2021.3.7f1/Unity.app/Contents/MacOS/Unity}"
mkdir -p "$P/Builds"
rm -f "$P/Builds/build-report.txt"
"$U" -projectPath "$P" -buildTarget WebGL -executeMethod Lamplighter.EditorTools.LamplighterBuild.BuildWebGLFromCli -logFile "$P/Builds/unity-build.log" &
UPID=$!
( sleep "${TIMEOUT:-2400}"; kill $UPID 2>/dev/null && echo "WATCHDOG KILLED UNITY" ) &
WPID=$!
wait $UPID
CODE=$?
pkill -P $WPID 2>/dev/null; kill $WPID 2>/dev/null
echo "UNITY EXIT: $CODE"
cat "$P/Builds/build-report.txt" 2>/dev/null
ls -la "$P/Builds/WebGL/Build" 2>/dev/null
exit $CODE
