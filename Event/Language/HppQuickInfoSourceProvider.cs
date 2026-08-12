using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Utilities;
using System.ComponentModel.Composition;

namespace PolarisTools.Event.Language
{
    [Export(typeof(IAsyncQuickInfoSourceProvider))]
    [Name("Hpp Quick Info Provider")]
    [ContentType(HppContentType.Name)]
    internal sealed class HppQuickInfoSourceProvider : IAsyncQuickInfoSourceProvider
    {
        [Import]
        internal ITextDocumentFactoryService TextDocumentFactoryService { get; set; }

        public IAsyncQuickInfoSource TryCreateQuickInfoSource(ITextBuffer textBuffer)
        {
            string filePath = TextDocumentFactoryService.TryGetTextDocument(textBuffer, out var document) ? document.FilePath : null;
            return textBuffer.Properties.GetOrCreateSingletonProperty(() => new HppQuickInfoSource(textBuffer, filePath));
        }
    }
}
