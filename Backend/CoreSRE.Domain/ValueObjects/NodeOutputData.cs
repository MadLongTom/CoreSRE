using System.Text.Json;
using System.Text.Json.Serialization;

namespace CoreSRE.Domain.ValueObjects;

/// <summary>
/// 节点执行输出数据 — 按连接类型和端口索引组织的数据项。
/// 与 NodeInputData 结构相同，端口索引对应节点声明的 OutputCount。
/// </summary>
public sealed record NodeOutputData
{
    /// <summary>
    /// 连接类型 → 端口数组。
    /// 键为连接类型（当前仅 "main"），值为端口数组（按端口索引排列的 PortDataVO）。
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<PortDataVO?>> Connections { get; init; }
        = new Dictionary<string, IReadOnlyList<PortDataVO?>>();

    /// <summary>连接类型常量</summary>
    public const string MainConnection = "main";

    /// <summary>空输出（无端口数据）</summary>
    public static NodeOutputData Empty { get; } = new();

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
    /// 从单端口数据创建 NodeOutputData（便捷方法，用于 OutputCount=1 的节点）。
    /// </summary>
    public static NodeOutputData FromSinglePort(PortDataVO portData)
    {
        return new NodeOutputData
        {
            Connections = new Dictionary<string, IReadOnlyList<PortDataVO?>>
            {
                [MainConnection] = [portData]
            }
        };
    }

    /// <summary>
    /// 从多端口数据创建 NodeOutputData。
    /// </summary>
    public static NodeOutputData FromPorts(IReadOnlyList<PortDataVO?> ports)
    {
        return new NodeOutputData
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
        // Reuse NodeInputData's serialization infrastructure
        var dto = new NodeInputData.SerializableNodeData();
        if (!Connections.TryGetValue(MainConnection, out var ports))
        {
            dto.Main = [];
        }
        else
        {
            var portArrays = new List<List<NodeInputData.SerializableItem>?>();
            foreach (var port in ports)
            {
                if (port is null)
                {
                    portArrays.Add(null);
                    continue;
                }

                var items = port.Items.Select(item => new NodeInputData.SerializableItem
                {
                    Json = item.Json,
                    Source = item.Source is not null
                        ? new NodeInputData.SerializableSource
                        {
                            NodeId = item.Source.NodeId,
                            OutputIndex = item.Source.OutputIndex,
                            ItemIndex = item.Source.ItemIndex
                        }
                        : null
                }).ToList();
                portArrays.Add(items);
            }
            dto.Main = portArrays;
        }

        return JsonSerializer.Serialize(dto, SerializerOptions);
    }

    /// <summary>
    /// 从 JSON 字符串反序列化。
    /// </summary>
    public static NodeOutputData FromJsonString(string json)
    {
        var dto = JsonSerializer.Deserialize<NodeInputData.SerializableNodeData>(json, SerializerOptions);
        if (dto?.Main is null)
            return Empty;

        var inputData = NodeInputData.FromSerializable(dto);
        return FromPorts(inputData.Connections.TryGetValue(MainConnection, out var ports)
            ? ports
            : []);
    }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}
