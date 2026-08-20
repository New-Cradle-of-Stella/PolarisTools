using EnvDTE;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Polaris.Pevt.Live;
using PolarisTools.Pui.PuiVisualEditor;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PolarisTools.Event.Pevt.Live;

/// <summary>
/// 保存 <c>.pevt</c> 时把整份项目快照推给正在运行的游戏。
///
/// 推的是"项目里现在有哪些 <c>.pevt</c>"而不是只推刚保存的那一个：游戏侧的外部导入是整批替换
/// 语义（作者删掉一个文件，它就该从 <c>/event</c> 里消失），只推一个文件做不到这件事。
///
/// 游戏没开着是常态，因此连接超时很短、连不上完全静默——不能让每次保存都弹一次"没连上游戏"。
/// </summary>
internal static class PevtLivePush
{
    /// <summary>自动推送的连接超时。短到作者感觉不到，游戏没开时不拖慢保存。</summary>
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromMilliseconds(400);

    /// <summary>
    /// 手动推送（编辑器工具栏上那个按钮）的连接超时。这一次作者是在等结果的，宁可多等一会儿
    /// 也不要把"游戏刚好在卡一帧"误报成"没连上游戏"。
    /// </summary>
    private static readonly TimeSpan ManualConnectTimeout = TimeSpan.FromSeconds(3);

    /// <summary>等游戏回执的超时。那一侧要做完整静态校验、登记，还可能重启当前事件。</summary>
    private static readonly TimeSpan ApplyTimeout = TimeSpan.FromSeconds(12);

    /// <summary>
    /// <c>.pevt</c> 单文件生成器跑完之后的钩子，由 <c>PolarisToolsPackage</c> 的生成绑定表调用。
    /// 排在生成之后而不是保存事件里，是为了让"编译进程序集的那一份"和"推给游戏的那一份"
    /// 出自同一次保存，不会一前一后差一个版本。
    /// </summary>
    public static void AfterGenerate(ProjectItem projectItem)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        string? filePath = TryGetFilePath(projectItem);
        if (string.IsNullOrEmpty(filePath))
            return;

        // 项目根必须在 UI 线程上解析：PuiProjectLocator 走 DTE，脱离 UI 线程只会退回文件自己的目录。
        string? root = PuiProjectLocator.ResolveProjectDir(filePath);
        if (string.IsNullOrEmpty(root))
            return;

        // 读盘与推送都不碰 VS 对象模型，用 Task.Run 丢到后台去，保存的那一下不等它。
        _ = ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
        {
            (bool connected, bool ok, string message) =
                await Task.Run(() => PushAsync(root!, filePath!)).ConfigureAwait(false);
            if (!connected)
                return;

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            ReportToStatusBar(ok
                ? "PEVT hot reload: " + FirstLine(message)
                : "PEVT hot reload failed: " + FirstLine(message));
            Debug.WriteLine("[PolarisTools] PEVT hot reload: " + message);
        });
    }

    /// <summary>扫一遍项目里的 <c>.pevt</c> 并推过去。</summary>
    public static async Task<(bool connected, bool ok, string message)> PushAsync(string root, string triggerPath)
        => await PushAsync(root, triggerPath, null, ConnectTimeout).ConfigureAwait(false);

    /// <summary>
    /// 编辑器工具栏上的手动推送：推的仍然是整份项目快照，但触发文件那一份取自**编辑缓冲区**而不是磁盘。
    ///
    /// 这样作者按下按钮就能在游戏里看到当前正在改的这一版，不必先 Ctrl+S；而其余文件仍然是磁盘上
    /// 那一份，符合"外部导入是整批替换"的语义。
    /// </summary>
    /// <param name="triggerText">触发文件的当前编辑器文本；null 表示照旧读磁盘。</param>
    public static async Task<(bool connected, bool ok, string message)> PushEditorBufferAsync(
        string root, string triggerPath, string triggerText)
        => await PushAsync(root, triggerPath, triggerText, ManualConnectTimeout).ConfigureAwait(false);

    private static async Task<(bool connected, bool ok, string message)> PushAsync(
        string root, string triggerPath, string? triggerText, TimeSpan connectTimeout)
    {
        IReadOnlyList<PevtLiveWireFile> files;
        IReadOnlyList<string> skipped;
        try
        {
            files = BuildSnapshot(root, out skipped);
            if (triggerText != null)
                files = WithEditorBuffer(files, ToRelative(root, triggerPath), triggerText);
        }
        catch (Exception ex)
        {
            return (true, false, "收集项目里的 .pevt 时出错：" + ex.Message);
        }

        (bool connected, bool ok, string message) = await PevtLiveClient
            .SendAsync(ToRelative(root, triggerPath), files, connectTimeout, ApplyTimeout)
            .ConfigureAwait(false);

        if (!connected || skipped.Count == 0)
            return (connected, ok, message);

        return (true, false, message + Environment.NewLine + "以下文件没能推过去：" + string.Join("；", skipped));
    }

    /// <summary>
    /// 项目根下的全部 <c>.pevt</c>，跳过 bin/obj 里的副本。
    /// 按完整路径的序数序排列，让同一份项目每次得到相同的登记顺序——顺序决定重复 ID 的胜负。
    /// </summary>
    public static IReadOnlyList<PevtLiveWireFile> BuildSnapshot(string root, out IReadOnlyList<string> skipped)
    {
        var files = new List<PevtLiveWireFile>();
        var failed = new List<string>();

        // 严格解码：一份编码坏了的文件在这里就点名，否则游戏侧只会看到一串词法错误，
        // 作者根本猜不到真正的原因是文件没存成 UTF-8。
        var strictUtf8 = new UTF8Encoding(false, throwOnInvalidBytes: true);

        IEnumerable<string> paths = Directory
            .EnumerateFiles(root, "*.pevt", SearchOption.AllDirectories)
            .Where(path => !PuiProjectLocator.IsBuildOutput(path))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Take(PevtLiveProtocol.MaxFiles);

        foreach (string path in paths)
        {
            try
            {
                byte[] bytes = PevtProjectPaths.ReadAllBytesOrEncode(path, null);
                files.Add(new PevtLiveWireFile(ToRelative(root, path), strictUtf8.GetString(bytes)));
            }
            catch (DecoderFallbackException)
            {
                failed.Add(ToRelative(root, path) + "（不是合法的 UTF-8）");
            }
            catch (IOException ex)
            {
                failed.Add(ToRelative(root, path) + "（" + ex.Message + "）");
            }
            catch (UnauthorizedAccessException ex)
            {
                failed.Add(ToRelative(root, path) + "（" + ex.Message + "）");
            }
        }

        skipped = failed;
        return files;
    }

    /// <summary>
    /// 把快照里的某一份换成编辑缓冲区的内容。文件还没落过盘（刚新建、从没保存过）时补一条，
    /// 位置按完整路径的序数序插进去——登记顺序决定重复 ID 的胜负，不能因为"这一份来自缓冲区"就跑到末尾。
    /// </summary>
    private static IReadOnlyList<PevtLiveWireFile> WithEditorBuffer(
        IReadOnlyList<PevtLiveWireFile> files, string relativePath, string text)
    {
        var result = new List<PevtLiveWireFile>(files.Count + 1);
        bool replaced = false;

        foreach (PevtLiveWireFile file in files)
        {
            if (!replaced && string.Equals(file.SourcePath, relativePath, StringComparison.OrdinalIgnoreCase))
            {
                result.Add(new PevtLiveWireFile(relativePath, text));
                replaced = true;
                continue;
            }

            result.Add(file);
        }

        if (!replaced && result.Count < PevtLiveProtocol.MaxFiles)
        {
            int index = result.FindIndex(f =>
                string.Compare(f.SourcePath, relativePath, StringComparison.OrdinalIgnoreCase) > 0);
            result.Insert(index < 0 ? result.Count : index, new PevtLiveWireFile(relativePath, text));
        }

        return result;
    }

    private static string? TryGetFilePath(ProjectItem projectItem)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        try
        {
            return projectItem.FileCount > 0 ? projectItem.FileNames[1] : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// 项目相对路径，统一写成 <c>/</c> 分隔。
    /// 不复用 <see cref="PevtProjectPaths.ToProjectRelative"/>：那个方法自己去解析项目根，
    /// 而它走 DTE，在后台线程上只会退回文件所在目录，于是目录结构会整个丢掉。
    /// 这里的根是在 UI 线程上算好再传进来的。
    /// </summary>
    private static string ToRelative(string root, string path)
    {
        try
        {
            string rootFull = Path.GetFullPath(root);
            if (!rootFull.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
                rootFull += Path.DirectorySeparatorChar;

            string full = Path.GetFullPath(path);
            if (full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
                return full.Substring(rootFull.Length).Replace('\\', '/');

            return Path.GetFileName(full);
        }
        catch (Exception)
        {
            return Path.GetFileName(path) ?? string.Empty;
        }
    }

    private static void ReportToStatusBar(string text)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        try
        {
            if (Package.GetGlobalService(typeof(SVsStatusbar)) is IVsStatusbar bar)
            {
                bar.FreezeOutput(0);
                bar.SetText(text);
            }
        }
        catch (Exception)
        {
            // 状态栏拿不到就算了，Debug 输出里还有一份。
        }
    }

    private static string FirstLine(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        int end = text.IndexOfAny(new[] { '\r', '\n' });
        return end < 0 ? text : text.Substring(0, end);
    }
}
