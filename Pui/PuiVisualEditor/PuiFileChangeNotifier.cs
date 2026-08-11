using System;

namespace PolarisTools.Pui.PuiVisualEditor
{
    /// <summary>
    /// .pui 文件保存后的广播点。PUISolutionsEditor 那边的关系图（可能同时开着好几个）订阅这个
    /// 事件，收到后按 PuiFilePath 匹配自己的 PuiState 节点并重新读取，不需要两个编辑器互相引用。
    /// </summary>
    public static class PuiFileChangeNotifier
    {
        public static event Action<string> Saved;

        public static void NotifySaved(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            Saved?.Invoke(path);
        }
    }
}
