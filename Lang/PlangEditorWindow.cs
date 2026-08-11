using Microsoft.VisualStudio.Shell;
using System.Runtime.InteropServices;

namespace PolarisTools.Lang
{
    /// <summary>
    /// .plang 表格编辑器的工具窗口入口（菜单 工具 &gt; Polaris &gt; PLang本地化工具窗口）。
    /// 和双击 .plang 打开的 <see cref="PlangEditorPane"/> 用的是同一个控件，区别只在于
    /// 工具窗口没有 VS 的文档持久化，要自己选文件、自己按"保存"。
    /// </summary>
    [Guid("D4E5F6A7-B8C9-4D5E-9F0A-2B3C4D5E6F7A")]
    public class PlangEditorWindow : ToolWindowPane
    {
        public PlangEditorWindow() : base(null)
        {
            this.Caption = "PLang Localization Editor";
            this.Content = new PlangEditorControl(isToolWindowHost: true);
        }

        public PlangEditorControl Control => Content as PlangEditorControl;

        /// <summary>每次显示工具窗口时恢复启动覆盖层</summary>
        public void OnShown()
        {
            Control?.ShowStartOverlay();
        }
    }
}
