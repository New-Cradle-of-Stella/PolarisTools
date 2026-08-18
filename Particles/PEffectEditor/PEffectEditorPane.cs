using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.Runtime.InteropServices;

namespace PolarisTools.Particles.PEffectEditor;

[ComVisible(true)]
[Guid("1E1D9DE8-1F93-4B92-9B45-ECD8B4FB8D2A")]
public sealed class PEffectEditorPane : WindowPane, IVsPersistDocData, IPersistFileFormat
{
    private readonly PEffectEditorControl _control = new PEffectEditorControl();
    private string? _filePath;
    private const uint FileFormat = 0;

    public PEffectEditorPane() : base(null) { }

    public override object Content => _control;

    public void LoadFile(string path)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        _filePath = path;
        _control.LoadFile(path);
    }

    public int GetGuidEditorType(out Guid pClassID) { pClassID = new Guid(PEffectEditorFactory.GuidString); return VSConstants.S_OK; }
    public int IsDocDataDirty(out int pfDirty) { pfDirty = _control.IsDirty ? 1 : 0; return VSConstants.S_OK; }
    public int SetUntitledDocPath(string pszDocDataPath) => VSConstants.S_OK;
    public int LoadDocData(string pszMkDocument) { ThreadHelper.ThrowIfNotOnUIThread(); LoadFile(pszMkDocument); return VSConstants.S_OK; }
    public int SaveDocData(VSSAVEFLAGS dwSave, out string pbstrMkDocumentNew, out int pfSaveCanceled)
    {
        pbstrMkDocumentNew = _filePath ?? string.Empty;
        pfSaveCanceled = 0;
        _control.SaveFile(_filePath);
        return VSConstants.S_OK;
    }
    public int Close() => VSConstants.S_OK;
    public int OnRegisterDocData(uint docCookie, IVsHierarchy pHierNew, uint itemidNew) => VSConstants.S_OK;
    public int RenameDocData(uint grfAttribs, IVsHierarchy pHierNew, uint itemidNew, string pszMkDocumentNew) { _filePath = pszMkDocumentNew; return VSConstants.S_OK; }
    public int IsDocDataReloadable(out int pfReloadable) { pfReloadable = 1; return VSConstants.S_OK; }
    public int ReloadDocData(uint grfFlags) { ThreadHelper.ThrowIfNotOnUIThread(); if (_filePath != null) LoadFile(_filePath); return VSConstants.S_OK; }

    public int GetClassID(out Guid pClassID) { pClassID = new Guid(PEffectEditorFactory.GuidString); return VSConstants.S_OK; }
    public int IsDirty(out int pfIsDirty) { pfIsDirty = _control.IsDirty ? 1 : 0; return VSConstants.S_OK; }
    public int InitNew(uint nFormatIndex) => VSConstants.S_OK;
    public int Load(string pszFilename, uint grfMode, int fReadOnly) { ThreadHelper.ThrowIfNotOnUIThread(); LoadFile(pszFilename); return VSConstants.S_OK; }
    public int Save(string pszFilename, int fRemember, uint nFormatIndex)
    {
        string path = string.IsNullOrWhiteSpace(pszFilename) ? _filePath ?? string.Empty : pszFilename;
        _control.SaveFile(path);
        if (fRemember != 0) _filePath = path;
        return VSConstants.S_OK;
    }
    public int SaveCompleted(string pszFilename) => VSConstants.S_OK;
    public int GetCurFile(out string ppszFilename, out uint pnFormatIndex) { ppszFilename = _filePath ?? string.Empty; pnFormatIndex = FileFormat; return VSConstants.S_OK; }
    public int GetFormatList(out string ppszFormatList) { ppszFormatList = "PEffect File (*.peffect)\n*.peffect\n"; return VSConstants.S_OK; }
}
