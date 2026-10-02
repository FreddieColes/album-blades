#!/usr/bin/env bash
# Builds the Nomad mod headless and zips it to dist/AlbumBlades.zip
set -uo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
WS=/workspaces
SDK="$WS/BasSDK"
VER=$(cat "$WS/.unity-version")
UNITY="$WS/unity/$VER/Editor/Unity"
OUT="$HERE/dist"
LOG="$HERE/dist/build.log"
mkdir -p "$OUT"

[ -x "$UNITY" ] || { echo "Unity not found at $UNITY. Did setup finish? Check /workspaces/setup.log"; exit 1; }

echo "Copying mod files into the SDK project"
rsync -a --delete "$HERE/mod/Assets/AlbumBlades/" "$SDK/Assets/AlbumBlades/" --exclude Generated --exclude '*.prefab' --exclude '*.meta'
mkdir -p "$SDK/BuildStaging/Catalogs/Mods"
rsync -a --delete "$HERE/mod/Catalog/AlbumBlades/" "$SDK/BuildStaging/Catalogs/Mods/AlbumBlades/"

echo "Running Unity (first run imports the whole SDK, so it can take 20+ minutes)..."
export ALBUM_BLADES_OUT="$OUT"
export DISPLAY=:1
"$UNITY" -batchmode -nographics -projectPath "$SDK" -buildTarget Android \
  -executeMethod FluidLove.AlbumBladesBuilder.BuildFromCommandLine -logFile "$LOG"
CODE=$?

if grep -qiE "no valid unity editor licen|licen[cs]e is not active|com.unity.editor.headless" "$LOG"; then
  echo; echo "Unity has no licence yet. Run ./start-hub.sh, sign in on the Desktop tab and add the free Personal licence, then run ./build.sh again."
  exit 2
fi
if [ $CODE -ne 0 ] || [ ! -f "$OUT/AlbumBlades/manifest.json" ]; then
  echo; echo "BUILD FAILED (exit $CODE). Key lines:"
  grep -E "\[AlbumBlades\]|error CS|Exception|Error" "$LOG" | tail -40
  echo; echo "Full log: dist/build.log  (send me the lines above)"
  exit 1
fi
(cd "$OUT" && rm -f AlbumBlades.zip && zip -qr AlbumBlades.zip AlbumBlades)
grep "\[AlbumBlades\]" "$LOG" | tail -15
echo; echo "SUCCESS: dist/AlbumBlades.zip is ready. Right-click it in the Explorer and choose Download."
