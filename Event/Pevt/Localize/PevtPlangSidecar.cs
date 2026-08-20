using System;
using System.IO;
using EnvDTE;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Polaris.Lang;
using PolarisTools.Lang;
using VSLangProj;
using IServiceProvider = System.IServiceProvider;

namespace PolarisTools.Event.Pevt.Localize;

/// <summary>
/// <c>.pevt</c> 旁边那份同名 <c>.plang</c>：读、写，以及"确保它挂在项目里并且挂着 .plang 生成器"。
///
/// 同名同目录是刻意的约定而不是配置项——作者一眼就能看出哪份表格属于哪个事件，
/// 而重命名事件文件时两份跟着一起走也不需要额外记一份映射。
/// </summary>
internal static class PevtPlangSidecar
{
    public const string Extension = ".plang";

    /// <summary><c>Foo.pevt</c> → 同目录的 <c>Foo.plang</c>。</summary>
    public static string PathFor(string pevtPath) =>
        Path.Combine(
            Path.GetDirectoryName(pevtPath) ?? "",
            Path.GetFileNameWithoutExtension(pevtPath) + Extension);

    /// <summary>读已有的表格；文件不存在时返回 null，内容坏掉时抛出（调用方要如实报告，不能静默丢数据）。</summary>
    public static PlangDocument? Load(string plangPath) =>
        File.Exists(plangPath) ? PlangDocument.Load(plangPath) : null;

    /// <summary>
    /// 这份 <c>.plang</c> 是不是正开在编辑器里且有未保存的修改。
    ///
    /// 是的话必须停手：我们直接写磁盘，而那个编辑器窗口手里还攥着旧数据，作者随后一次 Ctrl+S
    /// 就会把这次写进去的键全部盖掉——而且是悄无声息地盖掉。
    /// </summary>
    public static bool IsOpenAndDirty(IServiceProvider serviceProvider, string plangPath)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        return TryGetOpenDocData(serviceProvider, plangPath, out IVsPersistDocData docData)
            && docData.IsDocDataDirty(out int dirty) == VSConstants.S_OK
            && dirty != 0;
    }

    /// <summary>
    /// 写盘，并让已经打开（但没有未保存改动）的那个编辑器窗口重新加载。
    /// 不重载的话作者会看着一张停留在写入之前的表格，以为按钮没生效。
    /// </summary>
    public static void Save(IServiceProvider serviceProvider, string plangPath, PlangDocument document)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        document.Save(plangPath);

        if (TryGetOpenDocData(serviceProvider, plangPath, out IVsPersistDocData docData))
            docData.ReloadDocData(0);
    }

    /// <summary>
    /// 把这份 <c>.plang</c> 挂进 <paramref name="pevtPath"/> 所属的项目，并确保它挂着 .plang 生成器。
    ///
    /// SDK 风格项目会靠通配符自动包含新文件，那时 <c>AddFromFile</c> 只是把已有项拿回来；但
    /// <c>CustomTool</c> 属性无论如何都得显式设一次——没有它就不会生成注册类，文案在游戏里查不到。
    /// 失败不抛：表格已经写好了，挂载失败只值一条提示，不该让整个操作看起来像没做。
    /// </summary>
    /// <returns>挂载并触发生成成功时为 null，否则是给作者看的原因。</returns>
    public static string? EnsureInProject(string pevtPath, string plangPath)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        try
        {
            if (Package.GetGlobalService(typeof(SDTE)) is not DTE dte)
                return "Could not reach the Visual Studio project system.";

            ProjectItem? owner = dte.Solution.FindProjectItem(pevtPath);
            Project? project = owner?.ContainingProject;
            if (project is null)
                return "The .pevt file does not belong to a project in this solution.";

            ProjectItem? item = dte.Solution.FindProjectItem(plangPath) ?? project.ProjectItems.AddFromFile(plangPath);
            if (item is null)
                return "Could not add the .plang file to the project.";

            Property customTool = item.Properties.Item("CustomTool");
            if (!string.Equals(customTool.Value as string, PolarisLangGenerator.GeneratorName, StringComparison.Ordinal))
                customTool.Value = PolarisLangGenerator.GeneratorName;

            if (item.Object is VSProjectItem vsProjectItem)
                vsProjectItem.RunCustomTool();

            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    private static bool TryGetOpenDocData(IServiceProvider serviceProvider, string path, out IVsPersistDocData docData)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        docData = null!;

        try
        {
            if (!VsShellUtilities.IsDocumentOpen(
                    serviceProvider, path, Guid.Empty,
                    out IVsUIHierarchy _, out uint _, out IVsWindowFrame frame)
                || frame is null)
            {
                return false;
            }

            if (frame.GetProperty((int)__VSFPROPID.VSFPROPID_DocData, out object data) != VSConstants.S_OK)
                return false;

            docData = (data as IVsPersistDocData)!;
            return docData != null;
        }
        catch (Exception)
        {
            // 拿不到文档状态时按"没开着"处理：写盘本身仍然是对的，最坏情况是作者需要手动重新打开。
            return false;
        }
    }
}
