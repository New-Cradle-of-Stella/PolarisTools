using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;
using Microsoft.VisualStudio.Utilities;
using System.ComponentModel.Composition;

namespace PolarisTools.Event.Language
{
    /// <summary>把 <see cref="HppGotoDefinitionCommandFilter"/> 挂到每个 .phxx 文本视图的命令链上。</summary>
    [Export(typeof(IVsTextViewCreationListener))]
    [ContentType(HppContentType.Name)]
    [TextViewRole(PredefinedTextViewRoles.Editable)]
    internal sealed class HppTextViewCreationListener : IVsTextViewCreationListener
    {
        [Import]
        internal IVsEditorAdaptersFactoryService AdapterService { get; set; }

        [Import]
        internal ITextDocumentFactoryService TextDocumentFactoryService { get; set; }

        public void VsTextViewCreated(IVsTextView textViewAdapter)
        {
            var view = AdapterService.GetWpfTextView(textViewAdapter);
            if (view == null)
            {
                return;
            }

            string filePath = TextDocumentFactoryService.TryGetTextDocument(view.TextBuffer, out var document) ? document.FilePath : null;
            var filter = new HppGotoDefinitionCommandFilter(view, filePath);

            if (ErrorHandler.Succeeded(textViewAdapter.AddCommandFilter(filter, out var next)))
            {
                filter.Next = next;
            }
        }
    }
}
