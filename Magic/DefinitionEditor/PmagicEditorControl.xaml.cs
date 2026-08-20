using Microsoft.VisualStudio.Shell;
using System.Windows;
using System.Windows.Controls;

namespace PolarisTools.Magic.DefinitionEditor;

/// <summary>
/// <c>.pmagic</c> 定义编辑器的界面。单页两栏：左边基本属性、右边自定义静态属性表。
///
/// 交互全部落在 ViewModel 上，这里只做"哪一行"的取值和一次提示刷新：
/// 增删行之后立刻重算底部提示，作者不用先存盘才知道哪里还没填。
/// </summary>
public partial class PmagicEditorControl : UserControl
{
    public PmagicEditorControl()
    {
        InitializeComponent();
        DataContext = ViewModel;
    }

    public PmagicEditorViewModel ViewModel { get; } = new PmagicEditorViewModel();

    /// <summary>当前文件路径。诊断要按文件写进 Error List，所以编辑器得知道自己是谁。</summary>
    public string FilePath { get; private set; } = string.Empty;

    public void LoadFromFile(string path)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        FilePath = path;
        ViewModel.LoadFromFile(path);
    }

    public void SaveToFile(string path)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        FilePath = path;
        ViewModel.SaveToFile(path);
    }

    private void OnAdd(object sender, RoutedEventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        ViewModel.AddProperty();
        RefreshHint();
    }

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        ViewModel.RemoveProperty((sender as FrameworkElement)?.DataContext as MagicPropertyRowViewModel);
        RefreshHint();
    }

    private void OnMoveUp(object sender, RoutedEventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        Move(-1);
    }

    private void OnMoveDown(object sender, RoutedEventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        Move(1);
    }

    private void Move(int delta)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (PropertyGrid.SelectedItem is MagicPropertyRowViewModel row)
        {
            ViewModel.MoveProperty(row, delta);
            RefreshHint();
        }
    }

    private void RefreshHint() => ViewModel.RefreshHint();
}
