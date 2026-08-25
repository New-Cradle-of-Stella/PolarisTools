using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading.Tasks;
using Polaris.Map.HotReload;

namespace PolarisTools.Map.Editor;

internal static class PmapHotReloadClient
{
    internal static async Task<(bool ok, string error)> SendAsync(string key, string xml, TimeSpan timeout)
    {
        try
        {
            byte[] body = new UTF8Encoding(false, true).GetBytes(xml);
            if (body.Length > PmapWireProtocol.MaxDocumentBytes)
                return (false, "The .pmap document is too large for hot reload.");

            using (var pipe = new NamedPipeClientStream(".", PmapWireProtocol.PipeName,
                PipeDirection.InOut, PipeOptions.Asynchronous))
            {
                try { await pipe.ConnectAsync((int)timeout.TotalMilliseconds).ConfigureAwait(false); }
                catch (TimeoutException)
                {
                    return (false, "Game not connected. Load this .pmap first and mark the plugin class with [PMapHotFixEnabled].");
                }

                using (var writer = new BinaryWriter(pipe, Encoding.UTF8, true))
                {
                    writer.Write(PmapWireProtocol.Version);
                    writer.Write((byte)PmapWireRequest.HotReload);
                    writer.Write(key ?? "");
                    writer.Write(body.Length);
                    writer.Write(body);
                    writer.Flush();
                }
                using (var reader = new BinaryReader(pipe, Encoding.UTF8, true))
                    return (reader.ReadBoolean(), reader.ReadString());
            }
        }
        catch (Exception ex)
        {
            return (false, "PMap hot reload failed: " + ex.Message);
        }
    }

    internal static async Task<(bool ok, string message)> RequestPreviewAsync(
        bool extract, uint[]? imageIds, TimeSpan timeout)
    {
        try
        {
            using (var pipe = new NamedPipeClientStream(".", PmapWireProtocol.PipeName,
                PipeDirection.InOut, PipeOptions.Asynchronous))
            {
                try { await pipe.ConnectAsync((int)timeout.TotalMilliseconds).ConfigureAwait(false); }
                catch (TimeoutException)
                {
                    return (false, "Game not connected. Enter a hot-reload-enabled .pmap first.");
                }
                using (var writer = new BinaryWriter(pipe, Encoding.UTF8, true))
                {
                    writer.Write(PmapWireProtocol.Version);
                    writer.Write((byte)(extract
                        ? PmapWireRequest.ExtractOriginalMapPreview
                        : PmapWireRequest.ClearOriginalMapPreview));
                    if (extract)
                    {
                        uint[] ids = imageIds ?? Array.Empty<uint>();
                        if (ids.Length > PmapWireProtocol.MaxPreviewImageCount)
                            return (false, "The .pmap preview contains too many image ids.");
                        writer.Write(ids.Length);
                        foreach (uint id in ids) writer.Write(id);
                    }
                    writer.Flush();
                }
                using (var reader = new BinaryReader(pipe, Encoding.UTF8, true))
                    return (reader.ReadBoolean(), reader.ReadString());
            }
        }
        catch (Exception ex)
        {
            return (false, "Map preview request failed: " + ex.Message);
        }
    }
}
