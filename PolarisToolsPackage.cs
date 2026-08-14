using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.TextTemplating.VSHost;
using PolarisTools.Lang;
using PolarisTools.Pui.PuiSolutions;
using PolarisTools.Pui.PuiVisualEditor;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using VSLangProj;

namespace PolarisTools;

[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[ProvideAutoLoad(VSConstants.UICONTEXT.SolutionExists_string, PackageAutoLoadFlags.BackgroundLoad)]
[ProvideCodeGenerator(typeof(PolarisPuiGenerator), PolarisPuiGenerator.GeneratorName, "Polaris .pui Source Generator", true)]
[ProvideCodeGenerator(typeof(PolarisPuislnGenerator), PolarisPuislnGenerator.GeneratorName, "Polaris .puisln State Graph Source Generator", true)]
[ProvideCodeGenerator(typeof(PolarisLangGenerator), PolarisLangGenerator.GeneratorName, "Polaris .plang Source Generator", true)]
[Guid(PackageGuidString)]
[ProvideMenuResource("Menus.ctmenu", 1)]
[ProvideToolWindow(typeof(PuiSolutionWindow))]
[ProvideToolWindow(typeof(PuiVisualEditorWindow))]
[ProvideToolWindow(typeof(PlangEditorWindow))]
[ProvideBindingPath]
[ProvideEditorFactory(typeof(PuislnEditorFactory), 110)]
[ProvideEditorExtension(typeof(PuislnEditorFactory), ".puisln", 50)]
[ProvideEditorLogicalView(typeof(PuislnEditorFactory), "8940D8ED-3786-4EC5-A558-38F3AFF6AD46")]
[ProvideEditorFactory(typeof(PuiEditorFactory), 111)]
[ProvideEditorExtension(typeof(PuiEditorFactory), ".pui", 60)]
[ProvideEditorLogicalView(typeof(PuiEditorFactory), VSConstants.LOGVIEWID.Designer_string)]
[ProvideEditorFactory(typeof(PlangEditorFactory), 112)]
[ProvideEditorExtension(typeof(PlangEditorFactory), ".plang", 60)]
[ProvideEditorLogicalView(typeof(PlangEditorFactory), VSConstants.LOGVIEWID.Designer_string)]
public sealed class PolarisToolsPackage : AsyncPackage
{
    public const string PackageGuidString =
        "b5bc69d9-3854-4b4a-97b1-9045e0a15d4d"; // 请使用你自己的 GUID

    private DTE2? _dte;
    private Events2? _events2;
    private ProjectItemsEvents? _projectItemsEvents;
    private DocumentEvents? _documentEvents;
    
    protected override async Task InitializeAsync(
    CancellationToken cancellationToken,
    IProgress<ServiceProgressData> progress)
    {
        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
        RegisterEditorFactory(new PuislnEditorFactory(this));
        RegisterEditorFactory(new PuiEditorFactory());
        RegisterEditorFactory(new PlangEditorFactory());
        await base.InitializeAsync(cancellationToken, progress);
        await PuiSolutionWindowCommand.InitializeAsync(this);
        await PuiVisualEditorWindowCommand.InitializeAsync(this);
        await PlangEditorWindowCommand.InitializeAsync(this);
        try
        {
            _dte = await GetServiceAsync(typeof(DTE)) as DTE2;
            if (_dte is null) return;

            _events2 = _dte.Events as Events2;
            if (_events2 is not null)
            {
                _projectItemsEvents = _events2.ProjectItemsEvents;
                _projectItemsEvents.ItemAdded += OnProjectItemAdded;
            }

            _documentEvents = _dte.Events.DocumentEvents;
            _documentEvents.DocumentSaved += OnDocumentSaved;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
        }
    }

    protected override void Dispose(bool disposing)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (disposing)
        {
            if (_projectItemsEvents is not null)
                _projectItemsEvents.ItemAdded -= OnProjectItemAdded;

            if (_documentEvents is not null)
                _documentEvents.DocumentSaved -= OnDocumentSaved;
        }

        base.Dispose(disposing);
    }

    private void OnProjectItemAdded(ProjectItem projectItem)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        EnableAndGenerate(projectItem);
    }

    private void OnDocumentSaved(Document document)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (document.ProjectItem is null)
            return;

        EnableAndGenerate(document.ProjectItem);
    }

    /// <summary>
    /// .pui 和 .puisln 的自动生成流程只在两点上有区别：挂哪个单文件生成器（连带它输出文件的
    /// 扩展名），以及 .pui 多一步"确保用户手写的 code-behind 存在并补齐方法桩"——.puisln 没有
    /// 手写 code-behind 这个概念。所以这里用一张表描述这两种文件，共用同一段流程；
    /// 两者都不支持热重载改图即生效，改完必须保存触发重新生成再重新编译。
    /// </summary>
    private sealed class GeneratorBinding
    {
        public string SourceExtension { get; }
        public string GeneratorName { get; }

        /// <summary>生成结果的扩展名。单文件生成器的输出命名规则是"去掉源文件自身的扩展名，
        /// 再拼上 DefaultExtension"，所以 Foo.pui -> Foo.g.cs，不是 Foo.pui.g.cs。</summary>
        public string GeneratedExtension { get; }

        /// <summary>非空时在跑生成器之前执行（目前只有 .pui 用得上）。</summary>
        public Action<ProjectItem>? BeforeGenerate { get; }

        public GeneratorBinding(string sourceExtension, string generatorName, string generatedExtension,
            Action<ProjectItem>? beforeGenerate = null)
        {
            SourceExtension = sourceExtension;
            GeneratorName = generatorName;
            GeneratedExtension = generatedExtension;
            BeforeGenerate = beforeGenerate;
        }
    }

    private static readonly GeneratorBinding[] GeneratorBindings =
    {
        new GeneratorBinding(".pui", PolarisPuiGenerator.GeneratorName, ".g.cs", EnsureCodeBehindExists),
        new GeneratorBinding(".puisln", PolarisPuislnGenerator.GeneratorName, ".psg.cs"),
        // .plang 生成的类是纯自动内容，不需要用户手写 code-behind，和 .puisln 一样不用 BeforeGenerate。
        new GeneratorBinding(".plang", PolarisLangGenerator.GeneratorName, ".g.cs"),
    };

    /// <summary>
    /// 项目项是 .pui / .puisln 时，设置（或复用已有的）CustomTool 并触发一次生成，
    /// 然后在解决方案资源管理器里隐藏生成出来的纯样板文件——二次开发人员只需要看到
    /// 自己写的 Foo.pui.cs，不需要看到 Foo.g.cs / Foo.psg.cs。其它类型的项目项直接忽略。
    /// </summary>
    private static void EnableAndGenerate(ProjectItem projectItem)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        GeneratorBinding? binding = FindBinding(projectItem);
        if (binding is null)
            return;

        try
        {
            Property customToolProperty = projectItem.Properties.Item("CustomTool");

            if (!string.Equals(customToolProperty.Value as string, binding.GeneratorName, StringComparison.Ordinal))
                customToolProperty.Value = binding.GeneratorName;

            binding.BeforeGenerate?.Invoke(projectItem);

            RunCustomTool(projectItem);

            HideGeneratedFile(projectItem, binding.GeneratedExtension);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Polaris {binding.SourceExtension} code generation failed: {ex}");
        }
    }

    /// <summary>按文件扩展名找出该项目项适用的生成器绑定；文件夹、虚拟节点、特殊项目项
    /// 可能不存在 FileNames[1]，一律当成"不适用"。</summary>
    private static GeneratorBinding? FindBinding(ProjectItem projectItem)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        string filePath;
        try
        {
            // DTE 的 FileNames 下标从 1 开始。
            filePath = projectItem.FileNames[1];
        }
        catch
        {
            return null;
        }

        foreach (GeneratorBinding binding in GeneratorBindings)
        {
            if (filePath.EndsWith(binding.SourceExtension, StringComparison.OrdinalIgnoreCase))
                return binding;
        }
        return null;
    }

    /// <summary>在解决方案资源管理器中隐藏 sourceItem 生成出来的样板文件。</summary>
    private static void HideGeneratedFile(ProjectItem sourceItem, string generatedExtension)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        try
        {
            string sourceFileName = Path.GetFileName(sourceItem.FileNames[1]);
            string generatedName = Path.GetFileNameWithoutExtension(sourceFileName) + generatedExtension;

            foreach (ProjectItem child in sourceItem.ProjectItems)
            {
                if (!string.Equals(child.Name, generatedName, StringComparison.OrdinalIgnoreCase))
                    continue;

                var visibleProperty = child.Properties?.Item("Visible");
                if (visibleProperty != null)
                    visibleProperty.Value = false;
                break;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Polaris: failed to hide the generated {generatedExtension}: {ex}");
        }
    }

    /// <summary>
    /// 确保 Foo.pui 对应的 Foo.pui.cs code-behind 文件存在（只在文件不存在时创建一次骨架，
    /// 绝不覆盖已有内容），并把新增控件/属性所需的回调方法桩补齐。
    /// 实际的文件创建/追加逻辑在 <see cref="PuiCodeBehindSync"/> 里，
    /// 和可视化编辑器"一键创建回调"按钮共用同一份实现。
    /// </summary>
    private static void EnsureCodeBehindExists(ProjectItem projectItem)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        try
        {
            string puiPath = projectItem.FileNames[1];
            string defaultNamespace =
                projectItem.ContainingProject?.Properties.Item("DefaultNamespace")?.Value as string
                ?? string.Empty;

            string codeBehindPath = PuiCodeBehindSync.EnsureCodeBehindFile(puiPath, defaultNamespace, projectItem);
            PuiCodeBehindSync.EnsureAllHandlerStubs(puiPath, codeBehindPath);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Polaris: failed to create the .pui.cs code-behind file: {ex}");
        }
    }

    private static void RunCustomTool(ProjectItem projectItem)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        try
        {
            if (projectItem.Object is VSProjectItem vsProjectItem)
            {
                vsProjectItem.RunCustomTool();
                return;
            }
            System.Diagnostics.Debug.WriteLine(
                $"Polaris: could not cast the ProjectItem to VSProjectItem, " +
                $"so Custom Tool cannot run. File: {projectItem.Name}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Polaris: failed to Run Custom Tool: {ex}");
        }
    }
}
