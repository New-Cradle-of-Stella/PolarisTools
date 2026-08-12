using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using System.ComponentModel.Composition;

namespace PolarisTools.Event.Language
{
    [Export(typeof(IAsyncCompletionSourceProvider))]
    [Name("Hpp Completion Source Provider")]
    [ContentType(HppContentType.Name)]
    internal sealed class HppCompletionSourceProvider : IAsyncCompletionSourceProvider
    {
        [Import]
        internal ITextDocumentFactoryService TextDocumentFactoryService { get; set; }

        public IAsyncCompletionSource GetOrCreate(ITextView textView)
        {
            string filePath = TextDocumentFactoryService.TryGetTextDocument(textView.TextBuffer, out var document) ? document.FilePath : null;
            return textView.Properties.GetOrCreateSingletonProperty(() => new HppCompletionSource(textView, filePath));
        }
    }
}
