using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Polaris.Pevt.Actors;
using Polaris.Pevt.Commands;
using Polaris.Pevt.Loading;
using Polaris.Pevt.Text;
using PolarisTools.Pui.PuiVisualEditor;

namespace PolarisTools.Event.Pevt;

/// <summary>
/// 同一项目内 <c>.pevt</c> 事件 ID 与 <c>.pactor</c> 最终人物 ID 的索引。
///
/// 规范要求"同一程序集重复最终人物 ID 由 PolarisTools 报 PEVT9106 并拒绝生成有效注册器"，
/// 事件 ID 同理——工具侧能看到同项目全部文件时，重复必须是构建期错误，不能留到运行时才发现。
///
/// 索引按项目根缓存并由 <see cref="FileSystemWatcher"/> 置脏；监视 <c>.pevt</c> 与 <c>.pactor</c>
/// 两种扩展名，任何一个变化都整体重扫。
/// </summary>
internal sealed class PevtProjectIdIndex
{
    private static readonly Dictionary<string, PevtProjectIdIndex> Cache =
        new Dictionary<string, PevtProjectIdIndex>(StringComparer.OrdinalIgnoreCase);

    private static readonly PevtProjectIdIndex EmptyIndex = new PevtProjectIdIndex(null);

    private readonly string _rootDir;
    private readonly object _gate = new object();
    private Dictionary<string, List<string>> _eventIdToFiles;
    private Dictionary<string, List<string>> _actorIdToFiles;
    private FileSystemWatcher _watcher;

    private PevtProjectIdIndex(string rootDir) => _rootDir = rootDir;

    public static PevtProjectIdIndex ForFile(string filePath)
    {
        string root = PuiProjectLocator.ResolveProjectDir(filePath);
        if (string.IsNullOrEmpty(root))
        {
            return EmptyIndex;
        }

        lock (Cache)
        {
            if (!Cache.TryGetValue(root, out PevtProjectIdIndex index))
            {
                index = new PevtProjectIdIndex(root);
                Cache[root] = index;
            }
            return index;
        }
    }

    /// <summary>
    /// 同项目里是否还有别的文件声明了同一个事件 ID。返回那个文件的路径；没有重复时返回 null。
    /// </summary>
    public string FindDuplicateEventSource(string eventId, string currentFilePath) =>
        FindDuplicate(EnsureScanned().Events, eventId, currentFilePath);

    /// <summary>同项目里是否还有别的 <c>.pactor</c> 声明了同一个最终人物 ID。</summary>
    public string FindDuplicateActorSource(string actorId, string currentFilePath) =>
        FindDuplicate(EnsureScanned().Actors, actorId, currentFilePath);

    private static string FindDuplicate(Dictionary<string, List<string>> map, string id, string currentFilePath)
    {
        if (string.IsNullOrEmpty(id) || !map.TryGetValue(id, out List<string> files))
        {
            return null;
        }

        foreach (string file in files)
        {
            if (!string.Equals(file, currentFilePath, StringComparison.OrdinalIgnoreCase))
                return file;
        }

        return null;
    }

    private (Dictionary<string, List<string>> Events, Dictionary<string, List<string>> Actors) EnsureScanned()
    {
        lock (_gate)
        {
            if (_eventIdToFiles != null && _actorIdToFiles != null)
                return (_eventIdToFiles, _actorIdToFiles);

            var events = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            var actors = new Dictionary<string, List<string>>(ActorNaming.IdComparer);

            if (_rootDir != null)
            {
                EnsureWatcher();

                foreach (string file in Enumerate("*.pevt"))
                {
                    string id = TryReadEventId(file);
                    if (id != null)
                        Add(events, id, file);
                }

                foreach (string file in Enumerate("*.pactor"))
                {
                    foreach (string id in ReadActorIds(file))
                        Add(actors, id, file);
                }
            }

            _eventIdToFiles = events;
            _actorIdToFiles = actors;
            return (events, actors);
        }
    }

    private static void Add(Dictionary<string, List<string>> map, string id, string file)
    {
        if (!map.TryGetValue(id, out List<string> files))
            map[id] = files = new List<string>();

        files.Add(file);
    }

    private IEnumerable<string> Enumerate(string pattern)
    {
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(_rootDir, pattern, SearchOption.AllDirectories);
        }
        catch
        {
            yield break;
        }

        foreach (string file in files)
        {
            if (!PuiProjectLocator.IsBuildOutput(file))
                yield return file;
        }
    }

    /// <summary>
    /// 只为了取 <c>id</c>，仍然走共享前端而不是自己正则抓一行——否则工具侧对"什么算合法 id"的
    /// 判断会和运行时慢慢分叉。解析失败返回 null，交给该文件自己的生成器报诊断。
    /// </summary>
    private static string TryReadEventId(string file)
    {
        try
        {
            byte[] bytes = PevtProjectPaths.ReadAllBytesOrEncode(file, null);
            SourceTextLoadResult loaded = SourceText.FromUtf8(bytes, file);
            if (!loaded.Success)
                return null;

            PevtCompilation compilation = PevtSourceCompiler.Compile(
                loaded.Text, CommandDescriptorCatalog.Builtin.ToBuiltinApiTable());

            return compilation.Definition?.EventId;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>读一个 <c>.pactor</c> 的全部最终人物 ID；读不出来时返回空。</summary>
    private static IEnumerable<string> ReadActorIds(string file)
    {
        ActorCatalog catalog;
        try
        {
            byte[] bytes = PevtProjectPaths.ReadAllBytesOrEncode(file, null);
            ActorCatalogReadResult result = ActorCatalogReader.Read(bytes, file, ActorCatalogSourceKind.External);
            catalog = result.Catalog;
        }
        catch
        {
            yield break;
        }

        if (catalog == null)
            yield break;

        foreach (ActorDefinition actor in catalog.Actors)
            yield return catalog.GetActorId(actor);
    }

    private void EnsureWatcher()
    {
        if (_watcher != null || _rootDir == null)
        {
            return;
        }

        try
        {
            // FileSystemWatcher 只支持一个过滤器，这里放行全部文件再在回调里筛扩展名。
            _watcher = new FileSystemWatcher(_rootDir)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            };
            _watcher.Changed += OnFileChanged;
            _watcher.Created += OnFileChanged;
            _watcher.Deleted += OnFileChanged;
            _watcher.Renamed += OnFileChanged;
            _watcher.EnableRaisingEvents = true;
        }
        catch
        {
            _watcher = null;
        }
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        string extension = Path.GetExtension(e.FullPath);
        bool relevant = string.Equals(extension, ".pevt", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".pactor", StringComparison.OrdinalIgnoreCase);

        if (!relevant || PuiProjectLocator.IsBuildOutput(e.FullPath))
        {
            return;
        }

        lock (_gate)
        {
            _eventIdToFiles = null;
            _actorIdToFiles = null;
        }
    }
}
