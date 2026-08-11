using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Polaris.Lang;

namespace PolarisTools.Lang
{
    /// <summary>一份 .plang 支持的语言：代码 + 显示名 + 启用开关（决定是否在表格里出现一列/参与生成）。</summary>
    public partial class PlangLanguageViewModel : ObservableObject
    {
        [ObservableProperty][NotifyPropertyChangedFor(nameof(Label))] private string code = "";
        [ObservableProperty][NotifyPropertyChangedFor(nameof(Label))] private string displayName = "";
        [ObservableProperty] private bool enabled = true;

        /// <summary>这门语言下还没填文案的 Key 数量，由 <see cref="PlangEditorViewModel.RefreshLanguageStats"/> 统一刷新，语言条上显示成一个小徽标。</summary>
        [ObservableProperty][NotifyPropertyChangedFor(nameof(HasMissing))] private int missingCount;

        /// <summary>语言条上显示的名字：没填显示名就退回代码，不留空白 chip。</summary>
        public string Label => string.IsNullOrWhiteSpace(DisplayName) ? Code : DisplayName;

        public bool HasMissing => MissingCount > 0;
    }

    /// <summary>.plang 表格编辑器的数据模型：一份 .plang 文件对应一张表，行 = Key，语言列按 <see cref="Languages"/> 动态出现。</summary>
    public partial class PlangEditorViewModel : ObservableObject
    {
        [ObservableProperty] private bool isDirty;

        /// <summary>状态栏右侧那行提示（"已保存并生成代码"之类），纯展示用，不参与存盘。</summary>
        [ObservableProperty] private string statusMessage = "";

        /// <summary>加载/批量导入期间挂起统计与脏标记，省得一行一行地重算、也别把"刚打开"算成已修改。</summary>
        bool suspendTracking;

        public PlangEditorViewModel()
        {
            Rows.CollectionChanged += Rows_CollectionChanged;
            Languages.CollectionChanged += Languages_CollectionChanged;
        }

        public ObservableCollection<PlangRowViewModel> Rows { get; } = new();

        public ObservableCollection<PlangLanguageViewModel> Languages { get; } = new();

        /// <summary>状态栏/空状态提示用。</summary>
        public int RowCount => Rows.Count;

        public bool IsEmpty => Rows.Count == 0;

        public int EnabledLanguageCount => Languages.Count(l => l.Enabled && !string.IsNullOrEmpty(l.Code));

        public void LoadFromFile(string path)
        {
            suspendTracking = true;
            try
            {
                Rows.Clear();
                Languages.Clear();

                PlangDocument doc = SafeLoad(path);

                foreach (PlangLanguage lang in doc.Languages)
                {
                    Languages.Add(new PlangLanguageViewModel { Code = lang.Code, DisplayName = lang.DisplayName, Enabled = lang.Enabled });
                }

                foreach (PlangEntry e in doc.Entries)
                {
                    if (string.IsNullOrEmpty(e.Key)) continue;
                    var row = new PlangRowViewModel { Key = e.Key, Comment = e.Comment, NeutralValue = e.NeutralValue ?? "" };
                    row.ReplaceLanguageValues(e.Values);
                    Rows.Add(row);
                }
            }
            finally
            {
                suspendTracking = false;
            }

            RefreshCounts();
            IsDirty = false;
        }

        public void SaveToFile(string path)
        {
            var doc = new PlangDocument();

            foreach (PlangLanguageViewModel lang in Languages)
            {
                if (string.IsNullOrWhiteSpace(lang.Code)) continue;
                doc.Languages.Add(new PlangLanguage { Code = lang.Code, DisplayName = lang.DisplayName, Enabled = lang.Enabled });
            }

            foreach (PlangRowViewModel row in Rows)
            {
                if (string.IsNullOrEmpty(row.Key)) continue;
                var entry = new PlangEntry(row.Key, row.NeutralValue, row.Comment);
                foreach (var kv in row.LanguageValues)
                {
                    entry.Values[kv.Key] = kv.Value;
                }
                doc.Entries.Add(entry);
            }

            doc.Save(path);

            IsDirty = false;
        }

        /// <summary>新增一个 Key（可以顺带带上说明和中性值，都是「新增 Key」对话框里一次填好的）。</summary>
        public PlangRowViewModel AddKey(string key, string comment = null, string neutralValue = null)
        {
            if (string.IsNullOrWhiteSpace(key) || ContainsKey(key)) return null;

            var row = new PlangRowViewModel
            {
                Key = key,
                Comment = string.IsNullOrWhiteSpace(comment) ? null : comment,
                NeutralValue = neutralValue ?? "",
            };
            Rows.Add(row);
            IsDirty = true;
            return row;
        }

        public bool ContainsKey(string key) => Rows.Any(r => string.Equals(r.Key, key, StringComparison.Ordinal));

        public void RemoveKey(PlangRowViewModel row)
        {
            if (Rows.Remove(row)) IsDirty = true;
        }

        public bool RenameKey(PlangRowViewModel row, string newKey)
        {
            if (string.IsNullOrWhiteSpace(newKey) || Rows.Any(r => r != row && r.Key == newKey)) return false;
            row.Key = newKey;
            IsDirty = true;
            return true;
        }

        /// <summary>新增一种语言（重复代码不区分大小写去重）；新语言默认启用。</summary>
        public bool AddLanguage(string code, string displayName)
        {
            code = code?.Trim();
            if (string.IsNullOrEmpty(code) || ContainsLanguage(code))
                return false;

            Languages.Add(new PlangLanguageViewModel
            {
                Code = code,
                DisplayName = string.IsNullOrWhiteSpace(displayName) ? code : displayName,
                Enabled = true,
            });
            IsDirty = true;
            return true;
        }

        public bool ContainsLanguage(string code) =>
            !string.IsNullOrEmpty(code) && Languages.Any(l => string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// 移除一种语言：只从语言列表里摘掉（表格里的那一列消失、不再参与生成），已经写过的
        /// 各行文案原样留在 <see cref="PlangRowViewModel.LanguageValues"/> 里不删——万一是手滑删的，
        /// 重新用同样的代码 <see cref="AddLanguage"/> 能找回来。
        /// </summary>
        public void RemoveLanguage(PlangLanguageViewModel language)
        {
            if (Languages.Remove(language)) IsDirty = true;
        }

        /// <summary>CSV 导入这种批量改动用：期间不逐行重算统计，结束后统一刷一次。</summary>
        public void BeginBatch() => suspendTracking = true;

        public void EndBatch()
        {
            suspendTracking = false;
            RefreshCounts();
        }

        /// <summary>按各行文案重算"每门语言还差几条没填"，语言条上的徽标读这个。</summary>
        public void RefreshLanguageStats()
        {
            foreach (PlangLanguageViewModel lang in Languages)
            {
                if (string.IsNullOrEmpty(lang.Code))
                {
                    lang.MissingCount = 0;
                    continue;
                }

                lang.MissingCount = Rows.Count(r => string.IsNullOrWhiteSpace(r[lang.Code]));
            }
        }

        void RefreshCounts()
        {
            RefreshLanguageStats();
            OnPropertyChanged(nameof(RowCount));
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(EnabledLanguageCount));
        }

        void Rows_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
            {
                foreach (PlangRowViewModel row in e.OldItems)
                    row.PropertyChanged -= Row_PropertyChanged;
            }

            if (e.NewItems != null)
            {
                foreach (PlangRowViewModel row in e.NewItems)
                    row.PropertyChanged += Row_PropertyChanged;
            }

            if (suspendTracking) return;
            RefreshCounts();
        }

        // 表格里直接改单元格也算改动：没有这一条，编辑完文案 Ctrl+S 会被 VS 当成"没脏、不用存"。
        void Row_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (suspendTracking) return;
            IsDirty = true;
            RefreshLanguageStats();
        }

        void Languages_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
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

            if (suspendTracking) return;
            RefreshCounts();
        }

        void Language_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            // MissingCount 是我们自己算出来的派生量，别让它反过来把文档标成已修改。
            if (suspendTracking
                || e.PropertyName == nameof(PlangLanguageViewModel.MissingCount)
                || e.PropertyName == nameof(PlangLanguageViewModel.HasMissing)
                || e.PropertyName == nameof(PlangLanguageViewModel.Label))
                return;

            IsDirty = true;
            OnPropertyChanged(nameof(EnabledLanguageCount));
        }

        static PlangDocument SafeLoad(string path)
        {
            try { return PlangDocument.Load(path); }
            catch { return new PlangDocument(); }
        }
    }
}
