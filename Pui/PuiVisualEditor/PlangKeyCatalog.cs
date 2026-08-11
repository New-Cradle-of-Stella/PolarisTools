using Polaris.Lang;
using System;
using System.Collections.Generic;
using System.IO;

namespace PolarisTools.Pui.PuiVisualEditor
{
    /// <summary>
    /// 编辑器预览专用的"键 → 文案"查表：把某个 <c>.pui</c> 所属项目里的全部
    /// <c>.plang</c> 扫进一个字典，让画布上的 <c>&amp;mymod.hello</c> 直接显示成"你好"。
    /// <para>
    /// <b>只服务于预览</b>。真正的取值永远走 <c>XX.TX.Get</c>（编译期由
    /// <see cref="CSharpTextEmitter"/> 展开、热重载期由 <c>PuiText</c> 调用），
    /// 那条链路会问 resolver 链、会落回原版查表、会跟着游戏当前语言走——这些编辑器
    /// 都模拟不了，也不该模拟。这里只是让作者在设计时不用对着一串键名猜排版。
    /// </para>
    /// <para>
    /// 解析用的是源码链接过来的 <see cref="PlangDocument"/>（见 PolarisTools.csproj），
    /// 和运行时 <c>LangLoader</c> 是同一份读写实现，不重复写一遍 XML 解析。
    /// </para>
    /// </summary>
    internal sealed class PlangKeyCatalog
    {
        // 按项目根缓存：同一个项目下开多个 .pui 编辑器共用一份扫描结果和一个
        // FileSystemWatcher，不会开一个编辑器就多扫一遍磁盘、多挂一个监视器。
        private static readonly Dictionary<string, PlangKeyCatalog> Cache =
            new Dictionary<string, PlangKeyCatalog>(StringComparer.OrdinalIgnoreCase);

        private static readonly PlangKeyCatalog Empty = new PlangKeyCatalog(null);

        private readonly string _rootDir;
        private readonly object _gate = new object();
        private Dictionary<string, string> _entries;
        private FileSystemWatcher _watcher;

        /// <summary>扫描结果变化（有 .plang 被增删改）时触发，供预览重绘。</summary>
        public event EventHandler Changed;

        private PlangKeyCatalog(string rootDir)
        {
            _rootDir = rootDir;
        }

        /// <summary>
        /// 取某个 <c>.pui</c> 对应的查表。路径为空、或定位不到任何目录时返回一个
        /// 永远查不到的空表——预览会因此退回显示 <c>&amp;键</c>，不会报错。
        /// </summary>
        public static PlangKeyCatalog ForPuiFile(string puiFilePath)
        {
            string root = PuiProjectLocator.ResolveProjectDir(puiFilePath);
            if (string.IsNullOrEmpty(root))
            {
                return Empty;
            }

            lock (Cache)
            {
                if (!Cache.TryGetValue(root, out PlangKeyCatalog catalog))
                {
                    catalog = new PlangKeyCatalog(root);
                    Cache[root] = catalog;
                }
                return catalog;
            }
        }

        /// <summary>查表；查不到（含扫描失败、根目录不存在）一律返回 false。</summary>
        public bool TryGet(string key, out string text)
        {
            text = null;
            if (string.IsNullOrEmpty(key) || _rootDir == null)
            {
                return false;
            }

            Dictionary<string, string> entries;
            lock (_gate)
            {
                if (_entries == null)
                {
                    _entries = Scan();
                    EnsureWatcher();
                }
                entries = _entries;
            }

            return entries.TryGetValue(key, out text);
        }

        /// <summary>
        /// 递归扫描根目录下的 <c>.plang</c>（跟运行时 <c>LangLoader.LoadAll</c> 一样是
        /// 递归的），跳过 bin/obj。一个 key 只应该出现在一个文件里；撞了 key 保留先扫到的。
        /// </summary>
        private Dictionary<string, string> Scan()
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);

            string[] files;
            try
            {
                files = Directory.GetFiles(_rootDir, "*.plang", SearchOption.AllDirectories);
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

                Merge(result, file);
            }

            return result;
        }

        private static void Merge(Dictionary<string, string> target, string file)
        {
            try
            {
                foreach (PlangEntry entry in PlangDocument.Load(file).Entries)
                {
                    if (!string.IsNullOrEmpty(entry.Key) && !target.ContainsKey(entry.Key))
                    {
                        // 设计时预览用中性值：画布不模拟真实的当前语言解析，只是给作者看排版，
                        // 跟 PuiLocalization.md 里"预览显示的是 .plang 文件里写的原文"一致。
                        target[entry.Key] = entry.NeutralValue ?? "";
                    }
                }
            }
            catch
            {
                // 单个文件坏了（XML 不合法、被独占占用）只跳过它，不影响其它文件。
                // 预览查不到就显示 &键，作者一眼能看出来，不需要在这里弹框。
            }
        }

        /// <summary>
        /// 挂一个只置脏、不做解析的监视器：回调跑在线程池线程上，这里只把缓存清掉再
        /// 抛事件，真正的重扫推迟到下一次 <see cref="TryGet"/>（即下一次重绘）。
        /// 监视器跟着缓存的实例走，一个项目根只有一个，随 VS 进程存活，不需要显式释放。
        /// </summary>
        private void EnsureWatcher()
        {
            if (_watcher != null)
            {
                return;
            }

            try
            {
                _watcher = new FileSystemWatcher(_rootDir, "*.plang")
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                };
                _watcher.Changed += OnPlangFileChanged;
                _watcher.Created += OnPlangFileChanged;
                _watcher.Deleted += OnPlangFileChanged;
                _watcher.Renamed += OnPlangFileChanged;
                _watcher.EnableRaisingEvents = true;
            }
            catch
            {
                // 挂不上监视器（路径太长、权限不足）就退化成"这次会话内不自动刷新"，
                // 重新打开 .pui 编辑器仍会重扫。不值得为此打断编辑。
                _watcher = null;
            }
        }

        private void OnPlangFileChanged(object sender, FileSystemEventArgs e)
        {
            lock (_gate)
            {
                _entries = null;
            }
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
