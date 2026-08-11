using CommunityToolkit.Mvvm.Input;
using System.Windows.Input;

namespace PolarisTools.Pui.PuiSolutions.ViewModel
{
    public class PendingConnectionViewModel
    {
        private readonly EditorViewModel _editor;
        private ConnectorViewModel _source;

        public PendingConnectionViewModel(EditorViewModel editor)
        {
            _editor = editor;

            StartCommand = new RelayCommand<object>(param =>
            {
                _source = param as ConnectorViewModel;
            });

            FinishCommand = new RelayCommand<object>(param =>
            {
                var target = param as ConnectorViewModel;
                if (_source == null || target == null)
                    return;

                _editor.TryConnect(_source, target);
                _source = null;
            });
        }

        public ICommand StartCommand { get; }
        public ICommand FinishCommand { get; }
    }
}
