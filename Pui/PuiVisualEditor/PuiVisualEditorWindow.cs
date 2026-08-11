using Microsoft.VisualStudio.Shell;
using System.Runtime.InteropServices;

namespace PolarisTools.Pui.PuiVisualEditor
{
    [Guid("C3D4E5F6-A7B8-4C5D-9E0F-1A2B3C4D5E6F")]
    public class PuiVisualEditorWindow : ToolWindowPane
    {
        public PuiVisualEditorWindow() : base(null)
        {
            this.Caption = "PUI Visual Editor";
            this.Content = new PuiVisualEditorControl(isToolWindowHost: true);
        }

        public PuiVisualEditorControl Control => Content as PuiVisualEditorControl;

        /// <summary>每次显示工具窗口时恢复启动覆盖层</summary>
        public void OnShown()
        {
            Control?.ShowStartOverlay();
        }
    }
}
