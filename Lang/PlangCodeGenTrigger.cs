using EnvDTE;
using Microsoft.VisualStudio.Shell;
using VSLangProj;

namespace PolarisTools.Lang
{
    /// <summary>
    /// 存盘之后自动跑一遍代码生成：等价于在解决方案资源管理器里对这个 .plang 文件点
    /// Run Custom Tool，和 <c>PolarisToolsPackage.RunCustomTool</c> 是同一件事，只是入口在
    /// 表格编辑器的保存流程里——编辑器里不再有单独的"生成类"按钮，保存就是全部。
    /// </summary>
    internal static class PlangCodeGenTrigger
    {
        /// <summary>
        /// 跑代码生成。返回是否真的跑了：工具窗口可以打开解决方案外的 .plang，那种文件在项目里
        /// 找不到对应的 ProjectItem，也就没有 Custom Tool 可跑（调用方据此给用户不同的提示，
        /// 而不是假装生成过了）。
        /// </summary>
        public static bool RunCustomTool(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return false;

            ThreadHelper.ThrowIfNotOnUIThread();

            if (Package.GetGlobalService(typeof(DTE)) is not DTE dte) return false;

            ProjectItem item = dte.Solution?.FindProjectItem(filePath);
            if (item?.Object is not VSProjectItem vsProjectItem) return false;

            vsProjectItem.RunCustomTool();
            return true;
        }
    }
}
