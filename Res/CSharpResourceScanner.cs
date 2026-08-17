using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace PolarisTools.Res
{
    /// <summary>
    /// 一个扫描到的 PolarisRes 资源字段声明。
    ///
    /// 这里只记录源码里能静态看到的事实，不读取字段的值——运行时的值由 PolarisRes 的
    /// <c>AutoBindScanner</c> 在游戏就绪后回填，编辑器和生成器都只负责把字段名抄进生成代码。
    /// </summary>
    internal sealed class ResourceFieldDeclaration
    {
        /// <summary>C# 字段引用，形如 <c>MyMod.Res.testImage</c>。</summary>
        public string Reference { get; }

        /// <summary>去掉命名空间的短名，形如 <c>Res.testImage</c>。</summary>
        public string DisplayName { get; }

        /// <summary><c>[PolarisResourceFolder]</c> 里写的文件夹。</summary>
        public string Folder { get; }

        /// <summary><c>[PolarisResource]</c> 里写的挂载相对路径。</summary>
        public string ResourcePath { get; }

        /// <summary>字段的声明类型名，可能带命名空间限定与可空标记。</summary>
        public string TypeName { get; }

        public bool IsStatic { get; }

        /// <summary>可见性是否允许同程序集里的生成代码直接引用。</summary>
        public bool IsAccessible { get; }

        /// <summary>字段是否带 <c>[PolarisResource]</c>。扫描入口只收带特性的字段，因此恒为 true。</summary>
        public bool HasResourceAttribute { get; }

        /// <summary>字段所属类型是否带 <c>[PolarisResourceFolder]</c>。</summary>
        public bool DeclaringTypeHasFolderAttribute { get; }

        public ResourceFieldDeclaration(
            string reference,
            string displayName,
            string folder,
            string resourcePath,
            string typeName,
            bool isStatic,
            bool isAccessible,
            bool hasResourceAttribute,
            bool declaringTypeHasFolderAttribute)
        {
            Reference = reference;
            DisplayName = displayName;
            Folder = folder ?? "";
            ResourcePath = resourcePath ?? "";
            TypeName = typeName ?? "";
            IsStatic = isStatic;
            IsAccessible = isAccessible;
            HasResourceAttribute = hasResourceAttribute;
            DeclaringTypeHasFolderAttribute = declaringTypeHasFolderAttribute;
        }

        /// <summary>类型名的最后一段，去掉命名空间限定与可空标记。</summary>
        public string SimpleTypeName
        {
            get
            {
                string t = TypeName.TrimEnd('?');
                int lastDot = t.LastIndexOf('.');
                return lastDot >= 0 ? t.Substring(lastDot + 1) : t;
            }
        }

        public bool IsTypeNamed(string simpleName) =>
            string.Equals(SimpleTypeName, simpleName, StringComparison.Ordinal);

        public override string ToString() => $"{Reference} : {SimpleTypeName}";
    }

    /// <summary>
    /// PolarisRes 资源字段的共用源码扫描器。
    ///
    /// <b>用文本扫描而不是 Roslyn</b>：这里要的信息只有"哪个类打了文件夹特性、里面哪些字段打了
    /// 资源特性、字段是什么类型"，正则 + 花括号配对足够；接 Roslyn 工作区要把一整套
    /// Microsoft.CodeAnalysis 拖进 VSIX，还得处理项目未加载/编译中间态。
    ///
    /// 这份实现原本埋在 PUI 的 <c>PolarisResourceCatalog</c> 里，只认 <c>MImage</c>。<c>.pactor</c>
    /// 需要同一套扫描但要认 <c>PxlsCharacterHandle</c>，所以把与资源类型无关的部分抽到这里：
    /// 两边共用同一份掩码、花括号配对和特性识别规则，不可能出现"PUI 认得、人物目录不认"的情况。
    /// </summary>
    internal static class CSharpResourceScanner
    {
        // 类型/成员声明。record/struct 也一起认：作者把资源容器写成 static class 之外的形式时，
        // 运行时 AutoBindScanner 照样能扫到（它只看特性，不看类型种类）。
        private static readonly Regex TypeDeclRegex =
            new Regex(@"\b(?:class|struct|record)\s+(?<name>[A-Za-z_]\w*)", RegexOptions.Compiled);

        private static readonly Regex NamespaceRegex =
            new Regex(@"\bnamespace\s+(?<name>[A-Za-z_][\w.]*)", RegexOptions.Compiled);

        private static readonly Regex FolderAttrRegex =
            new Regex(@"\[\s*(?:PolarisResourceFolder|PolarisResourceFolderAttribute)\s*\(\s*(?<lit>@?""(?:[^""\\]|\\.|"""")*"")",
                RegexOptions.Compiled);

        // [PolarisResource("path")] + 修饰符 + 类型 + 字段名。修饰符里允许夹别的特性，
        // 类型允许带命名空间限定与可空标记。
        private static readonly Regex ResourceFieldRegex = new Regex(
            @"\[\s*(?:PolarisResource|PolarisResourceAttribute)\s*\(\s*(?<lit>@?""(?:[^""\\]|\\.|"""")*"")\s*\)\s*\]" +
            @"(?<mods>(?:\s*\[[^\]]*\]|\s+(?:public|internal|protected|private|static|readonly|volatile|unsafe|new))*)" +
            @"\s+(?<type>[A-Za-z_][\w.:]*\??)\s+(?<name>[A-Za-z_]\w*)\s*(?<tail>[=;,])",
            RegexOptions.Compiled);

        private sealed class TypeDecl
        {
            public string Name;
            public int DeclIndex;
            public int BodyStart;
            public int BodyEnd;
            public string Folder = "";
            public bool HasFolderAttribute;

            public bool Contains(int index) => index > BodyStart && index < BodyEnd;
        }

        /// <summary>
        /// 扫一个 <c>.cs</c> 的全部资源字段声明。
        ///
        /// 返回的是"源码里怎么写的"，不做取舍：<c>static</c>、可见性和文件夹特性是否满足自动绑定
        /// 都如实记录，由调用方决定是过滤掉（PUI 下拉框）还是报诊断（<c>.pactor</c> 生成器）。
        /// </summary>
        public static IReadOnlyList<ResourceFieldDeclaration> Scan(string text)
        {
            var result = new List<ResourceFieldDeclaration>();
            if (string.IsNullOrEmpty(text))
            {
                return result;
            }

            bool[] masked = BuildMask(text);
            List<TypeDecl> types = FindTypeDecls(text, masked);
            if (types.Count == 0)
            {
                return result;
            }

            // 文件夹特性 → 它后面最近的那个类型声明。
            foreach (Match attr in FolderAttrRegex.Matches(text))
            {
                if (masked[attr.Index] || !TryParseStringLiteral(attr.Groups["lit"].Value, out string folder))
                {
                    continue;
                }

                TypeDecl owner = null;
                foreach (TypeDecl type in types)
                {
                    if (type.DeclIndex > attr.Index && (owner == null || type.DeclIndex < owner.DeclIndex))
                    {
                        owner = type;
                    }
                }
                if (owner != null)
                {
                    owner.Folder = folder;
                    owner.HasFolderAttribute = true;
                }
            }

            foreach (Match field in ResourceFieldRegex.Matches(text))
            {
                if (masked[field.Index])
                {
                    continue;
                }

                if (!TryParseStringLiteral(field.Groups["lit"].Value, out string resourcePath))
                {
                    continue;
                }

                TypeDecl innermost = FindInnermost(types, field.Index);
                if (innermost == null)
                {
                    continue;
                }

                string mods = field.Groups["mods"].Value;
                bool isStatic = HasWord(mods, "static");

                // private/protected（含不写修饰符的默认 private）字段运行时也会被回填，
                // 但写进生成代码会直接编译不过。
                bool isAccessible = (HasWord(mods, "public") || HasWord(mods, "internal"))
                    && !HasWord(mods, "private") && !HasWord(mods, "protected");

                string chain = BuildTypeChain(types, field.Index);
                string ns = FindNamespace(text, masked, innermost.DeclIndex);
                string shortName = chain + "." + field.Groups["name"].Value;

                result.Add(new ResourceFieldDeclaration(
                    reference: string.IsNullOrEmpty(ns) ? shortName : ns + "." + shortName,
                    displayName: shortName,
                    folder: innermost.Folder,
                    resourcePath: resourcePath,
                    typeName: field.Groups["type"].Value,
                    isStatic: isStatic,
                    isAccessible: isAccessible,
                    hasResourceAttribute: true,
                    declaringTypeHasFolderAttribute: innermost.HasFolderAttribute));
            }

            return result;
        }

        // ---- 以下都是从 PolarisResourceCatalog 原样搬过来的私有实现 ----

        private static List<TypeDecl> FindTypeDecls(string text, bool[] masked)
        {
            var types = new List<TypeDecl>();
            foreach (Match m in TypeDeclRegex.Matches(text))
            {
                if (masked[m.Index])
                {
                    continue;
                }

                // 声明头之后的第一个花括号才是类体开始；先遇到 ';' 说明是无体声明。
                int bodyStart = -1;
                for (int i = m.Index + m.Length; i < text.Length; i++)
                {
                    if (masked[i]) continue;
                    if (text[i] == '{') { bodyStart = i; break; }
                    if (text[i] == ';') break;
                }
                if (bodyStart < 0)
                {
                    continue;
                }

                int bodyEnd = FindMatchingBrace(text, masked, bodyStart);
                if (bodyEnd < 0)
                {
                    continue;
                }

                types.Add(new TypeDecl
                {
                    Name = m.Groups["name"].Value,
                    DeclIndex = m.Index,
                    BodyStart = bodyStart,
                    BodyEnd = bodyEnd,
                });
            }
            return types;
        }

        private static int FindMatchingBrace(string text, bool[] masked, int openIndex)
        {
            int depth = 0;
            for (int i = openIndex; i < text.Length; i++)
            {
                if (masked[i]) continue;
                if (text[i] == '{') depth++;
                else if (text[i] == '}')
                {
                    depth--;
                    if (depth == 0) return i;
                }
            }
            return -1;
        }

        private static TypeDecl FindInnermost(List<TypeDecl> types, int index)
        {
            TypeDecl innermost = null;
            foreach (TypeDecl type in types)
            {
                if (!type.Contains(index)) continue;
                if (innermost == null || type.BodyStart > innermost.BodyStart) innermost = type;
            }
            return innermost;
        }

        private static string BuildTypeChain(List<TypeDecl> types, int index)
        {
            var chain = new List<TypeDecl>();
            foreach (TypeDecl type in types)
            {
                if (type.Contains(index))
                {
                    chain.Add(type);
                }
            }
            chain.Sort((a, b) => a.BodyStart.CompareTo(b.BodyStart));

            var sb = new StringBuilder();
            foreach (TypeDecl type in chain)
            {
                if (sb.Length > 0) sb.Append('.');
                sb.Append(type.Name);
            }
            return sb.ToString();
        }

        private static string FindNamespace(string text, bool[] masked, int declIndex)
        {
            string ns = "";
            foreach (Match m in NamespaceRegex.Matches(text))
            {
                if (m.Index >= declIndex || masked[m.Index])
                {
                    continue;
                }
                ns = m.Groups["name"].Value;
            }
            return ns;
        }

        private static bool HasWord(string modifiers, string word)
            => Regex.IsMatch(modifiers, @"\b" + word + @"\b");

        /// <summary>
        /// 逐字符标出"这个位置属于注释或字符串字面量，不是代码"。花括号配对和正则命中判定都先问
        /// 它一句，所以注释里贴的示例代码、字符串里的 <c>"{"</c> 都不会污染扫描结果。
        /// </summary>
        private static bool[] BuildMask(string text)
        {
            var masked = new bool[text.Length];
            int i = 0;
            while (i < text.Length)
            {
                char c = text[i];

                if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
                {
                    while (i < text.Length && text[i] != '\n') masked[i++] = true;
                    continue;
                }

                if (c == '/' && i + 1 < text.Length && text[i + 1] == '*')
                {
                    while (i < text.Length && !(text[i] == '*' && i + 1 < text.Length && text[i + 1] == '/')) masked[i++] = true;
                    if (i < text.Length) masked[i++] = true; // '*'
                    if (i < text.Length) masked[i++] = true; // '/'
                    continue;
                }

                // 逐字字符串：只有 "" 是转义，反斜杠不是。
                if (c == '@' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    masked[i++] = true; // '@'
                    masked[i++] = true; // 开引号
                    while (i < text.Length)
                    {
                        if (text[i] == '"')
                        {
                            if (i + 1 < text.Length && text[i + 1] == '"')
                            {
                                masked[i++] = true;
                                masked[i++] = true;
                                continue;
                            }

                            masked[i++] = true;
                            break;
                        }

                        masked[i++] = true;
                    }
                    continue;
                }

                if (c == '"' || c == '\'')
                {
                    char quote = c;
                    masked[i++] = true;
                    while (i < text.Length)
                    {
                        if (text[i] == '\\' && i + 1 < text.Length)
                        {
                            masked[i++] = true;
                            masked[i++] = true;
                            continue;
                        }

                        bool closing = text[i] == quote;
                        masked[i++] = true;
                        if (closing || text[i - 1] == '\n') break;
                    }
                    continue;
                }

                i++;
            }

            return masked;
        }

        /// <summary>
        /// C# 字符串字面量 → 实际值。普通字面量脱一层常见转义，逐字字面量只把 <c>""</c> 还原成
        /// 一个引号。认不出来（写的是 nameof/常量引用）返回 false——那种写法无法静态求值，
        /// 跳过比猜错好。
        /// </summary>
        public static bool TryParseStringLiteral(string literal, out string value)
        {
            value = null;
            if (string.IsNullOrEmpty(literal))
            {
                return false;
            }

            if (literal[0] == '@')
            {
                if (literal.Length < 3 || literal[1] != '"' || literal[literal.Length - 1] != '"')
                {
                    return false;
                }

                value = literal.Substring(2, literal.Length - 3).Replace("\"\"", "\"");
                return true;
            }

            if (literal.Length < 2 || literal[0] != '"' || literal[literal.Length - 1] != '"')
            {
                return false;
            }

            var sb = new StringBuilder(literal.Length);
            for (int i = 1; i < literal.Length - 1; i++)
            {
                char c = literal[i];
                if (c != '\\' || i + 1 >= literal.Length - 1)
                {
                    sb.Append(c);
                    continue;
                }

                char next = literal[++i];
                switch (next)
                {
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case '0': sb.Append('\0'); break;
                    case '\\': sb.Append('\\'); break;
                    case '"': sb.Append('"'); break;
                    case '\'': sb.Append('\''); break;
                    default: sb.Append('\\').Append(next); break;
                }
            }

            value = sb.ToString();
            return true;
        }
    }
}
