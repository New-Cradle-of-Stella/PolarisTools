using System;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace PolarisTools.Map.Editor;

[Guid(GuidString)]
public sealed class PmapEditorFactory : IVsEditorFactory
{
    public const string GuidString = "CB4FA6BB-72E7-4355-8B97-1AF5B80DD346";

    public int SetSite(Microsoft.VisualStudio.OLE.Interop.IServiceProvider site) => VSConstants.S_OK;
    public int Close() => VSConstants.S_OK;

    public int CreateEditorInstance(uint flags, string document, string physicalView, IVsHierarchy hierarchy,
        uint itemId, IntPtr existingDocData, out IntPtr view, out IntPtr docData,
        out string caption, out Guid commandUi, out int createWindowFlags)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        view = IntPtr.Zero;
        docData = IntPtr.Zero;
        caption = string.Empty;
        commandUi = Guid.Empty;
        createWindowFlags = 0;
        if (existingDocData != IntPtr.Zero) return VSConstants.VS_E_INCOMPATIBLEDOCDATA;

        var pane = new PmapEditorPane();
        pane.LoadFile(document);
        view = Marshal.GetIUnknownForObject(pane);
        docData = Marshal.GetIUnknownForObject(pane);
        caption = " [PMap Blueprint]";
        return VSConstants.S_OK;
    }

    public int MapLogicalView(ref Guid logicalView, out string physicalView)
    {
        physicalView = string.Empty;
        return logicalView == VSConstants.LOGVIEWID_Primary || logicalView == VSConstants.LOGVIEWID_Designer
            ? VSConstants.S_OK : VSConstants.E_NOTIMPL;
    }
}
