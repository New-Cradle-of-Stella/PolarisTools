using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PolarisTools.Pui.PuiSolutions
{
    public sealed class PuislnDocument
    {
        // Version 2：PUI 状态机重写，节点类型从通用事件流（ListButton/ShowPUI/...）换成
        // Entry/PuiState，字段结构不兼容旧文件；旧文件加载时直接报错要求重建，不做静默迁移。
        public int Version { get; set; } = 2;
        public List<PuislnNode> Nodes { get; set; } = new();
        public List<PuislnConnection> Connections { get; set; } = new();
    }

    public sealed class PuislnNode
    {
        public string Title { get; set; }
        public string Type { get; set; } // NodeType 名
        public double X { get; set; }
        public double Y { get; set; }
        // 占位保留：当前 Entry/PuiState 节点都不使用，早期 ListButton 节点类型遗留字段。
        public List<string> ItemCollection { get; set; } = new();
        public List<PuislnConnector> Inputs { get; set; } = new();
        public List<PuislnConnector> Outputs { get; set; } = new();

        // PuiState 专属：绑定的 .pui 文件相对于本 .puisln 所在目录的相对路径。
        public string PuiRelativePath { get; set; }
        // PuiState 专属：对应 IPUI.Name。
        public string PuiName { get; set; }
    }

    public sealed class PuislnConnector
    {
        public string Title { get; set; }
        public bool IsOutput { get; set; }
        // 仅输出连接器：对应 PuiStateTransition.Id，见 ConnectorViewModel.SourceTransitionId。
        public string SourceTransitionId { get; set; }
    }

    public sealed class PuislnConnection
    {
        public int SourceNode { get; set; }
        public bool SourceIsOutput { get; set; }
        public int SourceIndex { get; set; }
        public int TargetNode { get; set; }
        public bool TargetIsOutput { get; set; }
        public int TargetIndex { get; set; }
        public bool Removable { get; set; } = true;
    }

    public static class PuislnSerializer
    {
        private static readonly JsonSerializerOptions Options = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public static void Save(string path, PuislnDocument doc)
        {
            var json = JsonSerializer.Serialize(doc, Options);
            File.WriteAllText(path, json, Encoding.UTF8);
        }

        public static PuislnDocument Load(string path)
            => LoadFromString(File.ReadAllText(path, Encoding.UTF8));

        /// <summary>供 PolarisPuislnGenerator 直接用 VS 传入的内存内容解析，不强制先落盘再读盘。</summary>
        public static PuislnDocument LoadFromString(string json)
        {
            PuislnDocument doc = string.IsNullOrWhiteSpace(json)
                ? new PuislnDocument { Nodes = new(), Connections = new() }
                : JsonSerializer.Deserialize<PuislnDocument>(json, Options)
                    ?? throw new InvalidDataException("Empty .puisln file");
            if (doc.Version < 2)
                throw new InvalidDataException("Old .puisln files (using retired node types) are no longer supported; please recreate it");
            return doc;
        }
    }
}