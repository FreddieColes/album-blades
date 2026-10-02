#!/usr/bin/env bash
# One-off setup: system packages, Unity Hub, Warpfrog SDK, Unity editor + Android support.
set -euo pipefail
WS=/workspaces
SDK="$WS/BasSDK"
EDITORS="$WS/unity"
log(){ echo -e "\n=== $* ==="; }

log "System packages"
sudo apt-get update -y
sudo apt-get install -y --no-install-recommends \
  curl wget gpg ca-certificates xz-utils zip unzip p7zip-full cpio git git-lfs rsync \
  libgtk-3-0 libnss3 libasound2 libgbm1 libxss1 libxtst6 libsecret-1-0 gnome-keyring dbus-x11 \
  xvfb libglu1-mesa libgl1 libxcursor1 libxrandr2 libxinerama1 libxi6 libcanberra-gtk3-module \
  firefox-esr xdg-utils

log "Unity Hub"
if ! command -v unityhub >/dev/null; then
  wget -qO - https://hub.unity3d.com/linux/keys/public | gpg --dearmor | sudo tee /usr/share/keyrings/Unity_Technologies_ApS.gpg >/dev/null
  echo "deb [signed-by=/usr/share/keyrings/Unity_Technologies_ApS.gpg] https://hub.unity3d.com/linux/repos/deb stable main" | sudo tee /etc/apt/sources.list.d/unityhub.list
  sudo apt-get update -y && sudo apt-get install -y unityhub
fi
# Electron needs --no-sandbox in containers, including when the browser hands the login back via unityhub:// links
for f in /usr/share/applications/unityhub.desktop; do
  [ -f "$f" ] && sudo sed -i 's#^Exec=\(/opt/unityhub/unityhub\)\( --no-sandbox\)\?#Exec=\1 --no-sandbox#' "$f"
done
xdg-mime default unityhub.desktop x-scheme-handler/unityhub || true
xdg-settings set default-web-browser firefox-esr.desktop || true

log "Warpfrog Blade & Sorcery SDK"
if [ ! -d "$SDK/Assets" ]; then
  rm -rf "$SDK"
  if GIT_TERMINAL_PROMPT=0 git clone --depth 1 https://dev.azure.com/Warpfrog/BasSDK/_git/BasSDK "$SDK"; then
    echo "Cloned current SDK from Azure DevOps"
  else
    echo "Azure clone failed, falling back to the older GitHub copy"
    git clone --depth 1 https://github.com/KospY/BasSDK.git "$SDK"
  fi
  (cd "$SDK" && git lfs pull || true)
fi
VER=$(grep '^m_EditorVersionWithRevision' "$SDK/ProjectSettings/ProjectVersion.txt" | awk '{print $2}')
CS=$(grep '^m_EditorVersionWithRevision' "$SDK/ProjectSettings/ProjectVersion.txt" | sed 's/.*(\(.*\)).*/\1/')
echo "SDK wants Unity $VER ($CS)"
echo "$VER" > "$WS/.unity-version"

log "Unity editor $VER (direct download, about 3 GB)"
ED="$EDITORS/$VER"
if [ ! -x "$ED/Editor/Unity" ]; then
  mkdir -p "$ED" /tmp/unitydl
  BASE="https://download.unity3d.com/download_unity/$CS"
  wget -q --show-progress -O /tmp/unitydl/editor.tar.xz "$BASE/LinuxEditorInstaller/Unity.tar.xz"
  tar -xJf /tmp/unitydl/editor.tar.xz -C "$ED"
  rm -f /tmp/unitydl/editor.tar.xz
  if [ ! -x "$ED/Editor/Unity" ]; then
    BIN=$(find "$ED" -maxdepth 4 -type f -name Unity -path '*/Editor/Unity' | head -1)
    [ -n "$BIN" ] && mv "$(dirname "$(dirname "$BIN")")"/* "$ED"/ 2>/dev/null || true
  fi
  [ -x "$ED/Editor/Unity" ] || { echo "Could not find the Unity binary after extracting"; exit 1; }
fi
log "Android build support"
AP_DIR="$ED/Editor/Data/PlaybackEngines/AndroidPlayer"
hub(){ # Unity Hub's command line, trying the arg styles different Hub versions accept
  local disp=(); [ -z "${DISPLAY:-}" ] && disp=(xvfb-run -a)
  "${disp[@]}" unityhub --no-sandbox -- --headless "$@" || "${disp[@]}" unityhub --no-sandbox --headless "$@"
}
if [ ! -d "$AP_DIR" ]; then
  command -v xvfb-run >/dev/null || sudo apt-get install -y --no-install-recommends xvfb
  export DISPLAY="${DISPLAY:-}"
  echo "Pointing Hub at $EDITORS"
  hub install-path --set "$EDITORS" || true
  hub editors --installed || true
  echo "Installing Android module via Unity Hub (about 1 GB, progress may look quiet)"
  hub install-modules --version "$VER" --module android --childModules || echo "Hub install-modules returned an error"
fi
if [ ! -d "$AP_DIR" ]; then
  echo "Hub route failed, using the Android package directly"
  PKG=/workspaces/android-support.pkg
  BASE="https://download.unity3d.com/download_unity/$CS"
  if [ ! -s "$PKG" ]; then
    wget --progress=dot:giga -O "$PKG" "$BASE/MacEditorTargetInstaller/UnitySetup-Android-Support-for-Editor-$VER.pkg" || rm -f "$PKG"
  fi
  W=/tmp/unitydl/android; rm -rf "$W"; mkdir -p "$W"
  cp "$PKG" "$W/android.pkg"
  # Unpack archives inside archives until the Android editor DLL turns up
  for round in 1 2 3 4 5; do
    DLL=$(find "$W" -name "UnityEditor.Android.Extensions.dll" | head -1)
    [ -n "$DLL" ] && break
    echo "Unpack round $round"
    find "$W" -type f \( -name "*.pkg" -o -name "Payload*" -o -name "*.cpio" -o -name "*.gz" -o -name "*.xar" -o -name "*.tar" -o -name "*.xz" \) ! -name "*.done" | while read -r f; do
      d="$f.x"; mkdir -p "$d"
      7z x -y -o"$d" "$f" >/dev/null 2>&1 || (cd "$d" && (zcat "$f" 2>/dev/null || cat "$f") | cpio -idm --quiet 2>/dev/null) || true
      mv "$f" "$f.done"
    done
  done
  DLL=$(find "$W" -name "UnityEditor.Android.Extensions.dll" | head -1)
  if [ -n "$DLL" ]; then
    SRC=$(dirname "$DLL")
    echo "Found Android player files at ${SRC#$W/}"
    mkdir -p "$(dirname "$AP_DIR")"
    rm -rf "$AP_DIR"; mv "$SRC" "$AP_DIR"
  else
    echo "Could not find the Android files. Package layout:"
    find "$W" -maxdepth 6 -type d | sed "s#$W/##" | head -40
  fi
  rm -rf /tmp/unitydl
fi
if [ -d "$AP_DIR" ]; then echo "Android support installed"; else echo "ANDROID SUPPORT FAILED - send Claude the lines above"; exit 1; fi

log "Telling Unity Hub where the editor is"
mkdir -p ~/.config/UnityHub
echo "\"$EDITORS\"" > ~/.config/UnityHub/secondaryInstallPath.json || true

chmod +x "$WS"/*/start-hub.sh "$WS"/*/build.sh 2>/dev/null || true
log "SETUP DONE. Next: open the Desktop (port 6080) and run ./start-hub.sh"
