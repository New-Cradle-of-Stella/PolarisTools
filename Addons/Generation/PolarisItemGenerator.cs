using System.Runtime.InteropServices;
using Polaris.Addons.Authoring;

namespace PolarisTools.Addons.Generation;

[ComVisible(true)]
[Guid("c07cc7a7-31bf-4a18-8acd-a30ea21a0185")]
[ClassInterface(ClassInterfaceType.None)]
public sealed class PolarisItemGenerator : AddonSingleFileGenerator
{
    public const string GeneratorName = "PolarisItemGenerator";

    protected override AddonDocumentKind Kind => AddonDocumentKind.Item;

    protected override string SourceExtension => ".pitem";
}
