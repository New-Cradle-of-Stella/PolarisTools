using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.Runtime.InteropServices;

namespace PolarisTools.Pui.PuiVisualEditor
{
    [Guid(GuidString)]
    public class PuiEditorFactory : IVsEditorFactory
    {
        public const string GuidString = "A1B2C3D4-E5F6-4A5B-8C9D-0E1F2A3B4C5D";
        private ServiceProvider _serviceProvider;

        public int SetSite(Microsoft.VisualStudio.OLE.Interop.IServiceProvider psp)
        {
            _serviceProvider = new ServiceProvider(psp);
            return VSConstants.S_OK;
        }

        public int Close() => VSConstants.S_OK;

        public int CreateEditorInstance(
            uint grfCreateDoc,
            string pszMkDocument,
            string pszPhysicalView,
            IVsHierarchy pvHier,
            uint itemid,
            IntPtr punkDocDataExisting,
            out IntPtr ppunkDocView,
            out IntPtr ppunkDocData,
            out string pbstrEditorCaption,
            out Guid pguidCmdUI,
            out int pgrfCDW)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            ppunkDocView = IntPtr.Zero;
            ppunkDocData = IntPtr.Zero;
            pbstrEditorCaption = "";
            pguidCmdUI = Guid.Empty;
            pgrfCDW = 0;

            if (punkDocDataExisting != IntPtr.Zero)
                return VSConstants.VS_E_INCOMPATIBLEDOCDATA;

            var pane = new PuiEditorPane();
            pane.LoadFile(pszMkDocument);

            ppunkDocView = Marshal.GetIUnknownForObject(pane);
            ppunkDocData = Marshal.GetIUnknownForObject(pane);
            pbstrEditorCaption = " [PUI Visual Editor]";

            return VSConstants.S_OK;
        }

        public int MapLogicalView(ref Guid rguidLogicalView, out string pbstrPhysicalView)
        {
            pbstrPhysicalView = null;
            if (rguidLogicalView == VSConstants.LOGVIEWID_Primary ||
                rguidLogicalView == VSConstants.LOGVIEWID_Designer)
                return VSConstants.S_OK;
            return VSConstants.E_NOTIMPL;
        }
    }
}
