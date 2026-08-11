using System;
using System.Windows;
using Microsoft.VisualStudio.PlatformUI;

namespace PolarisTools.Lang
{
    /// <summary>
    /// 「新增 Key」对话框：一次把 Key、说明、中性值都声明好，替掉原来那个借长文本框当输入框、
    /// 只能填一个 Key 的临时做法。Key 的重名/空白校验是边打边校验的——按下"添加"才报错的话，
    /// 用户已经把三个框都填完了，才发现 Key 撞了要重来。
    /// </summary>
    public partial class PlangAddKeyDialog : DialogWindow
    {
        readonly Func<string, bool> keyExists;

        /// <param name="keyExists">判断某个 Key 在当前文档里是否已存在，用来实时校验。</param>
        public PlangAddKeyDialog(Func<string, bool> keyExists)
        {
            this.keyExists = keyExists;
            InitializeComponent();
            Loaded += (s, e) => KeyBox.Focus();
        }

        public string Key { get; private set; } = "";

        public string Comment { get; private set; }

        public string NeutralValue { get; private set; } = "";

        void KeyBox_TextChanged(object sender, RoutedEventArgs e)
        {
            string key = KeyBox.Text?.Trim() ?? "";

            string error = null;
            if (key.Length > 0 && keyExists != null && keyExists(key))
            {
                error = $"\"{key}\" is already in this file; pick another name.";
            }

            KeyError.Text = error ?? "";
            KeyError.Visibility = error == null ? Visibility.Collapsed : Visibility.Visible;
            OkButton.IsEnabled = key.Length > 0 && error == null;
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
