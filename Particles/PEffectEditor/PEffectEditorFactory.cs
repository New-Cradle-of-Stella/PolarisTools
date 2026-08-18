using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.Runtime.InteropServices;

namespace PolarisTools.Particles.PEffectEditor;

[Guid(GuidString)]
public sealed class PEffectEditorFactory : IVsEditorFactory
{
    public const string GuidString = "E627EB1B-704B-4BD9-B3EB-0847565D22CD";

    public int SetSite(Microsoft.VisualStudio.OLE.Interop.IServiceProvider psp) => VSConstants.S_OK;
    public int Close() => VSConstants.S_OK;

    public int CreateEditorInstance(uint grfCreateDoc, string pszMkDocument, string pszPhysicalView,
        IVsHierarchy pvHier, uint itemid, IntPtr punkDocDataExisting, out IntPtr ppunkDocView,
        out IntPtr ppunkDocData, out string pbstrEditorCaption, out Guid pguidCmdUI, out int pgrfCDW)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        ppunkDocView = IntPtr.Zero;
        ppunkDocData = IntPtr.Zero;
        pbstrEditorCaption = string.Empty;
        pguidCmdUI = Guid.Empty;
        pgrfCDW = 0;
        if (punkDocDataExisting != IntPtr.Zero)
            return VSConstants.VS_E_INCOMPATIBLEDOCDATA;

        var pane = new PEffectEditorPane();
        pane.LoadFile(pszMkDocument);
        ppunkDocView = Marshal.GetIUnknownForObject(pane);
        ppunkDocData = Marshal.GetIUnknownForObject(pane);
        pbstrEditorCaption = " [PEffect Editor]";
        return VSConstants.S_OK;
    }

    public int MapLogicalView(ref Guid rguidLogicalView, out string pbstrPhysicalView)
    {
        pbstrPhysicalView = string.Empty;
        return rguidLogicalView == VSConstants.LOGVIEWID_Primary || rguidLogicalView == VSConstants.LOGVIEWID_Designer
            ? VSConstants.S_OK : VSConstants.E_NOTIMPL;
    }
}
