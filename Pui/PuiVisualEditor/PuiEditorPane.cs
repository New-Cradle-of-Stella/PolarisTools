using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.Runtime.InteropServices;

namespace PolarisTools.Pui.PuiVisualEditor
{
    [ComVisible(true)]
    [Guid("B2C3D4E5-F6A7-4B5C-9D0E-1F2A3B4C5D6E")]
    public class PuiEditorPane : WindowPane, IVsPersistDocData, IPersistFileFormat
    {
        private readonly PuiVisualEditorControl _control;
        private string _filePath;
        private bool _isDirty;
        private const uint FileFormat = 0;

        public PuiEditorPane() : base(null)
        {
            _control = new PuiVisualEditorControl();
            _control.ViewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(PuiVisualEditorViewModel.IsDirty))
                    _isDirty = _control.ViewModel.IsDirty;
            };
        }

        public override object Content => _control;

        public void LoadFile(string path)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _filePath = path;
            _control.LoadFromFile(path);
        }

        #region IVsPersistDocData

        public int GetGuidEditorType(out Guid pClassID)
        {
            pClassID = new Guid(PuiEditorFactory.GuidString);
            return VSConstants.S_OK;
        }

        public int IsDocDataDirty(out int pfDirty)
        {
            pfDirty = _isDirty ? 1 : 0;
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
            pbstrMkDocumentNew = _filePath;
            pfSaveCanceled = 0;
            _control.SaveToFile(_filePath);
            _isDirty = false;
            return VSConstants.S_OK;
        }

        public int Close() => VSConstants.S_OK;

        public int OnRegisterDocData(uint docCookie, IVsHierarchy pHierNew, uint itemidNew) => VSConstants.S_OK;

        public int RenameDocData(uint grfAttribs, IVsHierarchy pHierNew, uint itemidNew, string pszMkDocumentNew)
        {
            _filePath = pszMkDocumentNew;
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
            LoadFile(_filePath);
            return VSConstants.S_OK;
        }

        #endregion

        #region IPersistFileFormat

        public int GetClassID(out Guid pClassID)
        {
            pClassID = new Guid(PuiEditorFactory.GuidString);
            return VSConstants.S_OK;
        }

        public int IsDirty(out int pfIsDirty)
        {
            pfIsDirty = _isDirty ? 1 : 0;
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
            var path = string.IsNullOrEmpty(pszFilename) ? _filePath : pszFilename;
            _control.SaveToFile(path);
            if (fRemember != 0)
            {
                _filePath = path;
                _isDirty = false;
            }
            return VSConstants.S_OK;
        }

        public int SaveCompleted(string pszFilename) => VSConstants.S_OK;

        public int GetCurFile(out string ppszFilename, out uint pnFormatIndex)
        {
            ppszFilename = _filePath;
            pnFormatIndex = FileFormat;
            return VSConstants.S_OK;
        }

        public int GetFormatList(out string ppszFormatList)
        {
            ppszFormatList = "PUI File (*.pui)\n*.pui\n";
            return VSConstants.S_OK;
        }

        #endregion
    }
}
