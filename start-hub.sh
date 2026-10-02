#!/usr/bin/env bash
# Opens Unity Hub on the browser desktop (port 6080) so you can sign in and add the free Personal licence.
export DISPLAY=:1
if [ -z "${DBUS_SESSION_BUS_ADDRESS:-}" ]; then eval "$(dbus-launch --sh-syntax)"; fi
echo -n "vscode" | gnome-keyring-daemon --unlock --components=secrets,pkcs11 >/dev/null 2>&1 || true
pkill -f unityhub 2>/dev/null; sleep 1

# The cloud machine has no graphics card, so try software-rendering flag sets until Hub stays up.
FLAGSETS=(
  "--no-sandbox --disable-gpu-sandbox --in-process-gpu --use-gl=swiftshader"
  "--no-sandbox --disable-gpu --disable-gpu-compositing --disable-gpu-sandbox"
  "--no-sandbox --disable-gpu --use-gl=disabled"
)
for FLAGS in "${FLAGSETS[@]}"; do
  echo "Starting Unity Hub with: $FLAGS"
  # Make sign-in links from Firefox reopen Hub with the same flags
  sudo sed -i "s#^Exec=/opt/unityhub/unityhub.*#Exec=/opt/unityhub/unityhub $FLAGS %U#" /usr/share/applications/unityhub.desktop 2>/dev/null || true
  nohup /opt/unityhub/unityhub $FLAGS >/tmp/unityhub.log 2>&1 &
  sleep 20
  if pgrep -f "/opt/unityhub/unityhub" >/dev/null && ! grep -q "Goodbye" /tmp/unityhub.log; then
    echo "Unity Hub is running. Go to the Desktop tab (port 6080, password: vscode)."
    echo "Sign in, then: cog icon > Licences > Add > Get a free personal licence."
    exit 0
  fi
  echo "That crashed, trying the next settings..."
  pkill -f unityhub 2>/dev/null; sleep 1
done
echo "Hub would not start. Send Claude the output of: tail -25 /tmp/unityhub.log | cut -c1-180"
exit 1
