using System.Runtime.InteropServices;
using Polaris.Addons.Authoring;

namespace PolarisTools.Addons.Generation;

[ComVisible(true)]
[Guid("cb7f23e2-8c4d-4d38-9a92-04b76ff7439e")]
[ClassInterface(ClassInterfaceType.None)]
public sealed class PolarisPluginGenerator : AddonSingleFileGenerator
{
    public const string GeneratorName = "PolarisPluginGenerator";

    protected override AddonDocumentKind Kind => AddonDocumentKind.Plugin;

    protected override string SourceExtension => ".pplugin";
}
