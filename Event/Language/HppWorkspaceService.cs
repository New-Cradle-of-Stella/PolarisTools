using System;
using System.Collections.Concurrent;
using System.IO;

namespace PolarisTools.Event.Language
{
    /// <summary>
    /// 实现计划 §6.3 里"alias 文件保存/磁盘变化"和"项目项增加、删除、重命名"两条重新诊断触发条件的
    /// 最小实现：按目录订阅一个 <see cref="FileSystemWatcher"/>（不递归），任何 <c>*.yaml</c>/<c>*.phxx</c>
    /// 变化都广播给这个目录的订阅者。不做完整的项目系统集成（VS 项目加载/卸载、多目标框架等）——
    /// 磁盘层面的变化已经覆盖了"改了 alias 保存"和"加了/删了/改名了同目录下的 .phxx"这两个真实场景。
    /// </summary>
    internal static class HppWorkspaceService
    {
        static readonly ConcurrentDictionary<string, FileSystemWatcher> Watchers =
            new ConcurrentDictionary<string, FileSystemWatcher>(StringComparer.OrdinalIgnoreCase);

        public static event EventHandler<string> DirectoryChanged;

        public static void EnsureWatching(string directory)
        {
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            {
                return;
            }

            Watchers.GetOrAdd(directory, CreateWatcher);
        }

        static FileSystemWatcher CreateWatcher(string directory)
        {
            var watcher = new FileSystemWatcher(directory)
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
            };

            FileSystemEventHandler onChanged = (s, e) => NotifyIfRelevant(directory, e.Name);
            RenamedEventHandler onRenamed = (s, e) =>
            {
                NotifyIfRelevant(directory, e.Name);
                NotifyIfRelevant(directory, e.OldName);
            };

            watcher.Changed += onChanged;
            watcher.Created += onChanged;
            watcher.Deleted += onChanged;
            watcher.Renamed += onRenamed;
            watcher.EnableRaisingEvents = true;
            return watcher;
        }

        static void NotifyIfRelevant(string directory, string fileName)
        {
            if (fileName == null)
            {
                return;
            }

            if (fileName.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase)
                || fileName.EndsWith(".phxx", StringComparison.OrdinalIgnoreCase))
            {
                DirectoryChanged?.Invoke(null, directory);
            }
        }
    }
}
