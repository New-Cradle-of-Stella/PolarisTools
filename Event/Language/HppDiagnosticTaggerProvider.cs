using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Tagging;
using Microsoft.VisualStudio.Utilities;
using System.ComponentModel.Composition;

namespace PolarisTools.Event.Language
{
    [Export(typeof(ITaggerProvider))]
    [ContentType(HppContentType.Name)]
    [TagType(typeof(IErrorTag))]
    internal sealed class HppDiagnosticTaggerProvider : ITaggerProvider
    {
        [Import]
        internal ITextDocumentFactoryService TextDocumentFactoryService { get; set; }

        [Import]
        internal HppErrorTableDataSource ErrorTableDataSource { get; set; }

        public ITagger<T> CreateTagger<T>(ITextBuffer buffer) where T : ITag
        {
            string filePath = TextDocumentFactoryService.TryGetTextDocument(buffer, out var document) ? document.FilePath : null;
            return HppDiagnosticTagger.GetOrCreate(buffer, filePath, ErrorTableDataSource) as ITagger<T>;
        }
    }
}
