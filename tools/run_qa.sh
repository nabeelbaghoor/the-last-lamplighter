#!/bin/bash
# Runs the automated QA suite (editor play mode). Wakes the display first: a sleeping display hangs the editor.
P="$(cd "$(dirname "$0")/.." && pwd)"
U="${UNITY:-/Applications/Unity/Hub/Editor/2021.3.7f1/Unity.app/Contents/MacOS/Unity}"
mkdir -p "$P/Builds"; rm -f "$P/Builds/qa-report.txt"
caffeinate -u -t 900 >/dev/null 2>&1 &
CPID=$!
"$U" -projectPath "$P" -buildTarget WebGL -executeMethod Lamplighter.EditorTools.LamplighterQA.RunFromCli -logFile "$P/Builds/qa.log" &
UPID=$!
( sleep "${TIMEOUT:-600}"; kill $UPID 2>/dev/null && echo "WATCHDOG KILLED UNITY" ) &
WPID=$!
wait $UPID; CODE=$?
pkill -P $WPID 2>/dev/null; kill $WPID 2>/dev/null
kill $CPID 2>/dev/null
echo "UNITY EXIT: $CODE"
cat "$P/Builds/qa-report.txt" 2>/dev/null || grep -vE "CAMetalLayer" "$P/Builds/qa.log" | tail -20
exit $CODE
