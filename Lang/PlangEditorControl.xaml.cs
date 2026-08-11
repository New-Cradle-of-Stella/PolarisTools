using Microsoft.VisualStudio.Shell;
using Microsoft.Win32;
using Polaris.Lang;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Threading;

namespace PolarisTools.Lang
{
    /// <summary>.plang 表格：行 = Key，列 = Key/说明/中性值 + 按启用语言动态出现的列，一份 .plang 文件对应一张表。</summary>
    public partial class PlangEditorControl : UserControl
    {
        public PlangEditorViewModel ViewModel { get; } = new();
        string filePath;

        // DataGridColumn 没有 Tag 属性（不是 FrameworkElement），标记不了"这是动态语言列"，
        // 自己维护一份列表来跟踪由 RebuildLanguageColumns 加进去的列，方便下次重建时精确摘除。
        readonly List<DataGridColumn> languageColumns = new();

        readonly bool isToolWindowHost;
        string searchText = "";

        public PlangEditorControl() : this(isToolWindowHost: false)
        {
        }

        public PlangEditorControl(bool isToolWindowHost)
        {
            this.isToolWindowHost = isToolWindowHost;
            InitializeComponent();
            DataContext = ViewModel;

            ViewModel.Languages.CollectionChanged += Languages_CollectionChanged;

            // 双击 .plang 打开的编辑器（PlangEditorPane）文件是现成的，不需要"新建/打开现有"这一步。
            if (!isToolWindowHost)
                HideStartOverlay();
        }

        /// <summary>工具窗口每次被打开时调用：重新显示「新建 / 打开现有」。</summary>
        public void ShowStartOverlay()
        {
            if (isToolWindowHost && StartOverlay != null)
                StartOverlay.Visibility = Visibility.Visible;
        }

        public void HideStartOverlay()
        {
            if (StartOverlay != null)
                StartOverlay.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// 新建：先让用户定好文件落在哪儿（工具窗口没有 VS 的文档持久化，没有路径就无从保存），
        /// 写一份空文档到磁盘再按普通流程加载——这样保存、代码生成全都走和"打开现有"完全一样的
        /// 那条路径。
        /// </summary>
        void New_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                Title = "New PLang localization file",
                Filter = "PLang File (*.plang)|*.plang|All files (*.*)|*.*",
                DefaultExt = ".plang",
                FileName = "NewLangFile.plang",
                OverwritePrompt = true,
            };

            if (ShowFileDialog(dialog) != true) return;

            try
            {
                // 已存在就直接打开，不用空文档覆盖掉用户现有的内容（SaveFileDialog 的覆盖提示
                // 问的是"要不要选这个文件"，这里理解成"就打开它"比清空它更符合预期）。
                if (!File.Exists(dialog.FileName))
                    new PlangDocument().Save(dialog.FileName);

                LoadFromFile(dialog.FileName);
                HideStartOverlay();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Create failed: {ex.Message}", "PLang Editor", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        void Open_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Open PLang localization file",
                Filter = "PLang File (*.plang)|*.plang|All files (*.*)|*.*",
                DefaultExt = ".plang",
                CheckFileExists = true,
                Multiselect = false,
            };

            if (ShowFileDialog(dialog) != true) return;

            try
            {
                LoadFromFile(dialog.FileName);
                HideStartOverlay();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Open failed: {ex.Message}", "PLang Editor", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 这个控件承载在 ToolWindowPane/WindowPane 里，视觉树上没有真正的 Window 祖先，
        /// 不给 owner 的文件对话框会撞上 Windows 的前台激活保护（窗口建出来了却只在任务栏闪，
        /// 看起来像"点了没反应"）。用承载它的 HwndSource 句柄造一个不可见的占位 Window 当 owner，
        /// EnsureHandle() 就够了，不需要真的 Show()。细节同
        /// <c>PuiVisualEditorControl.ShowFileDialog</c>。
        /// </summary>
        bool? ShowFileDialog(CommonDialog dialog)
        {
            System.Windows.Window owner = CreateDialogOwner();
            return owner != null ? dialog.ShowDialog(owner) : dialog.ShowDialog();
        }

        System.Windows.Window CreateDialogOwner()
        {
            if (PresentationSource.FromVisual(this) is not HwndSource hwndSource)
                return null;

            var owner = new System.Windows.Window
            {
                Width = 0,
                Height = 0,
                WindowStyle = WindowStyle.None,
                ShowInTaskbar = false,
                ShowActivated = false,
            };
            new WindowInteropHelper(owner) { Owner = hwndSource.Handle }.EnsureHandle();
            return owner;
        }

        public void LoadFromFile(string path)
        {
            filePath = path;
            ViewModel.LoadFromFile(path);
            RebuildLanguageColumns();
        }

        /// <summary>
        /// 存盘。存完顺手跑一遍代码生成——"保存"就是唯一的动作，不再需要用户记着另外点一下
        /// "生成类"（Ctrl+S 走 <c>PlangEditorPane.SaveDocData</c> 也会到这儿，两条路一致）。
        /// </summary>
        public void SaveToFile(string path)
        {
            filePath = path;
            ViewModel.SaveToFile(path);

            // 甩到下一个调度轮次再跑：这个方法可能正在 VS 的存盘流程里被调用，别在人家存盘的
            // 半路上再去驱动项目系统。
            Dispatcher.BeginInvoke(new Action(RunCodeGen), DispatcherPriority.Background);
        }

        void RunCodeGen()
        {
            if (string.IsNullOrEmpty(filePath)) return;

            string stamp = DateTime.Now.ToString("HH:mm:ss");
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                bool generated = PlangCodeGenTrigger.RunCustomTool(filePath);
                ViewModel.StatusMessage = generated
                    ? $"Saved and generated code - {stamp}"
                    : $"Saved - {stamp} (the file is not in the current solution, so code generation did not run)";
            }
            catch (Exception ex)
            {
                ViewModel.StatusMessage = $"Saved - {stamp}, but code generation failed: {ex.Message}";
            }
        }

        void AddKey_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new PlangAddKeyDialog(ViewModel.ContainsKey);
            if (dialog.ShowModal() != true) return;

            PlangRowViewModel row = ViewModel.AddKey(dialog.Key, dialog.Comment, dialog.NeutralValue);
            if (row == null) return;

            // 搜索开着而且新行不匹配的话，加完会"看不见"，这时候把搜索清掉更符合预期。
            if (searchText.Length > 0 && !row.Matches(searchText))
            {
                SearchBox.Text = "";
            }

            KeyGrid.SelectedItem = row;
            KeyGrid.CurrentCell = new DataGridCellInfo(row, NeutralColumn);
            KeyGrid.ScrollIntoView(row);
        }

        void RemoveKey_Click(object sender, RoutedEventArgs e)
        {
            List<PlangRowViewModel> selected = KeyGrid.SelectedItems.OfType<PlangRowViewModel>().ToList();
            if (selected.Count == 0)
            {
                ViewModel.StatusMessage = "Select the rows to delete in the grid first";
                return;
            }

            string what = selected.Count == 1 ? $"\"{selected[0].Key}\"" : $"these {selected.Count} keys";
            if (MessageBox.Show($"Delete {what}? The text already filled in for every language will be deleted too.", "PLang Editor",
                                MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK)
                return;

            KeyGrid.CommitEdit(DataGridEditingUnit.Row, true);
            foreach (PlangRowViewModel row in selected)
                ViewModel.RemoveKey(row);
        }

        void Save_Click(object sender, RoutedEventArgs e)
        {
            KeyGrid.CommitEdit(DataGridEditingUnit.Row, true);

            if (string.IsNullOrEmpty(filePath))
            {
                ViewModel.StatusMessage = "There is no file yet -- create a .plang first";
                return;
            }

            try
            {
                SaveToFile(filePath);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Save failed: {ex.Message}", "PLang Editor", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 把选中这一行的全部文案摊到一个窗口里改。按"行"而不是按"当前那一格"：表格是整行选中的，
        /// 当前格在哪儿用户看不见；而且点工具栏那一下焦点会离开表格，按格子走的话目标就丢了。
        /// </summary>
        void EditRow_Click(object sender, RoutedEventArgs e)
        {
            // 正在编辑的那一格先提交，免得对话框读到的是改之前的旧值。
            KeyGrid.CommitEdit(DataGridEditingUnit.Row, true);

            if (KeyGrid.SelectedItem is not PlangRowViewModel row)
            {
                ViewModel.StatusMessage = "Select a row in the grid first, then click \"Edit this row\"";
                return;
            }

            var dialog = new PlangEntryEditorDialog(
                row,
                ViewModel.Languages,
                key => ViewModel.Rows.Any(r => r != row && string.Equals(r.Key, key, StringComparison.Ordinal)));

            if (dialog.ShowModal() != true) return;

            // 对话框里已经实时校验过重名了，这里再挡一次纯属兜底：万一没改成，其余文案照样落地，
            // 不要因为一个 Key 把用户刚敲的一堆译文丢掉。
            if (!string.Equals(row.Key, dialog.Key, StringComparison.Ordinal) && !ViewModel.RenameKey(row, dialog.Key))
            {
                MessageBox.Show($"The key could not be changed to \"{dialog.Key}\" (duplicate or empty); keeping the original \"{row.Key}\". Other changes were saved.",
                                "PLang Editor", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            row.Comment = dialog.Comment;
            row.NeutralValue = dialog.NeutralValue;
            foreach (PlangEntryEditorDialog.LanguageField field in dialog.LanguageValues)
            {
                row[field.Code] = field.Value ?? "";
            }

            KeyGrid.SelectedItem = row;
            KeyGrid.ScrollIntoView(row);
        }

        void AddLanguage_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new PlangLocalePickerDialog(ViewModel.ContainsLanguage);
            if (dialog.ShowModal() != true) return;

            int added = 0;
            foreach ((string code, string name) in dialog.SelectedLocales)
            {
                if (ViewModel.AddLanguage(code, name)) added++;
            }

            ViewModel.StatusMessage = added switch
            {
                0 => "No languages added",
                1 => "Added 1 language",
                _ => $"Added {added} languages",
            };
        }

        void RemoveLanguage_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.Tag is not PlangLanguageViewModel lang) return;

            if (MessageBox.Show(
                    $"Remove language \"{lang.Label}\" ({lang.Code})? This column disappears from the grid and no longer takes part in code generation, " +
                    "but the text already filled in on each row stays in the file -- adding it back with the same code will bring it back.",
                    "PLang Editor", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK)
                return;

            ViewModel.RemoveLanguage(lang);
        }

        void ExportCsv_Click(object sender, RoutedEventArgs e)
        {
            KeyGrid.CommitEdit(DataGridEditingUnit.Row, true);

            var dialog = new SaveFileDialog
            {
                Title = "Export as CSV",
                Filter = "CSV file (*.csv)|*.csv|All files (*.*)|*.*",
                DefaultExt = ".csv",
                FileName = string.IsNullOrEmpty(filePath) ? "plang.csv" : Path.GetFileNameWithoutExtension(filePath) + ".csv",
                OverwritePrompt = true,
            };

            if (ShowFileDialog(dialog) != true) return;

            try
            {
                PlangCsvIo.Export(ViewModel, dialog.FileName);
                ViewModel.StatusMessage = $"Exported to {Path.GetFileName(dialog.FileName)}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Export failed: {ex.Message}", "PLang Editor", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        void ImportCsv_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Import from CSV",
                Filter = "CSV file (*.csv)|*.csv|All files (*.*)|*.*",
                DefaultExt = ".csv",
                CheckFileExists = true,
                Multiselect = false,
            };

            if (ShowFileDialog(dialog) != true) return;

            try
            {
                KeyGrid.CommitEdit(DataGridEditingUnit.Row, true);
                (int added, int updated, int newLanguages) = PlangCsvIo.Import(ViewModel, dialog.FileName);
                ViewModel.StatusMessage = $"Import finished: {added} rows added, {updated} rows updated, {newLanguages} languages added";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Import failed: {ex.Message}", "PLang Editor", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            searchText = SearchBox.Text?.Trim() ?? "";

            // 有单元格正在编辑时改 Filter 会抛（集合视图不允许在编辑事务中改筛选），先提交掉。
            KeyGrid.CommitEdit(DataGridEditingUnit.Row, true);

            ICollectionView view = CollectionViewSource.GetDefaultView(ViewModel.Rows);
            if (view == null) return;

            string text = searchText;
            view.Filter = text.Length == 0 ? null : o => o is PlangRowViewModel row && row.Matches(text);
        }

        void ClearSearch_Click(object sender, RoutedEventArgs e)
        {
            SearchBox.Text = "";
            SearchBox.Focus();
        }

        void Languages_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
            {
                foreach (PlangLanguageViewModel lang in e.OldItems)
                    lang.PropertyChanged -= Language_PropertyChanged;
            }

            if (e.NewItems != null)
            {
                foreach (PlangLanguageViewModel lang in e.NewItems)
                    lang.PropertyChanged += Language_PropertyChanged;
            }

            RebuildLanguageColumns();
        }

        void Language_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(PlangLanguageViewModel.Enabled)
                || e.PropertyName == nameof(PlangLanguageViewModel.DisplayName)
                || e.PropertyName == nameof(PlangLanguageViewModel.Code))
                RebuildLanguageColumns();
        }

        /// <summary>
        /// 按 <see cref="PlangEditorViewModel.Languages"/> 里启用的语言重建 DataGrid 的动态列，
        /// 固定列（Key/说明/中性值）不动。列用索引器绑定（<see cref="PlangRowViewModel.this[string]"/>），
        /// <c>Binding Path="[langCode]"</c> 是 WPF 绑定动态字典型属性的标准写法；显示/编辑外观和
        /// 中性值列共用同一套样式——所有文案都是可换行文本，没有例外。
        /// </summary>
        void RebuildLanguageColumns()
        {
            foreach (DataGridColumn col in languageColumns)
                KeyGrid.Columns.Remove(col);
            languageColumns.Clear();

            foreach (PlangLanguageViewModel lang in ViewModel.Languages.Where(l => l.Enabled && !string.IsNullOrEmpty(l.Code)))
            {
                string code = lang.Code;
                var col = new DataGridTextColumn
                {
                    Header = lang.Label,
                    Binding = new Binding($"[{code}]") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
                    ElementStyle = (Style)FindResource("TextCellStyle"),
                    EditingElementStyle = (Style)FindResource("TextCellEditStyle"),
                    Width = 200,
                };

                KeyGrid.Columns.Add(col);
                languageColumns.Add(col);
            }
        }
    }
}
