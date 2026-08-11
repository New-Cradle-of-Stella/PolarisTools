using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.VisualStudio.PlatformUI;

namespace PolarisTools.Lang
{
    /// <summary>
    /// 「编辑这一行」对话框：把选中那个 Key 的全部文案（Key/说明/中性值 + 每门启用语言）摊开在
    /// 一个窗口里改。
    /// <para>
    /// 之所以是"整行"而不是"当前那一格"：表格是整行选中的，光标在哪一格用户看不见，按格子走的话
    /// 点按钮那一下还会因为焦点离开表格而丢掉目标。按行走跟用户看到的选中状态一致，也顺手解决了
    /// 一个 Key 要在好几门语言之间来回挪的麻烦。
    /// </para>
    /// </summary>
    public partial class PlangEntryEditorDialog : DialogWindow
    {
        /// <summary>一门语言一个可编辑框；<see cref="Value"/> 由 TextBox 双向绑定写回。</summary>
        public sealed class LanguageField
        {
            internal LanguageField(string code, string label, string value)
            {
                Code = code;
                Label = label;
                Value = value;
            }

            public string Code { get; }
            public string Label { get; }
            public string Value { get; set; }
        }

        readonly Func<string, bool> keyTakenByOthers;
        readonly ObservableCollection<LanguageField> fields = new();

        /// <param name="row">要编辑的行，只读取初始值；改动由调用方在对话框确定后写回。</param>
        /// <param name="languages">当前文档里的全部语言（停用的会被过滤掉，只列启用的）。</param>
        /// <param name="keyTakenByOthers">这个 Key 是否已经被"别的行"占了，用来实时校验重名。</param>
        public PlangEntryEditorDialog(
            PlangRowViewModel row,
            IEnumerable<PlangLanguageViewModel> languages,
            Func<string, bool> keyTakenByOthers)
        {
            this.keyTakenByOthers = keyTakenByOthers;
            InitializeComponent();

            TitleText.Text = string.IsNullOrEmpty(row.Key) ? "Edit this row" : row.Key;
            KeyBox.Text = row.Key ?? "";
            CommentBox.Text = row.Comment ?? "";
            NeutralBox.Text = row.NeutralValue ?? "";

            List<PlangLanguageViewModel> all = languages?.Where(l => !string.IsNullOrEmpty(l.Code)).ToList() ?? new();
            foreach (PlangLanguageViewModel lang in all.Where(l => l.Enabled))
            {
                fields.Add(new LanguageField(lang.Code, lang.Label, row[lang.Code]));
            }
            LanguageList.ItemsSource = fields;

            int disabled = all.Count(l => !l.Enabled);
            if (disabled > 0)
            {
                DisabledHint.Text = $"There are {disabled} more languages currently disabled and not listed here -- re-enable them on the language bar to make them appear.";
                DisabledHint.Visibility = Visibility.Visible;
            }

            Loaded += (s, e) =>
            {
                NeutralBox.Focus();
                NeutralBox.CaretIndex = NeutralBox.Text.Length;
            };
        }

        public string Key { get; private set; } = "";

        public string Comment { get; private set; }

        public string NeutralValue { get; private set; } = "";

        /// <summary>确定之后每门语言的新文案，按语言代码取。</summary>
        public IReadOnlyList<LanguageField> LanguageValues => fields;

        void KeyBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string key = KeyBox.Text?.Trim() ?? "";

            string error = null;
            if (key.Length == 0)
            {
                error = "Key cannot be empty.";
            }
            else if (keyTakenByOthers != null && keyTakenByOthers(key))
            {
                error = $"\"{key}\" is already the key of another row; pick another name.";
            }

            KeyError.Text = error ?? "";
            KeyError.Visibility = error == null ? Visibility.Collapsed : Visibility.Visible;
            OkButton.IsEnabled = error == null;
        }

        void Ok_Click(object sender, RoutedEventArgs e)
        {
            Key = KeyBox.Text?.Trim() ?? "";
            Comment = string.IsNullOrWhiteSpace(CommentBox.Text) ? null : CommentBox.Text.Trim();
            NeutralValue = NeutralBox.Text ?? "";
            DialogResult = true;
        }
    }
}
