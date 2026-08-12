using Polaris.Event.Compiler.Aliases;
using Polaris.Event.Compiler.Diagnostics;
using Polaris.Event.Compiler.Text;
using System.IO;
using System.Linq;

namespace PolarisTools.Event.Language
{
    /// <summary>
    /// "从一个 .phxx 文件出发找它用的 polaris.events.yaml" 这条约定只应该有一份实现——
    /// <see cref="HppDiagnosticsService"/>（诊断）、<see cref="HppQuickInfoSource"/>（悬停）、
    /// 补全和 Go To Definition 都要认同同一个别名文件，否则会出现"诊断说未知角色，悬停却查得到"
    /// 这种自相矛盾。约定：从 .phxx 所在目录开始向上找 <c>polaris.events.yaml</c> 或
    /// <c>*.events.yaml</c>，最多 8 层。
    /// </summary>
    internal static class HppAliasFileLocator
    {
        const string AliasFileName = "polaris.events.yaml";

        public static SourceText FindAliasSource(string startDirectory)
        {
            string dir = startDirectory;
            for (int i = 0; dir != null && i < 8; i++)
            {
                string candidate = Path.Combine(dir, AliasFileName);
                if (File.Exists(candidate))
                {
                    return new SourceText(candidate, File.ReadAllText(candidate));
                }

                string alt = Directory.EnumerateFiles(dir, "*.events.yaml").FirstOrDefault();
                if (alt != null)
                {
                    return new SourceText(alt, File.ReadAllText(alt));
                }

                dir = Path.GetDirectoryName(dir);
            }

            return null;
        }

        public static AliasDocument FindAliasDocument(string startDirectory, out string aliasFilePath)
        {
            var source = FindAliasSource(startDirectory);
            aliasFilePath = source?.Path;
            if (source == null)
            {
                return null;
            }

            var diagnostics = new DiagnosticBag();
            return AliasLoader.Load(source, diagnostics);
        }
    }
}
