# Architecture

XIVLauncherCN (macOS) is a native Avalonia front-end that launches the Windows
FF14 client through a Wine-compatible layer. It shares the game installation and
Dalamud data with an existing XIVLauncherCN.Core / xlcore setup where possible.

## Projects

| Project | Responsibility |
| --- | --- |
| `XIVLauncher.Common` | Cross-platform core: game launch arguments, paths, versions, HTTP |
| `XIVLauncher.Common.Http` | HTTP handlers, fallbacks and diagnostics |
| `XIVLauncher.Account` | SQLite account store, encrypted credentials, Keychain provider |
| `XIVLauncher.Login` | SDO login flows: static password, quick login, QR code |
| `XIVLauncher.DCTravel` | Data-center travel client and local listener |
| `XIVLauncher.GamePatchV3` | V3 metadata, downloads, integrity checks, repair, update install |
| `XIVLauncher.Dalamud.Mac` | Dalamud updater / session / injector argument construction |
| `XIVLauncher.Common.Mac` | macOS platform layer: Wine detection, prefix execution, path mapping, game and Dalamud runners |
| `XIVLauncher.Avalonia` | Avalonia UI and desktop composition |
| `XIVLauncher.Test` | xUnit unit and integration tests |

## Launch flow

1. The UI authenticates against SDO and obtains a game session ticket.
2. `Launcher` builds the game command line (area, session, SDO IDs, arguments).
3. A game runner starts the client:
   - Without Dalamud: `MacGameRunner` runs `ffxiv_dx11.exe` inside the Wine prefix.
   - With Dalamud: `MacDalamudGameRunner` delegates to `DalamudSession`, which runs
     `Dalamud.Injector.exe` with `--mode=entrypoint` inside the same prefix.
4. The injector starts the game and injects Dalamud; the launcher maps the Wine
   process ID back to a macOS process via `winedbg` where possible.

## Wine layer

`WineSettings` probes for, in order:

1. xlcore / xom Wine (`~/.xlcore_cn/compatibilitytool/beta/wine`)
2. CrossOver
3. Whisky
4. Homebrew / system Wine

When xlcore is present, the existing prefix, game directory, .NET runtime,
Dalamud assets and configuration are reused. The launcher's own Dalamud binaries
live under `~/Library/Application Support/XIVLauncherCN/addon/Hooks`, so the old
launcher's Dalamud directory is not overwritten.

`CompatibilityTools` injects the macOS Wine environment used by the xom setup:

- DXMT / MetalFX variables (`DXMT_CONFIG`, `DXMT_METALFX_SPATIAL_SWAPCHAIN`)
- `WINEDLLOVERRIDES` with native `d3d11` and `dxgi`
- `WINEESYNC=1`, `WINEMSYNC=1` for xlcore, `WINEDEBUG=-all`
- `XL_WINEONLINUX` / `XL_WINEONMAC`

## Patching

`GameUpdater` compares the local `ffxivgame.ver` with the V3 metadata service.
`GamePatchInstaller` downloads patch packages, verifies sizes/MD5, and applies
deltas through `VcdiffClient`.

On macOS, `VcdiffClient.CreateLocal(xdelta3Path)` calls the bundled `xdelta3`
binary directly instead of the Windows shared-memory worker, then verifies the
resulting file size and MD5. `GameRepairer` verifies and re-downloads only
damaged files.

## Data layout

Launcher data:

```
~/Library/Application Support/XIVLauncherCN/
├── accounts.db
├── cred.json
├── launcherConfigV3.json
├── addon/
│   └── Hooks/<version>/       # launcher-managed Dalamud binaries
├── logs/
└── patches/
```

Shared xlcore data (when present):

```
~/.xlcore_cn/
├── compatibilitytool/beta/wine/
├── wineprefix/
├── ffxiv/
├── runtime/
├── dalamudAssets/
├── dalamudConfig.json
├── installedPlugins/
└── pluginConfigs/
```

## Security boundaries

- Credentials are encrypted and their keys are stored in the macOS Keychain.
- Game paths and patch outputs are validated before writes; archive extraction
  uses managed decompression or a bundled `xdelta3`.
- The launcher executes Wine, the game and Dalamud under the user's account; it
  does not run privileged operations itself.
- Third-party Dalamud plugins run inside the game process and are outside the
  launcher's security boundary.