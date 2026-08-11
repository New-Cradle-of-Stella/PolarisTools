using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.ComponentModel.Design;
using Task = System.Threading.Tasks.Task;

namespace PolarisTools.Pui.PuiSolutions
{
    /// <summary>
    /// 打开「PUI Graph」工具窗口的菜单命令处理器。
    /// </summary>
    internal sealed class PuiSolutionWindowCommand
    {
        public const int CommandId = 0x0100;

        /// <summary>命令集 GUID，必须与 PolarisToolsPackage.vsct 里的一致。</summary>
        public static readonly Guid CommandSet = new Guid("1ba8fc7a-877c-43a5-8937-e1ed1b2dacea");

        private readonly AsyncPackage _package;

        private PuiSolutionWindowCommand(AsyncPackage package, OleMenuCommandService commandService)
        {
            _package = package ?? throw new ArgumentNullException(nameof(package));
            if (commandService is null) throw new ArgumentNullException(nameof(commandService));

            commandService.AddCommand(new MenuCommand(Execute, new CommandID(CommandSet, CommandId)));
        }

        public static PuiSolutionWindowCommand Instance { get; private set; }

        public static async Task InitializeAsync(AsyncPackage package)
        {
            // 构造函数里的 AddCommand 要求 UI 线程。
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

            OleMenuCommandService commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
            Instance = new PuiSolutionWindowCommand(package, commandService);
        }

        private void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            ToolWindowPane window = _package.FindToolWindow(typeof(PuiSolutionWindow), 0, true);
            if (window?.Frame is null)
                throw new NotSupportedException("Cannot create tool window");

            // 每次打开工具窗口都重新显示覆盖层
            // （关闭 .puisln 文档编辑器不会影响；ToolWindow 实例是缓存的，必须手动恢复）
            if (window is PuiSolutionWindow puiWindow)
                puiWindow.OnShown();

            Microsoft.VisualStudio.ErrorHandler.ThrowOnFailure(((IVsWindowFrame)window.Frame).Show());
        }
    }
}
