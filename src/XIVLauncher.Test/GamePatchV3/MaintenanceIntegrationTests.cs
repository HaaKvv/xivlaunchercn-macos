using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using XIVLauncher.Common.Constant;
using XIVLauncher.GamePatchV3;
using XIVLauncher.GamePatchV3.Models;
using XIVLauncher.GamePatchV3.Update;
using XIVLauncher.GamePatchV3.Update.Models;
using Xunit;

namespace XIVLauncher.Test.GamePatchV3;

public sealed class MaintenanceIntegrationTests
{
    private static string? FindXdelta3()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Resources", "xdelta3"),
            "/opt/homebrew/bin/xdelta3",
            "/usr/local/bin/xdelta3",
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..",
                                            "XIVLauncher.Avalonia", "Resources", "xdelta3"))
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    private static void Run(string exe, params string[] args)
    {
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute        = false,
            RedirectStandardError  = true,
            RedirectStandardOutput = true
        };

        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi)!;
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.True(process.ExitCode == 0, $"{exe} failed: {error}");
    }

    [Fact]
    public async Task LocalVcdiff_ReproducesTargetAndVerifiesMd5()
    {
        var xdelta3 = FindXdelta3();
        if (xdelta3 == null)
            return;

        var dir = Path.Combine(Path.GetTempPath(), "xlcn-vcdiff-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        try
        {
            var sourcePath = Path.Combine(dir, "source.bin");
            var targetPath = Path.Combine(dir, "target.bin");
            var deltaPath  = Path.Combine(dir, "delta.bin");
            var outputPath = Path.Combine(dir, "output.bin");

            var sourceBytes = RandomNumberGenerator.GetBytes(256 * 1024);
            var targetBytes = sourceBytes.Concat(RandomNumberGenerator.GetBytes(4096)).ToArray();

            await File.WriteAllBytesAsync(sourcePath, sourceBytes);
            await File.WriteAllBytesAsync(targetPath, targetBytes);

            Run(xdelta3, "-f", "-e", "-s", sourcePath, targetPath, deltaPath);

            var expectedMd5 = Convert.ToHexString(MD5.HashData(targetBytes));

            using var client = VcdiffClient.CreateLocal(xdelta3);
            await client.ApplyVcdiff(sourcePath, deltaPath, outputPath, expectedMd5, targetBytes.Length);

            Assert.Equal(targetBytes, await File.ReadAllBytesAsync(outputPath));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InterruptedUpdate_RetainsVersionAndResumesFromCache(bool cancel)
    {
        if (OperatingSystem.IsMacOS() && System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture
                                     != System.Runtime.InteropServices.Architecture.Arm64)
            return;

        var xdelta3 = FindXdelta3();
        if (!OperatingSystem.IsMacOS() && xdelta3 == null)
            return;
        Assert.NotNull(xdelta3);

        using var temp = new XIVLauncher.Test.Common.RepositoryTests.TempDir();
        var root = temp.Dir;
        var install = root.CreateSubdirectory("install");
        var game = install.CreateSubdirectory("game");
        var cache = root.CreateSubdirectory("cache");
        const string oldVersion = "2026.11.03.0000.0000";
        const string newVersion = "2026.11.17.0000.0000";
        var files = new[]
        {
            (Name: "ffxivgame.ver", Before: oldVersion, After: newVersion),
            (Name: "first.dat", Before: "first original", After: "first patched"),
            (Name: "second.dat", Before: "second original", After: "second patched")
        };

        byte[] packageBytes;
        using (var packageStream = new MemoryStream())
        {
            using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Create, leaveOpen: true))
            {
                using (var map = new StreamWriter(archive.CreateEntry("patch_delta_direct.dat").Open()))
                {
                    map.Write("<DeltaPaths>");
                    foreach (var file in files)
                        map.Write($"<DeltaPathSubItem Key=\"game/{file.Name}\" Value=\"{file.Name}.delta\" />");
                    map.Write("</DeltaPaths>");
                }

                foreach (var file in files)
                {
                    var source = Path.Combine(game.FullName, file.Name);
                    var target = Path.Combine(root.FullName, file.Name + ".target");
                    var delta = Path.Combine(root.FullName, file.Name + ".delta");
                    File.WriteAllText(source, file.Before);
                    File.WriteAllText(target, file.After);
                    Run(xdelta3, "-f", "-e", "-s", source, target, delta);
                    archive.CreateEntryFromFile(delta, file.Name + ".delta");
                }
            }

            packageBytes = packageStream.ToArray();
        }

        var packageList = new GamePackageFileList
        {
            FileList = [new() { Path = "patch.zip", Url = "patch.zip", Size = packageBytes.Length,
                                Md5 = Convert.ToHexString(MD5.HashData(packageBytes)) }]
        };
        var listing = string.Join('\n', files.Select(file =>
        {
            var bytes = Encoding.UTF8.GetBytes(file.After);
            return $"game/{file.Name}|{bytes.Length}|{Convert.ToHexString(MD5.HashData(bytes))}";
        }));
        var handler = new PatchFixtureHandler($"https://patch.example/|0|0.0.0.2\n{listing}", packageList, packageBytes);
        var plan = new GameUpdatePlan
        {
            BaseUrl = "https://patch.example/",
            CurrentGameVersion = oldVersion,
            TargetGameVersion = newVersion,
            Packages = [new() { Name = "fixture", FileListUrl = "patch.json", From = "0.0.0.1", To = "0.0.0.2" }]
        };
        using var installer = new GamePatchInstaller(handler);
        using var vcdiff = VcdiffClient.CreateLocal(xdelta3);
        using var cancellation = new CancellationTokenSource();
        var interrupted = new PatchProgress(step =>
        {
            if (step.CurrentFile != "game/first.dat" || step.StatusText != "2/3")
                return;

            if (cancel)
                cancellation.Cancel();
            else
                throw new IOException("Simulated interruption after applying the first file");
        });

        var attempt = installer.InstallAsync(plan, install, cache, vcdiff, false, TimeSpan.Zero, interrupted, cancellation.Token);
        if (cancel)
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => attempt);
        else
            await Assert.ThrowsAsync<IOException>(() => attempt);

        Assert.Equal(oldVersion, File.ReadAllText(Path.Combine(game.FullName, "ffxivgame.ver")));
        Assert.Equal("first patched", File.ReadAllText(Path.Combine(game.FullName, "first.dat")));
        Assert.Equal("second original", File.ReadAllText(Path.Combine(game.FullName, "second.dat")));
        Assert.Empty(game.EnumerateFiles("*.tmp"));

        await installer.InstallAsync(plan, install, cache, vcdiff, false, TimeSpan.Zero, null, CancellationToken.None);

        Assert.All(files, file => Assert.Equal(file.After, File.ReadAllText(Path.Combine(game.FullName, file.Name))));
        Assert.Equal(1, handler.PackageDownloads);
    }

    private sealed class PatchProgress(Action<GamePatchProgress> report) : IProgress<GamePatchProgress>
    {
        public void Report(GamePatchProgress value) => report(value);
    }

    private sealed class PatchFixtureHandler(string listing, GamePackageFileList packageList, byte[] packageBytes) : HttpMessageHandler
    {
        public int PackageDownloads { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = request.RequestUri!.AbsolutePath;
            HttpContent content;
            if (path == new Uri(SdoInfos.CLIENT_ALL_FILES_LIST_URL).AbsolutePath)
                content = new StringContent(listing);
            else if (path.EndsWith("/patch.json", StringComparison.Ordinal))
                content = new StringContent(JsonSerializer.Serialize(packageList));
            else if (path.EndsWith("/patch.zip", StringComparison.Ordinal))
            {
                PackageDownloads++;
                content = new ByteArrayContent(packageBytes);
            }
            else
                throw new InvalidOperationException($"Unexpected fixture request: {path}");

            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = content });
        }
    }

    [Fact]
    public async Task CheckRealGame_ReturnsResult()
    {
        var gamePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".xlcore_cn", "ffxiv");
        if (!Directory.Exists(gamePath))
            return;

        var result = await GameUpdater.Check(new DirectoryInfo(gamePath), false);

        Assert.NotNull(result);
        if (result.NeedsUpdate)
            Assert.NotNull(result.UpdatePlan);
    }
}
