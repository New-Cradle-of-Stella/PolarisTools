using System;
using System.Collections.Generic;
using System.IO;
using PolarisTools.Res;

namespace PolarisTools.Pui.PuiVisualEditor
{
    /// <summary>
    /// 编辑器专用的"项目里有哪些自定义控件后端类型"清单：把某个 <c>.pui</c> 所属项目里全部 <c>.cs</c>
    /// 扫一遍，找出实现了 <c>Polaris.PUI.IPuiCustomControl</c> 的具体类型，供 Custom 元素的
    /// BackendType 下拉框选择——避免用户手填类型全名时漏写命名空间、导致生成代码里
    /// <c>new global::Xxx()</c> 编译不过（不是自己写的名字，是编辑器直接抄源码里扫到的引用）。
    /// <para>
    /// 跟 <see cref="PolarisResourceCatalog"/> 是同一个思路，也复用同一个共享扫描器
    /// <see cref="CSharpResourceScanner"/>：文本扫描 + 花括号配对，不接 Roslyn 工作区。
    /// </para>
    /// </summary>
    internal sealed class PolarisCustomControlCatalog
    {
        private const string InterfaceSimpleName = "IPuiCustomControl";

        private static readonly Dictionary<string, PolarisCustomControlCatalog> Cache =
            new Dictionary<string, PolarisCustomControlCatalog>(StringComparer.OrdinalIgnoreCase);

        private static readonly PolarisCustomControlCatalog EmptyCatalog = new PolarisCustomControlCatalog(null);

        private readonly string _rootDir;
        private readonly object _gate = new object();
        private List<TypeImplementation> _types;
        private Dictionary<string, TypeImplementation> _byQualifiedName;
        private FileSystemWatcher _watcher;

        /// <summary>扫描结果失效（有 <c>.cs</c> 被增删改）时触发，供下拉框重取。</summary>
        public event EventHandler Changed;

        private PolarisCustomControlCatalog(string rootDir)
        {
            _rootDir = rootDir;
        }

        /// <summary>
        /// 取某个 <c>.pui</c> 对应的后端类型清单。路径为空、或定位不到任何目录时返回一份永远为空的
        /// 清单——下拉框会显示"没有找到类型"，不会报错。
        /// </summary>
        public static PolarisCustomControlCatalog ForPuiFile(string puiFilePath)
        {
            string root = PuiProjectLocator.ResolveProjectDir(puiFilePath);
            if (string.IsNullOrEmpty(root))
            {
                return EmptyCatalog;
            }

            lock (Cache)
            {
                if (!Cache.TryGetValue(root, out PolarisCustomControlCatalog catalog))
                {
                    catalog = new PolarisCustomControlCatalog(root);
                    Cache[root] = catalog;
                }
                return catalog;
            }
        }

        /// <summary>按 <see cref="TypeImplementation.DisplayName"/> 排好序的全部后端类型。</summary>
        public IReadOnlyList<TypeImplementation> Types => EnsureScanned().Types;

        /// <summary>按完整引用（<c>.pui</c> 里存的那个字符串）查回条目。查不到返回 false。</summary>
        public bool TryGet(string qualifiedName, out TypeImplementation type)
        {
            type = null;
            if (string.IsNullOrEmpty(qualifiedName))
            {
                return false;
            }

            return EnsureScanned().ByQualifiedName.TryGetValue(qualifiedName, out type);
        }

        private (IReadOnlyList<TypeImplementation> Types, Dictionary<string, TypeImplementation> ByQualifiedName) EnsureScanned()
        {
            lock (_gate)
            {
                if (_types == null)
                {
                    _types = _rootDir == null ? new List<TypeImplementation>() : Scan();
                    _byQualifiedName = new Dictionary<string, TypeImplementation>(StringComparer.Ordinal);
                    foreach (TypeImplementation type in _types)
                    {
                        if (!_byQualifiedName.ContainsKey(type.QualifiedName))
                        {
                            _byQualifiedName[type.QualifiedName] = type;
                        }
                    }
                    EnsureWatcher();
                }
                return (_types, _byQualifiedName);
            }
        }

        private List<TypeImplementation> Scan()
        {
            var result = new List<TypeImplementation>();

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
                    result.AddRange(CSharpResourceScanner.ScanTypesImplementing(File.ReadAllText(file), InterfaceSimpleName));
                }
                catch
                {
                    // 单个文件读不了/解析炸了只跳过它，不影响其它文件。
                }
            }

            result.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase));
            return result;
        }

        /// <summary>挂一个只置脏、不做解析的监视器，跟 <see cref="PolarisResourceCatalog"/> 是同一套约定。</summary>
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
                    NotifyFilter = System.IO.NotifyFilters.LastWrite | System.IO.NotifyFilters.FileName | System.IO.NotifyFilters.Size,
                };
                _watcher.Changed += OnSourceFileChanged;
                _watcher.Created += OnSourceFileChanged;
                _watcher.Deleted += OnSourceFileChanged;
                _watcher.Renamed += OnSourceFileChanged;
                _watcher.EnableRaisingEvents = true;
            }
            catch
            {
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
                _types = null;
                _byQualifiedName = null;
            }
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
