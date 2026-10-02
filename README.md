# Album Blades

Fluid Love albums as Blade & Sorcery: Nomad weapons. First up: **Pleasure Island DLC**, a giant jewel case paddle that plays a random clip from the album every time it hits someone. Pull the trigger while holding it to play one on demand.

## Build it (no PC needed)

Do this in the **Quest browser**, it's much easier than on a phone.

1. **Start the Codespace.** On this repo page: green **Code** button > **Codespaces** > **Create codespace on main**. Setup runs by itself and takes about 20 to 30 minutes (it downloads Unity). Progress is in `/workspaces/setup.log`. Wait for `SETUP DONE`.
2. **Open the desktop.** Bottom panel > **Ports** tab > port **6080** > globe icon. Click **Connect**, password `vscode`.
3. **Licence (one time only).** Back in the VS Code tab, open a terminal and run `./start-hub.sh`. On the desktop tab, sign in to Unity Hub, then cog icon > **Licences** > **Add** > **Get a free personal licence**.
4. **Build.** In the terminal run `./build.sh`. The first run imports the whole SDK, so give it 20+ minutes.
5. **Download.** In the file explorer open `dist`, right-click `AlbumBlades.zip` > **Download**.
6. **Install.** Unzip, then move the `AlbumBlades` folder into `Android/data/com.Warpfrog.BladeAndSorcery/files/Mods` with Mobile VR Station. Launch the game, enable the mod, spawn it from the item book in Sandbox.

Stop the Codespace when you're done (Codespaces menu > Stop) to save free hours.

## What's where

- `mod/Assets/AlbumBlades/Editor/AlbumBladesBuilder.cs` builds the paddle, music and mod package
- `mod/Assets/AlbumBlades/PleasureIslandDLC/` cover art and the 9 clips
- `mod/Catalog/AlbumBlades/Items/` the weapon's stats file
- `build.sh` / `start-hub.sh` / `.devcontainer/` the cloud setup

If a build fails, send Claude the lines `build.sh` prints.
