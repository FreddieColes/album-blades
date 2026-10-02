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
  libglu1-mesa libgl1 libxcursor1 libxrandr2 libxinerama1 libxi6 libcanberra-gtk3-module \
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
if [ ! -d "$ED/Editor/Data/PlaybackEngines/AndroidPlayer" ]; then
  BASE="https://download.unity3d.com/download_unity/$CS"
  wget -q --show-progress -O /tmp/unitydl/android.tar.xz "$BASE/LinuxEditorTargetInstaller/UnitySetup-Android-Support-for-Editor-$VER.tar.xz"
  mkdir -p /tmp/unitydl/android && tar -xJf /tmp/unitydl/android.tar.xz -C /tmp/unitydl/android
  AP=$(find /tmp/unitydl/android -type d -name AndroidPlayer | head -1)
  mkdir -p "$ED/Editor/Data/PlaybackEngines"
  mv "$AP" "$ED/Editor/Data/PlaybackEngines/"
  rm -rf /tmp/unitydl
fi
ls "$ED/Editor/Data/PlaybackEngines"

log "Telling Unity Hub where the editor is"
mkdir -p ~/.config/UnityHub
echo "\"$EDITORS\"" > ~/.config/UnityHub/secondaryInstallPath.json || true

chmod +x "$WS"/*/start-hub.sh "$WS"/*/build.sh 2>/dev/null || true
log "SETUP DONE. Next: open the Desktop (port 6080) and run ./start-hub.sh"
