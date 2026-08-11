using System;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace PolarisTools.Pui.PuiSolutions
{
    [Guid(PaneGuidString)]
    public sealed class PuislnEditorPane : WindowPane, IVsPersistDocData, IPersistFileFormat
    {
        public const string PaneGuidString = "B2C3D4E5-F6A7-8901-BCDE-F12345678901";
        public static readonly Guid PaneGuid = new Guid(PaneGuidString);

        private const uint FileFormatIndex = 0;
        private const string Ext = ".puisln";

        private readonly PuiSolutionWindowControl _control;
        private string _fileName;
        private uint _docCookie; // RDT cookie

        public PuislnEditorPane() : base(null)
        {
            _control = new PuiSolutionWindowControl(initGraph: false);
            Content = _control;

            // ★ 脏状态一变就推给 VS（不要只挂空事件）
            _control.ViewModel.IsDirtyChanged += OnIsDirtyChanged;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _control.ViewModel.IsDirtyChanged -= OnIsDirtyChanged;
            base.Dispose(disposing);
        }

        private void OnIsDirtyChanged(object sender, EventArgs e)
        {
            // 保证在 UI 线程通知；有意 fire-and-forget（不能阻塞调用方等它切完线程），
            // FileAndForget 确保异常被记录而不是吞掉——VSSDK007 认的"已处理"只有 Join/await，
            // 识别不出 FileAndForget 这种做法，这里显式压掉。
#pragma warning disable VSSDK007
            ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                NotifyDocDataDirtyChanged();
            }).FileAndForget("PolarisTools/NotifyDocDataDirtyChanged");
#pragma warning restore VSSDK007
        }

        /// <summary>
        /// 立刻刷新标签 * / 保存按钮，而不是等 VS 偶尔轮询 IsDocDataDirty。
        /// </summary>
        private void NotifyDocDataDirtyChanged()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_docCookie == 0)
                return;

            var rdt = GetService(typeof(SVsRunningDocumentTable)) as IVsRunningDocumentTable;
            if (rdt == null)
                return;

            // 脏 / 不脏各通知一次对应标志
            uint attrib = _control.ViewModel.IsDirty
                ? (uint)__VSRDTATTRIB.RDTA_DocDataIsDirty
                : (uint)__VSRDTATTRIB.RDTA_DocDataIsNotDirty;

            rdt.NotifyDocumentChanged(_docCookie, attrib);

            // 立刻刷新工具栏 Save 等命令状态
            var uiShell = GetService(typeof(SVsUIShell)) as IVsUIShell;
            uiShell?.UpdateCommandUI(0);
        }

        private bool IsDirtyFlag => _control.ViewModel.IsDirty;

        // -------------------- IVsPersistDocData --------------------

        public int GetGuidEditorType(out Guid pClassID)
        {
            pClassID = PuislnEditorFactory.FactoryGuid;
            return VSConstants.S_OK;
        }

        public int IsDocDataDirty(out int pfDirty)
        {
            pfDirty = IsDirtyFlag ? 1 : 0;
            return VSConstants.S_OK;
        }

        public int SetUntitledDocPath(string pszDocDataPath)
        {
            _fileName = pszDocDataPath;
            return VSConstants.S_OK;
        }

        public int LoadDocData(string pszMkDocument)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _fileName = pszMkDocument;
            try
            {
                _control.LoadFromFile(pszMkDocument); // ClearDirty → 会触发 Notify
                NotifyDocDataDirtyChanged();          // 再推一次更稳
                return VSConstants.S_OK;
            }
            catch
            {
                return VSConstants.E_FAIL;
            }
        }

        public int SaveDocData(VSSAVEFLAGS dwSave, out string pbstrMkDocumentNew, out int pfSaveCanceled)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            pbstrMkDocumentNew = null;
            pfSaveCanceled = 0;

            try
            {
                bool saveAs = dwSave == VSSAVEFLAGS.VSSAVE_SaveAs
                              || dwSave == VSSAVEFLAGS.VSSAVE_SaveCopyAs
                              || string.IsNullOrEmpty(_fileName);

                string path = _fileName;

                if (saveAs)
                {
                    var dlg = new Microsoft.Win32.SaveFileDialog
                    {
                        Title = "Save PUI solution graph as",
                        Filter = "PUI Solution (*.puisln)|*.puisln",
                        DefaultExt = Ext,
                        AddExtension = true,
                        FileName = string.IsNullOrEmpty(_fileName)
                            ? "Untitled.puisln"
                            : System.IO.Path.GetFileName(_fileName),
                        InitialDirectory = !string.IsNullOrEmpty(_fileName)
                            ? System.IO.Path.GetDirectoryName(_fileName)
                            : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
                    };

                    if (dlg.ShowDialog() != true)
                    {
                        pfSaveCanceled = 1;
                        return VSConstants.S_OK;
                    }
                    path = dlg.FileName;
                }

                _control.ViewModel.SaveToFile(path); // ClearDirty

                pbstrMkDocumentNew = path;
                // SaveCopyAs 是"另存一份副本"，当前文档仍然指向原文件，不能改 _fileName。
                if (dwSave != VSSAVEFLAGS.VSSAVE_SaveCopyAs)
                    _fileName = path;

                NotifyDocDataDirtyChanged(); // 立刻去掉 *
                return VSConstants.S_OK;
            }
            catch
            {
                return VSConstants.E_FAIL;
            }
        }

        public int Close()
        {
            _docCookie = 0;
            return VSConstants.S_OK;
        }

        // ★ 必须记下 cookie，后面 Notify 才有效
        public int OnRegisterDocData(uint docCookie, IVsHierarchy pHierNew, uint itemidNew)
        {
            _docCookie = docCookie;
            return VSConstants.S_OK;
        }

        public int RenameDocData(uint grfAttribs, IVsHierarchy pHierNew, uint itemidNew, string pszMkDocumentNew)
        {
            _fileName = pszMkDocumentNew;
            if (_control.ViewModel != null)
                _control.ViewModel.FilePath = pszMkDocumentNew;
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
            return LoadDocData(_fileName);
        }

        // -------------------- IPersistFileFormat --------------------

        int Microsoft.VisualStudio.OLE.Interop.IPersist.GetClassID(out Guid pClassID)
        {
            pClassID = PaneGuid;
            return VSConstants.S_OK;
        }

        public int GetClassID(out Guid pClassID)
        {
            pClassID = PaneGuid;
            return VSConstants.S_OK;
        }

        public int IsDirty(out int pfIsDirty)
        {
            pfIsDirty = IsDirtyFlag ? 1 : 0;
            return VSConstants.S_OK;
        }

        public int InitNew(uint nFormatIndex)
        {
            _control.ViewModel.ClearDirty();
            return VSConstants.S_OK;
        }

        public int Load(string pszFilename, uint grfMode, int fReadOnly)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return LoadDocData(pszFilename);
        }

        public int Save(string pszFilename, int fRemember, uint nFormatIndex)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var path = string.IsNullOrEmpty(pszFilename) ? _fileName : pszFilename;
            if (string.IsNullOrEmpty(path))
                return VSConstants.E_FAIL;

            _control.ViewModel.SaveToFile(path);
            if (fRemember != 0)
                _fileName = path;

            NotifyDocDataDirtyChanged();
            return VSConstants.S_OK;
        }

        public int SaveCompleted(string pszFilename) => VSConstants.S_OK;

        public int GetCurFile(out string ppszFilename, out uint pnFormatIndex)
        {
            ppszFilename = _fileName;
            pnFormatIndex = FileFormatIndex;
            return VSConstants.S_OK;
        }

        public int GetFormatList(out string ppszFormatList)
        {
            ppszFormatList = "PUI Solution (*" + Ext + ")\n*" + Ext + "\n";
            return VSConstants.S_OK;
        }
    }
}