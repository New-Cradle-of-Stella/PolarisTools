using System;
using System.Collections.Generic;
using System.IO;
using PolarisTools.Pui.PuiVisualEditor;

namespace PolarisTools.Res
{
    /// <summary>
    /// 一个项目下全部 PolarisRes 资源字段的索引，按项目根缓存并由 <see cref="FileSystemWatcher"/>
    /// 置脏。
    ///
    /// 项目定位复用 <see cref="PuiProjectLocator"/>：<c>.pui</c> 编辑器、<c>.plang</c> 查表和
    /// <c>.pactor</c> 生成器必须认同同一个项目根，否则同一个项目会出现"这边查得到、那边查不到"
    /// 的结果。监视器只置脏不解析，重扫推迟到下一次取用。
    /// </summary>
    internal sealed class PolarisResourceIndex
    {
        private static readonly Dictionary<string, PolarisResourceIndex> Cache =
            new Dictionary<string, PolarisResourceIndex>(StringComparer.OrdinalIgnoreCase);

        private static readonly PolarisResourceIndex EmptyIndex = new PolarisResourceIndex(null);

        private readonly string _rootDir;
        private readonly object _gate = new object();
        private Dictionary<string, ResourceFieldDeclaration> _byReference;
        private FileSystemWatcher _watcher;

        /// <summary>扫描结果失效（有 <c>.cs</c> 被增删改）时触发。</summary>
        public event EventHandler Changed;

        private PolarisResourceIndex(string rootDir) => _rootDir = rootDir;

        /// <summary>取某个源文件所属项目的资源索引。定位不到项目时返回一份永远为空的索引。</summary>
        public static PolarisResourceIndex ForFile(string filePath)
        {
            string root = PuiProjectLocator.ResolveProjectDir(filePath);
            if (string.IsNullOrEmpty(root))
            {
                return EmptyIndex;
            }

            lock (Cache)
            {
                if (!Cache.TryGetValue(root, out PolarisResourceIndex index))
                {
                    index = new PolarisResourceIndex(root);
                    Cache[root] = index;
                }
                return index;
            }
        }

        /// <summary>项目根目录；定位不到时为 null。</summary>
        public string RootDirectory => _rootDir;

        /// <summary>按字段引用查一条声明。找不到时返回 null，调用方按"暂时解析不到"处理。</summary>
        public ResourceFieldDeclaration Find(string reference)
        {
            if (string.IsNullOrEmpty(reference))
            {
                return null;
            }

            return Snapshot().TryGetValue(reference, out ResourceFieldDeclaration declaration) ? declaration : null;
        }

        public IReadOnlyCollection<ResourceFieldDeclaration> All => Snapshot().Values;

        private Dictionary<string, ResourceFieldDeclaration> Snapshot()
        {
            lock (_gate)
            {
                if (_byReference != null)
                {
                    return _byReference;
                }

                var map = new Dictionary<string, ResourceFieldDeclaration>(StringComparer.Ordinal);
                if (_rootDir != null)
                {
                    EnsureWatcher();

                    foreach (string file in EnumerateSources())
                    {
                        string text;
                        try
                        {
                            text = File.ReadAllText(file);
                        }
                        catch
                        {
                            continue; // 文件被占用或编码异常时跳过，不打断整个索引。
                        }

                        foreach (ResourceFieldDeclaration declaration in CSharpResourceScanner.Scan(text))
                            map[declaration.Reference] = declaration;
                    }
                }

                _byReference = map;
                return map;
            }
        }

        private IEnumerable<string> EnumerateSources()
        {
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(_rootDir, "*.cs", SearchOption.AllDirectories);
            }
            catch
            {
                yield break;
            }

            foreach (string file in files)
            {
                // bin/obj 里是生成产物，不是作者写的源文件。
                if (!PuiProjectLocator.IsBuildOutput(file))
                    yield return file;
            }
        }

        /// <summary>
        /// 监视器只置脏、不解析：回调跑在线程池线程上，这里清掉缓存再抛事件即可。
        /// 一个项目根只有一个监视器，随 VS 进程存活。
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
                // 挂不上就退化成"这次会话内不自动刷新"。
                _watcher = null;
            }
        }

        private void OnSourceFileChanged(object sender, FileSystemEventArgs e)
        {
            if (PuiProjectLocator.IsBuildOutput(e.FullPath))
            {
                return;
            }

            lock (_gate)
            {
                _byReference = null;
            }

            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
