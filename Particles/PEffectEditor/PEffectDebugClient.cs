using Polaris.Particles.Debugging;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading.Tasks;

namespace PolarisTools.Particles.PEffectEditor;

internal static class PEffectDebugClient
{
    internal static async Task<(bool ok, string message)> SendAsync(
        IReadOnlyList<PEffectDebugWireFile> files,
        TimeSpan timeout)
    {
        try
        {
            using (var pipe = new NamedPipeClientStream(
                ".", PEffectDebugProtocol.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous))
            {
                try
                {
                    await pipe.ConnectAsync((int)timeout.TotalMilliseconds).ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                    return (false,
                        "Not connected to a running game. Start the game and add [PEffectDebugEnabled] to the mod's BepInPlugin class.");
                }

                using (var writer = new BinaryWriter(pipe, Encoding.UTF8, leaveOpen: true))
                {
                    writer.Write(PEffectDebugProtocol.Version);
                    writer.Write(files.Count);
                    foreach (PEffectDebugWireFile file in files)
                    {
                        writer.Write(file.VirtualName ?? string.Empty);
                        writer.Write(file.DisplayPath ?? string.Empty);
                        writer.Write(file.Text ?? string.Empty);
                    }
                    writer.Flush();
                }

                using (var reader = new BinaryReader(pipe, Encoding.UTF8, leaveOpen: true))
                    return (reader.ReadBoolean(), reader.ReadString());
            }
        }
        catch (Exception ex)
        {
            return (false, "Particle debug push failed: " + ex.Message);
        }
    }
}
