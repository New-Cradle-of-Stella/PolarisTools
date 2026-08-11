using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using Microsoft.VisualStudio.PlatformUI;

namespace PolarisTools.Lang
{
    /// <summary>
    /// 「添加语言」对话框：游戏自带的那几种语言直接列出来点选（可多选），代码由
    /// <see cref="PlangLocaleCatalog"/> 提供，用户不用记 <c>zh-tc</c> 到底是 tc 还是 tw；
    /// 装了第三方语言包的情况留了一个手填代码的入口兜底。
    /// </summary>
    public partial class PlangLocalePickerDialog : DialogWindow
    {
        /// <summary>列表里的一行。ToggleButton 直接绑这个对象，Visibility 这种展示细节直接算好，省一堆 Converter。</summary>
        public sealed class LocaleOption
        {
            // 参数类型是 internal 的目录条目，构造函数也只能是 internal（类本身保持 public，
            // 好让 XAML 里的绑定按公开属性反射取值）。
            internal LocaleOption(PlangLocaleInfo info, bool alreadyAdded)
            {
                Code = info.Code;
                Badge = info.Badge;
                DisplayName = info.DisplayName;
                Note = alreadyAdded ? null : info.Note;
                AlreadyAdded = alreadyAdded;
            }

            public string Code { get; }
            public string Badge { get; }
            public string DisplayName { get; }
            public string Note { get; }
            public bool AlreadyAdded { get; }

            /// <summary>已经在文档里的语言不给再选一遍。</summary>
            public bool IsSelectable => !AlreadyAdded;

            public bool IsSelected { get; set; }

            public Visibility NoteVisibility => string.IsNullOrEmpty(Note) ? Visibility.Collapsed : Visibility.Visible;

            public Visibility AddedTagVisibility => AlreadyAdded ? Visibility.Visible : Visibility.Collapsed;
        }

        readonly ObservableCollection<LocaleOption> options = new();
        readonly Func<string, bool> languageExists;

        /// <param name="languageExists">判断某个语言代码在当前文档里是否已存在。</param>
        public PlangLocalePickerDialog(Func<string, bool> languageExists)
        {
            this.languageExists = languageExists;
            InitializeComponent();

            foreach (PlangLocaleInfo info in PlangLocaleCatalog.BuiltIn)
                options.Add(new LocaleOption(info, languageExists?.Invoke(info.Code) == true));

            LocaleList.ItemsSource = options;
        }

        /// <summary>用户这次选中/填的语言，按列表顺序，自定义那条排最后。</summary>
        public IReadOnlyList<(string Code, string DisplayName)> SelectedLocales { get; private set; } =
            Array.Empty<(string, string)>();

        void LocaleCard_Click(object sender, RoutedEventArgs e) => RefreshState();

        void CustomCode_TextChanged(object sender, RoutedEventArgs e) => RefreshState();

        void RefreshState()
        {
            string custom = CustomCodeBox.Text?.Trim() ?? "";

            string error = null;
            if (custom.Length > 0)
            {
                if (languageExists?.Invoke(custom) == true)
                    error = $"Language code \"{custom}\" is already in this file.";
                else if (options.Any(o => !o.AlreadyAdded && o.IsSelected && string.Equals(o.Code, custom, StringComparison.OrdinalIgnoreCase)))
                    error = $"Language code \"{custom}\" is already selected above; no need to type it again.";
            }

            ErrorText.Text = error ?? "";
            ErrorText.Visibility = error == null ? Visibility.Collapsed : Visibility.Visible;
            OkButton.IsEnabled = error == null && (custom.Length > 0 || options.Any(o => o.IsSelected));
        }

        void Ok_Click(object sender, RoutedEventArgs e)
        {
            var picked = options.Where(o => o.IsSelected && !o.AlreadyAdded)
                                .Select(o => (o.Code, o.DisplayName))
                                .ToList();

            string custom = CustomCodeBox.Text?.Trim() ?? "";
            if (custom.Length > 0)
            {
                string name = CustomNameBox.Text?.Trim();
                if (string.IsNullOrEmpty(name))
                    name = PlangLocaleCatalog.DisplayNameFor(custom) ?? custom;

                picked.Add((custom, name));
            }

            SelectedLocales = picked;
            DialogResult = true;
        }
    }
}
