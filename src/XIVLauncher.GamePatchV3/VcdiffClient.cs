using System.Diagnostics;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Serilog;

namespace XIVLauncher.GamePatchV3;

/// <summary>
///     V3 差分合并客户端。
///     <para>macOS 移植：将 Windows 的 SharedMemory（内存映射文件 + 命名事件）RPC
///     替换为 Unix Domain Socket，协议格式保持与原版一致。</para>
///     <para>macOS 无 Windows 差分 worker 时，可用 <see cref="CreateLocal" /> 走本地
///     xdelta3 可执行文件直接合并。</para>
/// </summary>
public sealed class VcdiffClient
(
    string  workerExecutablePath,
    string? dotnetRootPath = null,
    bool    asAdmin        = false,
    string? localXdelta3Tool = null
) : IDisposable
{
    private readonly string? localXdelta3Path = localXdelta3Tool;
    private Process?      workerProcess;
    private Socket?       socket;
    private Stream?       stream;
    private bool          isDisposed;

    /// <summary>
    ///     创建本地差分客户端：不启动 worker，直接调用 xdelta3 可执行文件。
    /// </summary>
    public static VcdiffClient CreateLocal(string xdelta3Path) =>
        new(string.Empty, localXdelta3Tool: xdelta3Path);

    public void Dispose()
    {
        if (isDisposed)
            return;

        isDisposed = true;

        try
        {
            stream?.Dispose();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[VcdiffClient] 关闭连接时异常");
        }

        if (workerProcess is { HasExited: false })
        {
            workerProcess.WaitForExit(1000);

            try
            {
                workerProcess.Kill();
            }
            catch (Exception ex)
            {
                if (!workerProcess.HasExited)
                    throw;

                Log.Debug(ex, "[VcdiffClient] 差分进程已在终止期间退出");
            }
        }

        socket?.Dispose();
        stream?.Dispose();
        workerProcess?.Dispose();
        socket          = null;
        stream          = null;
        workerProcess   = null;
    }

    public async Task ApplyVcdiff
    (
        string                                  sourceFile,
        string                                  deltaFile,
        string                                  targetFile,
        string                                  expectedMd5,
        long                                    expectedSize,
        IProgress<(long Progress, long Total)>? progress          = null,
        CancellationToken                       cancellationToken = default
    )
    {
        await using var deltaStream = new FileStream
        (
            deltaFile,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            131072,
            FileOptions.Asynchronous | FileOptions.SequentialScan
        );
        if (deltaStream.Length > int.MaxValue)
            throw new InvalidDataException("V3 差分数据过大");

        await ApplyVcdiff(sourceFile, deltaStream, (int)deltaStream.Length, targetFile, expectedMd5, expectedSize, null, progress, cancellationToken).ConfigureAwait
            (false);
    }

    public async Task ApplyVcdiff
    (
        string                                  sourceFile,
        ReadOnlyMemory<byte>                    deltaData,
        string                                  targetFile,
        string                                  expectedMd5,
        long                                    expectedSize,
        IProgress<(long Progress, long Total)>? progress          = null,
        CancellationToken                       cancellationToken = default
    )
    {
        var requestData = BuildRequestData(sourceFile, targetFile, expectedMd5, expectedSize, deltaData.Span);
        await ApplyVcdiffRequest(sourceFile, deltaData.Length, targetFile, expectedSize, requestData, progress, cancellationToken).ConfigureAwait(false);
    }

    public async Task ApplyVcdiff
    (
        string                                  sourceFile,
        Stream                                  deltaStream,
        int                                     deltaLength,
        string                                  targetFile,
        string                                  expectedMd5,
        long                                    expectedSize,
        IProgress<(long Progress, long Total)>? extractionProgress,
        IProgress<(long Progress, long Total)>? mergeProgress,
        CancellationToken                       cancellationToken = default
    )
    {
        if (deltaLength < 0)
            throw new ArgumentOutOfRangeException(nameof(deltaLength));

        // 差分包上限保护：超出即拒绝，避免整包分配导致 OOM（正常游戏差分包远小于此值）
        if (deltaLength > MAX_DELTA_LENGTH)
            throw new IOException($"V3 差分数据过大 ({deltaLength} bytes)，超过上限 {MAX_DELTA_LENGTH}");

        var requestData = CreateRequestData(sourceFile, targetFile, expectedMd5, expectedSize, deltaLength, out var deltaOffset);
        var extracted   = 0;

        while (extracted < deltaLength)
        {
            var read = await deltaStream.ReadAsync(requestData.AsMemory(deltaOffset + extracted, deltaLength - extracted), cancellationToken).ConfigureAwait(false);
            if (read == 0)
                throw new EndOfStreamException("V3 差分数据提前结束");

            extracted += read;
            extractionProgress?.Report((extracted, deltaLength));
        }

        await ApplyVcdiffRequest(sourceFile, deltaLength, targetFile, expectedSize, requestData, mergeProgress, cancellationToken).ConfigureAwait(false);
    }

    private async Task ApplyVcdiffRequest
    (
        string                                  sourceFile,
        int                                     deltaLength,
        string                                  targetFile,
        long                                    expectedSize,
        byte[]                                  requestData,
        IProgress<(long Progress, long Total)>? progress,
        CancellationToken                       cancellationToken
    )
    {
        Log.Information
            ("[VcdiffClient] 请求 V3 差分合并, 源 {SourceFile}, 差分大小 {DeltaSize}, 目标 {TargetFile}, 期望大小 {ExpectedSize}", sourceFile, deltaLength, targetFile, expectedSize);

        if (!string.IsNullOrWhiteSpace(localXdelta3Path))
        {
            await ApplyVcdiffLocalAsync(requestData, progress, cancellationToken).ConfigureAwait(false);
            return;
        }

        await EnsureWorkerStartedAsync(cancellationToken).ConfigureAwait(false);

        // 发送请求: 4 字节长度前缀 + 数据（Unix Domain Socket 流式传输）
        await WriteWithLengthPrefixAsync(requestData, cancellationToken).ConfigureAwait(false);

        // 读取响应: 4 字节长度前缀 + 数据
        var response = await ReadWithLengthPrefixAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        using var reader = new BinaryReader(new MemoryStream(response));
        var       result = reader.ReadInt32();

        if (result == RESULT_ERROR)
            throw new IOException($"V3 差分合并失败: {reader.ReadString()}");

        if (result != RESULT_PASS)
            throw new InvalidOperationException("未知的 V3 差分结果码");

        if (progress != null)
        {
            var completedSize = File.Exists(targetFile) ?
                                    new FileInfo(targetFile).Length :
                                    expectedSize;
            progress.Report((completedSize, completedSize));
        }

        Log.Information("[VcdiffClient] V3 差分合并完成 {TargetFile}", targetFile);
    }

    internal static byte[] BuildRequestData
    (
        string             sourceFile,
        string             targetFile,
        string             expectedMd5,
        long               expectedSize,
        ReadOnlySpan<byte> deltaData
    )
    {
        var requestData = CreateRequestData(sourceFile, targetFile, expectedMd5, expectedSize, deltaData.Length, out var deltaOffset);
        deltaData.CopyTo(requestData.AsSpan(deltaOffset));
        return requestData;
    }

    private static byte[] CreateRequestData
    (
        string  sourceFile,
        string  targetFile,
        string  expectedMd5,
        long    expectedSize,
        int     deltaLength,
        out int deltaOffset
    )
    {
        var requestLength = checked
        (
            sizeof(int)                          +
            GetSerializedStringSize(sourceFile)  +
            GetSerializedStringSize(targetFile)  +
            GetSerializedStringSize(expectedMd5) +
            sizeof(long)                         +
            sizeof(int)                          +
            deltaLength
        );
        var       requestData   = GC.AllocateUninitializedArray<byte>(requestLength);
        using var requestStream = new MemoryStream(requestData, true);
        using var writer        = new BinaryWriter(requestStream, Encoding.UTF8, true);
        writer.Write(VCDIFF_OPCODE);
        writer.Write(sourceFile);
        writer.Write(targetFile);
        writer.Write(expectedMd5);
        writer.Write(expectedSize);
        writer.Write(deltaLength);
        deltaOffset = checked((int)requestStream.Position);
        return requestData;
    }

    private async Task EnsureWorkerStartedAsync(CancellationToken cancellationToken)
    {
        if (workerProcess is { HasExited: false } && socket is { Connected: true })
            return;

        CleanupWorker();

        var socketPath = Path.Combine(Path.GetTempPath(), $"xivlauncher-vcdiff-{Guid.NewGuid():N}.sock");
        socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);

        Log.Information("[VcdiffClient] 正在启动 V3 差分进程, 路径 {WorkerExecutablePath}, 提权 {AsAdmin}, 套接字 {SocketPath}", workerExecutablePath, asAdmin, socketPath);

        workerProcess = new()
        {
            StartInfo = CreateProcessStartInfo(workerExecutablePath, $"{Environment.ProcessId} {socketPath}")
        };
#if !DEBUG
        workerProcess.StartInfo.CreateNoWindow = true;
#endif

        // 先启动 worker，再连接其监听的 Unix Domain Socket
        if (!workerProcess.Start())
            throw new IOException("无法启动 V3 差分进程");

        // 等待 socket 就绪（worker 启动后立即监听）
        try
        {
            for (var i = 0; i < 100; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (workerProcess.HasExited)
                    throw new IOException($"V3 差分进程过早退出，退出码 {workerProcess.ExitCode}");

                try
                {
                    await socket.ConnectAsync(new UnixDomainSocketEndPoint(socketPath), cancellationToken).ConfigureAwait(false);
                    stream = new BufferedStream(new NetworkStream(socket, ownsSocket: false));
                    Log.Information("[VcdiffClient] V3 差分进程已连接, PID {ProcessId}", workerProcess.Id);
                    return;
                }
                catch (SocketException)
                {
                    await Task.Delay(50, cancellationToken).ConfigureAwait(false);
                }
            }

            throw new TimeoutException("等待 V3 差分进程套接字超时");
        }
        catch
        {
            // 连接失败/超时/取消时清理 worker 与 socket，避免泄漏（下次调用会重建）
            CleanupWorker();
            throw;
        }
    }

    /// <summary>
    ///     本地模式：调用捆绑的 xdelta3 直接合并，并在完成后校验大小与 MD5。
    /// </summary>
    private async Task ApplyVcdiffLocalAsync
    (
        byte[]                                  requestData,
        IProgress<(long Progress, long Total)>? progress,
        CancellationToken                       cancellationToken
    )
    {
        using var reader = new BinaryReader(new MemoryStream(requestData, false), Encoding.UTF8);
        var       opcode = reader.ReadInt32();
        if (opcode != VCDIFF_OPCODE)
            throw new InvalidDataException($"未知的操作码: {opcode}");

        var sourceFile      = reader.ReadString();
        var targetFile      = reader.ReadString();
        var expectedMd5     = reader.ReadString();
        var expectedSize    = reader.ReadInt64();
        var deltaLength     = reader.ReadInt32();
        var deltaData       = reader.ReadBytes(deltaLength);

        if (deltaData.Length != deltaLength)
            throw new EndOfStreamException("V3 差分数据不完整");

        var targetDirectory = Path.GetDirectoryName(targetFile);
        if (!string.IsNullOrEmpty(targetDirectory))
            Directory.CreateDirectory(targetDirectory);

        var deltaPath = targetFile + ".delta.tmp";
        var tempPath  = targetFile + ".xlcn.tmp";

        try
        {
            await File.WriteAllBytesAsync(deltaPath, deltaData, cancellationToken).ConfigureAwait(false);

            if (File.Exists(tempPath))
                File.Delete(tempPath);

            var psi = new ProcessStartInfo(localXdelta3Path!)
            {
                UseShellExecute        = false,
                CreateNoWindow         = true,
                RedirectStandardOutput = true,
                RedirectStandardError  = true
            };
            psi.ArgumentList.Add("-f");
            psi.ArgumentList.Add("-d");
            psi.ArgumentList.Add("-s");
            psi.ArgumentList.Add(sourceFile);
            psi.ArgumentList.Add(deltaPath);
            psi.ArgumentList.Add(tempPath);

            using var process = Process.Start(psi) ?? throw new IOException("无法启动 xdelta3");
            var stderr = process.StandardError.ReadToEndAsync();

            try
            {
                while (!process.HasExited)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (expectedSize > 0 && File.Exists(tempPath))
                    {
                        var current = new FileInfo(tempPath).Length;
                        progress?.Report((Math.Min(current, expectedSize), expectedSize));
                    }

                    await Task.Delay(200, cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                if (!process.HasExited)
                {
                    try { process.Kill(); }
                    catch (InvalidOperationException) when (process.HasExited) { }
                    await process.WaitForExitAsync().ConfigureAwait(false);
                }
            }

            var errorText = await stderr.ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (process.ExitCode != 0)
                throw new IOException($"xdelta3 合并失败（退出码 {process.ExitCode}）: {errorText}");

            var resultFile = new FileInfo(tempPath);
            if (!resultFile.Exists)
                throw new IOException("xdelta3 未生成目标文件");

            if (expectedSize >= 0 && resultFile.Length != expectedSize)
                throw new InvalidDataException($"目标文件大小不符: 期望 {expectedSize}, 实际 {resultFile.Length}");

            if (!string.IsNullOrWhiteSpace(expectedMd5))
            {
                await using var resultStream = File.OpenRead(tempPath);
                var hash = Convert.ToHexString(await MD5.HashDataAsync(resultStream, cancellationToken).ConfigureAwait(false));
                if (!string.Equals(hash, expectedMd5, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"目标文件 MD5 校验失败: 期望 {expectedMd5}, 实际 {hash}");
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(tempPath, targetFile, true);
            var finalSize = resultFile.Length;
            progress?.Report((expectedSize > 0 ? expectedSize : finalSize, expectedSize > 0 ? expectedSize : finalSize));
            Log.Information("[VcdiffClient] 本地差分合并完成 {TargetFile}", targetFile);
        }
        finally
        {
            try { if (File.Exists(deltaPath)) File.Delete(deltaPath); } catch { /* ignored */ }
            try { if (File.Exists(tempPath))  File.Delete(tempPath);  } catch { /* ignored */ }
        }
    }

    private void CleanupWorker()
    {
        if (workerProcess != null)
        {
            try
            {
                if (!workerProcess.HasExited)
                    workerProcess.Kill();
            }
            catch (Exception)
            {
                // ignored
            }

            workerProcess.Dispose();
            workerProcess = null;
        }

        if (socket != null)
        {
            try
            {
                stream?.Dispose();
                socket.Dispose();
            }
            catch (Exception)
            {
                // ignored
            }

            stream = null;
            socket = null;
        }
    }

    private async Task WriteWithLengthPrefixAsync(byte[] data, CancellationToken cancellationToken)
    {
        // 先写 4 字节长度前缀，再分块写数据，避免整包二次复制导致大差分包内存峰值翻倍
        var lengthBytes = BitConverter.GetBytes(data.Length);
        await stream!.WriteAsync(lengthBytes, cancellationToken).ConfigureAwait(false);

        const int CHUNK_SIZE = 256 * 1024;
        var       offset     = 0;
        while (offset < data.Length)
        {
            var count = Math.Min(CHUNK_SIZE, data.Length - offset);
            await stream.WriteAsync(data.AsMemory(offset, count), cancellationToken).ConfigureAwait(false);
            offset += count;
        }

        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<byte[]> ReadWithLengthPrefixAsync(CancellationToken cancellationToken)
    {
        var lengthBytes = new byte[sizeof(int)];
        await ReadExactAsync(lengthBytes, cancellationToken).ConfigureAwait(false);

        var length = BitConverter.ToInt32(lengthBytes, 0);
        if (length <= 0 || length > 64 * 1024 * 1024)
            throw new IOException($"V3 差分进程返回了非法长度 {length}");

        var data = new byte[length];
        await ReadExactAsync(data, cancellationToken).ConfigureAwait(false);
        return data;
    }

    private async Task ReadExactAsync(byte[] buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream!.ReadAsync(buffer.AsMemory(offset), cancellationToken).ConfigureAwait(false);
            if (read == 0)
                throw new IOException("V3 差分进程连接中断");

            offset += read;
        }
    }

    private static int GetSerializedStringSize
    (
        string value
    )
    {
        var byteCount = Encoding.UTF8.GetByteCount(value);
        var prefixSize = byteCount switch
        {
            < 1 << 7  => 1,
            < 1 << 14 => 2,
            < 1 << 21 => 3,
            < 1 << 28 => 4,
            _         => 5
        };
        return checked(prefixSize + byteCount);
    }

    private ProcessStartInfo CreateProcessStartInfo
    (
        string executablePath,
        string arguments
    )
    {
        var workingDirectory = Path.GetDirectoryName(executablePath) ?? string.Empty;

        var startInfo = new ProcessStartInfo(executablePath)
        {
            Arguments        = arguments,
            UseShellExecute  = asAdmin,
            WorkingDirectory = workingDirectory
        };

        if (asAdmin)
        {
            // macOS 上无 runas verb；如需提权使用 sudo（需用户输入密码）
            if (Environment.OSVersion.Platform == PlatformID.Unix)
            {
                startInfo.FileName       = "/usr/bin/sudo";
                startInfo.Arguments      = $"{executablePath} {arguments}";
                startInfo.UseShellExecute = false;
            }
            else
            {
                startInfo.Verb = "runas";
            }

            if (!string.IsNullOrWhiteSpace(dotnetRootPath))
                Environment.SetEnvironmentVariable("DOTNET_ROOT", dotnetRootPath);

            return startInfo;
        }

        if (!string.IsNullOrWhiteSpace(dotnetRootPath))
        {
            startInfo.Environment["DOTNET_ROOT"]              = dotnetRootPath;
            startInfo.Environment["DOTNET_MULTILEVEL_LOOKUP"] = "0";
        }

        return startInfo;
    }

    #region Constants

    private const int VCDIFF_OPCODE = 0;
    private const int RESULT_PASS   = 0;
    private const int RESULT_ERROR  = 2;

    /// <summary>差分包大小上限（512MB）。正常游戏差分包远小于此值，仅防御异常/恶意输入。</summary>
    private const int MAX_DELTA_LENGTH = 512 * 1024 * 1024;

    #endregion
}
