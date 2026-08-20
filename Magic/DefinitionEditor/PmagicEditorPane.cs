using System;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace PolarisTools.Magic.DefinitionEditor;

/// <summary>
/// <c>.pmagic</c> 编辑器的文档窗格。自己持有文档缓冲区并负责读写、脏状态与重载。
/// </summary>
[ComVisible(true)]
[Guid("6f2c9a41-4d8b-4a1e-9c4f-2f5a7b0d1e63")]
public sealed class PmagicEditorPane : WindowPane, IVsPersistDocData, IPersistFileFormat
{
    private const uint FileFormat = 0;

    private readonly PmagicEditorControl control;
    private string filePath = string.Empty;

    public PmagicEditorPane() : base(null)
    {
        control = new PmagicEditorControl();
        control.ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PmagicEditorViewModel.IsDirty))
            {
                isDirty = control.ViewModel.IsDirty;
            }
        };
    }

    private bool isDirty;

    public override object Content => control;

    public void LoadFile(string path)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        filePath = path;
        control.LoadFromFile(path);
    }

    #region IVsPersistDocData

    public int GetGuidEditorType(out Guid pClassID)
    {
        pClassID = new Guid(PmagicEditorFactory.GuidString);
        return VSConstants.S_OK;
    }

    public int IsDocDataDirty(out int pfDirty)
    {
        pfDirty = isDirty ? 1 : 0;
        return VSConstants.S_OK;
    }

    public int SetUntitledDocPath(string pszDocDataPath) => VSConstants.S_OK;

    public int LoadDocData(string pszMkDocument)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        LoadFile(pszMkDocument);
        return VSConstants.S_OK;
    }

    public int SaveDocData(VSSAVEFLAGS dwSave, out string pbstrMkDocumentNew, out int pfSaveCanceled)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        pbstrMkDocumentNew = filePath;
        pfSaveCanceled = 0;
        control.SaveToFile(filePath);
        isDirty = false;
        return VSConstants.S_OK;
    }

    public int Close() => VSConstants.S_OK;

    public int OnRegisterDocData(uint docCookie, IVsHierarchy pHierNew, uint itemidNew) => VSConstants.S_OK;

    public int RenameDocData(uint grfAttribs, IVsHierarchy pHierNew, uint itemidNew, string pszMkDocumentNew)
    {
        filePath = pszMkDocumentNew;
        return VSConstants.S_OK;
    }

    public int IsDocDataReloadable(out int pfReloadable)
    {
        pfReloadable = 1;
        return VSConstants.S_OK;
    }

    public int ReloadDocData(uint grfFlags)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        LoadFile(filePath);
        return VSConstants.S_OK;
    }

    #endregion

    #region IPersistFileFormat

    public int GetClassID(out Guid pClassID)
    {
        pClassID = new Guid(PmagicEditorFactory.GuidString);
        return VSConstants.S_OK;
    }

    public int IsDirty(out int pfIsDirty)
    {
        pfIsDirty = isDirty ? 1 : 0;
        return VSConstants.S_OK;
    }

    public int InitNew(uint nFormatIndex) => VSConstants.S_OK;

    public int Load(string pszFilename, uint grfMode, int fReadOnly)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        LoadFile(pszFilename);
        return VSConstants.S_OK;
    }

    public int Save(string pszFilename, int fRemember, uint nFormatIndex)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        string path = string.IsNullOrEmpty(pszFilename) ? filePath : pszFilename;
        control.SaveToFile(path);

        if (fRemember != 0)
        {
            filePath = path;
            isDirty = false;
        }

        return VSConstants.S_OK;
    }

    public int SaveCompleted(string pszFilename) => VSConstants.S_OK;

    public int GetCurFile(out string ppszFilename, out uint pnFormatIndex)
    {
        ppszFilename = filePath;
        pnFormatIndex = FileFormat;
        return VSConstants.S_OK;
    }

    public int GetFormatList(out string ppszFormatList)
    {
        ppszFormatList = "Polaris Magic Definition (*.pmagic)\n*.pmagic\n";
        return VSConstants.S_OK;
    }

    #endregion
}
