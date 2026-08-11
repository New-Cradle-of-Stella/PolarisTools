using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PolarisTools.Lang
{
    /// <summary>
    /// .plang 表格的 CSV 导入导出。仓库里没有任何现成 CSV 依赖，行数量级也不大，手写一个够用
    /// 的最小 RFC4180 读写器，不为这点事引第三方包。
    /// </summary>
    internal static class PlangCsvIo
    {
        const string ColKey = "Key";
        const string ColComment = "Comment";
        const string ColNeutral = "Neutral";

        /// <summary>
        /// 旧版导出过的 <c>Type</c> 列（Short/Long）已经没有对应概念了，导入时得认出来跳过——
        /// 不然它会被当成一个叫 "Type" 的语言代码，凭空多出一列来。
        /// </summary>
        const string ColLegacyType = "Type";

        /// <summary>导出全部语言（不管启用/禁用），保证导入导出可以无损往返；启用状态是编辑器内部概念，不进 CSV。</summary>
        public static void Export(PlangEditorViewModel vm, string path)
        {
            List<string> languageCodes = vm.Languages.Select(l => l.Code).Where(c => !string.IsNullOrEmpty(c)).ToList();

            var sb = new StringBuilder();

            var header = new List<string> { ColKey, ColComment, ColNeutral };
            header.AddRange(languageCodes);
            WriteRow(sb, header);

            foreach (PlangRowViewModel row in vm.Rows)
            {
                var fields = new List<string> { row.Key, row.Comment ?? "", row.NeutralValue ?? "" };
                fields.AddRange(languageCodes.Select(code => row[code]));
                WriteRow(sb, fields);
            }

            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        }

        /// <summary>
        /// 按 Key upsert：已存在的 Key 更新 Comment/Neutral/各语言值，不存在的新建。
        /// 表头里出现了文档里没有的语言代码会自动新增一个语言（默认启用）。
        /// </summary>
        public static (int Added, int Updated, int NewLanguages) Import(PlangEditorViewModel vm, string path)
        {
            List<List<string>> rows = ParseCsv(File.ReadAllText(path));
            if (rows.Count == 0)
            {
                return (0, 0, 0);
            }

            List<string> header = rows[0];
            int idxKey = header.IndexOf(ColKey);
            int idxComment = header.IndexOf(ColComment);
            int idxNeutral = header.IndexOf(ColNeutral);
            int idxLegacyType = header.IndexOf(ColLegacyType);
            if (idxKey < 0)
            {
                throw new InvalidDataException("CSV 缺少 \"Key\" 列，无法导入。");
            }

            var langColumns = new List<(int Index, string Code)>();
            for (int i = 0; i < header.Count; i++)
            {
                if (i == idxKey || i == idxComment || i == idxNeutral || i == idxLegacyType)
                {
                    continue;
                }

                string code = header[i]?.Trim();
                if (!string.IsNullOrEmpty(code))
                {
                    langColumns.Add((i, code));
                }
            }

            vm.BeginBatch();
            int added = 0, updated = 0, newLanguages = 0;
            try
            {
                foreach ((int _, string code) in langColumns)
                {
                    if (vm.AddLanguage(code, code))
                    {
                        newLanguages++;
                    }
                }

                for (int r = 1; r < rows.Count; r++)
                {
                    List<string> cols = rows[r];
                    string key = idxKey < cols.Count ? cols[idxKey]?.Trim() : null;
                    if (string.IsNullOrEmpty(key))
                    {
                        continue;
                    }

                    PlangRowViewModel row = vm.Rows.FirstOrDefault(x => x.Key == key);
                    bool isNew = row == null;
                    if (isNew)
                    {
                        row = new PlangRowViewModel { Key = key };
                        vm.Rows.Add(row);
                    }

                    if (idxComment >= 0 && idxComment < cols.Count)
                    {
                        row.Comment = cols[idxComment];
                    }
                    if (idxNeutral >= 0 && idxNeutral < cols.Count)
                    {
                        row.NeutralValue = cols[idxNeutral];
                    }

                    foreach ((int colIndex, string code) in langColumns)
                    {
                        if (colIndex < cols.Count)
                        {
                            row[code] = cols[colIndex];
                        }
                    }

                    if (isNew) added++; else updated++;
                }
            }
            finally
            {
                vm.EndBatch();
            }

            if (added > 0 || updated > 0 || newLanguages > 0)
            {
                vm.IsDirty = true;
            }

            return (added, updated, newLanguages);
        }

        static void WriteRow(StringBuilder sb, List<string> fields)
        {
            sb.Append(string.Join(",", fields.Select(EscapeField))).Append("\r\n");
        }

        static string EscapeField(string field)
        {
            field ??= "";
            bool needsQuote = field.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0;
            return needsQuote ? "\"" + field.Replace("\"", "\"\"") + "\"" : field;
        }

        /// <summary>最小 RFC4180 解析：支持带引号字段、字段内逗号/换行、"" 转义引号。</summary>
        static List<List<string>> ParseCsv(string text)
        {
            var rows = new List<List<string>>();
            var current = new List<string>();
            var field = new StringBuilder();
            bool inQuotes = false;
            int i = 0, n = text.Length;

            while (i < n)
            {
                char c = text[i];

                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < n && text[i + 1] == '"')
                        {
                            field.Append('"');
                            i += 2;
                            continue;
                        }

                        inQuotes = false;
                        i++;
                        continue;
                    }

                    field.Append(c);
                    i++;
                    continue;
                }

                switch (c)
                {
                    case '"':
                        inQuotes = true;
                        i++;
                        break;
                    case ',':
                        current.Add(field.ToString());
                        field.Clear();
                        i++;
                        break;
                    case '\r':
                        i++; // 统一靠 \n 断行，\r\n 里的 \r 直接跳过
                        break;
                    case '\n':
                        current.Add(field.ToString());
                        field.Clear();
                        rows.Add(current);
                        current = new List<string>();
                        i++;
                        break;
                    default:
                        field.Append(c);
                        i++;
                        break;
                }
            }

            // 文件末尾没有换行符时，最后一行不会被 '\n' 分支收尾，这里补上。
            if (field.Length > 0 || current.Count > 0)
            {
                current.Add(field.ToString());
                rows.Add(current);
            }

            // 过滤纯空行（常见于文件末尾多一个换行）。
            return rows.Where(r => r.Count > 1 || !string.IsNullOrEmpty(r.FirstOrDefault())).ToList();
        }
    }
}
