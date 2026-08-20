using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Polaris.Magic.Authoring;

namespace PolarisTools.Magic.Generation;

/// <summary>
/// <c>.pmagic</c> → <c>.pmagic.g.cs</c> 单文件生成器。
///
/// 三文件一组：<c>ExampleMagic.pmagic</c>（静态参数，编辑器写）、
/// <c>ExampleMagic.pmagic.g.cs</c>（本生成器写）、<c>ExampleMagic.pmagic.cs</c>（作者写 RunAsync）。
///
/// 失败方式与仓库里其它几个生成器一致：抛异常 → 把那句话交给
/// <see cref="IVsGeneratorProgress.GeneratorError"/> → 返回 <c>E_FAIL</c>。没有错误码体系，
/// 因为 <c>.pmagic</c> 只有十来个字段；真正需要逐行诊断的是作者写的 C#，那是编译器的活。
///
/// 生成全部在内存完成，只有 <c>S_OK</c> 时才把缓冲区交出去——半份生成文件带来的一串 CS 错误
/// 远比一句"读不了这个文件"难查。失败时 Visual Studio 保留上一次成功的 <c>.g.cs</c>。
/// </summary>
[ComVisible(true)]
[Guid("f68048c2-8be0-4a86-90e3-ff6cd81ed8be")]
[ClassInterface(ClassInterfaceType.None)]
public sealed class PolarisMagicGenerator : IVsSingleFileGenerator
{
    public const string GeneratorName = "PolarisMagicGenerator";

    /// <summary>
    /// Visual Studio 的输出命名规则是"去掉源文件自己的扩展名再拼上 DefaultExtension"，
    /// 所以这里必须返回 <c>.pmagic.g.cs</c>，<c>ExampleMagic.pmagic</c> 才会输出
    /// <c>ExampleMagic.pmagic.g.cs</c>（而不是 <c>ExampleMagic.g.cs</c>）。
    /// </summary>
    public int DefaultExtension(out string pbstrDefaultExtension)
    {
        pbstrDefaultExtension = ".pmagic.g.cs";
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
            string generated = Build(wszInputFilePath, bstrInputFileContents, wszDefaultNamespace);

            byte[] bytes = Encoding.UTF8.GetBytes(generated);
            IntPtr buffer = Marshal.AllocCoTaskMem(bytes.Length);
            Marshal.Copy(bytes, 0, buffer, bytes.Length);

            rgbOutputFileContents[0] = buffer;
            pcbOutput = (uint)bytes.Length;
            return VSConstants.S_OK;
        }
        catch (Exception ex)
        {
            pGenerateProgress?.GeneratorError(0, 0, ex.Message, 0, 0);
            return VSConstants.E_FAIL;
        }
    }

    /// <summary>生成流程。读不下去或名字用不了时抛异常，由调用方转成 Error List 里的一行。</summary>
    internal static string Build(string inputFilePath, string inputFileContents, string defaultNamespace)
    {
        string className = MagicFileGroup.ClassNameOf(inputFilePath);
        if (!MagicIdentifier.IsValidName(className))
        {
            throw new MagicFormatException(
                "'" + className + "' cannot be used as a C# class name; rename the .pmagic file.");
        }

        string namespaceName = ResolveNamespace(defaultNamespace);
        if (!MagicIdentifier.IsValidNamespace(namespaceName))
        {
            throw new MagicFormatException("'" + namespaceName + "' is not a valid C# namespace.");
        }

        MagicDefinitionDocument document = MagicDefinitionDocument.Parse(inputFileContents);

        // Id 是唯一一个不能靠默认值兜过去的字段：它要进注册表，还决定分配给玩家存档的数字 Id。
        // 其余字段填错只是数值不对，Id 填错的魔法根本注册不上，所以这里拦住而不是生成一份废定义。
        if (!MagicIdentifier.IsValidMagicId(document.Id))
        {
            throw new MagicFormatException(
                "'" + document.Id + "' is not a usable magic id; use at least two dot-separated segments, " +
                "for example 'mymod.fireball'.");
        }

        string codeBehindPath = MagicFileGroup.CodeBehindPathOf(inputFilePath);
        if (!File.Exists(codeBehindPath))
        {
            // 正常路径上 MagicFileCoordinator 会先把骨架建好；走到这里说明作者把它删了。
            // 缺了它，生成的那一半 partial 引用不到 RunAsync。
            throw new MagicFormatException(
                "Missing " + Path.GetFileName(codeBehindPath) + "; it must declare 'partial class " +
                className + "' with " + MagicCodeBehindContract.SignatureText + ".");
        }

        return MagicCSharpEmitter.Emit(document, className, namespaceName);
    }

    internal static string ResolveNamespace(string defaultNamespace) =>
        string.IsNullOrWhiteSpace(defaultNamespace)
            ? MagicCodeBehindContract.FallbackNamespace
            : defaultNamespace;
}
