namespace CoreSRE.Domain.Enums;

/// <summary>
/// 沙盒类型枚举 — 决定为 Agent 提供的工具集合。
/// 每种类型提供不同的工具集合，由高到低递增能力。
/// 沙盒容器基于 Kubernetes Pod 运行。
/// </summary>
public enum SandboxType
{
    /// <summary>基础沙盒：命令行执行、文件读写、目录浏览</summary>
    SimpleBox,

    /// <summary>代码执行沙盒：SimpleBox + 多语言代码片段执行 + 包安装</summary>
    CodeBox,

    /// <summary>交互式终端沙盒：CodeBox + 持久 PTY 会话（stdin/stdout 流式交互）</summary>
    InteractiveBox,

    /// <summary>浏览器自动化沙盒：CodeBox + Playwright 浏览器控制</summary>
    BrowserBox,

    /// <summary>桌面自动化沙盒：CodeBox + 屏幕截图 + 鼠标键盘操控</summary>
    ComputerBox
}
