using System;
using System.IO;
using System.Text;
using PolarisTools.Pui.PuiVisualEditor;

namespace PolarisTools.Event.Pevt;

/// <summary>
/// <c>.pevt</c> / <c>.pactor</c> 生成器共用的路径与字节读取。
/// </summary>
internal static class PevtProjectPaths
{
    /// <summary>
    /// 算出嵌入包里要写的项目相对路径。定位不到项目根时退回文件名——绝不能把开发机的绝对路径
    /// 写进生成代码，那既泄漏本机目录结构，也会让运行时的 SourcePath 校验直接失败。
    /// </summary>
    public static string ToProjectRelative(string filePath)
    {
        string fileName = Path.GetFileName(filePath) ?? "";

        try
        {
            string root = PuiProjectLocator.ResolveProjectDir(filePath);
            if (string.IsNullOrEmpty(root))
                return fileName;

            string full = Path.GetFullPath(filePath);
            string rootFull = Path.GetFullPath(root);
            if (!rootFull.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
                rootFull += Path.DirectorySeparatorChar;

            if (!full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
                return fileName;

            // 生成时统一写成 '/' 分隔。
            return full.Substring(rootFull.Length).Replace('\\', '/');
        }
        catch
        {
            return fileName;
        }
    }

    /// <summary>
    /// 优先读磁盘上的原始字节，这样源文本里现有的 CRLF/LF 与文件编码原样保留。
    /// 读不到（文件还没落盘、被占用）时退回用 VS 给的内容按 UTF-8 无 BOM 编码。
    /// </summary>
    public static byte[] ReadAllBytesOrEncode(string filePath, string contents)
    {
        try
        {
            if (File.Exists(filePath))
            {
                byte[] bytes = File.ReadAllBytes(filePath);

                // 去掉 BOM：嵌入包约定用 UTF-8 无 BOM 字节压缩。
                if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                {
                    var trimmed = new byte[bytes.Length - 3];
                    Array.Copy(bytes, 3, trimmed, 0, trimmed.Length);
                    return trimmed;
                }

                return bytes;
            }
        }
        catch
        {
            // 落到下面按内容编码。
        }

        return new UTF8Encoding(false).GetBytes(contents ?? "");
    }
}
