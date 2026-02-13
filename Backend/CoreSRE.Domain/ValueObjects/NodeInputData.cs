using System.Text.Json;
using System.Text.Json.Serialization;

namespace CoreSRE.Domain.ValueObjects;

/// <summary>
/// 节点执行输入数据 — 按连接类型和端口索引组织的数据项。
/// 序列化为 {"main": [[items], [items], ...]} 格式。
/// </summary>
public sealed record NodeInputData
{
    /// <summary>
    /// 连接类型 → 端口数组。
    /// 键为连接类型（当前仅 "main"），值为端口数组（按端口索引排列的 PortDataVO）。
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<PortDataVO?>> Connections { get; init; }
        = new Dictionary<string, IReadOnlyList<PortDataVO?>>();

    /// <summary>连接类型常量</summary>
    public const string MainConnection = "main";

    /// <summary>空输入（无端口数据）</summary>
    public static NodeInputData Empty { get; } = new();

    /// <summary>
    /// 获取 main 连接上指定端口索引的数据。
    /// </summary>
    public PortDataVO? GetPort(int index)
    {
        if (Connections.TryGetValue(MainConnection, out var ports) && index >= 0 && index < ports.Count)
            return ports[index];
        return null;
    }

    /// <summary>
    /// 从单端口数据创建 NodeInputData（便捷方法，用于 InputCount=1 的节点）。
    /// </summary>
    public static NodeInputData FromSinglePort(PortDataVO portData)
    {
        return new NodeInputData
        {
            Connections = new Dictionary<string, IReadOnlyList<PortDataVO?>>
            {
                [MainConnection] = [portData]
            }
        };
    }

    /// <summary>
    /// 从多端口数据创建 NodeInputData。
    /// </summary>
    public static NodeInputData FromPorts(IReadOnlyList<PortDataVO?> ports)
    {
        return new NodeInputData
        {
            Connections = new Dictionary<string, IReadOnlyList<PortDataVO?>>
            {
                [MainConnection] = ports
            }
        };
    }

    /// <summary>
    /// 序列化为 JSON 字符串，格式为 {"main": [[items], ...]}。
    /// </summary>
    public string ToJsonString()
    {
        return JsonSerializer.Serialize(ToSerializable(), SerializerOptions);
    }

    /// <summary>
    /// 从 JSON 字符串反序列化。
    /// </summary>
    public static NodeInputData FromJsonString(string json)
    {
        var dto = JsonSerializer.Deserialize<SerializableNodeData>(json, SerializerOptions);
        if (dto?.Main is null)
            return Empty;

        return FromSerializable(dto);
    }

    internal SerializableNodeData ToSerializable()
    {
        if (!Connections.TryGetValue(MainConnection, out var ports))
            return new SerializableNodeData { Main = [] };

        var portArrays = new List<List<SerializableItem>?>();
        foreach (var port in ports)
        {
            if (port is null)
            {
                portArrays.Add(null);
                continue;
            }

            var items = port.Items.Select(item => new SerializableItem
            {
                Json = item.Json,
                Source = item.Source is not null
                    ? new SerializableSource
                    {
                        NodeId = item.Source.NodeId,
                        OutputIndex = item.Source.OutputIndex,
                        ItemIndex = item.Source.ItemIndex
                    }
                    : null
            }).ToList();
            portArrays.Add(items);
        }

        return new SerializableNodeData { Main = portArrays };
    }

    internal static NodeInputData FromSerializable(SerializableNodeData dto)
    {
        if (dto.Main is null)
            return Empty;

        var ports = new List<PortDataVO?>();
        foreach (var portItems in dto.Main)
        {
            if (portItems is null)
            {
                ports.Add(null);
                continue;
            }

            var items = portItems.Select(si => new WorkflowItemVO(
                si.Json,
                si.Source is not null
                    ? new ItemSourceVO(si.Source.NodeId, si.Source.OutputIndex, si.Source.ItemIndex)
                    : null
            )).ToList();
            ports.Add(new PortDataVO(items));
        }

        return FromPorts(ports);
    }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>JSON 序列化 DTO</summary>
    internal sealed class SerializableNodeData
    {
        [JsonPropertyName("main")]
        public List<List<SerializableItem>?>? Main { get; set; }
    }

    internal sealed class SerializableItem
    {
        [JsonPropertyName("json")]
        public JsonElement Json { get; set; }

        [JsonPropertyName("source")]
        public SerializableSource? Source { get; set; }
    }

    internal sealed class SerializableSource
    {
        [JsonPropertyName("nodeId")]
        public string NodeId { get; set; } = string.Empty;

        [JsonPropertyName("outputIndex")]
        public int OutputIndex { get; set; }

        [JsonPropertyName("itemIndex")]
        public int ItemIndex { get; set; }
    }
}
