using System;
using System.Collections.Generic;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;

namespace PolarisTools.Lang
{
    /// <summary>
    /// 表格里的一行 = 一个 Key。语言列的值走索引器，配合 DataGrid 动态列的 <c>Binding="[langCode]"</c>。
    /// <para>
    /// 所有文案字段都是"可以换行的普通文本"，没有短/长类型之分——单元格里直接换行，存盘时
    /// 一律走 CDATA，不需要用户先声明这一条是长还是短。
    /// </para>
    /// </summary>
    public partial class PlangRowViewModel : ObservableObject
    {
        [ObservableProperty] private string key = "";
        [ObservableProperty] private string comment;

        /// <summary>中性值：没有任何启用语言命中时的兜底文案，语义等价于旧版唯一的 Value。</summary>
        [ObservableProperty] private string neutralValue = "";

        readonly Dictionary<string, string> languageValues = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 按语言代码取/设文案，给动态生成的语言列用（<c>DataGridTextColumn.Binding = new
        /// Binding("[langCode]")</c>）。没写过的语言视为空串，不是 null，省得 DataGrid 那边
        /// 到处判空。
        /// </summary>
        public string this[string langCode]
        {
            get => !string.IsNullOrEmpty(langCode) && languageValues.TryGetValue(langCode, out string v) ? v : "";
            set
            {
                if (string.IsNullOrEmpty(langCode) || this[langCode] == (value ?? "")) return;
                languageValues[langCode] = value ?? "";
                OnPropertyChanged(Binding.IndexerName);
            }
        }

        /// <summary>保存/CSV 导出用的只读视图。</summary>
        public IReadOnlyDictionary<string, string> LanguageValues => languageValues;

        /// <summary>整体替换语言值（加载文件、CSV 导入时用），一次性触发一条索引器变更通知。</summary>
        public void ReplaceLanguageValues(IEnumerable<KeyValuePair<string, string>> values)
        {
            languageValues.Clear();
            if (values != null)
            {
                foreach (KeyValuePair<string, string> kv in values)
                {
                    languageValues[kv.Key] = kv.Value ?? "";
                }
            }

            OnPropertyChanged(Binding.IndexerName);
        }

        /// <summary>搜索框用：Key/说明/中性值/任意语言文案里出现过这段文字（不区分大小写）就算命中。</summary>
        public bool Matches(string text)
        {
            if (string.IsNullOrEmpty(text)) return true;

            if (Contains(Key, text) || Contains(Comment, text) || Contains(NeutralValue, text)) return true;

            foreach (string value in languageValues.Values)
            {
                if (Contains(value, text)) return true;
            }

            return false;
        }

        static bool Contains(string haystack, string needle) =>
            !string.IsNullOrEmpty(haystack) && haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
