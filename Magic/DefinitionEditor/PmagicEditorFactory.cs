using System;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace PolarisTools.Magic.DefinitionEditor;

/// <summary>
/// <c>.pmagic</c> 的编辑器工厂。双击文件时打开定义编辑器而不是 XML 文本编辑器。
///
/// 编辑器自带文档缓冲区（<see cref="PmagicEditorPane"/> 实现 <c>IVsPersistDocData</c> /
/// <c>IPersistFileFormat</c>），不挂在文本编辑器缓冲区上：<c>.pmagic</c> 的规范写法由共享的
/// <c>MagicDefinitionDocument.ToXml</c> 决定，让文本缓冲区参与只会引入第二种排版。
/// 需要看原始 XML 时用"打开方式 → XML 编辑器"。
/// </summary>
[Guid(GuidString)]
public sealed class PmagicEditorFactory : IVsEditorFactory
{
    public const string GuidString = "ea93f7b3-cd65-4d74-b40f-d01b7e872b15";

    private ServiceProvider? serviceProvider;

    public int SetSite(Microsoft.VisualStudio.OLE.Interop.IServiceProvider psp)
    {
        serviceProvider = new ServiceProvider(psp);
        return VSConstants.S_OK;
    }

    public int Close() => VSConstants.S_OK;

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
        pbstrEditorCaption = string.Empty;
        pguidCmdUI = Guid.Empty;
        pgrfCDW = 0;

        // 已经有别的编辑器打开了同一份文档数据时不抢：两个缓冲区各写一次的结果无法预测。
        if (punkDocDataExisting != IntPtr.Zero)
        {
            return VSConstants.VS_E_INCOMPATIBLEDOCDATA;
        }

        var pane = new PmagicEditorPane();
        pane.LoadFile(pszMkDocument);

        ppunkDocView = Marshal.GetIUnknownForObject(pane);
        ppunkDocData = Marshal.GetIUnknownForObject(pane);
        pbstrEditorCaption = " [Polaris Magic Definition]";

        return VSConstants.S_OK;
    }

    public int MapLogicalView(ref Guid rguidLogicalView, out string pbstrPhysicalView)
    {
        pbstrPhysicalView = string.Empty;

        if (rguidLogicalView == VSConstants.LOGVIEWID_Primary
            || rguidLogicalView == VSConstants.LOGVIEWID_Designer)
        {
            return VSConstants.S_OK;
        }

        return VSConstants.E_NOTIMPL;
    }
}
