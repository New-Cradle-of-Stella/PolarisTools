using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace PolarisTools.Addons.DefinitionEditor;

[Guid(GuidString)]
public sealed class AddonDefinitionEditorFactory : IVsEditorFactory
{
    public const string GuidString = "0c33ae39-6344-45fa-88aa-e44bd14a2f01";

    public int SetSite(Microsoft.VisualStudio.OLE.Interop.IServiceProvider serviceProvider) => VSConstants.S_OK;

    public int Close() => VSConstants.S_OK;

    public int CreateEditorInstance(
        uint createFlags,
        string documentPath,
        string physicalView,
        IVsHierarchy hierarchy,
        uint itemId,
        IntPtr existingDocData,
        out IntPtr docView,
        out IntPtr docData,
        out string editorCaption,
        out Guid commandUi,
        out int createDocumentWindowFlags)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        docView = IntPtr.Zero;
        docData = IntPtr.Zero;
        editorCaption = string.Empty;
        commandUi = Guid.Empty;
        createDocumentWindowFlags = 0;

        if (existingDocData != IntPtr.Zero)
        {
            return VSConstants.VS_E_INCOMPATIBLEDOCDATA;
        }

        var pane = new AddonDefinitionEditorPane();
        pane.LoadFile(documentPath);
        docView = Marshal.GetIUnknownForObject(pane);
        docData = Marshal.GetIUnknownForObject(pane);
        editorCaption = " [Polaris " + Path.GetExtension(documentPath).TrimStart('.') + "]";
        return VSConstants.S_OK;
    }

    public int MapLogicalView(ref Guid logicalView, out string physicalView)
    {
        physicalView = string.Empty;
        return logicalView == VSConstants.LOGVIEWID_Primary || logicalView == VSConstants.LOGVIEWID_Designer
            ? VSConstants.S_OK
            : VSConstants.E_NOTIMPL;
    }
}
