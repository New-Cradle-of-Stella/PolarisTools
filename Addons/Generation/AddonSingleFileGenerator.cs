using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Polaris.Addons.Authoring;

namespace PolarisTools.Addons.Generation;

public abstract class AddonSingleFileGenerator : IVsSingleFileGenerator
{
    protected abstract AddonDocumentKind Kind { get; }

    protected abstract string SourceExtension { get; }

    public int DefaultExtension(out string extension)
    {
        extension = SourceExtension + ".g.cs";
        return VSConstants.S_OK;
    }

    public int Generate(
        string inputFilePath,
        string inputFileContents,
        string defaultNamespace,
        IntPtr[] outputFileContents,
        out uint outputSize,
        IVsGeneratorProgress progress)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        outputSize = 0;

        try
        {
            string generated = Build(inputFilePath, inputFileContents, defaultNamespace);
            byte[] bytes = Encoding.UTF8.GetBytes(generated);
            IntPtr buffer = Marshal.AllocCoTaskMem(bytes.Length);
            Marshal.Copy(bytes, 0, buffer, bytes.Length);
            outputFileContents[0] = buffer;
            outputSize = (uint)bytes.Length;
            return VSConstants.S_OK;
        }
        catch (Exception ex)
        {
            progress?.GeneratorError(0, 0, ex.Message, 0, 0);
            return VSConstants.E_FAIL;
        }
    }

    internal string Build(string path, string source, string defaultNamespace)
    {
        string className = Path.GetFileNameWithoutExtension(path);
        if (!AddonIdentifier.IsValidName(className))
        {
            throw new AddonFormatException(
                "'" + className + "' cannot be used as a generated C# type name; rename the file.");
        }

        string namespaceName = string.IsNullOrWhiteSpace(defaultNamespace)
            ? "Polaris.Generated"
            : defaultNamespace.Trim();
        if (!AddonIdentifier.IsValidNamespace(namespaceName))
        {
            throw new AddonFormatException("'" + namespaceName + "' is not a valid C# namespace.");
        }

        AddonDefinitionDocument document = Parse(source);
        Validate(document);
        return AddonCSharpEmitter.Emit(document, className, namespaceName, SourceExtension);
    }

    private AddonDefinitionDocument Parse(string source)
    {
        switch (Kind)
        {
            case AddonDocumentKind.Item: return ItemDefinitionDocument.Parse(source);
            case AddonDocumentKind.Plugin: return PluginDefinitionDocument.Parse(source);
            case AddonDocumentKind.Skill: return SkillDefinitionDocument.Parse(source);
            default: throw new AddonFormatException("Unsupported Addons document kind.");
        }
    }

    private static void Validate(AddonDefinitionDocument document)
    {
        if (!AddonIdentifier.IsValidId(document.Id))
        {
            throw new AddonFormatException(
                "'" + document.Id + "' is not a stable Addons id; use a lowercase namespaced id such as mymod.item.");
        }

        if (!AddonIdentifier.IsValidOptionalTypeName(document.BehaviorType))
        {
            throw new AddonFormatException(
                "'" + document.BehaviorType + "' is not a valid C# Behavior type name.");
        }

        switch (document)
        {
            case ItemDefinitionDocument item when item.Price < 0 || item.StackLimit < 1:
                throw new AddonFormatException("Item Price must be non-negative and StackLimit must be at least 1.");
            case PluginDefinitionDocument plugin when
                !AddonIdentifier.IsValidId(plugin.ItemId) || plugin.Cost < 0:
                throw new AddonFormatException("Plugin ItemId must be valid and Cost must be non-negative.");
            case SkillDefinitionDocument skill when !AddonIdentifier.IsValidId(skill.ItemId):
                throw new AddonFormatException("Skill ItemId must be a valid Addons item id.");
        }
    }
}
