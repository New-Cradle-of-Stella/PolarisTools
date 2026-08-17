using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PolarisTools.Res;

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

        /// <summary>
        /// 扫一个 <c>.cs</c>，挑出能被 PUI <c>Image</c> 引用的资源字段。
        ///
        /// 掩码、花括号配对和特性识别全部走共享的 <see cref="CSharpResourceScanner"/>——
        /// <c>.pactor</c> 生成器用的是同一份实现，两边不可能对"什么算一个资源字段"产生分歧。
        /// 这里只保留 PUI 自己的取舍：必须是 <c>MImage</c>、必须能被同程序集的生成代码引用、
        /// 所属类必须打了文件夹特性（否则运行时 AutoBindScanner 根本不会回填，列出来只会误导）。
        /// </summary>
        private void ScanFile(string text, List<PolarisImageResource> result)
        {
            foreach (ResourceFieldDeclaration declaration in CSharpResourceScanner.Scan(text))
            {
                if (!declaration.IsTypeNamed("MImage"))
                {
                    continue;
                }

                if (!declaration.IsStatic || !declaration.IsAccessible || !declaration.DeclaringTypeHasFolderAttribute)
                {
                    continue;
                }

                result.Add(new PolarisImageResource(
                    declaration.Reference,
                    declaration.DisplayName,
                    declaration.Folder,
                    declaration.ResourcePath,
                    ResolveImageFile(declaration.Folder, declaration.ResourcePath)));
            }
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
