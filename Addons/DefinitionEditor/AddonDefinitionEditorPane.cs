using System;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace PolarisTools.Addons.DefinitionEditor;

[ComVisible(true)]
[Guid("6abf6d5a-e1f4-4507-beba-ae8e8b73bdfa")]
public sealed class AddonDefinitionEditorPane : WindowPane, IVsPersistDocData, IPersistFileFormat
{
    private const uint FileFormat = 0;
    private readonly AddonDefinitionEditorControl control;
    private string filePath = string.Empty;
    private bool isDirty;

    public AddonDefinitionEditorPane() : base(null)
    {
        control = new AddonDefinitionEditorControl();
        control.ViewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(AddonDefinitionEditorViewModel.IsDirty))
            {
                isDirty = control.ViewModel.IsDirty;
            }
        };
    }

    public override object Content => control;

    public void LoadFile(string path)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        filePath = path;
        control.LoadFromFile(path);
    }

    public int GetGuidEditorType(out Guid classId)
    {
        classId = new Guid(AddonDefinitionEditorFactory.GuidString);
        return VSConstants.S_OK;
    }

    public int IsDocDataDirty(out int dirty)
    {
        dirty = isDirty ? 1 : 0;
        return VSConstants.S_OK;
    }

    public int SetUntitledDocPath(string path) => VSConstants.S_OK;

    public int LoadDocData(string documentPath)
    {
        LoadFile(documentPath);
        return VSConstants.S_OK;
    }

    public int SaveDocData(VSSAVEFLAGS flags, out string newPath, out int saveCancelled)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        newPath = filePath;
        saveCancelled = 0;
        control.SaveToFile(filePath);
        isDirty = false;
        return VSConstants.S_OK;
    }

    public int Close() => VSConstants.S_OK;
    public int OnRegisterDocData(uint cookie, IVsHierarchy hierarchy, uint itemId) => VSConstants.S_OK;

    public int RenameDocData(uint attributes, IVsHierarchy hierarchy, uint itemId, string newPath)
    {
        filePath = newPath;
        return VSConstants.S_OK;
    }

    public int IsDocDataReloadable(out int reloadable)
    {
        reloadable = 1;
        return VSConstants.S_OK;
    }

    public int ReloadDocData(uint flags)
    {
        LoadFile(filePath);
        return VSConstants.S_OK;
    }

    public int GetClassID(out Guid classId)
    {
        classId = new Guid(AddonDefinitionEditorFactory.GuidString);
        return VSConstants.S_OK;
    }

    public int IsDirty(out int dirty)
    {
        dirty = isDirty ? 1 : 0;
        return VSConstants.S_OK;
    }
    public int InitNew(uint formatIndex) => VSConstants.S_OK;
    public int GetCurFile(out string fileName, out uint formatIndex)
    {
        fileName = filePath;
        formatIndex = FileFormat;
        return VSConstants.S_OK;
    }

    public int Load(string fileName, uint mode, int readOnly)
    {
        LoadFile(fileName);
        return VSConstants.S_OK;
    }

    public int Save(string fileName, int remember, uint formatIndex)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        control.SaveToFile(fileName);
        if (remember != 0) filePath = fileName;
        isDirty = false;
        return VSConstants.S_OK;
    }

    public int SaveCompleted(string fileName) => VSConstants.S_OK;
    public int GetFormatList(out string formatList)
    {
        formatList = "Polaris Addons Definition (*.pitem;*.pplugin;*.pskill)\n*.pitem;*.pplugin;*.pskill\n";
        return VSConstants.S_OK;
    }
}
