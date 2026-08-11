using EnvDTE;
using Microsoft.VisualStudio.Shell;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace PolarisTools.Pui.PuiVisualEditor;

/// <summary>
/// .pui.cs 代码后置文件的创建/同步逻辑。VS 包"保存 .pui"事件那条路径和可视化编辑器
/// "一键创建回调"按钮那条路径共用这一份实现，避免两处各写一份、日后走样。
/// </summary>
internal static class PuiCodeBehindSync
{
    /// <summary>
    /// 确保 puiPath 对应的 .pui.cs 存在（不存在就用骨架创建一份），返回它的完整路径。
    /// <paramref name="puiProjectItem"/> 非空时，还会把新建的文件挂到解决方案里
    /// （加入项目、设置 DependentUpon）；传 null 表示只做磁盘文件操作，不碰解决方案。
    /// </summary>
    public static string EnsureCodeBehindFile(string puiPath, string defaultNamespace, ProjectItem puiProjectItem)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        string codeBehindPath = puiPath + ".cs";

        // 磁盘上还没有就写一份骨架；已经有了则绝不覆盖（用户的交互逻辑都在里面）。
        if (!File.Exists(codeBehindPath))
        {
            string skeleton = PolarisPuiGenerator.GenerateCodeBehindSkeleton(puiPath, defaultNamespace);
            File.WriteAllText(codeBehindPath, skeleton, Encoding.UTF8);
        }

        // 不管文件是刚创建的还是本来就在，只要还没挂进解决方案就挂一次（AddFromFile 对已在项目里的
        // 文件会重复添加，所以先用 HasChildNamed 挡掉；刚创建的文件必然还没挂上，同一个判断能覆盖两种情况）。
        if (puiProjectItem != null && !HasChildNamed(puiProjectItem, Path.GetFileName(codeBehindPath)))
            NestCodeBehind(puiProjectItem.ProjectItems.AddFromFile(codeBehindPath), puiPath);

        return codeBehindPath;
    }

    /// <summary>
    /// 确保 codeBehindPath 里存在 <paramref name="handler"/> 对应的方法
    /// （不存在就按签名追加桩，已存在就什么都不改），返回该方法签名所在的 1-based 行号，
    /// 供编辑器跳转定位用。
    /// </summary>
    public static int EnsureHandlerStub(string codeBehindPath, PolarisPuiGenerator.HandlerRequirement handler)
    {
        string text = File.ReadAllText(codeBehindPath);
        int line = FindMethodLine(text, handler.MethodName);
        if (line > 0)
            return line;

        int lastBrace = text.LastIndexOf('}');
        if (lastBrace < 0)
            return 1;

        var stub = new StringBuilder();
        stub.Append("    // 自动追加的方法桩；可以直接在这里改成真正的逻辑。\n");
        stub.Append("    public ").Append(handler.ReturnType).Append(' ').Append(handler.MethodName)
            .Append('(').Append(handler.Parameters).Append(")\n");
        stub.Append("    {\n");
        if (!string.IsNullOrEmpty(handler.DefaultBody))
            stub.Append("        ").Append(handler.DefaultBody).Append('\n');
        stub.Append("    }\n\n");

        string updated = text.Substring(0, lastBrace) + stub + text.Substring(lastBrace);
        File.WriteAllText(codeBehindPath, updated, Encoding.UTF8);

        return FindMethodLine(updated, handler.MethodName);
    }

    /// <summary>
    /// 扫描 puiPath 里所有回调/配置属性指向的方法，把 codeBehindPath 里还没有的方法桩
    /// （按各自应有签名）追加到文件末尾。已有方法（不管是不是这里生成的桩）一律不动，
    /// 多个控件复用同一个方法名时也只会生成一份桩。
    /// </summary>
    public static void EnsureAllHandlerStubs(string puiPath, string codeBehindPath)
    {
        if (!File.Exists(puiPath) || !File.Exists(codeBehindPath))
            return;

        string puiXml = File.ReadAllText(puiPath);
        IReadOnlyList<PolarisPuiGenerator.HandlerRequirement> required =
            PolarisPuiGenerator.CollectRequiredHandlers(puiXml);
        if (required.Count == 0)
            return;

        var seen = new HashSet<string>();
        foreach (PolarisPuiGenerator.HandlerRequirement handler in required)
        {
            if (seen.Add(handler.MethodName))
                EnsureHandlerStub(codeBehindPath, handler);
        }
    }

    private static int FindMethodLine(string text, string methodName)
    {
        Match match = Regex.Match(text, $@"\b{Regex.Escape(methodName)}\s*\(");
        if (!match.Success)
            return -1;
        return text.Substring(0, match.Index).Count(c => c == '\n') + 1;
    }

    private static bool HasChildNamed(ProjectItem parent, string fileName)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        try
        {
            foreach (ProjectItem child in parent.ProjectItems)
            {
                if (string.Equals(child.Name, fileName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        catch { }
        return false;
    }

    private static void NestCodeBehind(ProjectItem codeBehindItem, string puiPath)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        try
        {
            codeBehindItem.Properties.Item("DependentUpon").Value = Path.GetFileName(puiPath);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Polaris：设置 DependentUpon 失败：{ex}");
        }
    }
}
