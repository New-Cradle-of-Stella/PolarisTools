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
        readonly bool singleSelect;

        /// <param name="languageExists">判断某个语言代码在当前文档里是否已存在。</param>
        /// <param name="title">标题栏与页面标题；null 用默认的「添加语言」。</param>
        /// <param name="description">标题下那行说明；null 用默认的那句。</param>
        /// <param name="singleSelect">
        /// 只能选一个。给「快速本地化」这种"这批文案是用哪门语言写的"的问法用——那里选两门语言没有意义。
        /// </param>
        /// <param name="okText">确认按钮文案；null 用默认的 Add。</param>
        public PlangLocalePickerDialog(
            Func<string, bool> languageExists,
            string title = null,
            string description = null,
            bool singleSelect = false,
            string okText = null)
        {
            this.languageExists = languageExists;
            this.singleSelect = singleSelect;
            InitializeComponent();

            if (!string.IsNullOrEmpty(title))
            {
                Title = title;
                TitleText.Text = title;
            }

            if (!string.IsNullOrEmpty(description))
                DescriptionText.Text = description;

            if (!string.IsNullOrEmpty(okText))
                OkButton.Content = okText;

            foreach (PlangLocaleInfo info in PlangLocaleCatalog.BuiltIn)
                options.Add(new LocaleOption(info, languageExists?.Invoke(info.Code) == true));

            LocaleList.ItemsSource = options;
        }

        /// <summary>用户这次选中/填的语言，按列表顺序，自定义那条排最后。</summary>
        public IReadOnlyList<(string Code, string DisplayName)> SelectedLocales { get; private set; } =
            Array.Empty<(string, string)>();

        void LocaleCard_Click(object sender, RoutedEventArgs e)
        {
            // 单选模式下靠代码维持互斥，而不是换一套 RadioButton 模板：列表模板只有一份，
            // 两种模式的外观和交互手感因此完全一致。
            if (singleSelect && sender is System.Windows.Controls.Primitives.ToggleButton clicked
                && clicked.IsChecked == true && clicked.DataContext is LocaleOption picked)
            {
                foreach (LocaleOption option in options)
                {
                    if (!ReferenceEquals(option, picked))
                        option.IsSelected = false;
                }

                LocaleList.Items.Refresh();
            }

            RefreshState();
        }

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

            // 单选模式下手填的代码优先：作者既点了卡片又打了字，说明后打的那个才是他要的。
            if (singleSelect && !string.IsNullOrEmpty(CustomCodeBox.Text?.Trim()))
                picked.Clear();

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
