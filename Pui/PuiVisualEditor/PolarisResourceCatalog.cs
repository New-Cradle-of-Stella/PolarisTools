using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PolarisTools.Pui.PuiVisualEditor
{
    /// <summary>
    /// 一个可以被 PUI <c>Image</c> 元素引用的 PolarisRes 图片资源：项目源码里某个打了
    /// <c>[PolarisResourceFolder]</c> 的 static 类里、打了 <c>[PolarisResource]</c> 的
    /// <c>MImage</c> static 字段。
    /// <para>
    /// <see cref="Reference"/> 是存进 <c>.pui</c>、也是生成代码里直接写出来的那个字段引用
    /// （生成时加 <c>global::</c> 前缀）——运行时的 <c>MImage</c> 由 PolarisRes 的
    /// <c>AutoBindScanner</c> 早已回填进这个字段，PUI 只是把它读出来塞给 <c>DsnDataImg.MI</c>，
    /// 不需要在 PUI 侧再挂载一遍目录、再解码一遍图片。
    /// </para>
    /// </summary>
    public sealed class PolarisImageResource
    {
        private bool _thumbnailLoaded;
        private ImageSource _thumbnail;
        private bool _fullImageLoaded;
        private BitmapSource _fullImage;

        /// <summary>C# 字段引用，形如 <c>MyMod.Res.testImage</c>（无命名空间时就是 <c>Res.testImage</c>）。</summary>
        public string Reference { get; }

        /// <summary>面板/下拉里显示的短名，形如 <c>Res.testImage</c>（去掉命名空间）。</summary>
        public string DisplayName { get; }

        /// <summary><c>[PolarisResourceFolder]</c> 里写的文件夹（相对 dll 目录；编辑器按项目目录找）。</summary>
        public string Folder { get; }

        /// <summary><c>[PolarisResource]</c> 里写的挂载相对路径（通常不带扩展名）。</summary>
        public string ResourcePath { get; }

        /// <summary>磁盘上探测到的图片文件绝对路径；探测不到为 null（引用本身依然可用，只是没有预览）。</summary>
        public string FilePath { get; }

        /// <summary>下拉里显示的副标题：<c>文件夹/路径</c>，探测不到文件时额外标注一句。</summary>
        public string Detail { get; }

        private readonly string _searchText;

        internal PolarisImageResource(string reference, string displayName, string folder, string resourcePath, string filePath)
        {
            Reference = reference;
            DisplayName = displayName;
            Folder = folder ?? "";
            ResourcePath = resourcePath ?? "";
            FilePath = filePath;

            // 显示用的挂载路径统一成 '/' 分隔：特性里两种写法都合法（"sub\inner" / "sub/inner"），
            // 下拉里混着显示只会让人以为是两个不同的东西。
            string displayPath = ResourcePath.Replace('\\', '/');
            string mountPath = string.IsNullOrEmpty(Folder)
                ? displayPath
                : Folder.Replace('\\', '/').TrimEnd('/') + "/" + displayPath;
            Detail = filePath == null ? mountPath + "  (file not found)" : mountPath;
            _searchText = (reference + " " + mountPath).ToLowerInvariant();
        }

        /// <summary>下拉列表里的小图（32px 解码）；文件不存在/解码失败为 null。</summary>
        public ImageSource Thumbnail
        {
            get
            {
                if (!_thumbnailLoaded)
                {
                    _thumbnailLoaded = true;
                    _thumbnail = Load(32);
                }
                return _thumbnail;
            }
        }

        /// <summary>
        /// 画布预览用的原尺寸图；文件不存在/解码失败为 null。类型是 <see cref="BitmapSource"/> 而不是
        /// <see cref="ImageSource"/>：预览要按 Uv 的像素矩形裁剪（<see cref="CroppedBitmap"/>），
        /// 那要求源是位图而不是任意可绘制对象。
        /// </summary>
        public BitmapSource FullImage
        {
            get
            {
                if (!_fullImageLoaded)
                {
                    _fullImageLoaded = true;
                    _fullImage = Load(0);
                }
                return _fullImage;
            }
        }

        /// <summary>
        /// 搜索匹配：空词一律算命中，多个词之间是"全部都要出现"（顺序无关），匹配范围是
        /// 字段引用 + 挂载路径，所以按类名、字段名、文件夹、文件名任一片段都能搜到。
        /// </summary>
        public bool Matches(string[] terms)
        {
            if (terms == null)
            {
                return true;
            }

            foreach (string term in terms)
            {
                if (_searchText.IndexOf(term, StringComparison.Ordinal) < 0)
                {
                    return false;
                }
            }
            return true;
        }

        // OnLoad + Freeze：解码后立刻释放文件句柄（否则会锁住用户项目里的 png，重新导出图片时
        // 报"文件被占用"），并且允许跨线程共享/长期缓存这一份位图。
        private BitmapSource Load(int decodePixelWidth)
        {
            if (string.IsNullOrEmpty(FilePath) || !File.Exists(FilePath))
            {
                return null;
            }

            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                bitmap.UriSource = new Uri(FilePath, UriKind.Absolute);
                if (decodePixelWidth > 0)
                {
                    bitmap.DecodePixelWidth = decodePixelWidth;
                }
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch
            {
                // 图片坏了/格式不认（比如占位的 0 字节文件）就当没有预览，引用本身照样能用。
                return null;
            }
        }
    }

    /// <summary>
    /// 编辑器专用的"项目里有哪些 PolarisRes 图片资源"清单：把某个 <c>.pui</c> 所属项目里全部
    /// <c>.cs</c> 扫一遍，找出打了 <c>[PolarisResourceFolder]</c> 的类里那些打了
    /// <c>[PolarisResource]</c> 的 <c>MImage</c> static 字段，供 Image 元素的资源下拉框选择、
    /// 供画布预览直接画出真实图片。
    /// <para>
    /// <b>用文本扫描而不是 Roslyn</b>：这里要的信息只有"哪个类打了文件夹特性、里面哪些
    /// MImage 字段打了资源特性"，正则 + 花括号配对足够；接 Roslyn 工作区要拉一整套
    /// Microsoft.CodeAnalysis 依赖进 VSIX，还得处理项目未加载/编译中间态。跟
    /// <see cref="PlangKeyCatalog"/> 扫 <c>.plang</c> 是同一个思路：编辑器侧的辅助查表，
    /// 查不到就退化成"没有预览"，绝不能因为解析失败打断编辑。
    /// </para>
    /// <para>
    /// 真正的取值永远在运行时：PolarisRes 的 <c>AutoBindScanner</c> 按类特性挂载目录、回填字段，
    /// 编辑器只是把字段名抄进生成代码。因此这里对文件夹的解析（项目目录下找同名子目录）只影响
    /// 预览图能不能显示，影响不到运行时行为。
    /// </para>
    /// </summary>
    internal sealed class PolarisResourceCatalog
    {
        // 按项目根缓存：同一个项目下开多个 .pui 编辑器共用一份扫描结果和一个 FileSystemWatcher。
        private static readonly Dictionary<string, PolarisResourceCatalog> Cache =
            new Dictionary<string, PolarisResourceCatalog>(StringComparer.OrdinalIgnoreCase);

        private static readonly PolarisResourceCatalog EmptyCatalog = new PolarisResourceCatalog(null);

        // 图片候选扩展名与探测顺序跟运行时 Polaris.Res.Mounts.ResourceKindExtensions 的
        // TextureExtensions 保持一致：那边先 .png 再 .jpg/.jpeg，这里也一样，免得同名的
        // png/jpg 同时存在时预览显示的跟运行时加载的不是同一个文件。
        private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg" };

        private readonly string _rootDir;
        private readonly object _gate = new object();
        private List<PolarisImageResource> _images;
        private Dictionary<string, PolarisImageResource> _byReference;
        private string[] _directoryCache;
        private FileSystemWatcher _watcher;

        /// <summary>扫描结果失效（有 <c>.cs</c> 被增删改）时触发，供下拉框/预览重取。</summary>
        public event EventHandler Changed;

        private PolarisResourceCatalog(string rootDir)
        {
            _rootDir = rootDir;
        }

        /// <summary>
        /// 取某个 <c>.pui</c> 对应的资源清单。路径为空、或定位不到任何目录时返回一份永远为空的
        /// 清单——下拉框会显示"没有找到资源"，不会报错。
        /// </summary>
        public static PolarisResourceCatalog ForPuiFile(string puiFilePath)
        {
            string root = PuiProjectLocator.ResolveProjectDir(puiFilePath);
            if (string.IsNullOrEmpty(root))
            {
                return EmptyCatalog;
            }

            lock (Cache)
            {
                if (!Cache.TryGetValue(root, out PolarisResourceCatalog catalog))
                {
                    catalog = new PolarisResourceCatalog(root);
                    Cache[root] = catalog;
                }
                return catalog;
            }
        }

        /// <summary>按 <see cref="PolarisImageResource.DisplayName"/> 排好序的全部图片资源。</summary>
        public IReadOnlyList<PolarisImageResource> Images => EnsureScanned().Images;

        /// <summary>
        /// 按字段引用（<c>.pui</c> 里存的那个字符串）查回条目。查不到返回 false——通常意味着
        /// 字段被改名/删掉了，调用方应该把这个引用当作"仍然写在文档里但已失效"提示出来，
        /// 而不是悄悄清掉用户填的值。
        /// </summary>
        public bool TryGet(string reference, out PolarisImageResource resource)
        {
            resource = null;
            if (string.IsNullOrEmpty(reference))
            {
                return false;
            }

            return EnsureScanned().ByReference.TryGetValue(reference, out resource);
        }

        private (IReadOnlyList<PolarisImageResource> Images, Dictionary<string, PolarisImageResource> ByReference) EnsureScanned()
        {
            lock (_gate)
            {
                if (_images == null)
                {
                    _images = _rootDir == null ? new List<PolarisImageResource>() : Scan();
                    _byReference = new Dictionary<string, PolarisImageResource>(StringComparer.Ordinal);
                    foreach (PolarisImageResource image in _images)
                    {
                        // 同一个引用理论上只会出现一次（同名字段不可能在同一个类里声明两遍）；
                        // 真撞上（比如同一个类被 #if 分支写了两份）保留先扫到的那份。
                        if (!_byReference.ContainsKey(image.Reference))
                        {
                            _byReference[image.Reference] = image;
                        }
                    }
                    EnsureWatcher();
                }
                return (_images, _byReference);
            }
        }

        private List<PolarisImageResource> Scan()
        {
            var result = new List<PolarisImageResource>();
            // 每次重扫都重新枚举一遍目录：上次扫描之后作者很可能刚把资源目录建出来/挪了位置。
            _directoryCache = null;

            string[] files;
            try
            {
                files = Directory.GetFiles(_rootDir, "*.cs", SearchOption.AllDirectories);
            }
            catch
            {
                return result;
            }

            foreach (string file in files)
            {
                if (PuiProjectLocator.IsBuildOutput(file))
                {
                    continue;
                }

                try
                {
                    ScanFile(File.ReadAllText(file), result);
                }
                catch
                {
                    // 单个文件读不了/解析炸了只跳过它，不影响其它文件。
                }
            }

            result.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase));
            return result;
        }

        // 类型/成员声明。record/struct 也一起认：作者要是把资源容器写成 static class 之外的形式，
        // 运行时 AutoBindScanner 照样能扫到（它只看特性，不看类型种类）。
        private static readonly Regex TypeDeclRegex =
            new Regex(@"\b(?:class|struct|record)\s+(?<name>[A-Za-z_]\w*)", RegexOptions.Compiled);

        private static readonly Regex NamespaceRegex =
            new Regex(@"\bnamespace\s+(?<name>[A-Za-z_][\w.]*)", RegexOptions.Compiled);

        private static readonly Regex FolderAttrRegex =
            new Regex(@"\[\s*(?:PolarisResourceFolder|PolarisResourceFolderAttribute)\s*\(\s*(?<lit>@?""(?:[^""\\]|\\.|"""")*"")",
                RegexOptions.Compiled);

        // [PolarisResource("path")] + 修饰符 + 类型 + 字段名。修饰符里允许夹别的特性
        // （比如 [Obsolete]），类型允许带命名空间限定/可空标记（XX.MImage、global::XX.MImage?）。
        private static readonly Regex ResourceFieldRegex = new Regex(
            @"\[\s*(?:PolarisResource|PolarisResourceAttribute)\s*\(\s*(?<lit>@?""(?:[^""\\]|\\.|"""")*"")\s*\)\s*\]" +
            @"(?<mods>(?:\s*\[[^\]]*\]|\s+(?:public|internal|protected|private|static|readonly|volatile|unsafe|new))*)" +
            @"\s+(?<type>[A-Za-z_][\w.:]*\??)\s+(?<name>[A-Za-z_]\w*)\s*(?<tail>[=;,])",
            RegexOptions.Compiled);

        /// <summary>
        /// 扫一个 <c>.cs</c>：先算出"哪些位置在注释/字符串里"的掩码（后面的花括号配对和正则命中
        /// 判定都靠它，免得代码里一句 <c>"{"</c> 就把类体范围算歪），再取全部类型声明的体范围、
        /// 打了文件夹特性的类，最后把每个资源字段归属到最内层的那个类。
        /// </summary>
        private void ScanFile(string text, List<PolarisImageResource> result)
        {
            bool[] masked = BuildMask(text);
            List<TypeDecl> types = FindTypeDecls(text, masked);
            if (types.Count == 0)
            {
                return;
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

                string mods = field.Groups["mods"].Value;
                // 必须是 static（AutoBindScanner 只回填 static 字段），而且必须能被同程序集里
                // 生成出来的 .pui.cs 引用到——private/protected（含不写修饰符的默认 private）
                // 字段虽然运行时也会被回填，但写进生成代码里会直接编译不过，不列进候选。
                if (!HasWord(mods, "static") || !(HasWord(mods, "public") || HasWord(mods, "internal"))
                    || HasWord(mods, "private") || HasWord(mods, "protected"))
                {
                    continue;
                }

                // 只要 MImage：DsnDataImg.MI 就是这个类型，Texture2D/AudioClip 之类的字段
                // 塞给它编译不过，列出来只会误导。
                if (!IsMImageType(field.Groups["type"].Value))
                {
                    continue;
                }

                if (!TryParseStringLiteral(field.Groups["lit"].Value, out string resourcePath))
                {
                    continue;
                }

                TypeDecl innermost = FindInnermost(types, field.Index);
                if (innermost == null || !innermost.HasFolderAttribute)
                {
                    // 类本身没打 [PolarisResourceFolder] 就不会被运行时自动绑定（AutoBindScanner
                    // 只记一条警告），这里跟着一起跳过，免得列出一个运行时永远是 null 的字段。
                    continue;
                }

                string chain = BuildTypeChain(types, field.Index);
                string ns = FindNamespace(text, masked, innermost.DeclIndex);
                string reference = string.IsNullOrEmpty(ns)
                    ? chain + "." + field.Groups["name"].Value
                    : ns + "." + chain + "." + field.Groups["name"].Value;

                result.Add(new PolarisImageResource(
                    reference,
                    chain + "." + field.Groups["name"].Value,
                    innermost.Folder,
                    resourcePath,
                    ResolveImageFile(innermost.Folder, resourcePath)));
            }
        }

        private sealed class TypeDecl
        {
            public string Name;
            public int DeclIndex;
            public int BodyStart;
            public int BodyEnd; // 闭花括号的下标
            public string Folder = "";
            public bool HasFolderAttribute;

            public bool Contains(int index) => index > BodyStart && index < BodyEnd;
        }

        private static List<TypeDecl> FindTypeDecls(string text, bool[] masked)
        {
            var types = new List<TypeDecl>();
            foreach (Match m in TypeDeclRegex.Matches(text))
            {
                if (masked[m.Index])
                {
                    continue;
                }

                // 声明头之后的第一个花括号才是类体开始；先遇到 ';' 说明是无体声明
                // （record Foo(...); / partial 声明的前向引用之类），没有可扫的字段。
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
                if (type.Contains(index) && (innermost == null || type.BodyStart > innermost.BodyStart))
                {
                    innermost = type;
                }
            }
            return innermost;
        }

        /// <summary>
        /// 嵌套类的完整访问链，外层在前（<c>Outer.Inner</c>）——生成代码里必须写全，
        /// 只写最内层的 <c>Inner.field</c> 编译不过。
        /// </summary>
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

        /// <summary>
        /// 类所在的命名空间：取声明位置之前最后一个 <c>namespace</c>（块作用域和文件作用域
        /// 两种写法都能覆盖）。同一个文件里写了多个平级块命名空间时这只是近似——那种写法
        /// 极少见，猜错的后果也只是引用少/多一层前缀，用户在下拉里一眼能看出来。
        /// </summary>
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

        private static bool IsMImageType(string type)
        {
            string t = type.TrimEnd('?');
            int lastDot = t.LastIndexOf('.');
            if (lastDot >= 0)
            {
                t = t.Substring(lastDot + 1);
            }
            return string.Equals(t, "MImage", StringComparison.Ordinal);
        }

        private static bool HasWord(string modifiers, string word)
            => Regex.IsMatch(modifiers, @"\b" + word + @"\b");

        /// <summary>
        /// C# 字符串字面量 → 实际值。普通字面量脱一层常见转义，逐字字面量（<c>@"..."</c>）
        /// 只把 <c>""</c> 还原成一个引号。认不出来（不是字面量，比如写的是 nameof/常量引用）
        /// 返回 false——那种写法编辑器无法静态求值，跳过比猜错好。
        /// </summary>
        private static bool TryParseStringLiteral(string literal, out string value)
        {
            value = null;
            if (string.IsNullOrEmpty(literal))
            {
                return false;
            }

            bool verbatim = literal[0] == '@';
            string body = verbatim ? literal.Substring(1) : literal;
            if (body.Length < 2 || body[0] != '"' || body[body.Length - 1] != '"')
            {
                return false;
            }
            body = body.Substring(1, body.Length - 2);

            var sb = new StringBuilder(body.Length);
            for (int i = 0; i < body.Length; i++)
            {
                char c = body[i];
                if (verbatim)
                {
                    if (c == '"' && i + 1 < body.Length && body[i + 1] == '"') i++;
                    sb.Append(c);
                    continue;
                }

                if (c != '\\' || i + 1 >= body.Length)
                {
                    sb.Append(c);
                    continue;
                }

                char next = body[++i];
                switch (next)
                {
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case '0': sb.Append('\0'); break;
                    // \\ \" \' 以及其它没列出来的转义：原样取被转义的那个字符。资源路径里
                    // 真正会出现的只有 \\（目录分隔），这样处理就够。
                    default: sb.Append(next); break;
                }
            }

            value = sb.ToString();
            return true;
        }

        /// <summary>
        /// 预览用的文件探测：先按"项目目录 + 特性里的文件夹"直接找（约定俗成的布局：资源目录就
        /// 放在项目里，生成时原样拷到 dll 同级），找不到再在项目里递归找一个末尾同名的目录试一次
        /// （资源目录被塞进更深一层的情况）。两次都找不到就返回 null：引用照样能用，只是画布上
        /// 画不出真实图片。
        /// </summary>
        private string ResolveImageFile(string folder, string resourcePath)
        {
            if (string.IsNullOrEmpty(resourcePath))
            {
                return null;
            }

            string relative = string.IsNullOrEmpty(folder)
                ? resourcePath
                : Path.Combine(folder.Replace('/', '\\'), resourcePath.Replace('/', '\\'));

            string direct = ProbeExtensions(SafeCombine(_rootDir, relative));
            if (direct != null)
            {
                return direct;
            }

            if (string.IsNullOrEmpty(folder))
            {
                return null;
            }

            string folderTail = folder.Replace('/', '\\').Trim('\\');
            foreach (string dir in AllDirectories())
            {
                if (PuiProjectLocator.IsBuildOutput(dir))
                {
                    continue;
                }
                if (!dir.EndsWith("\\" + folderTail, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string hit = ProbeExtensions(SafeCombine(dir, resourcePath.Replace('/', '\\')));
                if (hit != null)
                {
                    return hit;
                }
            }

            return null;
        }

        /// <summary>
        /// 兜底探测用的项目目录清单，一次扫描过程里只枚举一遍：好几个资源都探测不到时（比如资源
        /// 目录压根不在项目里），逐个去递归枚举整个项目会把一次扫描拖成好几秒。
        /// 调用点都在 <see cref="Scan"/> 里，已经被 <see cref="_gate"/> 串行化。
        /// </summary>
        private IReadOnlyList<string> AllDirectories()
        {
            if (_directoryCache == null)
            {
                try
                {
                    _directoryCache = Directory.GetDirectories(_rootDir, "*", SearchOption.AllDirectories);
                }
                catch
                {
                    _directoryCache = new string[0];
                }
            }
            return _directoryCache;
        }

        private static string SafeCombine(string root, string relative)
        {
            try
            {
                return Path.GetFullPath(Path.Combine(root, relative));
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 路径自带扩展名就直接看那个文件，否则按候选扩展名顺序探测——跟运行时
        /// <c>DirectoryMount</c> 的规则一致（<c>[PolarisResource("test")]</c> 对应 test.png）。
        /// </summary>
        private static string ProbeExtensions(string pathWithoutExtension)
        {
            if (string.IsNullOrEmpty(pathWithoutExtension))
            {
                return null;
            }

            try
            {
                if (Path.HasExtension(pathWithoutExtension))
                {
                    return File.Exists(pathWithoutExtension) ? pathWithoutExtension : null;
                }

                foreach (string ext in ImageExtensions)
                {
                    string candidate = pathWithoutExtension + ext;
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }
            catch
            {
                // 路径非法（超长、含非法字符）当作找不到。
            }

            return null;
        }

        /// <summary>
        /// 逐字符标出"这个位置属于注释或字符串字面量，不是代码"。花括号配对、正则命中判定都先问
        /// 它一句，所以代码注释里贴的一段示例代码、字符串里的 <c>"{"</c> 都不会污染扫描结果。
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
                            masked[i++] = true; // 闭引号
                            break;
                        }
                        masked[i++] = true;
                    }
                    continue;
                }

                if (c == '"' || c == '\'')
                {
                    char quote = c;
                    masked[i++] = true; // 开引号
                    while (i < text.Length)
                    {
                        if (text[i] == '\\' && i + 1 < text.Length)
                        {
                            masked[i++] = true;
                            masked[i++] = true;
                            continue;
                        }
                        if (text[i] == quote)
                        {
                            masked[i++] = true; // 闭引号
                            break;
                        }
                        if (text[i] == '\n')
                        {
                            // 未闭合（大概率是被 #if 切开的半句代码）：不吞掉换行，避免整个文件失真。
                            break;
                        }
                        masked[i++] = true;
                    }
                    continue;
                }

                i++;
            }
            return masked;
        }

        /// <summary>
        /// 挂一个只置脏、不做解析的监视器：回调跑在线程池线程上，这里只把缓存清掉再抛事件，
        /// 真正的重扫推迟到下一次取用。监视器跟着缓存的实例走，一个项目根只有一个，随 VS
        /// 进程存活，不需要显式释放（同 <see cref="PlangKeyCatalog"/>）。
        /// </summary>
        private void EnsureWatcher()
        {
            if (_watcher != null || _rootDir == null)
            {
                return;
            }

            try
            {
                _watcher = new FileSystemWatcher(_rootDir, "*.cs")
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                };
                _watcher.Changed += OnSourceFileChanged;
                _watcher.Created += OnSourceFileChanged;
                _watcher.Deleted += OnSourceFileChanged;
                _watcher.Renamed += OnSourceFileChanged;
                _watcher.EnableRaisingEvents = true;
            }
            catch
            {
                // 挂不上（路径太长、权限不足）就退化成"这次会话内不自动刷新"，重开编辑器仍会重扫。
                _watcher = null;
            }
        }

        private void OnSourceFileChanged(object sender, FileSystemEventArgs e)
        {
            if (PuiProjectLocator.IsBuildOutput(e.FullPath))
            {
                // obj\ 下的生成代码（.pui.g.cs、AssemblyInfo 之类）改一下就重扫一遍整个项目太浪费，
                // 而且它们本来就不参与扫描。
                return;
            }

            lock (_gate)
            {
                _images = null;
                _byReference = null;
            }
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
