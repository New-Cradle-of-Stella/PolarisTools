using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Polaris.Lang;
using Polaris.Pevt.Diagnostics;
using Polaris.Pevt.Syntax;
using Polaris.Pevt.Text;
using PolarisTools.Event.Pevt.Live;
using PolarisTools.Event.Pevt.Localize;
using PolarisTools.Lang;
using PolarisTools.Pui.PuiVisualEditor;

namespace PolarisTools.Event.Pevt.Editor;

/// <summary>
/// 命令条上两个按钮背后的实现。
///
/// 与 <see cref="PevtEditorToolbarMargin"/> 分开，是为了让那一侧只剩"画按钮"：这里全部是
/// 跨线程、要碰磁盘和项目系统的活儿，两件事的失败方式完全不同，混在一个类里会互相遮住。
/// </summary>
internal sealed class PevtEditorCommands
{
    private const string DialogTitle = "Polaris PEVT";

    private readonly IWpfTextView _textView;
    private readonly ITextDocumentFactoryService _documentFactory;
    private readonly Action<bool> _setBusy;
    private readonly Action<string> _setStatus;

    public PevtEditorCommands(
        IWpfTextView textView,
        ITextDocumentFactoryService documentFactory,
        Action<bool> setBusy,
        Action<string> setStatus)
    {
        _textView = textView;
        _documentFactory = documentFactory;
        _setBusy = setBusy;
        _setStatus = setStatus;
    }

    // ---- 热重载 ----

    public void HotReload() => Run(HotReloadAsync);

    /// <summary>
    /// 把整份项目快照推给正在跑的游戏，其中这一份取自编辑缓冲区。
    ///
    /// 和保存时的自动推送共用 <see cref="PevtLivePush"/>：连的是同一个管道、同一套帧格式、同一套
    /// 整批替换语义。区别只有两点——这一次作者在等结果，所以连接超时更宽、没连上也要如实告诉他。
    /// </summary>
    private async Task HotReloadAsync()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        if (!TryGetFilePath(out string? filePath))
        {
            _setStatus("Save this file to disk before pushing it to the game.");
            return;
        }

        // 项目根必须在 UI 线程上解析：PuiProjectLocator 走 DTE，脱离 UI 线程只会退回文件自己的目录。
        string root = PuiProjectLocator.ResolveProjectDir(filePath!);
        if (string.IsNullOrEmpty(root))
            root = Path.GetDirectoryName(filePath!) ?? "";

        string text = _textView.TextBuffer.CurrentSnapshot.GetText();
        _setStatus("Pushing to the game…");

        (bool connected, bool ok, string message) = await Task.Run(
            () => PevtLivePush.PushEditorBufferAsync(root, filePath!, text));

        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        if (!connected)
        {
            // 没连上不是错误，只是游戏没开着——这是常态，不该弹窗打断。
            _setStatus(FirstLine(message));
            return;
        }

        _setStatus((ok ? "Hot reload: " : "Hot reload failed: ") + FirstLine(message));
        if (!ok)
            MessageBox.Show(message, DialogTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    // ---- 快速本地化 ----

    public void QuickLocalize() => Run(QuickLocalizeAsync);

    /// <summary>
    /// 把这份文件里全部"给玩家看的文案"换成 <c>&amp;键</c>，并把原文写进同名 <c>.plang</c>。
    ///
    /// 顺序刻意是"先算好全部改动，再一次性落盘"：缓冲区改一半、表格写一半的中间状态没人能收拾。
    /// </summary>
    private async Task QuickLocalizeAsync()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        if (!TryGetFilePath(out string? filePath))
        {
            _setStatus("Save this file to disk before running quick localization.");
            return;
        }

        ITextSnapshot snapshot = _textView.TextBuffer.CurrentSnapshot;
        string text = snapshot.GetText();

        SourceText? source = PevtEditorText.Load(text);
        if (source == null)
        {
            Report("This file is not valid UTF-8 text, so it cannot be parsed.");
            return;
        }

        // 词法/语法有错时实参的位置本身就不可信，替换会改到不该改的地方。绑定错误（未知人物、
        // 类型不符）不影响"第几个实参在哪里"，因此只拦这两道门。
        var bag = new DiagnosticBag();
        IReadOnlyList<SyntaxToken> tokens = Lexer.Tokenize(source, bag);
        DocumentSyntax document = new Parser(tokens, bag, source).ParseDocument();
        if (bag.HasErrors)
        {
            Report("This file still has syntax errors. Fix them first — argument positions cannot be trusted while the file does not parse.");
            return;
        }

        IReadOnlyList<PevtTextOccurrence> occurrences = PevtLocalizePass.Collect(document, source);
        if (occurrences.Count == 0)
        {
            _setStatus("No player-facing text literals found in this file.");
            return;
        }

        string plangPath = PevtPlangSidecar.PathFor(filePath!);
        if (PevtPlangSidecar.IsOpenAndDirty(ServiceProvider.GlobalProvider, plangPath))
        {
            Report($"{Path.GetFileName(plangPath)} is open with unsaved changes. Save or close it first, otherwise those changes would overwrite the keys written here.");
            return;
        }

        PlangDocument? existing;
        try
        {
            existing = PevtPlangSidecar.Load(plangPath);
        }
        catch (Exception ex)
        {
            Report($"{Path.GetFileName(plangPath)} could not be read, so it will not be overwritten: {ex.Message}");
            return;
        }

        if (!TryAskLanguage(existing, out string languageCode, out string languageName))
            return;

        string keyPrefix = PevtLocalizePass.KeyPrefix(document, Path.GetFileNameWithoutExtension(filePath!));
        PevtLocalizePlan plan = PevtLocalizePass.Plan(occurrences, existing, keyPrefix, languageCode, languageName);

        if (plan.Replacements.Count == 0)
        {
            _setStatus($"Every text literal in this file is already a localization key ({plan.AlreadyLocalized.Count}).");
            return;
        }

        if (!TryApplyToBuffer(snapshot, plan))
        {
            Report("The file changed while quick localization was running; nothing was written. Try again.");
            return;
        }

        try
        {
            PevtPlangSidecar.Save(ServiceProvider.GlobalProvider, plangPath, plan.Document);
        }
        catch (Exception ex)
        {
            // 缓冲区已经改了，但那是可撤销的；表格没写成必须说清楚，否则作者会得到一份指向空表的脚本。
            Report($"The text was replaced in the editor, but {Path.GetFileName(plangPath)} could not be written: {ex.Message}. Undo the edit (Ctrl+Z) or fix the file and run this again.");
            return;
        }

        string? projectWarning = PevtPlangSidecar.EnsureInProject(filePath!, plangPath);

        _setStatus(Summary(plan, plangPath, projectWarning));
        if (projectWarning != null)
        {
            MessageBox.Show(
                $"{Path.GetFileName(plangPath)} was written, but it could not be wired into the project: {projectWarning}\n\n"
                + "Add it to the project manually and set its Custom Tool to " + PolarisLangGenerator.GeneratorName + ".",
                DialogTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>
    /// 问一次"这批文案现在是用哪门语言写的"。复用 <c>.plang</c> 编辑器那个语言选择框，
    /// 只是换成单选——这里选两门语言没有意义，同一段原文不可能同时是两种语言。
    /// </summary>
    private static bool TryAskLanguage(PlangDocument? existing, out string code, out string displayName)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        code = "";
        displayName = "";

        var dialog = new PlangLocalePickerDialog(
            // 已经在表里的语言照样可选：这次是往那一列里补文案，不是新建一列。
            _ => false,
            title: "Quick localization",
            description: "Which language is the text in this file written in? The original text goes into that language's column (and into the neutral fallback), and every literal is replaced with its \"&key\".",
            singleSelect: true,
            okText: "Localize");

        if (dialog.ShowDialog() != true || dialog.SelectedLocales.Count == 0)
            return false;

        (string picked, string pickedName) = dialog.SelectedLocales[0];

        // 表里已经有这门语言时沿用它原来的写法，别让一次快速本地化把「简体中文」改成「zh-cn」，
        // 也别因为大小写不同凭空多出一列。
        PlangLanguage? known = existing?.Languages
            .FirstOrDefault(l => string.Equals(l.Code, picked, StringComparison.OrdinalIgnoreCase));

        code = known?.Code ?? picked;
        displayName = known?.DisplayName ?? pickedName;
        return true;
    }

    /// <summary>
    /// 一次性把全部替换写进缓冲区。<see cref="ITextEdit"/> 的全部改动是同一个撤销单元，
    /// 因此作者一次 Ctrl+Z 就能整体退回——这对"一下子改了三十处台词"的操作是必需的。
    /// </summary>
    private bool TryApplyToBuffer(ITextSnapshot snapshot, PevtLocalizePlan plan)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        using (ITextEdit edit = _textView.TextBuffer.CreateEdit())
        {
            foreach (PevtLocalizeReplacement replacement in plan.Replacements)
            {
                TextSpan span = replacement.Occurrence.Span;
                if (span.End > snapshot.Length
                    || !edit.Replace(span.Start, span.Length, PevtLocalizePass.ReplacementLiteral(replacement.Key)))
                {
                    edit.Cancel();
                    return false;
                }
            }

            return edit.Apply() != snapshot;
        }
    }

    private static string Summary(PevtLocalizePlan plan, string plangPath, string? projectWarning)
    {
        var parts = new List<string>
        {
            $"{plan.Replacements.Count} replaced",
            $"{plan.AddedEntryCount} new key(s) in {Path.GetFileName(plangPath)}",
        };

        int reused = plan.Replacements.Count(r => r.ReusedExistingKey);
        if (reused > 0)
            parts.Add($"{reused} reused an existing key");
        if (plan.AlreadyLocalized.Count > 0)
            parts.Add($"{plan.AlreadyLocalized.Count} already localized");
        if (plan.AddedLanguage)
            parts.Add("added a language column");
        if (projectWarning != null)
            parts.Add("not wired into the project");

        return "Quick localization: " + string.Join(", ", parts) + ". Save this file to regenerate.";
    }

    // ---- 共用 ----

    /// <summary>缓冲区背后的磁盘文件；文件从没存过盘时没有路径，两个动作都做不了。</summary>
    private bool TryGetFilePath(out string? filePath)
    {
        filePath = null;

        if (!_documentFactory.TryGetTextDocument(_textView.TextDataModel.DocumentBuffer, out ITextDocument document))
            return false;

        filePath = document.FilePath;
        return !string.IsNullOrEmpty(filePath) && File.Exists(filePath);
    }

    /// <summary>按钮点下去到结果回来之间禁用两个按钮，并保证异常一定会被看到而不是静静吃掉。</summary>
    private void Run(Func<Task> action)
    {
        _setBusy(true);

        _ = ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
        {
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                _setStatus(ex.Message);
                MessageBox.Show(ex.ToString(), DialogTitle, MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                _setBusy(false);
            }
        });
    }

    private void Report(string message)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        _setStatus(message);
        MessageBox.Show(message, DialogTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private static string FirstLine(string text)
    {
        if (string.IsNullOrEmpty(text))
            return "";

        int end = text.IndexOfAny(new[] { '\r', '\n' });
        return end < 0 ? text : text.Substring(0, end);
    }
}
