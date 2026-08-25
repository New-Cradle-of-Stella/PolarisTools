using System;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace PolarisTools.AI.BehaviorEditor;

[ComVisible(true)]
[Guid("55ED681A-0A69-47B5-89A4-0AF088A9D10E")]
public sealed class PaiEditorPane : WindowPane, IVsPersistDocData, IPersistFileFormat
{
    readonly PaiEditorControl control = new PaiEditorControl();
    string? path;
    uint cookie;

    public PaiEditorPane() : base(null) { control.DirtyChanged += OnDirtyChanged; }
    public override object Content => control;
    public void LoadFile(string filePath) { ThreadHelper.ThrowIfNotOnUIThread(); path = filePath; control.LoadFile(filePath); }

    public int GetGuidEditorType(out Guid id) { id = new Guid(PaiEditorFactory.GuidString); return VSConstants.S_OK; }
    public int IsDocDataDirty(out int dirty) { dirty = control.IsDirty ? 1 : 0; return VSConstants.S_OK; }
    public int SetUntitledDocPath(string filePath) { path = filePath; return VSConstants.S_OK; }
    public int LoadDocData(string filePath) { ThreadHelper.ThrowIfNotOnUIThread(); LoadFile(filePath); return VSConstants.S_OK; }
    public int SaveDocData(VSSAVEFLAGS flags, out string newPath, out int canceled)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        canceled = 0;
        newPath = path ?? string.Empty;
        if (flags == VSSAVEFLAGS.VSSAVE_SaveAs || flags == VSSAVEFLAGS.VSSAVE_SaveCopyAs || string.IsNullOrEmpty(path))
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Polaris AI Behavior (*.pai)|*.pai",
                DefaultExt = ".pai",
                AddExtension = true,
                FileName = string.IsNullOrEmpty(path) ? "Behavior.pai" : System.IO.Path.GetFileName(path),
            };
            if (dialog.ShowDialog() != true) { canceled = 1; return VSConstants.S_OK; }
            newPath = dialog.FileName;
        }
        if (!control.TrySaveFile(newPath)) return VSConstants.E_FAIL;
        if (flags != VSSAVEFLAGS.VSSAVE_SaveCopyAs) path = newPath;
        NotifyDirty();
        return VSConstants.S_OK;
    }
    public int Close() { cookie = 0; return VSConstants.S_OK; }
    public int OnRegisterDocData(uint docCookie, IVsHierarchy hierarchy, uint itemId) { cookie = docCookie; return VSConstants.S_OK; }
    public int RenameDocData(uint flags, IVsHierarchy hierarchy, uint itemId, string newPath) { path = newPath; return VSConstants.S_OK; }
    public int IsDocDataReloadable(out int reloadable) { reloadable = 1; return VSConstants.S_OK; }
    public int ReloadDocData(uint flags) { ThreadHelper.ThrowIfNotOnUIThread(); if (path != null) LoadFile(path); return VSConstants.S_OK; }
    public int GetClassID(out Guid id) { id = new Guid(PaiEditorFactory.GuidString); return VSConstants.S_OK; }
    public int IsDirty(out int dirty) { dirty = control.IsDirty ? 1 : 0; return VSConstants.S_OK; }
    public int InitNew(uint format) => VSConstants.S_OK;
    public int Load(string filePath, uint mode, int readOnly) { ThreadHelper.ThrowIfNotOnUIThread(); LoadFile(filePath); return VSConstants.S_OK; }
    public int Save(string filePath, int remember, uint format)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        string target = string.IsNullOrWhiteSpace(filePath) ? path ?? string.Empty : filePath;
        if (!control.TrySaveFile(target)) return VSConstants.E_FAIL;
        if (remember != 0) path = target;
        NotifyDirty();
        return VSConstants.S_OK;
    }
    public int SaveCompleted(string filePath) => VSConstants.S_OK;
    public int GetCurFile(out string filePath, out uint format) { filePath = path ?? string.Empty; format = 0; return VSConstants.S_OK; }
    public int GetFormatList(out string formats) { formats = "Polaris AI Behavior (*.pai)\n*.pai\n"; return VSConstants.S_OK; }

    void OnDirtyChanged(object sender, EventArgs args)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        NotifyDirty();
    }

    void NotifyDirty()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (cookie == 0) return;
        var rdt = GetService(typeof(SVsRunningDocumentTable)) as IVsRunningDocumentTable;
        uint attribute = control.IsDirty ? (uint)__VSRDTATTRIB.RDTA_DocDataIsDirty : (uint)__VSRDTATTRIB.RDTA_DocDataIsNotDirty;
        rdt?.NotifyDocumentChanged(cookie, attribute);
    }
}
