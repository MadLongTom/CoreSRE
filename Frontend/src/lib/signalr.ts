import {
  HubConnectionBuilder,
  HubConnection,
  LogLevel,
} from "@microsoft/signalr";

/**
 * 创建并配置 SignalR HubConnection 实例。
 * 使用 JSON 协议 + 自动重连策略。
 */
export function createWorkflowHubConnection(
  url: string = "/hubs/workflow"
): HubConnection {
  return new HubConnectionBuilder()
    .withUrl(url)
    .withAutomaticReconnect([0, 1000, 3000, 5000, 10000, 30000])
    .configureLogging(LogLevel.Information)
    .build();
}

/**
 * 创建 Incident Hub 连接实例。
 */
export function createIncidentHubConnection(
  url: string = "/hubs/incident"
): HubConnection {
  return new HubConnectionBuilder()
    .withUrl(url)
    .withAutomaticReconnect([0, 1000, 3000, 5000, 10000, 30000])
    .configureLogging(LogLevel.Information)
    .build();
}
