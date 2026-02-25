using CoreSRE.Application.Alerts.DTOs;

namespace CoreSRE.Application.Alerts.Interfaces;

/// <summary>
/// Alertmanager payload 解析器接口。
/// </summary>
public interface IAlertmanagerPayloadParser
{
    /// <summary>
    /// 解析 Alertmanager JSON payload → AlertVO 列表。
    /// </summary>
    List<AlertVO> Parse(string jsonPayload);
}
