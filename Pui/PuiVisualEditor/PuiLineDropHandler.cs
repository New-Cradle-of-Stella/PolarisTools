using GongSolutions.Wpf.DragDrop;
using System.Windows;

namespace PolarisTools.Pui.PuiVisualEditor
{
    public class PuiLineDropHandler : DefaultDropHandler
    {
        private readonly PuiVisualEditorViewModel _viewModel;

        public PuiLineDropHandler(PuiVisualEditorViewModel viewModel)
        {
            _viewModel = viewModel;
        }

        public override void DragOver(IDropInfo dropInfo)
        {
            if (dropInfo.DragInfo?.SourceItem is PuiLineInfo && dropInfo.TargetItem is PuiLineInfo)
            {
                dropInfo.Effects = DragDropEffects.Move;
                dropInfo.DropTargetAdorner = DropTargetAdorners.Insert;
            }
        }

        public override void Drop(IDropInfo dropInfo)
        {
            if (dropInfo.DragInfo?.SourceItem is PuiLineInfo source)
                _viewModel.MoveLine(source, dropInfo.InsertIndex);
        }
    }
}
