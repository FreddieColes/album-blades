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

"$HERE/fix-libssl.sh" || exit 1

echo "Freeing disk space"
rm -f /workspaces/android-support.pkg; rm -rf /tmp/unitydl; sudo apt-get clean >/dev/null 2>&1
df -h /workspaces | tail -1
FREE=$(df -Pk /workspaces | awk 'NR==2{print int($4/1024/1024)}')
[ "$FREE" -lt 4 ] && echo "WARNING: only ${FREE}GB free, Unity may run out of space"

echo "Copying mod files into the SDK project"
rsync -a --delete "$HERE/mod/Assets/AlbumBlades/" "$SDK/Assets/AlbumBlades/" --exclude Generated --exclude '*.prefab' --exclude '*.meta'
mkdir -p "$SDK/BuildStaging/Catalogs/Mods"
rsync -a --delete "$HERE/mod/Catalog/AlbumBlades/" "$SDK/BuildStaging/Catalogs/Mods/AlbumBlades/"

# Leftover Unity processes from earlier runs eat memory, so clear them out
pkill -f "Editor/Unity -batchmode" 2>/dev/null; pkill -f AssetImportWorker 2>/dev/null; sleep 2
# Import assets one at a time instead of in parallel worker processes (much less memory)
sed -i 's/m_RefreshImportMode: 1/m_RefreshImportMode: 0/' "$SDK/ProjectSettings/EditorSettings.asset"
free -h | sed -n 2p

export ALBUM_BLADES_OUT="$OUT"
export DISPLAY=:1
for ATTEMPT in 1 2 3; do
  echo "Running Unity, attempt $ATTEMPT (it carries on from where the last one stopped)..."
  "$UNITY" -batchmode -nographics -projectPath "$SDK" -buildTarget Android \
    -executeMethod FluidLove.AlbumBladesBuilder.BuildFromCommandLine -logFile "$LOG"
  CODE=$?
  echo "Unity exited with code $CODE"
  [ -f "$OUT/AlbumBlades/manifest.json" ] && break
  grep -q "Scripts have compiler errors" "$LOG" && break
  if [ $CODE -eq 137 ] || [ $CODE -eq 143 ] || [ $CODE -eq 134 ] || [ $CODE -eq 139 ]; then
    echo "Unity was killed or crashed (probably memory). Retrying..."
    pkill -f AssetImportWorker 2>/dev/null; sleep 5
  else
    break
  fi
done
echo "$CODE" > "$OUT/exitcode"

if grep -qiE "no valid unity editor licen|licen[cs]e is not active|com.unity.editor.headless" "$LOG"; then
  echo; echo "Unity has no licence yet. Run ./start-hub.sh, sign in on the Desktop tab and add the free Personal licence, then run ./build.sh again."
  exit 2
fi
if [ $CODE -ne 0 ] || [ ! -f "$OUT/AlbumBlades/manifest.json" ]; then
  echo; echo "BUILD FAILED (exit $CODE). Sending the logs to the repo for Claude..."
  "$HERE/report.sh"
  exit 1
fi
sed -i 's/"Name": "Album Blades"/"Name": "Album Blades"/' "$OUT/AlbumBlades/manifest.json"
# mod.io wants the files at the top of the zip (no wrapping folder)
(cd "$OUT/AlbumBlades" && rm -f ../AlbumBlades.zip && zip -qr ../AlbumBlades.zip .)
grep "\[AlbumBlades\]" "$LOG" | tail -15
echo; echo "SUCCESS: dist/AlbumBlades.zip is ready. Right-click it in the Explorer and choose Download."
