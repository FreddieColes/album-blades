#!/usr/bin/env bash
# Builds the Ol' Mac Nomad mod headless and zips it to dist/OlMac.zip
# Same Codespace, Unity and SDK as the album weapons (build.sh).
set -uo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
WS=/workspaces
SDK="$WS/BasSDK"
VER=$(cat "$WS/.unity-version")
UNITY="$WS/unity/$VER/Editor/Unity"
OUT="$HERE/dist"
LOG="$HERE/dist/build-olmac.log"
mkdir -p "$OUT"

[ -x "$UNITY" ] || { echo "Unity not found at $UNITY. Did setup finish? Check /workspaces/setup.log"; exit 1; }

N=$(ls "$HERE"/olmac/Assets/OlMac/Sprites/*.png 2>/dev/null | wc -l)
[ "$N" -ge 11 ] || { echo "Only $N drawings in olmac/Assets/OlMac/Sprites. Copy all 11 FatChicken PNGs in there, commit and push, then git pull."; exit 1; }

"$HERE/fix-libssl.sh" || exit 1

echo "Freeing disk space"
rm -f /workspaces/android-support.pkg; rm -rf /tmp/unitydl; sudo apt-get clean >/dev/null 2>&1
df -h /workspaces | tail -1

echo "Copying Ol' Mac into the SDK project"
rsync -a --delete "$HERE/olmac/Assets/OlMac/" "$SDK/Assets/OlMac/" --exclude Generated --exclude '*.prefab' --exclude '*.meta' --exclude Audio
# Banjo sounds: the Old Mac Daddy clips from Ready For Business
mkdir -p "$SDK/Assets/OlMac/Audio"
cp -f "$HERE"/mod/Assets/AlbumBlades/ReadyForBusiness/Audio/REA_OldMacDaddy_*.wav "$SDK/Assets/OlMac/Audio/"
mkdir -p "$SDK/BuildStaging/Catalogs/Mods"
rsync -a --delete "$HERE/olmac/Catalog/OlMac/" "$SDK/BuildStaging/Catalogs/Mods/OlMac/"

pkill -f "Editor/Unity -batchmode" 2>/dev/null; pkill -f AssetImportWorker 2>/dev/null; sleep 2
sed -i 's/m_RefreshImportMode: 1/m_RefreshImportMode: 0/' "$SDK/ProjectSettings/EditorSettings.asset"
free -h | sed -n 2p

export OLMAC_OUT="$OUT"
export DISPLAY=:1
for ATTEMPT in 1 2 3; do
  echo "Running Unity, attempt $ATTEMPT..."
  "$UNITY" -batchmode -nographics -projectPath "$SDK" -buildTarget Android \
    -executeMethod FluidLove.OlMacBuilder.BuildFromCommandLine -logFile "$LOG"
  CODE=$?
  echo "Unity exited with code $CODE"
  [ -f "$OUT/OlMac/manifest.json" ] && break
  grep -q "Scripts have compiler errors" "$LOG" && break
  if [ $CODE -eq 137 ] || [ $CODE -eq 143 ] || [ $CODE -eq 134 ] || [ $CODE -eq 139 ]; then
    echo "Unity was killed or crashed (probably memory). Retrying..."
    pkill -f AssetImportWorker 2>/dev/null; sleep 5
  else
    break
  fi
done
echo "$CODE" > "$OUT/exitcode-olmac"

send_report() {
  R="$HERE/reports"; mkdir -p "$R"
  {
    echo "== date"; date
    echo "== exit code"; echo "$CODE"
    echo "== Key log lines"
    grep -nE "error CS|Shader error|Exception|\[OlMac\]|compiler errors|will not be loaded|not found|licen" "$LOG" | grep -v "Start importing" | tail -100 | cut -c1-400
    echo "== 60 lines before 'compiler errors'"
    grep -n -B60 "Scripts have compiler errors" "$LOG" | grep -v "Start importing" | tail -60 | cut -c1-400
  } > "$R/olmac-diag.txt" 2>&1
  gzip -c "$LOG" > "$R/build-olmac.log.gz" 2>/dev/null
  cd "$HERE" && git add -f reports && git commit -qm "Ol' Mac build report $(date +%H:%M)" && git pull -q --rebase && git push -q && echo "Report sent. Tell Claude: report pushed."
}

if [ ! -f "$OUT/OlMac/manifest.json" ] && grep -qiE "no valid unity editor licen|licen[cs]e is not active" "$LOG"; then
  echo; echo "Unity says it has no licence:"
  grep -iE "licen" "$LOG" | head -15 | cut -c1-200
  echo; echo "Sending the log to Claude anyway..."
  send_report
  exit 2
fi

if [ $CODE -ne 0 ] || [ ! -f "$OUT/OlMac/manifest.json" ]; then
  echo; echo "BUILD FAILED (exit $CODE). Sending the logs to the repo for Claude..."
  send_report
  exit 1
fi
python3 -c "import json,sys;p=sys.argv[1];m=json.load(open(p));m['Name']='OlMac';json.dump(m,open(p,'w'),indent=2)" "$OUT/OlMac/manifest.json"
(cd "$OUT" && rm -f OlMac.zip && zip -qr OlMac.zip OlMac)
grep "\[OlMac\]" "$LOG" | tail -15
send_report >/dev/null 2>&1 || true
echo; echo "SUCCESS: dist/OlMac.zip is ready. Right-click it in the Explorer and choose Download."
