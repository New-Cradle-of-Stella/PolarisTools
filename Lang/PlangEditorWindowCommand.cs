using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.ComponentModel.Design;
using Task = System.Threading.Tasks.Task;

namespace PolarisTools.Lang
{
    /// <summary>
    /// Command handler for opening the PLang localization tool window.
    /// </summary>
    internal sealed class PlangEditorWindowCommand
    {
        public const int CommandId = 0x0102;

        public static readonly Guid CommandSet = new Guid("1ba8fc7a-877c-43a5-8937-e1ed1b2dacea");

        private readonly AsyncPackage _package;

        private PlangEditorWindowCommand(AsyncPackage package, OleMenuCommandService commandService)
        {
            _package = package ?? throw new ArgumentNullException(nameof(package));
            if (commandService is null) throw new ArgumentNullException(nameof(commandService));

            commandService.AddCommand(new MenuCommand(Execute, new CommandID(CommandSet, CommandId)));
        }

        public static PlangEditorWindowCommand Instance { get; private set; }

        public static async Task InitializeAsync(AsyncPackage package)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

            OleMenuCommandService commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
            Instance = new PlangEditorWindowCommand(package, commandService);
        }

        private void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            ToolWindowPane window = _package.FindToolWindow(typeof(PlangEditorWindow), 0, true);
            if (window?.Frame is null)
                throw new NotSupportedException("Cannot create tool window");

            // 每次打开工具窗口都重新显示覆盖层（ToolWindow 实例是缓存的，必须手动恢复）
            if (window is PlangEditorWindow plangWindow)
                plangWindow.OnShown();

            Microsoft.VisualStudio.ErrorHandler.ThrowOnFailure(((IVsWindowFrame)window.Frame).Show());
        }
    }
}
