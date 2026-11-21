# Building

## Prerequisites

- macOS 11+ (Apple Silicon recommended)
- .NET 10 SDK
- Xcode Command Line Tools (`xcode-select --install`)
- Optional: Wine / CrossOver / Whisky for end-to-end runtime testing
- Optional: Rust + C toolchain only if you need to rebuild the xdelta3 binary

## Build and test

```bash
export PATH="$HOME/.dotnet:$PATH"

dotnet restore src/XIVLauncher.slnx
dotnet build   src/XIVLauncher.slnx -c Debug
dotnet test    src/XIVLauncher.Test/XIVLauncher.Test.csproj -c Debug
```

`MaintenanceIntegrationTests` requires a real game installation, Wine and
network access. In CI it is excluded:

```bash
dotnet test src/XIVLauncher.Test/XIVLauncher.Test.csproj -c Release \
  --filter "FullyQualifiedName!~MaintenanceIntegrationTests"
```

## Publish and package

```bash
./scripts/build-macos.sh arm64
./scripts/package-app.sh arm64

# Optional custom output
APP_OUT=/tmp/XIVLauncherCN.app ./scripts/package-app.sh arm64
```

The `.app` uses the published native app host (`XIVLauncherCN.bin`) behind a
small wrapper that locates a .NET runtime. It is ad-hoc signed by default.
`MAKE_DMG=1` produces a DMG as well.

## xdelta3

The bundled `src/XIVLauncher.Avalonia/Resources/xdelta3` is built from the
vendored xdelta3 3.0.12 source at `third_party/xdelta3/source`. To rebuild it:

```bash
cd third_party/xdelta3/source
cc -O2 -DXD3_MAIN=1 -DXD3_USE_LARGEFILE64=1 -DXD3_POSIX=1 -DXD3_STDIO=0 \
   -DXD3_DEBUG=0 -DSIZEOF_SIZE_T=8 -DSIZEOF_UNSIGNED_LONG=8 \
   -DSIZEOF_UNSIGNED_LONG_LONG=8 -DSIZEOF_UNSIGNED_INT=4 \
   -DEXTERNAL_COMPRESSION=0 -DHAVE_CONFIG_H=0 -o xdelta3 xdelta3.c
cp xdelta3 ../../../src/XIVLauncher.Avalonia/Resources/xdelta3
chmod +x ../../../src/XIVLauncher.Avalonia/Resources/xdelta3
```

The current binary is arm64. An Intel build needs an x64 binary in the same
location.

## CI and releases

- `.github/workflows/ci.yml` builds and runs unit tests on a macOS runner.
- `.github/workflows/release.yml` builds an arm64 `.app` on `v*` tags and
  publishes a zip to GitHub Releases.

Release artifacts are ad-hoc signed and require .NET 10 Runtime on the user's
machine.