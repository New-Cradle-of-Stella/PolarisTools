using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Polaris.Pevt.Live;

namespace PolarisTools.Event.Pevt.Live;

/// <summary>
/// 把一批 <c>.pevt</c> 源文本推给正在运行的游戏进程，等它回执。
///
/// 管道名与帧格式的唯一定义在 <see cref="PevtLiveProtocol"/>，而那个类型来自 PolarisEvent 的
/// netstandard2.0 目标（本项目对它是 ProjectReference）——两侧不存在"各写一份常量然后慢慢分叉"的可能。
/// </summary>
internal static class PevtLiveClient
{
    /// <summary>
    /// 推送一次。
    /// </summary>
    /// <param name="focusPath">触发本次推送的文件（作者刚保存的那一个），只进游戏侧的回执文案。</param>
    /// <param name="connectTimeout">
    /// 连接超时。保存时的自动推送要短——游戏没开着是常态，不能让每次保存都卡在这里等。
    /// </param>
    /// <param name="applyTimeout">
    /// 等回执的超时。游戏侧要在主线程做完整静态校验与登记，可能还要重启当前事件，给得比连接宽。
    /// </param>
    /// <returns>
    /// <c>connected</c> 为 false 表示没有游戏在听（自动推送时应当静默）；
    /// 为 true 时 <c>ok</c> 与 <c>message</c> 就是游戏侧的回执。
    /// </returns>
    public static async Task<(bool connected, bool ok, string message)> SendAsync(
        string? focusPath,
        IReadOnlyList<PevtLiveWireFile> files,
        TimeSpan connectTimeout,
        TimeSpan applyTimeout)
    {
        if (files is null)
            throw new ArgumentNullException(nameof(files));

        try
        {
            using (var pipe = new NamedPipeClientStream(
                ".", PevtLiveProtocol.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous))
            {
                try
                {
                    await pipe.ConnectAsync((int)connectTimeout.TotalMilliseconds).ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                    return (false, false,
                        "没有连接到运行中的游戏。请先启动游戏，并在设置的“事件（PEVT）”分组里打开“外部导入 .pevt（热重载）”。");
                }

                WriteRequest(pipe, focusPath, files);

                byte[] response = await ReadResponseAsync(pipe, applyTimeout).ConfigureAwait(false);
                if (response.Length == 0)
                    return (true, false, "游戏没有在超时之前回执这次推送。");

                using (var buffer = new MemoryStream(response, writable: false))
                using (var reader = new BinaryReader(buffer, Encoding.UTF8))
                {
                    bool ok = reader.ReadBoolean();
                    return (true, ok, reader.ReadString());
                }
            }
        }
        catch (Exception ex)
        {
            return (true, false, "PEVT 热重载推送失败：" + ex.Message);
        }
    }

    private static void WriteRequest(Stream pipe, string? focusPath, IReadOnlyList<PevtLiveWireFile> files)
    {
        using (var writer = new BinaryWriter(pipe, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(PevtLiveProtocol.Version);
            writer.Write(focusPath ?? string.Empty);
            writer.Write(files.Count);

            foreach (PevtLiveWireFile file in files)
            {
                writer.Write(file.SourcePath ?? string.Empty);
                writer.Write(file.Text ?? string.Empty);
            }

            writer.Flush();
        }
    }

    /// <summary>
    /// 把回执读到流末尾。游戏侧写完回执就关掉这一次连接，因此"读到 EOF"就是"回执完整了"。
    /// <para>
    /// 不在同步的 <c>BinaryReader</c> 上套一层 <c>Task.Delay</c> 竞速：那样超时之后管道会被
    /// 释放，而还阻塞在里面的那次读会在后台抛一个没人观察的异常。带取消的异步读才真的能放手。
    /// </para>
    /// 超时（或对端一个字节都没写）时返回空数组。
    /// </summary>
    private static async Task<byte[]> ReadResponseAsync(Stream pipe, TimeSpan timeout)
    {
        using (var cancellation = new CancellationTokenSource(timeout))
        using (var received = new MemoryStream())
        {
            var buffer = new byte[4096];
            try
            {
                while (true)
                {
                    int read = await pipe.ReadAsync(buffer, 0, buffer.Length, cancellation.Token).ConfigureAwait(false);
                    if (read <= 0)
                        break;

                    received.Write(buffer, 0, read);
                }
            }
            catch (OperationCanceledException)
            {
                return new byte[0];
            }
            catch (IOException)
            {
                // 对端关连接的时候，读可能以"管道已断开"的形式结束而不是返回 0。
                // 已经收到的字节仍然是完整的回执，照常解析。
            }

            return received.ToArray();
        }
    }
}
