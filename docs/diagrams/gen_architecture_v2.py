"""
CoreSRE 系统分层架构图 v2
左侧层级标签 + 右侧模块块 + 层间箭头 + 安全/可观测性侧边栏
"""
import graphviz
import os
import shutil

OUTPUT_DIR = os.path.dirname(os.path.abspath(__file__))
THESIS_FIGURES = os.path.join(os.path.dirname(os.path.dirname(OUTPUT_DIR)), "paper", "thesis", "figures")


def gen_architecture_v2():
    g = graphviz.Digraph(
        "architecture_v2",
        format="png",
        engine="dot",
        graph_attr={
            "label": "",
            "labelloc": "t",
            "fontsize": "28",
            "rankdir": "TB",
            "bgcolor": "white",
            "pad": "0.4",
            "nodesep": "0.3",
            "ranksep": "0.6",
            "dpi": "200",
            "compound": "true",
        },
    )
    g.attr(fontname="Microsoft YaHei")
    g.node_attr.update(fontname="Microsoft YaHei", fontsize="13", shape="box", style="rounded,filled")
    g.edge_attr.update(fontname="Microsoft YaHei", fontsize="11", color="#666666")

    # ══════════════════════════════════════
    # Layer 1: 前端展示层
    # ══════════════════════════════════════
    with g.subgraph(name="cluster_fe") as c:
        c.attr(
            label="前端展示层\n(React 19 + TypeScript 5.9 + Vite 7)",
            style="filled,rounded",
            fillcolor="#e0f2f1",
            color="#009688",
            fontsize="16",
            labeljust="l",
        )
        c.node("fe_agent", "智能体管理\n页面", fillcolor="#80cbc4")
        c.node("fe_tool", "工具管理\n页面", fillcolor="#80cbc4")
        c.node("fe_wf", "工作流编排器\n(ReactFlow)", fillcolor="#80cbc4")
        c.node("fe_aiops", "AIOps告警\n事故面板", fillcolor="#80cbc4")
        c.node("fe_chat", "Agent对话\n界面", fillcolor="#80cbc4")
        c.node("fe_ds", "数据源管理\n页面", fillcolor="#80cbc4")

    # ══════════════════════════════════════
    # Layer 2: API网关层
    # ══════════════════════════════════════
    with g.subgraph(name="cluster_api") as c:
        c.attr(
            label="API网关层\n(ASP.NET Core 10 Minimal API)",
            style="filled,rounded",
            fillcolor="#e3f2fd",
            color="#1976d2",
            fontsize="16",
            labeljust="l",
        )
        c.node("api_rest", "RESTful API\n端点", fillcolor="#90caf9")
        c.node("api_signalr", "SignalR Hub\n实时推送", fillcolor="#90caf9")
        c.node("api_a2a", "A2A Server\n端点", fillcolor="#90caf9")
        c.node("api_webhook", "Webhook\n告警接入", fillcolor="#90caf9")

    # ══════════════════════════════════════
    # Layer 3: 应用层
    # ══════════════════════════════════════
    with g.subgraph(name="cluster_app") as c:
        c.attr(
            label="应用层\n(CQRS + MediatR)",
            style="filled,rounded",
            fillcolor="#fff3e0",
            color="#f57c00",
            fontsize="16",
            labeljust="l",
        )
        c.node("app_cmd", "Command\nHandlers", fillcolor="#ffcc80")
        c.node("app_query", "Query\nHandlers", fillcolor="#ffcc80")
        c.node("app_resolver", "AgentResolver\nService", fillcolor="#ffcc80")
        c.node("app_wf", "Workflow\nTranspiler", fillcolor="#ffcc80")
        c.node("app_aiops", "Incident\nOrchestrator", fillcolor="#ffcc80")

    # ══════════════════════════════════════
    # Layer 4: 领域层
    # ══════════════════════════════════════
    with g.subgraph(name="cluster_domain") as c:
        c.attr(
            label="领域层\n(DDD 聚合根 + 领域事件)",
            style="filled,rounded",
            fillcolor="#fce4ec",
            color="#c62828",
            fontsize="16",
            labeljust="l",
        )
        c.node("dm_agent", "AgentRegistration\n聚合根", fillcolor="#ef9a9a")
        c.node("dm_tool", "ToolRegistration\n聚合根", fillcolor="#ef9a9a")
        c.node("dm_wf", "WorkflowDefinition\n聚合根", fillcolor="#ef9a9a")
        c.node("dm_incident", "Incident\n聚合根", fillcolor="#ef9a9a")
        c.node("dm_ds", "DataSource\nRegistration", fillcolor="#ef9a9a")
        c.node("dm_skill", "SkillRegistration\n聚合根", fillcolor="#ef9a9a")

    # ══════════════════════════════════════
    # Layer 5: 基础设施层
    # ══════════════════════════════════════
    with g.subgraph(name="cluster_infra") as c:
        c.attr(
            label="基础设施层\n(EF Core + 协议客户端 + 外部集成)",
            style="filled,rounded",
            fillcolor="#e8eaf6",
            color="#3f51b5",
            fontsize="16",
            labeljust="l",
        )
        c.node("infra_db", "PostgreSQL\n+ pgvector", fillcolor="#c5cae9")
        c.node("infra_a2a", "A2A Client\n协议通信", fillcolor="#c5cae9")
        c.node("infra_mcp", "MCP Client\n工具调用", fillcolor="#c5cae9")
        c.node("infra_llm", "IChatClient\nLLM调用", fillcolor="#c5cae9")
        c.node("infra_ds", "数据源适配器\nProm/Loki/Jaeger", fillcolor="#c5cae9")

    # ══════════════════════════════════════
    # Layer 6: 编排层
    # ══════════════════════════════════════
    with g.subgraph(name="cluster_aspire") as c:
        c.attr(
            label="编排层\n(.NET Aspire AppHost)",
            style="filled,rounded",
            fillcolor="#f3e5f5",
            color="#7b1fa2",
            fontsize="16",
            labeljust="l",
        )
        c.node("aspire_pg", "PostgreSQL\n容器编排", fillcolor="#ce93d8")
        c.node("aspire_minio", "MinIO\n对象存储", fillcolor="#ce93d8")
        c.node("aspire_dash", "Aspire\nDashboard", fillcolor="#ce93d8")
        c.node("aspire_sd", "ServiceDefaults\n服务发现", fillcolor="#ce93d8")

    # ══════════════════════════════════════
    # 右侧边栏: 安全 & 可观测性 (纵贯全层)
    # ══════════════════════════════════════
    with g.subgraph(name="cluster_cross") as c:
        c.attr(
            label="横切关注点",
            style="filled,rounded",
            fillcolor="#fffde7",
            color="#f9a825",
            fontsize="16",
            labeljust="l",
        )
        c.node("cross_auth", "JWT认证\nRBAC授权", fillcolor="#fff59d")
        c.node("cross_otel", "OpenTelemetry\nTraces/Metrics/Logs", fillcolor="#fff59d")
        c.node("cross_audit", "操作审计\n日志记录", fillcolor="#fff59d")
        c.node("cross_hitl", "HITL\n人工审批", fillcolor="#fff59d")

    # ── 层间调用关系 ──
    g.edge("fe_chat", "api_signalr", label="SignalR", style="bold")
    g.edge("fe_wf", "api_rest", label="HTTP", style="bold")
    g.edge("fe_aiops", "api_rest", style="bold")
    g.edge("fe_agent", "api_rest", style="bold")

    g.edge("api_rest", "app_cmd", label="Command", style="bold")
    g.edge("api_rest", "app_query", label="Query", style="bold")
    g.edge("api_webhook", "app_aiops", label="告警", style="bold", color="#c62828")
    g.edge("api_a2a", "app_resolver", label="A2A", style="bold")

    g.edge("app_cmd", "dm_agent", style="dashed")
    g.edge("app_cmd", "dm_tool", style="dashed")
    g.edge("app_wf", "dm_wf", style="dashed")
    g.edge("app_aiops", "dm_incident", style="dashed")
    g.edge("app_resolver", "dm_agent", style="dashed")

    g.edge("dm_agent", "infra_db", style="dotted")
    g.edge("dm_tool", "infra_db", style="dotted")
    g.edge("dm_wf", "infra_db", style="dotted")
    g.edge("dm_incident", "infra_db", style="dotted")
    g.edge("app_resolver", "infra_llm", label="LLM", color="#845ef7")
    g.edge("app_resolver", "infra_a2a", label="A2A", color="#1976d2")
    g.edge("app_resolver", "infra_mcp", label="MCP", color="#f57c00")
    g.edge("app_aiops", "infra_ds", label="查询", color="#c62828")

    g.edge("infra_db", "aspire_pg", style="dotted", color="#999")
    g.edge("infra_llm", "aspire_sd", style="dotted", color="#999")

    # 横切 - 虚线连接
    g.edge("cross_otel", "aspire_dash", style="dashed", color="#f9a825", label="OTLP")
    g.edge("cross_auth", "api_rest", style="dashed", color="#f9a825", constraint="false")
    g.edge("cross_hitl", "api_signalr", style="dashed", color="#f9a825", constraint="false")

    output_path = os.path.join(OUTPUT_DIR, "architecture_v2")
    g.render(output_path, cleanup=True)
    print(f"✅ architecture_v2.png generated at {output_path}.png")

    # Copy to thesis figures
    src = output_path + ".png"
    dst = os.path.join(THESIS_FIGURES, "architecture.png")
    if os.path.exists(src):
        shutil.copy2(src, dst)
        print(f"✅ Copied to {dst}")


if __name__ == "__main__":
    gen_architecture_v2()
