# Third-Party Notices

This project would not exist without the open-source work listed below.
License texts are available in the linked repositories and in the referenced
license files.

## Upstream launcher projects

- **AtmoOmen/FFXIVQuickLauncher** (XIVLauncherCN, Soil branch) — GPL-3.0
  https://github.com/AtmoOmen/FFXIVQuickLauncher
  This macOS port is based on this branch.
- **GoatCorp/FFXIVQuickLauncher** — GPL-3.0
  https://github.com/GoatCorp/FFXIVQuickLauncher
- **ottercorp/XIVLauncher.Core** — GPL-3.0 (macOS/Wine compatibility reference)
  https://github.com/ottercorp/XIVLauncher.Core

## Bundled binary

- **xdelta3 3.0.12** — Apache License 2.0
  - Binary: `src/XIVLauncher.Avalonia/Resources/xdelta3`
  - License: [`third_party/xdelta3/LICENSE`](third_party/xdelta3/LICENSE)
  - Used for V3 patch delta merging.

## Runtime NuGet dependencies

The launcher uses, among others:

- **Avalonia** — MIT
- **.NET / Microsoft.Extensions.*** — MIT
- **CommunityToolkit.Mvvm** — MIT
- **Serilog** — Apache-2.0
- **Newtonsoft.Json** — MIT
- **SharpCompress** — MIT
- **sqlite-net-pcl / SQLitePCLRaw** — MIT / Apache-2.0
- **Konscious.Security.Cryptography.Argon2** — MIT
- **NSec.Cryptography** — MIT
- **EmbedIO** — Apache-2.0
- **Downloader** — MIT

Refer to each package's NuGet metadata and repository for the exact license text.

## Dalamud and plugins

Dalamud and its plugins are **not bundled** with this project. The launcher may
download Dalamud from a third-party distribution at runtime. Dalamud, its
plugins, and their assets remain subject to their own licenses and terms.

## Icons and assets

- `src/XIVLauncher.Avalonia/Assets/avalonia-logo.ico` — Avalonia project (MIT)
- `src/XIVLauncher.Avalonia/Assets/dalamud_icon.ico` — Dalamud project
- Other launcher icons/logos are from the upstream XIVLauncherCN projects or are
  used for identification purposes.

FINAL FANTASY is a registered trademark of Square Enix Holdings Co., Ltd.
All trademarks and copyrights remain the property of their respective owners.

If you believe an asset is used incorrectly, please open an issue so it can be
removed or replaced.