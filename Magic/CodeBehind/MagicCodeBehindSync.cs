using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Polaris.Magic.Authoring;

namespace PolarisTools.Magic.CodeBehind;

/// <summary>
/// <c>.pmagic.cs</c>（作者手写那一半 partial）的创建与补桩。
///
/// 这里刻意<b>不做</b>签名校验。作者的 <c>RunAsync</c> 签名不对时，生成的那一半 partial 引用不到它，
/// C# 编译器会直接在出问题的那一行报错——它比我们自己造一套错误码说得更准，也能一键跳转。
/// 我们唯一需要做的是"文件不存在就建、方法不存在就补一个空壳"，剩下的交给编译器。
///
/// 用 Roslyn 语法树而不是正则，是为了找准插入位置：嵌套类、同名局部函数、注释里的假签名都会让正则
/// 插到错的地方，而插错的后果是改坏作者的文件。
///
/// 两条硬规则：文件存在就绝不整体覆盖；补桩只在目标类闭合大括号所在行的行首之前插入，不调
/// <c>NormalizeWhitespace</c>、不重排 using、不格式化用户代码、不修改已有字符。
/// </summary>
internal static class MagicCodeBehindSync
{
    /// <summary>
    /// 确保 <c>.pmagic.cs</c> 存在；不存在时按骨架创建一次。
    /// 新文件用 UTF-8 无 BOM、CRLF。
    /// </summary>
    internal static void EnsureFile(string codeBehindPath, string namespaceName, string className)
    {
        if (!File.Exists(codeBehindPath))
        {
            File.WriteAllText(
                codeBehindPath,
                MagicCodeBehindContract.BuildSkeleton(namespaceName, className),
                new UTF8Encoding(false));
        }
    }

    /// <summary>
    /// 目标 partial 类里没有 <c>RunAsync</c> 时补一个空壳。
    ///
    /// 找不到目标类、或者已经有同名方法（不管签名对不对）时什么都不做：前者说明作者改了命名空间或
    /// 类名，那是编译器该说的话；后者说明作者已经在写自己的实现了，我们不能碰。
    /// </summary>
    internal static void EnsureRunMethod(string codeBehindPath, string namespaceName, string className)
    {
        string source;
        try
        {
            source = File.ReadAllText(codeBehindPath);
        }
        catch (Exception)
        {
            // 读不了就不动它。文件真的有问题时，编译器会在构建时说清楚。
            return;
        }

        SyntaxNode root = CSharpSyntaxTree.ParseText(source).GetRoot();

        List<ClassDeclarationSyntax> candidates = root
            .DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .Where(declaration =>
                string.Equals(declaration.Identifier.ValueText, className, StringComparison.Ordinal)
                && declaration.Modifiers.Any(SyntaxKind.PartialKeyword)
                && MatchesNamespace(declaration, namespaceName))
            .ToList();

        if (candidates.Count != 1)
        {
            return;
        }

        ClassDeclarationSyntax target = candidates[0];
        bool alreadyThere = target.Members
            .OfType<MethodDeclarationSyntax>()
            .Any(method => string.Equals(
                method.Identifier.ValueText,
                MagicCodeBehindContract.RunMethodName,
                StringComparison.Ordinal));

        if (!alreadyThere)
        {
            AppendRunMethod(codeBehindPath, source, target);
        }
    }

    private static bool MatchesNamespace(SyntaxNode node, string namespaceName)
    {
        for (SyntaxNode? current = node.Parent; current != null; current = current.Parent)
        {
            switch (current)
            {
                case NamespaceDeclarationSyntax block:
                    return string.Equals(block.Name.ToString(), namespaceName, StringComparison.Ordinal);
                case FileScopedNamespaceDeclarationSyntax scoped:
                    return string.Equals(scoped.Name.ToString(), namespaceName, StringComparison.Ordinal);
            }
        }

        // 全局命名空间：只有在生成侧也用全局命名空间时才算匹配。
        return string.IsNullOrEmpty(namespaceName);
    }

    /// <summary>
    /// 在目标类的闭合大括号前插入方法文本。用语法树的 Span 定位，但不动周围任何字符：
    /// 写回时沿用原文件的换行风格与 BOM。
    /// </summary>
    private static void AppendRunMethod(string codeBehindPath, string source, ClassDeclarationSyntax target)
    {
        // 插到"闭合大括号所在那一行"的行首之前：这样既不碰大括号那一行已有的缩进字符，
        // 也不会在上一行留下尾随空格。
        int braceColumn = target.CloseBraceToken.GetLocation().GetLineSpan().StartLinePosition.Character;
        int lineStart = Math.Max(0, target.CloseBraceToken.SpanStart - braceColumn);
        string newLine = source.Contains("\r\n") ? "\r\n" : "\n";

        string method = MagicCodeBehindContract.BuildRunMethod(new string(' ', braceColumn + 4));
        if (newLine != "\r\n")
        {
            method = method.Replace("\r\n", newLine);
        }

        // 类里已经有成员时空一行隔开；空类不加，免得出现一个孤零零的空行。
        if (target.Members.Count > 0)
        {
            method = newLine + method;
        }

        string updated = source.Substring(0, lineStart) + method + source.Substring(lineStart);

        // 原文件带 BOM 就继续带，不带就不加：这个文件在源码控制里，编码抖动会制造无意义的 diff。
        File.WriteAllText(codeBehindPath, updated, new UTF8Encoding(HasUtf8Bom(codeBehindPath)));
    }

    private static bool HasUtf8Bom(string path)
    {
        try
        {
            using FileStream stream = File.OpenRead(path);
            byte[] head = new byte[3];
            return stream.Read(head, 0, 3) == 3 && head[0] == 0xEF && head[1] == 0xBB && head[2] == 0xBF;
        }
        catch
        {
            return false;
        }
    }
}
