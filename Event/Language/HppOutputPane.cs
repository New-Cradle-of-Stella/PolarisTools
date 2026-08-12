using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;

namespace PolarisTools.Event.Language
{
    /// <summary>"哈++: Show Generated CMD"/"Generate Alias Candidates" 两个命令共用的一块输出窗口面板。</summary>
    internal static class HppOutputPane
    {
        static readonly Guid PaneGuid = new Guid("2f0a6e3a-8f4a-4a7c-9a02-9f8f6c3a9a01");

        public static void WriteLine(string text)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (!(Package.GetGlobalService(typeof(SVsOutputWindow)) is IVsOutputWindow outputWindow))
            {
                return;
            }

            var paneGuid = PaneGuid;
            if (outputWindow.GetPane(ref paneGuid, out var pane) != Microsoft.VisualStudio.VSConstants.S_OK || pane == null)
            {
                outputWindow.CreatePane(ref paneGuid, "Polaris Event (哈++)", 1, 1);
                outputWindow.GetPane(ref paneGuid, out pane);
            }

            pane?.Activate();
            pane?.OutputStringThreadSafe(text + Environment.NewLine);
        }
    }
}
