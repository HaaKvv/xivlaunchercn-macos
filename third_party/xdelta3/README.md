# xdelta3 (vendored source)

- Version: 3.0.12
- License: Apache License 2.0 — see [`LICENSE`](LICENSE)
- Upstream: https://github.com/jmacd/xdelta
- Source vendored through the AtmoOmen/FFXIVQuickLauncher XdeltaShim source tree.

`src/XIVLauncher.Avalonia/Resources/xdelta3` is built from `source/xdelta3.c`.
The checked-in binary is **arm64**. For an Intel build, rebuild the same source
for x64 and replace the binary.

## macOS arm64 build

```bash
cd third_party/xdelta3/source
cc -O2 -DXD3_MAIN=1 -DXD3_USE_LARGEFILE64=1 -DXD3_POSIX=1 -DXD3_STDIO=0 \
   -DXD3_DEBUG=0 -DSIZEOF_SIZE_T=8 -DSIZEOF_UNSIGNED_LONG=8 \
   -DSIZEOF_UNSIGNED_LONG_LONG=8 -DSIZEOF_UNSIGNED_INT=4 \
   -DEXTERNAL_COMPRESSION=0 -DHAVE_CONFIG_H=0 -o xdelta3 xdelta3.c
cp xdelta3 ../../../src/XIVLauncher.Avalonia/Resources/xdelta3
chmod +x ../../../src/XIVLauncher.Avalonia/Resources/xdelta3
```

The launcher uses xdelta3 to merge SDO V3 game patches; the result is verified
against the expected size and MD5 before being installed.