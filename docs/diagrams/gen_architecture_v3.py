"""
CoreSRE 系统分层架构图 v3
- 左侧: 每层的中文层名作为独立标签列（与该层模块同 rank）
- 中部: 该层的具体模块方块
- 右侧: 安全管理 / 智能运维 全流程横切关注点侧边栏（与所有层 rank 对齐）
- 上下: 清晰的垂直箭头显示请求/响应方向
"""
import graphviz
import os
import shutil

OUTPUT_DIR = os.path.dirname(os.path.abspath(__file__))
THESIS_FIGURES = os.path.join(
    os.path.dirname(os.path.dirname(OUTPUT_DIR)), "paper", "thesis", "figures"
)


def L(name, fill, fg="#222"):
    """层名标签节点样式"""
    return dict(
        label=name,
        shape="box",
        style="filled,rounded,bold",
        fillcolor=fill,
        fontcolor=fg,
        fontsize="18",
        width="2.0",
        height="1.0",
        fixedsize="true",
    )


def M(label, fill):
    """模块方块样式"""
    return dict(
        label=label,
        shape="box",
        style="filled,rounded",
        fillcolor=fill,
        fontsize="12",
        width="1.6",
        height="0.7",
        fixedsize="true",
    )


def C(label, fill="#fff59d"):
    """横切关注点节点样式"""
    return dict(
        label=label,
        shape="box",
        style="filled,rounded",
        fillcolor=fill,
        fontsize="11",
        width="1.7",
        height="0.7",
        fixedsize="true",
    )


def gen():
    g = graphviz.Digraph(
        "architecture_v3",
        format="png",
        engine="dot",
        graph_attr={
            "rankdir": "TB",
            "bgcolor": "white",
            "pad": "0.5",
            "nodesep": "0.18",
            "ranksep": "0.7",
            "dpi": "200",
            "splines": "ortho",
            "newrank": "true",
        },
    )
    g.attr(fontname="Microsoft YaHei")
    g.node_attr.update(fontname="Microsoft YaHei")
    g.edge_attr.update(fontname="Microsoft YaHei", fontsize="11", color="#555")

    # ============== 每层左侧标签 ==============
    g.node("LBL_FE", **L("前端展示层", "#b2dfdb"))
    g.node("LBL_API", **L("API 网关层", "#bbdefb"))
    g.node("LBL_APP", **L("应用层\n(CQRS)", "#ffe0b2"))
    g.node("LBL_DOM", **L("领域层\n(DDD)", "#f8bbd0"))
    g.node("LBL_INF", **L("基础设施层", "#c5cae9"))
    g.node("LBL_ASP", **L(".NET Aspire\n编排层", "#e1bee7"))

    # ============== 前端展示层模块 ==============
    g.node("fe1", **M("智能体管理\n页面", "#80cbc4"))
    g.node("fe2", **M("工具管理\n页面", "#80cbc4"))
    g.node("fe3", **M("工作流编排器\nReactFlow", "#80cbc4"))
    g.node("fe4", **M("AIOps 事故\n面板", "#80cbc4"))
    g.node("fe5", **M("Agent 对话\n界面", "#80cbc4"))
    g.node("fe6", **M("数据源管理\n页面", "#80cbc4"))

    # ============== API 网关层模块 ==============
    g.node("api1", **M("RESTful API\n端点", "#90caf9"))
    g.node("api2", **M("SignalR Hub\n实时推送", "#90caf9"))
    g.node("api3", **M("A2A Server\n端点", "#90caf9"))
    g.node("api4", **M("Webhook\n告警接入", "#90caf9"))

    # ============== 应用层模块 ==============
    g.node("app1", **M("Command\nHandlers", "#ffcc80"))
    g.node("app2", **M("Query\nHandlers", "#ffcc80"))
    g.node("app3", **M("AgentResolver\nService", "#ffcc80"))
    g.node("app4", **M("Workflow\nTranspiler", "#ffcc80"))
    g.node("app5", **M("Incident\nOrchestrator", "#ffcc80"))

    # ============== 领域层模块 ==============
    g.node("dm1", **M("Agent\nRegistration", "#f48fb1"))
    g.node("dm2", **M("Tool\nRegistration", "#f48fb1"))
    g.node("dm3", **M("Workflow\nDefinition", "#f48fb1"))
    g.node("dm4", **M("Incident\n聚合根", "#f48fb1"))
    g.node("dm5", **M("DataSource\nRegistration", "#f48fb1"))
    g.node("dm6", **M("Skill\nRegistration", "#f48fb1"))

    # ============== 基础设施层模块 ==============
    g.node("inf1", **M("EF Core +\nPostgreSQL", "#9fa8da"))
    g.node("inf2", **M("A2A Client\n协议通信", "#9fa8da"))
    g.node("inf3", **M("MCP Client\n工具调用", "#9fa8da"))
    g.node("inf4", **M("IChatClient\nLLM 调用", "#9fa8da"))
    g.node("inf5", **M("数据源适配器\nProm/Loki/Jaeger", "#9fa8da"))

    # ============== 编排层模块 ==============
    g.node("asp1", **M("PostgreSQL\n+ pgvector", "#ce93d8"))
    g.node("asp2", **M("MinIO\n对象存储", "#ce93d8"))
    g.node("asp3", **M("Aspire\nDashboard", "#ce93d8"))
    g.node("asp4", **M("Service\nDefaults", "#ce93d8"))

    # ============== 右侧横切关注点侧边栏 ==============
    # 安全管理（顶部 → 中部）
    g.node(
        "SEC_TITLE",
        label="安全管理\n全流程",
        shape="box",
        style="filled,rounded,bold",
        fillcolor="#ffab91",
        fontsize="14",
        width="1.9",
        height="0.7",
        fixedsize="true",
    )
    g.node("sec1", **C("JWT 认证\n+ RBAC 授权"))
    g.node("sec2", **C("凭据加密\n存储与解密"))
    g.node("sec3", **C("API 限流\n+ 熔断保护"))
    g.node("sec4", **C("HITL\n人工审批"))

    # 智能运维（中部 → 底部）
    g.node(
        "OBS_TITLE",
        label="智能运维\n可观测性",
        shape="box",
        style="filled,rounded,bold",
        fillcolor="#a5d6a7",
        fontsize="14",
        width="1.9",
        height="0.7",
        fixedsize="true",
    )
    g.node("obs1", **C("OpenTelemetry\nTraces / Metrics", "#c8e6c9"))
    g.node("obs2", **C("结构化日志\n+ 操作审计", "#c8e6c9"))
    g.node("obs3", **C("Aspire\nDashboard", "#c8e6c9"))

    # ============== rank=same 横向对齐 ==============
    # 行 0: 前端
    with g.subgraph() as s:
        s.attr(rank="same")
        s.node("LBL_FE")
        s.node("fe1"); s.node("fe2"); s.node("fe3")
        s.node("fe4"); s.node("fe5"); s.node("fe6")
        s.node("SEC_TITLE")

    # 行 1: API
    with g.subgraph() as s:
        s.attr(rank="same")
        s.node("LBL_API")
        s.node("api1"); s.node("api2"); s.node("api3"); s.node("api4")
        s.node("sec1")

    # 行 2: 应用
    with g.subgraph() as s:
        s.attr(rank="same")
        s.node("LBL_APP")
        s.node("app1"); s.node("app2"); s.node("app3"); s.node("app4"); s.node("app5")
        s.node("sec2")

    # 行 3: 领域
    with g.subgraph() as s:
        s.attr(rank="same")
        s.node("LBL_DOM")
        s.node("dm1"); s.node("dm2"); s.node("dm3"); s.node("dm4"); s.node("dm5"); s.node("dm6")
        s.node("sec3")

    # 行 4: 基础设施
    with g.subgraph() as s:
        s.attr(rank="same")
        s.node("LBL_INF")
        s.node("inf1"); s.node("inf2"); s.node("inf3"); s.node("inf4"); s.node("inf5")
        s.node("sec4")

    # 行 5: 编排（含智能运维侧边栏标题与所有运维节点）
    with g.subgraph() as s:
        s.attr(rank="same")
        s.node("LBL_ASP")
        s.node("asp1"); s.node("asp2"); s.node("asp3"); s.node("asp4")
        s.node("OBS_TITLE")
        s.node("obs1"); s.node("obs2"); s.node("obs3")

    # ============== 不可见水平边强制左右顺序 ==============
    def hchain(*nodes):
        for a, b in zip(nodes[:-1], nodes[1:]):
            g.edge(a, b, style="invis", weight="50", minlen="1")

    hchain("LBL_FE", "fe1", "fe2", "fe3", "fe4", "fe5", "fe6", "SEC_TITLE")
    hchain("LBL_API", "api1", "api2", "api3", "api4", "sec1")
    hchain("LBL_APP", "app1", "app2", "app3", "app4", "app5", "sec2")
    hchain("LBL_DOM", "dm1", "dm2", "dm3", "dm4", "dm5", "dm6", "sec3")
    hchain("LBL_INF", "inf1", "inf2", "inf3", "inf4", "inf5", "sec4")
    hchain("LBL_ASP", "asp1", "asp2", "asp3", "asp4", "OBS_TITLE", "obs1", "obs2", "obs3")

    # ============== 不可见竖直链强制所有层名标签左对齐到同一列 ==============
    for a, b in zip(
        ["LBL_FE", "LBL_API", "LBL_APP", "LBL_DOM", "LBL_INF"],
        ["LBL_API", "LBL_APP", "LBL_DOM", "LBL_INF", "LBL_ASP"],
    ):
        g.edge(a, b, style="invis", weight="100", minlen="1")

    # ============== 垂直层间调用箭头 ==============
    arrow = dict(color="#1976d2", penwidth="2", weight="10")
    rev = dict(color="#388e3c", penwidth="1.5", style="dashed", weight="5")

    # 前端 -> API
    g.edge("fe3", "api1", label="HTTP / REST", **arrow)
    g.edge("fe5", "api2", label="WebSocket", **arrow)
    g.edge("api2", "fe5", label="实时推送", **rev)

    # API -> 应用
    g.edge("api1", "app1", label="Command", **arrow)
    g.edge("api1", "app2", label="Query", **arrow)
    g.edge("api3", "app3", label="A2A", **arrow)
    g.edge("api4", "app5", label="告警", color="#c62828", penwidth="2", weight="10")

    # 应用 -> 领域
    g.edge("app1", "dm1", label="操作聚合根", **arrow)
    g.edge("app4", "dm3", **arrow)
    g.edge("app5", "dm4", color="#c62828", penwidth="2", weight="10")

    # 领域 -> 基础设施 (持久化)
    g.edge("dm1", "inf1", label="持久化", **arrow)
    g.edge("dm3", "inf1", **arrow)
    g.edge("dm4", "inf1", **arrow)

    # 应用 -> 基础设施 (协议)
    g.edge("app3", "inf2", label="A2A", color="#1976d2", penwidth="1.5", weight="3")
    g.edge("app3", "inf3", label="MCP", color="#f57c00", penwidth="1.5", weight="3")
    g.edge("app3", "inf4", label="LLM", color="#7b1fa2", penwidth="1.5", weight="3")
    g.edge("app5", "inf5", label="数据源查询", color="#c62828", weight="3")

    # 基础设施 -> 编排（容器）
    g.edge("inf1", "asp1", label="容器内", style="dashed", color="#888", weight="5")
    g.edge("inf4", "asp4", style="dashed", color="#888", weight="3")

    # ============== 横切侧边栏内部连线 ==============
    sec_arrow = dict(style="dashed", color="#d84315", penwidth="1.2", weight="2")
    g.edge("SEC_TITLE", "sec1", **sec_arrow)
    g.edge("sec1", "sec2", **sec_arrow)
    g.edge("sec2", "sec3", **sec_arrow)
    g.edge("sec3", "sec4", **sec_arrow)
    g.edge("sec4", "OBS_TITLE", **sec_arrow)

    output_path = os.path.join(OUTPUT_DIR, "architecture_v3")
    g.render(output_path, cleanup=True)
    print(f"OK architecture_v3.png at {output_path}.png")

    src = output_path + ".png"
    dst = os.path.join(THESIS_FIGURES, "architecture.png")
    if os.path.exists(src):
        shutil.copy2(src, dst)
        print(f"OK Copied to {dst}")


if __name__ == "__main__":
    gen()
