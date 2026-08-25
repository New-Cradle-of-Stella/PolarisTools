using System.Windows.Controls;

namespace PolarisTools.Addons.DefinitionEditor;

public partial class AddonDefinitionEditorControl : UserControl
{
    public AddonDefinitionEditorControl()
    {
        InitializeComponent();
        DataContext = ViewModel;
    }

    public AddonDefinitionEditorViewModel ViewModel { get; } = new AddonDefinitionEditorViewModel();

    public void LoadFromFile(string path) => ViewModel.LoadFromFile(path);

    public void SaveToFile(string path) => ViewModel.SaveToFile(path);
}
