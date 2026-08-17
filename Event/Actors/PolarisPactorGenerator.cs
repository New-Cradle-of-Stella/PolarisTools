using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Polaris.Pevt.Actors;
using Polaris.Pevt.Diagnostics;
using PolarisTools.Event.Pevt;
using PolarisTools.Pui;
using PolarisTools.Res;

namespace PolarisTools.Event.Actors;

/// <summary>
/// <c>.pactor</c> → C# 单文件生成器。
///
/// 生成的类只提交两样东西：已经校验过的不可变人物数据，以及强类型延迟资源访问器。
/// 它不含 XML 解析器、不加载文件、不执行任意方法名，也不生成 PEVT 源码——读取和校验全部调用
/// 共享的 <see cref="ActorCatalogReader"/>，因此工具侧与游戏侧对同一份 <c>.pactor</c> 必然得到
/// 相同的目录和相同的 PEVT91xx。
///
/// 资源字段的 <c>static</c>、可见性、特性与类型由共享的 <see cref="ActorResourceBinding"/> 判定，
/// 字段事实来自项目源码扫描（<see cref="PolarisResourceIndex"/>）——扫描期不读取字段值，
/// 生成的访问器也只是一个还没被调用的 lambda。
/// </summary>
[ComVisible(true)]
[Guid("8f3d6a21-4b95-4c07-ae12-5d9b0f7c3a68")]
[ClassInterface(ClassInterfaceType.None)]
public sealed class PolarisPactorGenerator : IVsSingleFileGenerator
{
    public const string GeneratorName = "PolarisPactorGenerator";

    public int DefaultExtension(out string pbstrDefaultExtension)
    {
        pbstrDefaultExtension = ".g.cs";
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

    internal static string ComputeClassName(string inputFilePath)
        => CSharpLiteral.SanitizeIdentifier(Path.GetFileNameWithoutExtension(inputFilePath));

    internal static string ResolveNamespace(string defaultNamespace)
        => string.IsNullOrWhiteSpace(defaultNamespace) ? "Polaris.Generated" : defaultNamespace;

    private static string GenerateCSharp(
        string inputFilePath,
        string inputFileContents,
        string defaultNamespace,
        IVsGeneratorProgress progress)
    {
        string className = ComputeClassName(inputFilePath);
        string ns = ResolveNamespace(defaultNamespace);
        string sourcePath = PevtProjectPaths.ToProjectRelative(inputFilePath);

        byte[] utf8 = PevtProjectPaths.ReadAllBytesOrEncode(inputFilePath, inputFileContents);

        // 工具侧永远以外部目录身份读取：只有 Polaris 自己嵌入的内置目录才能声明 BuiltIn/aic。
        ActorCatalogReadResult result = ActorCatalogReader.Read(utf8, sourcePath, ActorCatalogSourceKind.External);
        ReportDiagnostics(progress, result.Diagnostics);

        if (!result.Success)
            return EmptyResult(className, ns, inputFilePath, "`.pactor` 存在静态错误，未生成注册器。");

        ActorCatalog catalog = result.Catalog;

        // 同项目内重复最终人物 ID：PEVT9106，拒绝生成有效注册器。
        PevtProjectIdIndex idIndex = PevtProjectIdIndex.ForFile(inputFilePath);
        foreach (ActorDefinition actor in catalog.Actors)
        {
            string actorId = catalog.GetActorId(actor);
            string duplicate = idIndex.FindDuplicateActorSource(actorId, inputFilePath);
            if (duplicate == null)
                continue;

            progress?.GeneratorError(0, 0, $"PEVT9106: 最终人物 ID `{actorId}` 与同项目的 `{duplicate}` 重复。", 0, 0);
            return EmptyResult(className, ns, inputFilePath, $"最终人物 ID `{actorId}` 在同项目内重复。");
        }

        // 资源字段绑定：类型、static、可见性与特性。字段解析不到时只降级为 PEVT9118 警告。
        PolarisResourceIndex resources = PolarisResourceIndex.ForFile(inputFilePath);
        var bindings = new DiagnosticBag();
        var accessors = new List<VisualAccessor>();

        foreach (ActorDefinition actor in catalog.Actors)
        {
            string actorId = catalog.GetActorId(actor);
            CollectVisual(actor.WorldSprite, actorId, "world", resources, bindings, accessors);

            foreach (ActorVisual portrait in actor.Portraits)
                CollectVisual(portrait, actorId, "portrait:" + portrait.Id, resources, bindings, accessors);

            foreach (ActorVisual uiPortrait in actor.UiPortraits)
                CollectVisual(uiPortrait, actorId, "ui:" + uiPortrait.Id, resources, bindings, accessors);

            if (actor.Icon != null)
                CollectResource(actor.Icon, ActorVisualKind.Icon, actorId, "icon", resources, bindings, accessors);
        }

        ReportDiagnostics(progress, bindings.ToReadOnly());
        if (bindings.HasErrors)
            return EmptyResult(className, ns, inputFilePath, "资源字段不满足自动绑定条件，未生成注册器。");

        string catalogHash = Polaris.Pevt.Loading.PevtEmbeddedSource.ComputeContentHash(utf8);

        return $$"""
            // <auto-generated />
            // Generated by polaris source code generator from {{Path.GetFileName(inputFilePath)}}
            //
            // 只包含已校验的不可变人物数据与延迟资源访问器：没有 XML 解析器，不加载文件，
            // 不执行任意方法名，也不生成 PEVT 源码。访问器在首次演出前不会被调用。

            namespace {{ns}}
            {
                [global::Polaris.Pevt.Registration.PevtActorAutoRegistration]
                public sealed class {{className}}_PevtActorRegistrar : global::Polaris.Pevt.Registration.IPevtActorRegistrar
                {
                    public void Register(global::Polaris.Pevt.Registration.PevtActorRegistrationContext context)
                    {
                        context.Register(BuildCatalog(), "{{catalogHash}}", BuildAccessors());
                    }

                    private static global::Polaris.Pevt.Actors.ActorCatalog BuildCatalog() =>
                        new global::Polaris.Pevt.Actors.ActorCatalog(
                            "{{CSharpLiteral.Escape(catalog.Namespace)}}",
                            {{catalog.Version}},
                            false,
                            "{{CSharpLiteral.Escape(catalog.SourcePath)}}",
                            new global::Polaris.Pevt.Actors.ActorDefinition[]
                            {
            {{EmitActors(catalog)}}                });

            {{EmitAccessors(accessors)}}    }
            }
            """;
    }

    // ---- 资源字段 ----

    private sealed class VisualAccessor
    {
        public string Key;
        public string Reference;
        public string TypeName;
    }

    private static void CollectVisual(
        ActorVisual visual,
        string actorId,
        string key,
        PolarisResourceIndex resources,
        DiagnosticBag bindings,
        List<VisualAccessor> accessors)
    {
        if (visual == null)
            return;

        CollectResource(visual.Resource, visual.Kind, actorId, key, resources, bindings, accessors);
    }

    private static void CollectResource(
        ActorVisualResource resource,
        ActorVisualKind kind,
        string actorId,
        string key,
        PolarisResourceIndex resources,
        DiagnosticBag bindings,
        List<VisualAccessor> accessors)
    {
        if (resource.Provider != ActorVisualProvider.PolarisRes)
            return;

        ResourceFieldDeclaration declaration = resources.Find(resource.FieldReference);

        // 判定用共享实现，工具侧不自己另写一套规则。
        ActorResourceFieldInfo info = declaration == null
            ? null
            : new ActorResourceFieldInfo(
                declaration.TypeName,
                declaration.IsStatic,
                declaration.IsAccessible,
                declaration.HasResourceAttribute,
                declaration.DeclaringTypeHasFolderAttribute);

        ActorResourceBinding.Validate(resource, kind, info, null, bindings);

        if (declaration == null)
            return; // 解析不到字段时已经报了 PEVT9118；此时不生成访问器，避免引用一个不存在的名字。

        accessors.Add(new VisualAccessor
        {
            Key = actorId + "/" + key,
            Reference = resource.FieldReference,
            TypeName = ActorResourceBinding.GetTypeName(ActorResourceBinding.GetRequiredResourceType(kind)),
        });
    }

    private static string EmitAccessors(List<VisualAccessor> accessors)
    {
        var builder = new StringBuilder();
        builder.Append("        /// <summary>延迟资源访问器：lambda 在首次演出前不会被调用，扫描期不触发资源加载。</summary>\n");
        builder.Append("        private static global::System.Collections.Generic.IReadOnlyDictionary<string, global::System.Func<object>> BuildAccessors() =>\n");
        builder.Append("            new global::System.Collections.Generic.Dictionary<string, global::System.Func<object>>(global::System.StringComparer.Ordinal)\n");
        builder.Append("            {\n");

        foreach (VisualAccessor accessor in accessors)
        {
            // 直接引用真实字段：类型不对或字段不存在时，模组自己的编译就会失败。
            builder.Append("                [\"").Append(CSharpLiteral.Escape(accessor.Key)).Append("\"] = () => global::")
                .Append(accessor.Reference).Append(",\n");
        }

        builder.Append("            };\n");
        return builder.ToString();
    }

    // ---- 人物数据 ----

    private static string EmitActors(ActorCatalog catalog)
    {
        var builder = new StringBuilder();

        foreach (ActorDefinition actor in catalog.Actors)
        {
            builder.Append("                    new global::Polaris.Pevt.Actors.ActorDefinition(\n")
                .Append("                        localId: \"").Append(CSharpLiteral.Escape(actor.LocalId)).Append("\",\n")
                .Append("                        displayKey: ").Append(Literal(actor.DisplayKey)).Append(",\n")
                .Append("                        displayName: ").Append(Literal(actor.DisplayName)).Append(",\n")
                .Append("                        voice: ").Append(Literal(actor.Voice)).Append(",\n")
                .Append("                        color: ").Append(ColorLiteral(actor.Color)).Append(",\n")
                .Append("                        icon: ").Append(ResourceLiteral(actor.Icon)).Append(",\n")
                .Append("                        defaultPortraitId: ").Append(Literal(actor.DefaultPortraitId)).Append(",\n")
                .Append("                        legacyPerson: ").Append(Literal(actor.LegacyPerson)).Append(",\n")
                .Append("                        worldSprite: ").Append(VisualLiteral(actor.WorldSprite)).Append(",\n")
                .Append("                        portraits: ").Append(VisualArray(actor.Portraits)).Append(",\n")
                .Append("                        uiPortraits: ").Append(VisualArray(actor.UiPortraits)).Append(",\n")
                .Append("                        appearances: ").Append(AppearanceArray(actor.Appearances)).Append(",\n")
                .Append("                        anchors: ").Append(AnchorArray(actor.Anchors)).Append("),\n");
        }

        return builder.ToString();
    }

    private static string Literal(string value) =>
        value == null ? "null" : "\"" + CSharpLiteral.Escape(value) + "\"";

    private static string ColorLiteral(ActorColor? color) =>
        color == null
            ? "null"
            : $"new global::Polaris.Pevt.Actors.ActorColor({color.Value.R}, {color.Value.G}, {color.Value.B}, {color.Value.A})";

    private static string ResourceLiteral(ActorVisualResource resource)
    {
        if (resource == null)
            return "null";

        return resource.Provider == ActorVisualProvider.GamePxls
            ? $"global::Polaris.Pevt.Actors.ActorVisualResource.FromGameAsset(\"{CSharpLiteral.Escape(resource.Asset)}\")"
            : $"global::Polaris.Pevt.Actors.ActorVisualResource.FromPolarisResField(\"{CSharpLiteral.Escape(resource.FieldReference)}\")";
    }

    private static string VisualLiteral(ActorVisual visual)
    {
        if (visual == null)
            return "null";

        return "new global::Polaris.Pevt.Actors.ActorVisual(\"" + CSharpLiteral.Escape(visual.Id) + "\", "
            + "global::Polaris.Pevt.Actors.ActorVisualKind." + visual.Kind + ", "
            + ResourceLiteral(visual.Resource) + ", "
            + Literal(visual.LegacyPerson) + ", "
            + "global::Polaris.Pevt.Actors.ActorVisualLifetime." + visual.Lifetime + ")";
    }

    private static string VisualArray(IReadOnlyList<ActorVisual> visuals) =>
        visuals.Count == 0
            ? "null"
            : "new global::Polaris.Pevt.Actors.ActorVisual[] { " + string.Join(", ", visuals.Select(VisualLiteral)) + " }";

    private static string AppearanceArray(IReadOnlyList<ActorAppearance> appearances) =>
        appearances.Count == 0
            ? "null"
            : "new global::Polaris.Pevt.Actors.ActorAppearance[] { " + string.Join(", ", appearances.Select(a =>
                "new global::Polaris.Pevt.Actors.ActorAppearance(\"" + CSharpLiteral.Escape(a.Id) + "\", \""
                + CSharpLiteral.Escape(a.PortraitId) + "\", \"" + CSharpLiteral.Escape(a.Pose) + "\", \""
                + CSharpLiteral.Escape(a.Frame) + "\")")) + " }";

    private static string AnchorArray(IReadOnlyList<ActorAnchor> anchors) =>
        anchors.Count == 0
            ? "null"
            : "new global::Polaris.Pevt.Actors.ActorAnchor[] { " + string.Join(", ", anchors.Select(a =>
                "new global::Polaris.Pevt.Actors.ActorAnchor(\"" + CSharpLiteral.Escape(a.Id) + "\", "
                + FloatLiteral(a.X) + ", " + FloatLiteral(a.Y) + ", "
                + NullableFloatLiteral(a.EnterX) + ", " + NullableFloatLiteral(a.EnterY) + ")")) + " }";

    private static string FloatLiteral(float value) =>
        value.ToString("R", CultureInfo.InvariantCulture) + "f";

    private static string NullableFloatLiteral(float? value) =>
        value == null ? "null" : FloatLiteral(value.Value);

    // ---- 诊断 ----

    private static string EmptyResult(string className, string ns, string inputFilePath, string reason) =>
        $$"""
        // <auto-generated />
        // Generated by polaris source code generator from {{Path.GetFileName(inputFilePath)}}
        //
        // {{reason}}

        namespace {{ns}}
        {
            internal static class {{className}}_PevtActorNotGenerated
            {
                public const string Reason = "{{CSharpLiteral.Escape(reason)}}";
            }
        }
        """;

    private static void ReportDiagnostics(IVsGeneratorProgress progress, IReadOnlyList<Diagnostic> diagnostics)
    {
        if (progress == null)
            return;

        foreach (Diagnostic diagnostic in diagnostics)
        {
            uint line = diagnostic.Location != null ? (uint)Math.Max(0, diagnostic.Location.StartLine - 1) : 0;
            uint column = diagnostic.Location != null ? (uint)Math.Max(0, diagnostic.Location.StartColumn - 1) : 0;
            string message = $"{diagnostic.Id}: {diagnostic.Message}";

            if (diagnostic.Severity == DiagnosticSeverity.Error)
                progress.GeneratorError(0, 0, message, line, column);
            else
                progress.GeneratorError(1, 0, message, line, column);
        }
    }
}
