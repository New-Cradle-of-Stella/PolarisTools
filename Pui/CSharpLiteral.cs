namespace PolarisTools.Pui;

/// <summary>
/// 生成 C# 源码文本时共用的两个字面量/标识符处理函数。原本 PolarisPuiGenerator、
/// CSharpTextEmitter、PolarisPuislnGenerator 三处各自抄了一份同样的 Esc()（其中两处还各抄了
/// 一份同样的 SanitizeIdentifier()），三份实现必须永远一致却没有任何机制保证——统一收在这里。
/// </summary>
internal static class CSharpLiteral
{
    /// <summary>转义成能直接塞进 C# 双引号字符串字面量里的形式；null 视作空串。</summary>
    public static string Escape(string value) =>
        (value ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");

    /// <summary>
    /// 把任意名字（元素 Name、图节点 key）压成合法的 C# 标识符：非字母数字下划线一律换成
    /// 下划线，开头是数字时再补一个下划线前缀。空名字返回 "Unnamed"。
    /// </summary>
    public static string SanitizeIdentifier(string name)
    {
        if (string.IsNullOrEmpty(name)) return "Unnamed";

        var sb = new System.Text.StringBuilder(name.Length + 1);
        foreach (char c in name)
            sb.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');

        if (char.IsDigit(sb[0])) sb.Insert(0, '_');
        return sb.ToString();
    }
}
