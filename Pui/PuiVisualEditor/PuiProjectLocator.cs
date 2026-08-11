using EnvDTE;
using Microsoft.VisualStudio.Shell;
using System;
using System.IO;

namespace PolarisTools.Pui.PuiVisualEditor
{
    /// <summary>
    /// "正在编辑的 <c>.pui</c> 属于哪个项目"的唯一一份定位实现。<see cref="PlangKeyCatalog"/>
    /// （扫 <c>.plang</c> 做键 → 文案查表）和 <see cref="PolarisResourceCatalog"/>（扫 <c>.cs</c>
    /// 枚举 PolarisRes 资源字段）都按项目根扫盘并按项目根缓存，两边必须认同同一个根目录，
    /// 否则同一个 <c>.pui</c> 会出现"文案查得到、图片查不到"这类各自为政的结果。
    /// </summary>
    internal static class PuiProjectLocator
    {
        /// <summary>
        /// 项目根 = <c>.pui</c> 所属项目文件所在目录，走 DTE 的
        /// <c>Solution.FindProjectItem</c> → <c>ContainingProject</c> 定位；拿不到（不在解决
        /// 方案里、不在 UI 线程、DTE 不可用）就退回 <c>.pui</c> 自己所在的目录——总比什么都不查好。
        /// 两者都拿不到时返回 null，调用方按"空表"处理。
        /// </summary>
        public static string ResolveProjectDir(string puiFilePath)
        {
            if (string.IsNullOrEmpty(puiFilePath))
            {
                return null;
            }

            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();

                if (Package.GetGlobalService(typeof(DTE)) is DTE dte)
                {
                    ProjectItem item = dte.Solution?.FindProjectItem(puiFilePath);
                    string projectPath = item?.ContainingProject?.FullName;
                    if (!string.IsNullOrEmpty(projectPath))
                    {
                        string dir = Path.GetDirectoryName(projectPath);
                        if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                        {
                            return dir;
                        }
                    }
                }
            }
            catch
            {
                // 定位项目失败不该影响编辑体验，往下退回 .pui 自己的目录。
            }

            try
            {
                string dir = Path.GetDirectoryName(puiFilePath);
                return !string.IsNullOrEmpty(dir) && Directory.Exists(dir) ? dir : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>路径落在 bin/obj 里（生成产物，不是作者写的源文件）。</summary>
        public static bool IsBuildOutput(string path)
        {
            string p = path.Replace('/', '\\');
            return p.IndexOf("\\bin\\", StringComparison.OrdinalIgnoreCase) >= 0
                || p.IndexOf("\\obj\\", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
