using System;
using System.IO;
using Polaris.Magic.Authoring;

namespace PolarisTools.Magic;

/// <summary>
/// 一组魔法文件的命名规则。生成器、文件协调器和编辑器都从这里取路径，不各自拼字符串。
///
/// 一组固定三个文件，基名完全相同（<c>OrdinalIgnoreCase</c> 比较）、必须同目录：
/// <list type="bullet">
///   <item><c>ExampleMagic.pmagic</c>——静态参数，根项目项。</item>
///   <item><c>ExampleMagic.pmagic.g.cs</c>——生成物，AutoGen + 不可见。</item>
///   <item><c>ExampleMagic.pmagic.cs</c>——作者手写的 RunAsync。</item>
/// </list>
/// 不允许跨目录关联：靠"同目录同基名"确定关系，比维护一份显式清单少一种失步来源。
/// </summary>
internal static class MagicFileGroup
{
    internal const string DefinitionExtension = ".pmagic";
    internal const string GeneratedExtension = ".pmagic.g.cs";
    internal const string CodeBehindExtension = ".pmagic.cs";

    internal static bool IsDefinition(string? path) =>
        path != null
        && path.EndsWith(DefinitionExtension, StringComparison.OrdinalIgnoreCase)
        && !path.EndsWith(CodeBehindExtension, StringComparison.OrdinalIgnoreCase);

    internal static bool IsCodeBehind(string? path) =>
        path != null && path.EndsWith(CodeBehindExtension, StringComparison.OrdinalIgnoreCase);

    internal static bool IsGenerated(string? path) =>
        path != null && path.EndsWith(GeneratedExtension, StringComparison.OrdinalIgnoreCase);

    /// <summary>类名 = <c>.pmagic</c> 的文件基名。</summary>
    internal static string ClassNameOf(string? definitionPath) =>
        Path.GetFileNameWithoutExtension(definitionPath ?? string.Empty);

    internal static string CodeBehindPathOf(string? definitionPath) => definitionPath + ".cs";

    internal static string GeneratedPathOf(string? definitionPath) => definitionPath + ".g.cs";

    /// <summary>
    /// 从组里任意一个文件反推根 <c>.pmagic</c> 的路径。认不出来时返回 <c>null</c>。
    /// </summary>
    internal static string? DefinitionPathOf(string? anyPath)
    {
        if (anyPath == null)
        {
            return null;
        }

        if (IsGenerated(anyPath))
        {
            return anyPath.Substring(0, anyPath.Length - GeneratedExtension.Length) + DefinitionExtension;
        }

        if (IsCodeBehind(anyPath))
        {
            return anyPath.Substring(0, anyPath.Length - CodeBehindExtension.Length) + DefinitionExtension;
        }

        return IsDefinition(anyPath) ? anyPath : null;
    }

    /// <summary>基名是否能直接当 C# 类名用。</summary>
    internal static bool HasUsableClassName(string? definitionPath) =>
        MagicIdentifier.IsValidName(ClassNameOf(definitionPath));
}
