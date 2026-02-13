using CoreSRE.Domain.ValueObjects;

namespace CoreSRE.Infrastructure.Services;

/// <summary>
/// 等待队列条目 — 多输入节点（InputCount > 1）在等待所有端口数据到齐。
/// </summary>
internal sealed class WaitingNodeData
{
    /// <summary>期望的输入端口数量</summary>
    public int ExpectedPortCount { get; }

    /// <summary>已接收的端口数据（按端口索引）</summary>
    private readonly Dictionary<int, PortDataVO> _receivedPorts = new();

    /// <summary>已接收端口的只读视图</summary>
    public IReadOnlyDictionary<int, PortDataVO> ReceivedPorts => _receivedPorts;

    /// <summary>所有端口是否已接收到数据</summary>
    public bool IsComplete => _receivedPorts.Count >= ExpectedPortCount;

    public WaitingNodeData(int expectedPortCount)
    {
        if (expectedPortCount < 1)
            throw new ArgumentOutOfRangeException(nameof(expectedPortCount), "Expected port count must be at least 1.");
        ExpectedPortCount = expectedPortCount;
    }

    /// <summary>
    /// 接收指定端口的数据。重复接收同一端口会覆盖之前的数据。
    /// </summary>
    public void ReceivePort(int portIndex, PortDataVO data)
    {
        _receivedPorts[portIndex] = data;
    }

    /// <summary>
    /// 将已接收的端口数据组装为 NodeInputData。
    /// </summary>
    public NodeInputData BuildInputData()
    {
        var ports = new PortDataVO?[ExpectedPortCount];
        foreach (var (index, data) in _receivedPorts)
        {
            if (index >= 0 && index < ports.Length)
                ports[index] = data;
        }
        return NodeInputData.FromPorts(ports);
    }
}
