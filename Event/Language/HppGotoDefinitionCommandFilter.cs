using EnvDTE;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace PolarisTools.Event.Language
{
    /// <summary>
    /// 阶段5 §8 的 Go To Definition：从 <c>Noel.Happy</c> 跳到别名文件里 <c>Noel:</c>/<c>Happy:</c>
    /// 那一行。走经典的 <c>IOleCommandTarget</c> 链式拦截——在文本视图上截获
    /// <c>VSStd97CmdID.GotoDefn</c>（F12），能处理就自己处理，不能处理就转给链上的下一个 command target。
    /// 没有语义模型可用，定位只能靠"在别名 yaml 文本里找那一行"的文本搜索，不是精确的 YAML 节点定位，
    /// 但对人工维护的、结构规整的别名文件来说已经够用。
    /// </summary>
    internal sealed class HppGotoDefinitionCommandFilter : IOleCommandTarget
    {
        static readonly Regex DialoguePrefix = new Regex(
            @"^\s*(?<actor>[A-Za-z_][A-Za-z0-9_]*)(\.(?<pose>[A-Za-z_][A-Za-z0-9_]*))?:",
            RegexOptions.Compiled);

        readonly IWpfTextView textView;
        readonly string filePath;

        public IOleCommandTarget Next { get; set; }

        public HppGotoDefinitionCommandFilter(IWpfTextView textView, string filePath)
        {
            this.textView = textView;
            this.filePath = filePath;
        }

        public int QueryStatus(ref Guid pguidCmdGroup, uint cCmds, OLECMD[] prgCmds, IntPtr pCmdText)
        {
            return Next?.QueryStatus(ref pguidCmdGroup, cCmds, prgCmds, pCmdText) ?? (int)Microsoft.VisualStudio.OLE.Interop.Constants.OLECMDERR_E_NOTSUPPORTED;
        }

        public int Exec(ref Guid pguidCmdGroup, uint nCmdID, uint nCmdexecopt, IntPtr pvaIn, IntPtr pvaOut)
        {
            if (pguidCmdGroup == typeof(VSConstants.VSStd97CmdID).GUID && nCmdID == (uint)VSConstants.VSStd97CmdID.GotoDefn)
            {
                if (TryGotoDefinition())
                {
                    return VSConstants.S_OK;
                }
            }

            return Next?.Exec(ref pguidCmdGroup, nCmdID, nCmdexecopt, pvaIn, pvaOut) ?? (int)Microsoft.VisualStudio.OLE.Interop.Constants.OLECMDERR_E_NOTSUPPORTED;
        }

        bool TryGotoDefinition()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var caret = textView.Caret.Position.BufferPosition;
            var line = caret.GetContainingLine();
            string lineText = line.GetText();
            int offset = caret.Position - line.Start.Position;

            if (!TryGetWordSpan(lineText, offset, out int start, out int length))
            {
                return false;
            }

            string word = lineText.Substring(start, length);
            string actorName = word;
            string poseName = null;
            int dot = word.IndexOf('.');
            if (dot > 0)
            {
                actorName = word.Substring(0, dot);
                poseName = word.Substring(dot + 1);
            }
            else
            {
                var match = DialoguePrefix.Match(lineText);
                if (!match.Success || !string.Equals(match.Groups["actor"].Value, word, StringComparison.Ordinal))
                {
                    return false;
                }

                if (match.Groups["pose"].Success)
                {
                    poseName = match.Groups["pose"].Value;
                }
            }

            string directory = string.IsNullOrEmpty(filePath) ? null : Path.GetDirectoryName(filePath);
            var aliasSource = HppAliasFileLocator.FindAliasSource(directory);
            if (aliasSource == null || !File.Exists(aliasSource.Path))
            {
                return false;
            }

            int targetLine = FindLineInAliasFile(aliasSource.Content, actorName, poseName);
            if (targetLine < 0)
            {
                targetLine = 0; // 找不到具体行也值得把文件打开，好过完全不反应
            }

            OpenAndNavigate(aliasSource.Path, targetLine);
            return true;
        }

        /// <summary>
        /// 在别名 yaml 的原始文本里找 "  ActorName:" 这一行，有姿势名的话再从那一行往下找
        /// "    PoseName:"（缩进比角色行更深）。纯文本启发式，不解析 YAML 结构，胜在对人工维护的
        /// 规整缩进文件足够可靠、不需要额外依赖。
        /// </summary>
        static int FindLineInAliasFile(string content, string actorName, string poseName)
        {
            string[] lines = content.Replace("\r\n", "\n").Split('\n');
            var actorPattern = new Regex($@"^\s*{Regex.Escape(actorName)}\s*:\s*$");
            int actorLine = -1;
            int actorIndent = -1;

            for (int i = 0; i < lines.Length; i++)
            {
                if (actorPattern.IsMatch(lines[i]))
                {
                    actorLine = i;
                    actorIndent = lines[i].Length - lines[i].TrimStart().Length;
                    break;
                }
            }

            if (actorLine < 0 || poseName == null)
            {
                return actorLine;
            }

            var posePattern = new Regex($@"^\s*{Regex.Escape(poseName)}\s*:");
            for (int i = actorLine + 1; i < lines.Length; i++)
            {
                int indent = lines[i].Length - lines[i].TrimStart().Length;
                if (lines[i].Trim().Length > 0 && indent <= actorIndent)
                {
                    break; // 走出了这个角色的块
                }

                if (posePattern.IsMatch(lines[i]))
                {
                    return i;
                }
            }

            return actorLine;
        }

        static void OpenAndNavigate(string filePath, int zeroBasedLine)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (Package.GetGlobalService(typeof(DTE)) is DTE dte)
            {
                var window = dte.ItemOperations.OpenFile(filePath);
                var selection = window?.Document?.Selection as TextSelection;
                selection?.GotoLine(zeroBasedLine + 1, true);
            }
        }

        static bool TryGetWordSpan(string text, int offset, out int start, out int length)
        {
            start = 0;
            length = 0;
            if (offset < 0 || offset > text.Length)
            {
                return false;
            }

            bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '.';

            int s = offset;
            while (s > 0 && IsWordChar(text[s - 1]))
            {
                s--;
            }

            int e = offset;
            while (e < text.Length && IsWordChar(text[e]))
            {
                e++;
            }

            if (e <= s)
            {
                return false;
            }

            start = s;
            length = e - s;
            return true;
        }
    }
}
