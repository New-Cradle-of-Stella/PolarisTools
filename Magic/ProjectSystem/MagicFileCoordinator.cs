using System;
using System.IO;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using Polaris.Magic.Authoring;
using PolarisTools.Magic.CodeBehind;

namespace PolarisTools.Magic;

/// <summary>
/// 文件组协调器：<c>.pmagic</c> 的新建、保存和重命名都经过这里。
///
/// 它负责的是"三个文件与项目项元数据始终自洽"这件事——作者手写的 <c>.pmagic.cs</c> 存在、
/// 生成物挂在根项目项下面且不可见、根项目项挂着生成器。这些如果散落在包的事件处理里，
/// 就会出现"模板新建能用、手动添加现有文件不能用"这类只在某条路径上成立的行为。
///
/// 这里不报诊断。文件名当不了类名之类的问题会让生成器失败并在 Error List 留下一行说明；
/// 作者写的 C# 有问题则由编译器说。
/// </summary>
internal static class MagicFileCoordinator
{
    /// <summary>
    /// 跑生成器之前的准备：确保作者文件存在、挂进项目、并补上缺失的 <c>RunAsync</c>。
    /// 作为 <c>GeneratorBindings</c> 的 <c>BeforeGenerate</c> 调用。
    /// </summary>
    internal static void Prepare(ProjectItem definitionItem)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        string? definitionPath = TryGetPath(definitionItem);
        if (!MagicFileGroup.IsDefinition(definitionPath) || !MagicFileGroup.HasUsableClassName(definitionPath))
        {
            // 文件名当不了 C# 类名时不建 code-behind：生成器会拒绝并说清要改文件名。
            return;
        }

        string className = MagicFileGroup.ClassNameOf(definitionPath);
        string namespaceName = ResolveNamespace(definitionItem);
        string codeBehindPath = MagicFileGroup.CodeBehindPathOf(definitionPath);

        try
        {
            MagicCodeBehindSync.EnsureFile(codeBehindPath, namespaceName, className);
            MagicCodeBehindSync.EnsureRunMethod(codeBehindPath, namespaceName, className);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("Polaris: failed to prepare " + codeBehindPath + ": " + ex);
        }

        Nest(definitionItem, codeBehindPath, generated: false);
    }

    /// <summary>生成器跑完之后：把生成物挂到根项目项下面并标成自动生成、不可见。</summary>
    internal static void AfterGenerate(ProjectItem definitionItem)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        string? definitionPath = TryGetPath(definitionItem);
        if (MagicFileGroup.IsDefinition(definitionPath))
        {
            Nest(definitionItem, MagicFileGroup.GeneratedPathOf(definitionPath), generated: true);
        }
    }

    /// <summary>
    /// 根 <c>.pmagic</c> 被重命名：把两个关联文件跟着改名。
    ///
    /// 任何目标文件已存在时整个关联重命名取消、保留现有文件——覆盖掉一个同名的、可能是作者别的
    /// 魔法的 <c>.pmagic.cs</c> 是不可撤销的。这种情况下关联文件还叫旧名字，生成器随后会因为
    /// 找不到 code-behind 而报出来。
    /// </summary>
    internal static void OnRenamed(ProjectItem definitionItem, string oldName)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        string? newPath = TryGetPath(definitionItem);
        if (!MagicFileGroup.IsDefinition(newPath) || string.IsNullOrEmpty(oldName))
        {
            return;
        }

        string? directory = Path.GetDirectoryName(newPath);
        if (string.IsNullOrEmpty(directory))
        {
            return;
        }

        string oldDefinitionPath = Path.Combine(directory, oldName);
        if (!MagicFileGroup.IsDefinition(oldDefinitionPath))
        {
            return;
        }

        string[] oldPaths =
        {
            MagicFileGroup.CodeBehindPathOf(oldDefinitionPath),
            MagicFileGroup.GeneratedPathOf(oldDefinitionPath),
        };

        string[] newPaths =
        {
            MagicFileGroup.CodeBehindPathOf(newPath),
            MagicFileGroup.GeneratedPathOf(newPath),
        };

        for (int i = 0; i < oldPaths.Length; i++)
        {
            if (File.Exists(newPaths[i])
                && !string.Equals(oldPaths[i], newPaths[i], StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        for (int i = 0; i < oldPaths.Length; i++)
        {
            RenameChild(definitionItem, oldPaths[i], newPaths[i]);
        }
    }

    /// <summary>
    /// 生成代码落到哪个命名空间：项目项的 <c>CustomToolNamespace</c> 优先，其次项目的
    /// <c>DefaultNamespace</c>，都没有就用兜底命名空间。
    ///
    /// 这个结果必须与生成器收到的 <c>wszDefaultNamespace</c> 一致——作者文件的
    /// <c>namespace</c> 是按这里的结论写出去的，两边不一致时生成的那一半 partial 会落到
    /// 另一个命名空间，编译期表现为"找不到 RunAsync"。
    /// </summary>
    internal static string ResolveNamespace(ProjectItem definitionItem)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        string? custom = TryGetProperty(definitionItem?.Properties, "CustomToolNamespace");
        if (custom != null && custom.Trim().Length > 0 && MagicIdentifier.IsValidNamespace(custom))
        {
            return custom;
        }

        string? projectDefault = TryGetProperty(definitionItem?.ContainingProject?.Properties, "DefaultNamespace");
        if (projectDefault != null && projectDefault.Trim().Length > 0 && MagicIdentifier.IsValidNamespace(projectDefault))
        {
            return projectDefault;
        }

        return MagicCodeBehindContract.FallbackNamespace;
    }

    private static void Nest(ProjectItem definitionItem, string childPath, bool generated)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (!File.Exists(childPath))
        {
            return;
        }

        try
        {
            ProjectItem child = FindChild(definitionItem, Path.GetFileName(childPath))
                ?? definitionItem.ProjectItems.AddFromFile(childPath);


            SetProperty(child, "DependentUpon", Path.GetFileName(TryGetPath(definitionItem)));

            if (generated)
            {
                // 生成物是纯样板：作者只需要看到自己写的 .pmagic.cs。
                SetProperty(child, "AutoGen", true);
                SetProperty(child, "DesignTime", true);
                SetProperty(child, "Visible", false);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("Polaris: failed to nest " + childPath + ": " + ex);
        }
    }

    private static void RenameChild(ProjectItem definitionItem, string oldPath, string newPath)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (!File.Exists(oldPath) || string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            ProjectItem? child = FindChild(definitionItem, Path.GetFileName(oldPath));
            if (child != null)
            {
                // 交给 DTE 改名：它会同时改磁盘文件和项目文件里的项，比我们自己搬文件再补项目项少一步失步。
                child.Name = Path.GetFileName(newPath);
                SetProperty(child, "DependentUpon", Path.GetFileName(TryGetPath(definitionItem)));
                return;
            }

            File.Move(oldPath, newPath);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("Polaris: failed to rename " + oldPath + ": " + ex);
        }
    }

    private static ProjectItem? FindChild(ProjectItem parent, string? fileName)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        try
        {
            foreach (ProjectItem child in parent.ProjectItems)
            {
                if (string.Equals(child.Name, fileName, StringComparison.OrdinalIgnoreCase))
                {
                    return child;
                }
            }
        }
        catch
        {
            // 某些项目系统在项还没实体化时枚举会抛；当作"没有这个子项"。
        }

        return null;
    }

    private static void SetProperty(ProjectItem item, string name, object value)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        try
        {
            Property? property = item.Properties?.Item(name);
            if (property != null && !Equals(property.Value, value))
            {
                property.Value = value;
            }
        }
        catch
        {
            // 不同项目系统暴露的属性集不一样（CPS 缺 AutoGen 之类）；缺一个不影响其余。
        }
    }

    private static string? TryGetProperty(Properties? properties, string name)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        try
        {
            return properties?.Item(name)?.Value as string;
        }
        catch
        {
            return null;
        }
    }

    private static string? TryGetPath(ProjectItem? item)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        try
        {
            // DTE 的 FileNames 下标从 1 开始。
            return item?.FileNames[1];
        }
        catch
        {
            return null;
        }
    }
}
