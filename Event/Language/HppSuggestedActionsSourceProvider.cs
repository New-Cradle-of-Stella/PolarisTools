using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using System.ComponentModel.Composition;

namespace PolarisTools.Event.Language
{
    [Export(typeof(ISuggestedActionsSourceProvider))]
    [Name("Hpp Suggested Actions")]
    [ContentType(HppContentType.Name)]
    internal sealed class HppSuggestedActionsSourceProvider : ISuggestedActionsSourceProvider
    {
        [Import]
        internal ITextDocumentFactoryService TextDocumentFactoryService { get; set; }

        [Import]
        internal HppErrorTableDataSource ErrorTableDataSource { get; set; }

        public ISuggestedActionsSource CreateSuggestedActionsSource(ITextView textView, ITextBuffer textBuffer)
        {
            if (textBuffer == null)
            {
                return null;
            }

            string filePath = TextDocumentFactoryService.TryGetTextDocument(textBuffer, out var document) ? document.FilePath : null;
            return new HppSuggestedActionsSource(textBuffer, filePath, ErrorTableDataSource);
        }
    }
}
