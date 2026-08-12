using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace PolarisTools.Event.Language
{
    /// <summary>
    /// "创建缺失别名" Code Action 用的纯文本 YAML 编辑：不解析成结构化模型再序列化回去（那样会打乱
    /// 用户的格式/注释/顺序），只做最小的行级插入——识别 <c>key: {}</c> 单行空 map 和
    /// <c>key:\n  ...</c> 块两种形态，找到块的插入点后原样插入新行。只覆盖本轮 Code Action 实际
    /// 用到的两种形状：顶层简单 map（actors/positions/events）和两层嵌套 map（audio.sfx）。
    /// </summary>
    internal static class HppAliasFileEditor
    {
        public const string DefaultSkeleton = "actors: {}\npositions: {}\nboxStyles: {}\naudio:\n  sfx: {}\nevents: {}\n";

        public static string AddSimpleEntry(string yamlText, string topKey, string entryName, string[] entryBodyLines)
        {
            var lines = SplitLines(yamlText);
            int keyIndex = lines.FindIndex(l => Regex.IsMatch(l, $@"^{Regex.Escape(topKey)}\s*:"));

            if (keyIndex < 0)
            {
                lines.Add($"{topKey}:");
                lines.Add($"  {entryName}:");
                lines.AddRange(entryBodyLines.Select(b => "    " + b));
                return Join(lines);
            }

            if (IsInlineEmptyMap(lines[keyIndex]))
            {
                var replacement = new List<string> { $"{topKey}:", $"  {entryName}:" };
                replacement.AddRange(entryBodyLines.Select(b => "    " + b));
                lines.RemoveAt(keyIndex);
                lines.InsertRange(keyIndex, replacement);
                return Join(lines);
            }

            int keyIndent = Indent(lines[keyIndex]);
            int insertAt = FindBlockEnd(lines, keyIndex + 1, keyIndent);

            var newLines = new List<string> { $"  {entryName}:" };
            newLines.AddRange(entryBodyLines.Select(b => "    " + b));
            lines.InsertRange(insertAt, newLines);
            return Join(lines);
        }

        /// <summary>顶层就是扁平 map 的情况（如 <c>events: {FarmRule: ...}</c>），插入 "entryName: value"
        /// 而不是 <see cref="AddSimpleEntry"/> 那种"entryName: \n  子字段"块。</summary>
        public static string AddFlatLeafEntry(string yamlText, string topKey, string entryName, string value)
        {
            var lines = SplitLines(yamlText);
            int keyIndex = lines.FindIndex(l => Regex.IsMatch(l, $@"^{Regex.Escape(topKey)}\s*:"));

            if (keyIndex < 0)
            {
                lines.Add($"{topKey}:");
                lines.Add($"  {entryName}: {value}");
                return Join(lines);
            }

            if (IsInlineEmptyMap(lines[keyIndex]))
            {
                lines[keyIndex] = $"{topKey}:";
                lines.Insert(keyIndex + 1, $"  {entryName}: {value}");
                return Join(lines);
            }

            int keyIndent = Indent(lines[keyIndex]);
            int insertAt = FindBlockEnd(lines, keyIndex + 1, keyIndent);
            lines.Insert(insertAt, $"  {entryName}: {value}");
            return Join(lines);
        }

        public static string AddLeafEntry(string yamlText, string topKey, string subKey, string entryName, string value)
        {
            var lines = SplitLines(yamlText);
            int topIndex = lines.FindIndex(l => Regex.IsMatch(l, $@"^{Regex.Escape(topKey)}\s*:"));

            if (topIndex < 0)
            {
                lines.Add($"{topKey}:");
                lines.Add($"  {subKey}:");
                lines.Add($"    {entryName}: {value}");
                return Join(lines);
            }

            if (IsInlineEmptyMap(lines[topIndex]))
            {
                lines[topIndex] = $"{topKey}:";
                lines.Insert(topIndex + 1, $"  {subKey}:");
                lines.Insert(topIndex + 2, $"    {entryName}: {value}");
                return Join(lines);
            }

            int topIndent = Indent(lines[topIndex]);
            int end = FindBlockEnd(lines, topIndex + 1, topIndent);

            int subIndex = -1;
            for (int i = topIndex + 1; i < end; i++)
            {
                if (lines[i].Trim().Length == 0)
                {
                    continue;
                }

                if (Indent(lines[i]) == topIndent + 2 && Regex.IsMatch(lines[i], $@"^\s*{Regex.Escape(subKey)}\s*:"))
                {
                    subIndex = i;
                    break;
                }
            }

            if (subIndex < 0)
            {
                lines.Insert(end, $"  {subKey}:");
                lines.Insert(end + 1, $"    {entryName}: {value}");
                return Join(lines);
            }

            if (IsInlineEmptyMap(lines[subIndex]))
            {
                lines[subIndex] = $"  {subKey}:";
                lines.Insert(subIndex + 1, $"    {entryName}: {value}");
                return Join(lines);
            }

            int subIndent = Indent(lines[subIndex]);
            int insertAt = FindBlockEnd(lines, subIndex + 1, subIndent, end);
            lines.Insert(insertAt, $"    {entryName}: {value}");
            return Join(lines);
        }

        static bool IsInlineEmptyMap(string line) => Regex.IsMatch(line, @":\s*\{\s*\}\s*$");

        static int Indent(string line) => line.Length - line.TrimStart().Length;

        /// <summary>从 <paramref name="from"/> 开始找第一行"非空且缩进 &lt;= parentIndent"的位置，
        /// 即上一个块结束的地方；找不到就是 <paramref name="limit"/>（默认整份文档末尾）。</summary>
        static int FindBlockEnd(List<string> lines, int from, int parentIndent, int? limit = null)
        {
            int end = limit ?? lines.Count;
            for (int i = from; i < end; i++)
            {
                if (lines[i].Trim().Length == 0)
                {
                    continue;
                }

                if (Indent(lines[i]) <= parentIndent)
                {
                    return i;
                }
            }

            return end;
        }

        static List<string> SplitLines(string text) => new List<string>((text ?? string.Empty).Replace("\r\n", "\n").Split('\n'));

        static string Join(List<string> lines) => string.Join("\n", lines);
    }
}
