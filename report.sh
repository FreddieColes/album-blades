#!/usr/bin/env bash
# Bundles build logs + diagnostics and pushes them to the repo so Claude can read them.
HERE="$(cd "$(dirname "$0")" && pwd)"
SDK=/workspaces/BasSDK
R="$HERE/reports"; mkdir -p "$R"
{
  echo "== date"; date
  echo "== SDK source"; (cd "$SDK" && git remote -v | head -1 && git log -1 --format='%H %cd')
  echo "== Unity version"; cat /workspaces/.unity-version
  echo "== PlaybackEngines"; ls /workspaces/unity/*/Editor/Data/PlaybackEngines
  echo "== LFS pointer files (should be none)"; grep -rlI "version https://git-lfs" "$SDK/Assets" "$SDK/Packages" 2>/dev/null | head -20
  echo "== AlbumBlades files in SDK"; find "$SDK/Assets/AlbumBlades" -maxdepth 3 | head -30
  echo "== Key log lines"; grep -nE "error CS|will not be loaded|Reference has errors|Unable to resolve|non-existent|not a valid|Assembly .* has|compiler errors|\[AlbumBlades\]|Exception|licen|executeMethod" "$HERE/dist/build.log" | grep -v "Start importing" | tail -80 | cut -c1-400
  echo "== 60 lines before 'compiler errors'"; grep -n -B60 "Scripts have compiler errors" "$HERE/dist/build.log" | grep -v "Start importing" | tail -60 | cut -c1-400
} > "$R/diag.txt" 2>&1
gzip -c "$HERE/dist/build.log" > "$R/build.log.gz" 2>/dev/null
ls -1 /tmp/unityhub.log >/dev/null 2>&1 && tail -200 /tmp/unityhub.log > "$R/unityhub.log"
cd "$HERE" && git add -f reports && git commit -qm "Build report $(date +%H:%M)" && git pull -q --rebase && git push -q && echo "Report sent. Tell Claude: report pushed."
