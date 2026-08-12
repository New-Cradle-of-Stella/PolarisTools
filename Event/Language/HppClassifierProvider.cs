using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Utilities;
using System.ComponentModel.Composition;

namespace PolarisTools.Event.Language
{
    [Export(typeof(IClassifierProvider))]
    [ContentType(HppContentType.Name)]
    internal sealed class HppClassifierProvider : IClassifierProvider
    {
        [Import]
        internal IClassificationTypeRegistryService ClassificationRegistry { get; set; }

        public IClassifier GetClassifier(ITextBuffer buffer)
        {
            return buffer.Properties.GetOrCreateSingletonProperty(() => new HppClassifier(buffer, ClassificationRegistry));
        }
    }
}
