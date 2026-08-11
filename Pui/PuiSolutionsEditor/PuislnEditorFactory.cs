using System;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace PolarisTools.Pui.PuiSolutions
{
    /// <summary>
    /// 双击 .puisln → 打开 PUISolution ToolWindow 并加载，不走 InitGraph。
    /// </summary>
    [Guid(FactoryGuidString)]
    public sealed class PuislnEditorFactory : IVsEditorFactory, IDisposable
    {
        public const string FactoryGuidString = "8940D8ED-3786-4EC5-A558-38F3AFF6AD46"; // 请换成你自己的新 GUID
        public static readonly Guid FactoryGuid = new Guid(FactoryGuidString);

        private readonly AsyncPackage _package;
        private ServiceProvider _serviceProvider;

        public PuislnEditorFactory(AsyncPackage package)
        {
            _package = package ?? throw new ArgumentNullException(nameof(package));
        }

        public int SetSite(Microsoft.VisualStudio.OLE.Interop.IServiceProvider psp)
        {
            _serviceProvider = new ServiceProvider(psp);
            return VSConstants.S_OK;
        }

        public int Close() => VSConstants.S_OK;

        public int MapLogicalView(ref Guid rguidLogicalView, out string pbstrPhysicalView)
        {
            pbstrPhysicalView = null;
            // 仅支持主视图
            if (rguidLogicalView == VSConstants.LOGVIEWID_Primary
                || rguidLogicalView == VSConstants.LOGVIEWID_Any)
                return VSConstants.S_OK;

            return VSConstants.E_NOTIMPL;
        }

        public int CreateEditorInstance(
            uint grfCreateDoc,
            string pszMkDocument,
            string pszPhysicalView,
            IVsHierarchy pvHier,
            uint itemid,
            IntPtr punkDocDataExisting,
            out IntPtr ppunkDocView,
            out IntPtr ppunkDocData,
            out string pbstrEditorCaption,
            out Guid pguidCmdUI,
            out int pgrfCDW)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            ppunkDocView = IntPtr.Zero;
            ppunkDocData = IntPtr.Zero;
            pbstrEditorCaption = null;
            pguidCmdUI = Guid.Empty;
            pgrfCDW = 0;

            if ((grfCreateDoc & (VSConstants.CEF_OPENFILE | VSConstants.CEF_SILENT)) == 0)
                return VSConstants.E_INVALIDARG;

            // 已有 DocData 且不是我们的类型 → 不兼容
            if (punkDocDataExisting != IntPtr.Zero)
                return VSConstants.VS_E_INCOMPATIBLEDOCDATA;

            // View 必须是 WindowPane（IVsWindowPane）；同一对象既作 View 又作 DocData。
            var editor = new PuislnEditorPane();
            ppunkDocView = Marshal.GetIUnknownForObject(editor);
            ppunkDocData = Marshal.GetIUnknownForObject(editor);
            pbstrEditorCaption = " [PUI Graph]";
            pguidCmdUI = FactoryGuid;

            // 正常情况下 VS 随后会自己调 LoadDocData(pszMkDocument)；这里主动加载一次以防不调。
            if (!string.IsNullOrEmpty(pszMkDocument))
                editor.LoadDocData(pszMkDocument);

            return VSConstants.S_OK;
        }

        public void Dispose() => _serviceProvider?.Dispose();
    }
}