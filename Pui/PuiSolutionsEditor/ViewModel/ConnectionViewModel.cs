namespace PolarisTools.Pui.PuiSolutions.ViewModel
{
    public class ConnectionViewModel
    {
        public ConnectionViewModel(ConnectorViewModel source, ConnectorViewModel target, bool removable = true)
        {
            Source = source;
            Target = target;
            Removable = removable;

            Source.IsConnected = true;
            Target.IsConnected = true;
        }

        public bool Removable { get; }
        public ConnectorViewModel Source { get; }
        public ConnectorViewModel Target { get; }
    }
}
