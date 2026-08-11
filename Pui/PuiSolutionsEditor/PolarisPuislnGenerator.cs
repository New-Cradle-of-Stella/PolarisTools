using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using PolarisTools.Pui.PuiVisualEditor;

namespace PolarisTools.Pui.PuiSolutions;

/// <summary>
/// .puisln → C# 单文件生成器：把图里"哪个 PUI 状态节点的哪个输出连接器连到哪个目标节点"的
/// 结构，编译期固化成一份不可变的 Polaris.PUI.PUIGraphDefinition 蓝图（节点 + 边），
/// 而不是像过去那样拍平进一个全局静态字典——运行时可以对同一份蓝图多次
/// CreateSolution()，得到互相独立、各自维护"当前节点"的图状态机实例。
/// 目标/阻塞信息不落在 .puisln 自己的 JSON 里——阻塞标记实际存在源节点绑定的 .pui 文件
/// 的 StateTransitions 列表上（按 SourceTransitionId 匹配），这里生成时重新读一次那个
/// .pui 文件取值，不需要在 .puisln 里再复制一份。
/// 不支持热重载：改一次图必须重新触发这个生成器（保存文件即可），详见项目文档。
/// </summary>
// 独立 GUID：不能复用 PolarisToolsPackage.PackageGuidString——两个 COM 可见的单文件生成器
// 类如果共享同一个 CLSID，VS 按 CLSID 做的生成器分发会互相踩。
[ComVisible(true)]
[Guid("5b9c133d-7543-4a42-b203-b21a337f7a47")]
[ClassInterface(ClassInterfaceType.None)]
public sealed class PolarisPuislnGenerator : IVsSingleFileGenerator
{
    public const string GeneratorName = "PolarisPuislnGenerator";

    public int DefaultExtension(out string pbstrDefaultExtension)
    {
        // Foo.puisln -> Foo.psg.cs；不能叫 .g.cs，会跟同目录 Foo.pui 生成的 Foo.g.cs 撞名。
        pbstrDefaultExtension = ".psg.cs";
        return VSConstants.S_OK;
    }

    public int Generate(
        string wszInputFilePath,
        string bstrInputFileContents,
        string wszDefaultNamespace,
        IntPtr[] rgbOutputFileContents,
        out uint pcbOutput,
        IVsGeneratorProgress pGenerateProgress)
    {
        // VS 保证单文件生成器在主线程上调用；显式断言而不是隐式依赖这个前提。
        ThreadHelper.ThrowIfNotOnUIThread();
        pcbOutput = 0;

        try
        {
            string generatedCode = GenerateCSharp(wszInputFilePath, bstrInputFileContents, wszDefaultNamespace, pGenerateProgress);

            byte[] bytes = Encoding.UTF8.GetBytes(generatedCode);
            IntPtr outputBuffer = Marshal.AllocCoTaskMem(bytes.Length);
            Marshal.Copy(bytes, 0, outputBuffer, bytes.Length);

            rgbOutputFileContents[0] = outputBuffer;
            pcbOutput = (uint)bytes.Length;
            return VSConstants.S_OK;
        }
        catch (Exception ex)
        {
            pGenerateProgress?.GeneratorError(0, 0, ex.Message, 0, 0);
            return VSConstants.E_FAIL;
        }
    }

    private sealed class ResolvedTransition
    {
        public string SourceNodeKey;
        public string TriggerKey;
        public string TargetNodeKey;
        public bool Blocking;
        public bool IsExit;
    }

    private static string GenerateCSharp(
        string inputFilePath,
        string inputFileContents,
        string defaultNamespace,
        IVsGeneratorProgress progress)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        string graphDir = Path.GetDirectoryName(inputFilePath) ?? "";
        string graphName = Path.GetFileNameWithoutExtension(inputFilePath);
        string className = graphName + "_Solution";
        string ns = PolarisPuiGenerator.ResolveNamespace(defaultNamespace);

        PuislnDocument doc = PuislnSerializer.LoadFromString(inputFileContents);

        // 按节点索引缓存"该 PuiState 节点绑定的 .pui 里的状态连接点列表"，避免同一个 .pui
        // 被多条连线引用时重复解析。Entry 节点没有对应文件，值为 null。
        var nodeTransitions = new Dictionary<int, IReadOnlyList<PuiStateTransition>>();
        IReadOnlyList<PuiStateTransition> GetTransitions(int nodeIndex)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (nodeTransitions.TryGetValue(nodeIndex, out var cached))
                return cached;

            PuislnNode node = doc.Nodes[nodeIndex];
            IReadOnlyList<PuiStateTransition> result = Array.Empty<PuiStateTransition>();
            if (node.Type == "PuiState" && !string.IsNullOrEmpty(node.PuiRelativePath))
            {
                string puiPath = Path.GetFullPath(Path.Combine(graphDir, node.PuiRelativePath));
                if (File.Exists(puiPath))
                {
                    string xml = File.ReadAllText(puiPath);
                    PuiElement root = PolarisPuiGenerator.ParseRoot(xml, Path.GetFileNameWithoutExtension(puiPath));
                    result = root.StateTransitions.ToList();
                }
                else
                {
                    progress?.GeneratorError(1, 0, $"找不到状态节点「{node.Title}」绑定的 .pui 文件：{puiPath}", 0, 0);
                }
            }
            nodeTransitions[nodeIndex] = result;
            return result;
        }

        // 第一遍：给每个 PuiState 节点分配一个图内唯一的 key。今天允许同一个 .pui 被图里的
        // 多个节点绑定（各自连到不同目标），此前这些节点会在拍平后的 (source,trigger) 字典
        // 里静默互相覆盖；现在改为按 PuiName 出现次数决定要不要加序号区分：只出现一次的
        // PuiName 直接用作 key（生成的代码更干净），出现多次的 PuiName 上的每个节点都加上
        // "#{序号}"（序号从 1 开始，按节点声明顺序），彼此不再冲突。
        var puiNameCounts = new Dictionary<string, int>();
        foreach (PuislnNode n in doc.Nodes)
        {
            if (n.Type == "PuiState" && !string.IsNullOrEmpty(n.PuiName))
            {
                puiNameCounts.TryGetValue(n.PuiName, out int count);
                puiNameCounts[n.PuiName] = count + 1;
            }
        }

        var nameOrdinal = new Dictionary<string, int>();
        var nodeKeys = new Dictionary<int, string>();
        var nodeKeyList = new List<(string Key, string PuiName)>();

        for (int i = 0; i < doc.Nodes.Count; i++)
        {
            PuislnNode n = doc.Nodes[i];
            if (n.Type != "PuiState" || string.IsNullOrEmpty(n.PuiName))
                continue;

            nameOrdinal.TryGetValue(n.PuiName, out int ordinal);
            ordinal++;
            nameOrdinal[n.PuiName] = ordinal;

            string key = puiNameCounts[n.PuiName] > 1 ? $"{n.PuiName}#{ordinal}" : n.PuiName;
            nodeKeys[i] = key;
            nodeKeyList.Add((key, n.PuiName));
        }

        var resolved = new List<ResolvedTransition>();
        var edgeKeys = new HashSet<(string source, string trigger)>();
        string entryNodeKey = null;

        foreach (PuislnConnection conn in doc.Connections)
        {
            if (conn.SourceNode < 0 || conn.SourceNode >= doc.Nodes.Count) continue;
            if (conn.TargetNode < 0 || conn.TargetNode >= doc.Nodes.Count) continue;

            PuislnNode sourceNode = doc.Nodes[conn.SourceNode];
            PuislnNode targetNode = doc.Nodes[conn.TargetNode];

            bool isExitTarget = targetNode.Type == "Exit";
            string targetKey = null;
            if (!isExitTarget && (targetNode.Type != "PuiState" || !nodeKeys.TryGetValue(conn.TargetNode, out targetKey)))
                continue;

            if (sourceNode.Type == "Entry")
            {
                // 入口直接连到出口没有意义（起点即终点），忽略这条连线。
                if (!isExitTarget)
                    entryNodeKey ??= targetKey;
                continue;
            }

            if (sourceNode.Type != "PuiState" || !nodeKeys.TryGetValue(conn.SourceNode, out string sourceKey))
                continue;

            List<PuislnConnector> sourceOutputs = sourceNode.Outputs;
            if (conn.SourceIndex < 0 || conn.SourceIndex >= sourceOutputs.Count) continue;
            string transitionId = sourceOutputs[conn.SourceIndex].SourceTransitionId;

            PuiStateTransition transition = GetTransitions(conn.SourceNode)
                .FirstOrDefault(t => t.Id == transitionId);
            if (transition == null)
            {
                progress?.GeneratorError(1, 0,
                    $"状态节点「{sourceNode.Title}」的连接点已失效（对应的 .pui 状态连接点被删除或改动），已跳过这条连线，请刷新该节点后重新连线", 0, 0);
                continue;
            }

            string triggerKey = transition.ResolveTriggerKey();
            if (string.IsNullOrEmpty(triggerKey))
            {
                progress?.GeneratorError(1, 0,
                    $"状态节点「{sourceNode.Title}」的连接点「{transition.DisplayLabel}」未配置有效触发方式，已跳过这条连线", 0, 0);
                continue;
            }

            if (!edgeKeys.Add((sourceKey, triggerKey)))
            {
                progress?.GeneratorError(1, 0,
                    $"状态节点「{sourceNode.Title}」的连接点「{transition.DisplayLabel}」与另一条连线的触发键重复" +
                    "（同一个来源节点的同一个触发键只能指向一个目标），已跳过这条连线", 0, 0);
                continue;
            }

            resolved.Add(new ResolvedTransition
            {
                SourceNodeKey = sourceKey,
                TriggerKey = triggerKey,
                TargetNodeKey = targetKey,
                Blocking = transition.Blocking,
                IsExit = isExitTarget,
            });
        }

        var nodeKeyConsts = new StringBuilder();
        var nodeDecls = new StringBuilder();
        foreach ((string key, string puiName) in nodeKeyList)
        {
            string identifier = SanitizeIdentifier(key);
            nodeKeyConsts.Append("        public const string ").Append(identifier)
                .Append(" = \"").Append(Esc(key)).Append("\";\n");
            nodeDecls.Append("            .Node(NodeKeys.").Append(identifier)
                .Append(", \"").Append(Esc(puiName)).Append("\")\n");
        }

        var edgeDecls = new StringBuilder();
        foreach (ResolvedTransition r in resolved)
        {
            if (r.IsExit)
            {
                edgeDecls.Append("            .ExitEdge(NodeKeys.").Append(SanitizeIdentifier(r.SourceNodeKey))
                    .Append(", \"").Append(Esc(r.TriggerKey)).Append("\")\n");
            }
            else
            {
                edgeDecls.Append("            .Edge(NodeKeys.").Append(SanitizeIdentifier(r.SourceNodeKey))
                    .Append(", \"").Append(Esc(r.TriggerKey)).Append("\", NodeKeys.")
                    .Append(SanitizeIdentifier(r.TargetNodeKey)).Append(", blocking: ")
                    .Append(r.Blocking ? "true" : "false").Append(")\n");
            }
        }

        string entryLine = entryNodeKey != null ? "            .Entry(EntryNodeKey)\n" : "";

        return $$"""
            // <auto-generated />
            // Generated by polaris source code generator from {{Path.GetFileName(inputFilePath)}}
            // 图结构编译期固化为一份不可变蓝图（PUIGraphDefinition）；每次 CreateSolution() 得到
            // 一个互相完全独立的运行时实例（各自的当前节点、各自的 PUI 副本）。

            namespace {{ns}};

            [Polaris.PUI.PUISolutionAutoRegistration]
            public static class {{className}}
            {
                public const string GraphName = "{{Esc(graphName)}}";

                /// <summary>入口节点连线指向的节点 key；Start() 无参时进入这里。</summary>
                public const string EntryNodeKey = "{{Esc(entryNodeKey ?? "")}}";

                /// <summary>节点 key 常量：供业务代码 solution.TryGetNode(...) / Fire(...) 使用，避免裸字符串。</summary>
                public static class NodeKeys
                {
            {{nodeKeyConsts}}
                }

                private static Polaris.PUI.PUIGraphDefinition definition;

                /// <summary>本图的不可变蓝图；PUIManager.Init 会反射收集进图目录并自动创建一份默认共享实例。</summary>
                public static Polaris.PUI.PUIGraphDefinition Definition => definition ??= Build();

                /// <summary>新建一个与其它实例互不干扰的运行时状态机；用完请调用其 Dispose()。</summary>
                public static Polaris.PUI.PUISolution CreateSolution(string instanceName = null)
                    => Definition.CreateSolution(instanceName);

                private static Polaris.PUI.PUIGraphDefinition Build()
                {
                    return Polaris.PUI.PUIGraphDefinition.CreateBuilder(GraphName)
            {{nodeDecls}}{{entryLine}}{{edgeDecls}}            .Build();
                }
            }
            """;
    }

    private static string SanitizeIdentifier(string name) => CSharpLiteral.SanitizeIdentifier(name);

    private static string Esc(string value) => CSharpLiteral.Escape(value);
}
