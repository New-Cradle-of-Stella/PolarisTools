using Polaris.PUI.Wire;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading.Tasks;

namespace PolarisTools.Pui.PuiVisualEditor.HotReload;

/// <summary>
/// 通过命名管道把一份热重载指令发给正在运行的游戏进程，等待它回执成功/失败。
/// 管道名字、二进制帧格式要跟 Polaris 里的 PuiHotReloadServer
/// （PUI/HotReload/PuiHotReloadServer.cs）保持一致。
/// </summary>
public static class PuiHotReloadClient
{
    /// <summary>跟 Polaris.PUI.HotReload.PuiHotReloadServer.PipeName 保持一致。</summary>
    public const string PipeName = "Polaris.PUI.HotReload";

    public static async Task<(bool ok, string error)> SendAsync(string puiName, IReadOnlyList<PuiWireCommand> commands, TimeSpan timeout)
    {
        try
        {
            using (var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous))
            {
                try
                {
                    await pipe.ConnectAsync((int)timeout.TotalMilliseconds).ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                    return (false, "Not connected to a running game (check that the game is started and the target plugin has PUIHotFixEnabled)");
                }

                using (var writer = new BinaryWriter(pipe, Encoding.UTF8, leaveOpen: true))
                {
                    PuiWireWriter.Write(writer, puiName, commands);
                    writer.Flush();
                }

                using (var reader = new BinaryReader(pipe, Encoding.UTF8, leaveOpen: true))
                {
                    bool ok = reader.ReadBoolean();
                    string error = reader.ReadString();
                    return (ok, error);
                }
            }
        }
        catch (Exception ex)
        {
            return (false, $"Hot reload failed: {ex.Message}");
        }
    }
}
