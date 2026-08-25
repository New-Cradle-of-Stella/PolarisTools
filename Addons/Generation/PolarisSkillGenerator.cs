using System.Runtime.InteropServices;
using Polaris.Addons.Authoring;

namespace PolarisTools.Addons.Generation;

[ComVisible(true)]
[Guid("00473e0d-7251-4b52-bad7-c7e8808e3d91")]
[ClassInterface(ClassInterfaceType.None)]
public sealed class PolarisSkillGenerator : AddonSingleFileGenerator
{
    public const string GeneratorName = "PolarisSkillGenerator";

    protected override AddonDocumentKind Kind => AddonDocumentKind.Skill;

    protected override string SourceExtension => ".pskill";
}
