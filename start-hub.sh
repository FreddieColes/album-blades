#!/usr/bin/env bash
# Opens Unity Hub on the browser desktop (port 6080) so you can sign in and add the free Personal licence.
export DISPLAY=:1
if [ -z "${DBUS_SESSION_BUS_ADDRESS:-}" ]; then eval "$(dbus-launch --sh-syntax)"; fi
echo -n "vscode" | gnome-keyring-daemon --unlock --components=secrets,pkcs11 >/dev/null 2>&1 || true
nohup unityhub --no-sandbox >/tmp/unityhub.log 2>&1 &
echo "Unity Hub is starting on the Desktop tab (port 6080, password: vscode)."
echo "Sign in, then: Preferences (cog) > Licences > Add > Get a free personal licence."
