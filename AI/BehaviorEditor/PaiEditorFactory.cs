using System;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace PolarisTools.AI.BehaviorEditor;

[Guid(GuidString)]
public sealed class PaiEditorFactory : IVsEditorFactory
{
    public const string GuidString = "66C78E71-DB37-4A7F-ACF9-96B6E762B74C";

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

        var pane = new PaiEditorPane();
        pane.LoadFile(document);
        view = Marshal.GetIUnknownForObject(pane);
        docData = Marshal.GetIUnknownForObject(pane);
        caption = " [Polaris AI Tree]";
        commandUi = new Guid(GuidString);
        return VSConstants.S_OK;
    }

    public int MapLogicalView(ref Guid logicalView, out string physicalView)
    {
        physicalView = string.Empty;
        return logicalView == VSConstants.LOGVIEWID_Primary || logicalView == VSConstants.LOGVIEWID_Designer
            ? VSConstants.S_OK : VSConstants.E_NOTIMPL;
    }
}
