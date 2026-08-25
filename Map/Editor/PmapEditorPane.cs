using System;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace PolarisTools.Map.Editor;

[ComVisible(true)]
[Guid("D887C233-10B2-4A9F-B260-B1254980754D")]
public sealed class PmapEditorPane : WindowPane, IVsPersistDocData, IPersistFileFormat
{
    private readonly PmapEditorControl _control = new PmapEditorControl();
    private string? _path;
    private const uint Format = 0;

    public PmapEditorPane() : base(null) { }
    public override object Content => _control;

    public void LoadFile(string path) { ThreadHelper.ThrowIfNotOnUIThread(); _path = path; _control.LoadFile(path); }
    public int GetGuidEditorType(out Guid id) { id = new Guid(PmapEditorFactory.GuidString); return VSConstants.S_OK; }
    public int IsDocDataDirty(out int dirty) { dirty = _control.IsDirty ? 1 : 0; return VSConstants.S_OK; }
    public int SetUntitledDocPath(string path) => VSConstants.S_OK;
    public int LoadDocData(string path) { LoadFile(path); return VSConstants.S_OK; }
    public int SaveDocData(VSSAVEFLAGS flags, out string newPath, out int canceled) { newPath = _path ?? ""; canceled = 0; _control.SaveFile(_path); return VSConstants.S_OK; }
    public int Close() => VSConstants.S_OK;
    public int OnRegisterDocData(uint cookie, IVsHierarchy hierarchy, uint itemId) => VSConstants.S_OK;
    public int RenameDocData(uint flags, IVsHierarchy hierarchy, uint itemId, string path) { _path = path; return VSConstants.S_OK; }
    public int IsDocDataReloadable(out int reloadable) { reloadable = 1; return VSConstants.S_OK; }
    public int ReloadDocData(uint flags) { if (_path != null) LoadFile(_path); return VSConstants.S_OK; }
    public int GetClassID(out Guid id) { id = new Guid(PmapEditorFactory.GuidString); return VSConstants.S_OK; }
    public int IsDirty(out int dirty) { dirty = _control.IsDirty ? 1 : 0; return VSConstants.S_OK; }
    public int InitNew(uint format) => VSConstants.S_OK;
    public int Load(string path, uint mode, int readOnly) { LoadFile(path); return VSConstants.S_OK; }
    public int Save(string path, int remember, uint format) { string target = string.IsNullOrWhiteSpace(path) ? _path ?? "" : path; _control.SaveFile(target); if (remember != 0) _path = target; return VSConstants.S_OK; }
    public int SaveCompleted(string path) => VSConstants.S_OK;
    public int GetCurFile(out string path, out uint format) { path = _path ?? ""; format = Format; return VSConstants.S_OK; }
    public int GetFormatList(out string formats) { formats = "Polaris Map (*.pmap)\n*.pmap\n"; return VSConstants.S_OK; }
}
