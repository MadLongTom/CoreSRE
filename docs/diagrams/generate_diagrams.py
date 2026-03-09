"""
CoreSRE 系统架构图生成器
使用 Graphviz 生成：业务流程图、业务链路图、数据流转图、架构图、概念图
"""
import graphviz
import os

OUTPUT_DIR = os.path.dirname(os.path.abspath(__file__))


def set_chinese_font(g):
    """设置中文字体"""
    g.attr(fontname="Microsoft YaHei")
    g.node_attr.update(fontname="Microsoft YaHei")
    g.edge_attr.update(fontname="Microsoft YaHei")


# ═══════════════════════════════════════════════════════════════
# 图 1: 业务流程图 — 告警事故处置全链路
# ═══════════════════════════════════════════════════════════════
def gen_business_flow():
    g = graphviz.Digraph(
        "business_flow",
        format="png",
        engine="dot",
        graph_attr={
            "label": "CoreSRE 业务流程图 — 告警事故处置全链路",
            "labelloc": "t",
            "fontsize": "32",
            "rankdir": "LR",
            "bgcolor": "#fafafa",
            "pad": "0.6",
            "nodesep": "0.35",
            "ranksep": "0.7",
            "dpi": "200",
            "size": "24,13.5!",
            "ratio": "fill",
        },
    )
    set_chinese_font(g)
    g.node_attr.update(shape="box", style="rounded,filled", fontsize="14", height="0.6", width="2.0")
    g.edge_attr.update(fontsize="12", color="#666666")

    # ── 告警接入 ──
    with g.subgraph(name="cluster_alert_in") as c:
        c.attr(label="告警接入", style="dashed", color="#999999", fontsize="18")
        c.node("A", "Alertmanager Webhook", fillcolor="#ff6b6b", fontcolor="white")
        c.node("B", "AlertPayload 解析", fillcolor="#ffd8d8")
        c.node("C", "MatchAlertRules\n标签匹配", shape="diamond", fillcolor="#ffe8cc")
        c.node("D", "丢弃（冷却期内）", fillcolor="#e0e0e0", fontcolor="#888")

    g.edge("A", "B", label="POST webhook")
    g.edge("B", "C")
    g.edge("C", "D", label="冷却期内")

    # ── 链路 A: SOP 自动执行（紧凑节点）──
    with g.subgraph(name="cluster_chain_a") as c:
        c.attr(label="链路 A — SOP 自动执行", style="filled", color="#e8f5e9", fillcolor="#f1f8e9", fontsize="18")
        c.node("F", "创建 Incident\n(SopExecution)", fillcolor="#c8e6c9")
        c.node("G", "Conversation +\n上下文初始化", fillcolor="#a5d6a7")
        c.node("H", "IAgentCaller\nResponder Agent", fillcolor="#81c784")
        c.node("H1", "SOP 解析 →\n逐步执行", fillcolor="#66bb6a")
        c.node("I", "结果?", shape="diamond", fillcolor="#ffe8cc", width="0.9")
        c.node("J", "Resolve\n记录 MTTR", fillcolor="#4caf50", fontcolor="white")

    g.edge("C", "F", label="有 SOP")
    g.edge("F", "G")
    g.edge("G", "H")
    g.edge("H", "H1")
    g.edge("H1", "I")
    g.edge("I", "J", label="成功")

    # ── 链路 B: 根因分析 ──
    with g.subgraph(name="cluster_chain_b") as c:
        c.attr(label="链路 B — 根因分析", style="filled", color="#e3f2fd", fillcolor="#e8f4fd", fontsize="18")
        c.node("M", "创建 Incident\n(RCA 路由)", fillcolor="#bbdefb")
        c.node("N", "Conversation +\n上下文初始化", fillcolor="#90caf9")
        c.node("O", "ITeamOrchestrator\n多 Agent 协作", fillcolor="#64b5f6")
        c.node("Q", "根因结论\nSetRootCause", fillcolor="#2196f3", fontcolor="white")
        c.node("R", "生成SOP?", shape="diamond", fillcolor="#ffe8cc", width="0.9")

    g.edge("C", "M", label="无 SOP")
    g.edge("M", "N")
    g.edge("N", "O")
    g.edge("O", "Q")
    g.edge("Q", "R")

    # 降级
    g.node("K", "降级 →\nFallbackToRca", fillcolor="#ffcc80")
    g.edge("I", "K", label="失败/超时")
    g.edge("K", "M", constraint="false")

    # HITL（紧凑）
    with g.subgraph(name="cluster_hitl") as c:
        c.attr(label="HITL 人工介入", style="filled", color="#fff3e0", fillcolor="#fff8e1", fontsize="18")
        c.node("L", "RequestIntervention\nHITL 阻塞", fillcolor="#ffd54f")
        c.node("BB", "SignalR → 前端\n审批/输入/选择", fillcolor="#ffecb3")
        c.node("BC", "Respond → 解除阻塞", fillcolor="#ffd54f")

    g.edge("I", "L", label="需人工")
    g.edge("L", "BB")
    g.edge("BB", "BC")
    g.edge("BC", "H1", style="dashed", label="继续", constraint="false")

    # ── 链路 C: SOP 生成（紧凑）──
    with g.subgraph(name="cluster_chain_c") as c:
        c.attr(label="链路 C — SOP 自动生成", style="filled", color="#f3e5f5", fillcolor="#fce4ec", fontsize="18")
        c.node("S", "Summarizer Agent\n生成 SOP Markdown", fillcolor="#ba68c8")
        c.node("V", "SOP 解析 +\n校验工具引用", fillcolor="#9c27b0", fontcolor="white")
        c.node("X", "SkillRegistration\n(Draft)", fillcolor="#7b1fa2", fontcolor="white")

    g.edge("R", "S", label="有 Summarizer")
    g.edge("S", "V")
    g.edge("V", "X")

    # ── SOP 质量保证（紧凑）──
    with g.subgraph(name="cluster_qa") as c:
        c.attr(label="SOP 质量保证", style="filled", color="#ede7f6", fillcolor="#f3e5f5", fontsize="18")
        c.node("Y", "Validate +\nDryRun", fillcolor="#d1c4e9")
        c.node("AA", "审核?", shape="diamond", fillcolor="#ffe8cc", width="0.8")
        c.node("AB", "Publish → Active\n绑定 AlertRule", fillcolor="#4caf50", fontcolor="white")
        c.node("AD", "Reject", fillcolor="#e0e0e0")

    g.edge("X", "Y")
    g.edge("Y", "AA")
    g.edge("AA", "AB", label="通过")
    g.edge("AA", "AD", label="驳回")

    # ── 金丝雀验证（紧凑）──
    with g.subgraph(name="cluster_canary") as c:
        c.attr(label="金丝雀验证", style="filled", color="#e0f7fa", fillcolor="#e0f2f1", fontsize="18")
        c.node("AE", "StartCanary\nShadow 执行", fillcolor="#80deea")
        c.node("AG", "一致?", shape="diamond", fillcolor="#ffe8cc", width="0.8")
        c.node("AH", "Promote\n切换新 SOP", fillcolor="#4caf50", fontcolor="white")
        c.node("AI", "Stop\n保留旧 SOP", fillcolor="#e0e0e0")

    g.edge("AB", "AE")
    g.edge("AE", "AG")
    g.edge("AG", "AH", label="是")
    g.edge("AG", "AI", label="否")

    # ── 评估反馈（紧凑）──
    with g.subgraph(name="cluster_eval") as c:
        c.attr(label="评估反馈闭环", style="filled", color="#e8eaf6", fillcolor="#e8eaf6", fontsize="18")
        c.node("CA", "Post-Mortem 标注", fillcolor="#c5cae9")
        c.node("CB", "Evaluation Dashboard\nMTTR / 覆盖率 / 准确率", fillcolor="#9fa8da")
        c.node("CC", "Prompt 优化建议", fillcolor="#7986cb", fontcolor="white")

    g.edge("J", "CA")
    g.edge("CA", "CB")
    g.edge("CB", "CC")

    g.render(os.path.join(OUTPUT_DIR, "01_business_flow"), cleanup=True)
    print("✅ 01_business_flow.png")


# ═══════════════════════════════════════════════════════════════
# 图 2: 业务链路图 — 系统交互全景
# ═══════════════════════════════════════════════════════════════
def gen_interaction_map():
    g = graphviz.Digraph(
        "interaction_map",
        format="png",
        engine="dot",
        graph_attr={
            "label": "CoreSRE 业务链路图 — 系统交互全景",
            "labelloc": "t",
            "fontsize": "32",
            "rankdir": "LR",
            "bgcolor": "#fafafa",
            "pad": "0.6",
            "nodesep": "0.4",
            "ranksep": "1.4",
            "dpi": "200",
        },
    )
    set_chinese_font(g)
    g.node_attr.update(shape="box", style="rounded,filled", fontsize="14")
    g.edge_attr.update(fontsize="12", color="#888888")

    # ── 外部系统 ──
    with g.subgraph(name="cluster_ext") as c:
        c.attr(label="外部系统", style="filled", fillcolor="#fff3e0", fontsize="18")
        c.node("AM", "Alertmanager", fillcolor="#ff6b6b", fontcolor="white")
        c.node("PROM", "Prometheus", fillcolor="#e8f5e9")
        c.node("LOKI", "Loki", fillcolor="#e8f5e9")
        c.node("JAEGER", "Jaeger", fillcolor="#e8f5e9")
        c.node("K8S", "Kubernetes", fillcolor="#e3f2fd")
        c.node("ARGO", "ArgoCD", fillcolor="#e3f2fd")
        c.node("GH", "GitHub / GitLab", fillcolor="#f3e5f5")
        c.node("LLM", "LLM Provider\n(OpenAI API)", fillcolor="#845ef7", fontcolor="white")
        c.node("MCP_EXT", "外部 MCP Server", fillcolor="#fff9c4")
        c.node("REST_EXT", "REST API 工具", fillcolor="#fff9c4")

    # ── 前端 ──
    with g.subgraph(name="cluster_fe") as c:
        c.attr(label="前端 (React + TS)", style="filled", fillcolor="#e0f2f1", fontsize="18")
        c.node("FE_CHAT", "对话页面\nAG-UI 流式", fillcolor="#80cbc4")
        c.node("FE_INC", "事故管理页面\nSignalR 实时", fillcolor="#80cbc4")
        c.node("FE_WF", "工作流编辑器\nDAG 可视化", fillcolor="#80cbc4")
        c.node("FE_MGMT", "资源管理\nAgent/Tool/Skill/DS", fillcolor="#80cbc4")
        c.node("FE_EVAL", "评估仪表板", fillcolor="#80cbc4")

    # ── API 层 ──
    with g.subgraph(name="cluster_api") as c:
        c.attr(label="API 层 (Minimal API)", style="filled", fillcolor="#e8eaf6", fontsize="18")
        c.node("WH", "Webhook 端点", fillcolor="#c5cae9")
        c.node("AGENT_API", "Agent API", fillcolor="#c5cae9")
        c.node("CHAT_API", "Chat API", fillcolor="#c5cae9")
        c.node("WF_API", "Workflow API", fillcolor="#c5cae9")
        c.node("INC_API", "Incident API", fillcolor="#c5cae9")
        c.node("TOOL_API", "Tool API", fillcolor="#c5cae9")
        c.node("DS_API", "DataSource API", fillcolor="#c5cae9")
        c.node("SK_API", "Skill API", fillcolor="#c5cae9")
        c.node("SB_API", "Sandbox API", fillcolor="#c5cae9")
        c.node("EVAL_API", "Evaluation API", fillcolor="#c5cae9")
        c.node("SR", "SignalR Hub\nWorkflow + Incident", fillcolor="#9fa8da")

    # ── 告警处置引擎 ──
    with g.subgraph(name="cluster_alert_eng") as c:
        c.attr(label="告警处置引擎", style="filled", fillcolor="#fce4ec", fontsize="18")
        c.node("MATCH", "AlertRule\n标签匹配", fillcolor="#f48fb1")
        c.node("DISP_A", "链路A 调度\nSOP 执行", fillcolor="#f48fb1")
        c.node("DISP_B", "链路B 调度\n根因分析", fillcolor="#f48fb1")
        c.node("DISP_C", "链路C\nSOP 生成", fillcolor="#f48fb1")
        c.node("HITL2", "HITL 人工介入", fillcolor="#f48fb1")

    # ── Agent 运行时 ──
    with g.subgraph(name="cluster_agent_rt") as c:
        c.attr(label="Agent 运行时", style="filled", fillcolor="#ede7f6", fontsize="18")
        c.node("AR", "AgentResolver\n构建 Agent", fillcolor="#b39ddb")
        c.node("TO", "TeamOrchestrator\n多 Agent 编排", fillcolor="#b39ddb")
        c.node("AC", "AgentCaller\nAgent 调用", fillcolor="#b39ddb")
        c.node("TFF", "ToolFunctionFactory\n工具绑定", fillcolor="#ce93d8")
        c.node("DFF", "DataSourceFunctionFactory\n数据源绑定", fillcolor="#ce93d8")
        c.node("STP", "SandboxToolProvider\n沙箱绑定", fillcolor="#ce93d8")

    # ── 工作流引擎 ──
    with g.subgraph(name="cluster_wf_eng") as c:
        c.attr(label="工作流引擎", style="filled", fillcolor="#fff3e0", fontsize="18")
        c.node("WE", "WorkflowEngine\nDAG 执行", fillcolor="#ffcc80")
        c.node("CE", "ConditionEvaluator", fillcolor="#ffcc80")
        c.node("EE", "V8ExpressionEvaluator", fillcolor="#ffcc80")
        c.node("WBG", "Background Worker\nChannel<T>", fillcolor="#ffcc80")

    # ── 基础设施 ──
    with g.subgraph(name="cluster_infra") as c:
        c.attr(label="基础设施 (Aspire)", style="filled", fillcolor="#e3f2fd", fontsize="18")
        c.node("PG", "PostgreSQL\npgvector", shape="cylinder", fillcolor="#42a5f5", fontcolor="white")
        c.node("MINIO", "MinIO\nS3 存储", shape="cylinder", fillcolor="#42a5f5", fontcolor="white")

    # ── 连线 ──
    # 外部 → API
    g.edge("AM", "WH", label="Webhook")
    g.edge("WH", "MATCH")
    g.edge("MATCH", "DISP_A")
    g.edge("MATCH", "DISP_B")
    g.edge("DISP_A", "AC")
    g.edge("DISP_B", "TO")
    g.edge("TO", "AC")
    g.edge("AC", "AR")
    g.edge("AR", "LLM", label="ChatCompletion")
    g.edge("AR", "TFF")
    g.edge("AR", "DFF")
    g.edge("AR", "STP")
    g.edge("TFF", "REST_EXT", label="REST")
    g.edge("TFF", "MCP_EXT", label="MCP")
    g.edge("DFF", "PROM")
    g.edge("DFF", "LOKI")
    g.edge("DFF", "JAEGER")
    g.edge("DFF", "K8S")
    g.edge("DFF", "ARGO")
    g.edge("DFF", "GH")
    g.edge("STP", "K8S", label="Pod Exec")
    g.edge("DISP_A", "DISP_C", style="dashed")
    g.edge("DISP_C", "AC")
    g.edge("HITL2", "SR")

    # 工作流
    g.edge("WF_API", "WBG")
    g.edge("WBG", "WE")
    g.edge("WE", "CE")
    g.edge("WE", "EE")
    g.edge("WE", "AC")

    # 持久化
    for api in ["AGENT_API", "CHAT_API", "WF_API", "INC_API", "TOOL_API", "DS_API", "SK_API"]:
        g.edge(api, "PG", style="dotted", color="#aaaaaa")
    g.edge("SK_API", "MINIO", style="dotted", color="#aaaaaa", label="文件包")
    g.edge("SB_API", "K8S", label="Pod 管理")

    # 前端 → API
    g.edge("FE_CHAT", "CHAT_API", label="AG-UI SSE")
    g.edge("FE_INC", "SR", label="SignalR")
    g.edge("FE_WF", "SR", label="SignalR")
    g.edge("FE_MGMT", "AGENT_API")
    g.edge("FE_MGMT", "TOOL_API")
    g.edge("FE_MGMT", "DS_API")
    g.edge("FE_MGMT", "SK_API")
    g.edge("FE_EVAL", "EVAL_API")

    g.render(os.path.join(OUTPUT_DIR, "02_interaction_map"), cleanup=True)
    print("✅ 02_interaction_map.png")


# ═══════════════════════════════════════════════════════════════
# 图 3: 数据流转图
# ═══════════════════════════════════════════════════════════════
def gen_data_flow():
    g = graphviz.Digraph(
        "data_flow",
        format="png",
        engine="dot",
        graph_attr={
            "label": "CoreSRE 数据流转图 — 全系统数据生命周期",
            "labelloc": "t",
            "fontsize": "32",
            "rankdir": "TB",
            "bgcolor": "#fafafa",
            "pad": "0.6",
            "nodesep": "0.45",
            "ranksep": "0.8",
            "dpi": "200",
        },
    )
    set_chinese_font(g)
    g.node_attr.update(shape="box", style="rounded,filled", fontsize="14")
    g.edge_attr.update(fontsize="12", color="#888888")

    # ── 数据输入 ──
    with g.subgraph(name="cluster_input") as c:
        c.attr(label="数据输入", style="filled", fillcolor="#fff3e0", fontsize="18", rank="same")
        c.node("IN1", "Alertmanager\n告警 JSON", fillcolor="#ff6b6b", fontcolor="white")
        c.node("IN2", "用户对话消息\nAG-UI SSE", fillcolor="#80cbc4")
        c.node("IN3", "前端表单\nREST API", fillcolor="#80cbc4")
        c.node("IN4", "OpenAPI 规范\n工具导入", fillcolor="#fff9c4")

    # ── API 网关 ──
    with g.subgraph(name="cluster_gw") as c:
        c.attr(label="API 网关层", style="filled", fillcolor="#e8eaf6", fontsize="18")
        c.node("GW1", "Webhook\n告警接入", fillcolor="#c5cae9")
        c.node("GW2", "AgentChat\n流式对话", fillcolor="#c5cae9")
        c.node("GW3", "资源 CRUD", fillcolor="#c5cae9")
        c.node("GW4", "工具导入/调用", fillcolor="#c5cae9")

    # ── MediatR 处理 ──
    with g.subgraph(name="cluster_mediatr") as c:
        c.attr(label="命令/查询处理 (MediatR + FluentValidation)", style="filled", fillcolor="#f3e5f5", fontsize="18")
        c.node("VAL", "ValidationBehavior\n前置校验", fillcolor="#ce93d8")
        c.node("CMD", "Command Handler\n写操作", fillcolor="#ba68c8", fontcolor="white")
        c.node("QRY", "Query Handler\n读操作", fillcolor="#ab47bc", fontcolor="white")

    # ── 领域实体 ──
    with g.subgraph(name="cluster_domain") as c:
        c.attr(label="领域层 — 16 实体 + 39 值对象 (JSONB)", style="filled", fillcolor="#fff8e1", fontsize="18")
        c.node("AG_REG", "AgentRegistration\n(A2A/ChatClient/Workflow/Team)", fillcolor="#845ef7", fontcolor="white")
        c.node("CONV", "Conversation\n对话元数据", fillcolor="#ffd54f")
        c.node("SESS", "AgentSessionRecord\n会话JSONB", fillcolor="#ffd54f")
        c.node("INC", "Incident\n事故生命周期", fillcolor="#ff6b6b", fontcolor="white")
        c.node("AR_ENT", "AlertRule\n告警路由规则", fillcolor="#f48fb1")
        c.node("WF_DEF", "WorkflowDefinition\nDAG 图", fillcolor="#ffcc80")
        c.node("WF_EXE", "WorkflowExecution\n执行快照", fillcolor="#ffcc80")
        c.node("TOOL_ENT", "ToolRegistration\n工具源", fillcolor="#fff9c4")
        c.node("MCP_ITEM", "McpToolItem\nMCP 子工具", fillcolor="#fff9c4")
        c.node("SKILL_ENT", "SkillRegistration\nSOP/技能", fillcolor="#90caf9")
        c.node("DS_ENT", "DataSourceRegistration\n数据源配置", fillcolor="#a5d6a7")
        c.node("LLM_ENT", "LlmProvider\nAPI密钥+模型", fillcolor="#b39ddb")
        c.node("SB_ENT", "SandboxInstance\nK8s Pod", fillcolor="#80deea")
        c.node("CANARY_ENT", "CanaryResult\n金丝雀结果", fillcolor="#c8e6c9")
        c.node("PROMPT_ENT", "PromptOptimizationSuggestion", fillcolor="#d1c4e9")

    # ── Agent 运行时 ──
    with g.subgraph(name="cluster_agent_data") as c:
        c.attr(label="Agent 运行时数据流", style="filled", fillcolor="#ede7f6", fontsize="18")
        c.node("AGENT_RT", "AIAgent 实例", fillcolor="#845ef7", fontcolor="white")
        c.node("LLM_CALL", "LLM API\nChat Completion", fillcolor="#845ef7", fontcolor="white")
        c.node("TOOL_CALL", "工具函数调用\nAIFunction", fillcolor="#ffd54f")
        c.node("DS_CALL", "数据源函数调用\nAIFunction", fillcolor="#a5d6a7")
        c.node("SB_CALL", "沙箱函数调用\nAIFunction", fillcolor="#80deea")

    # ── 持久化 ──
    with g.subgraph(name="cluster_persist") as c:
        c.attr(label="持久化层", style="filled", fillcolor="#e3f2fd", fontsize="18")
        c.node("PG", "PostgreSQL + pgvector\nEF Core", shape="cylinder", fillcolor="#1e88e5", fontcolor="white")
        c.node("S3", "MinIO S3\n文件存储", shape="cylinder", fillcolor="#1e88e5", fontcolor="white")

    # ── 实时推送 ──
    with g.subgraph(name="cluster_push") as c:
        c.attr(label="实时推送", style="filled", fillcolor="#e0f2f1", fontsize="18")
        c.node("SR_WF", "SignalR\nWorkflowHub", fillcolor="#26a69a", fontcolor="white")
        c.node("SR_INC", "SignalR\nIncidentHub", fillcolor="#26a69a", fontcolor="white")

    # ── 输出 ──
    with g.subgraph(name="cluster_output") as c:
        c.attr(label="数据输出", style="filled", fillcolor="#e8f5e9", fontsize="18")
        c.node("OUT1", "前端 UI 渲染", fillcolor="#66bb6a", fontcolor="white")
        c.node("OUT2", "Evaluation 指标\nMTTR/覆盖率/准确率", fillcolor="#66bb6a", fontcolor="white")
        c.node("OUT3", "通知渠道\nSlack / Teams", fillcolor="#66bb6a", fontcolor="white")

    # ── 连线 ──
    g.edge("IN1", "GW1")
    g.edge("IN2", "GW2")
    g.edge("IN3", "GW3")
    g.edge("IN4", "GW4")

    for gw in ["GW1", "GW2", "GW3", "GW4"]:
        g.edge(gw, "VAL")
    g.edge("VAL", "CMD")
    g.edge("VAL", "QRY")

    # Command → Entities
    for ent in ["AG_REG", "CONV", "INC", "AR_ENT", "WF_DEF", "TOOL_ENT", "SKILL_ENT", "DS_ENT", "LLM_ENT", "SB_ENT"]:
        g.edge("CMD", ent, style="dotted", color="#aaa")

    # 实体关联
    g.edge("INC", "CONV", label="创建对话")
    g.edge("CONV", "SESS", label="关联")
    g.edge("AR_ENT", "INC", label="匹配触发")
    g.edge("INC", "SKILL_ENT", label="引用/生成")
    g.edge("SKILL_ENT", "AR_ENT", label="绑定到", style="dashed")
    g.edge("AR_ENT", "CANARY_ENT", label="金丝雀")
    g.edge("INC", "PROMPT_ENT", label="反馈")

    # Agent 运行时
    g.edge("AG_REG", "AGENT_RT")
    g.edge("AGENT_RT", "LLM_CALL")
    g.edge("AGENT_RT", "TOOL_CALL")
    g.edge("AGENT_RT", "DS_CALL")
    g.edge("AGENT_RT", "SB_CALL")
    g.edge("TOOL_CALL", "TOOL_ENT")
    g.edge("TOOL_ENT", "MCP_ITEM")
    g.edge("DS_CALL", "DS_ENT")

    # 工作流
    g.edge("WF_DEF", "WF_EXE", label="执行")
    g.edge("WF_EXE", "AGENT_RT")

    # 持久化
    for ent in ["AG_REG", "CONV", "SESS", "INC", "AR_ENT", "WF_DEF", "WF_EXE",
                 "TOOL_ENT", "MCP_ITEM", "SKILL_ENT", "DS_ENT", "LLM_ENT",
                 "SB_ENT", "CANARY_ENT", "PROMPT_ENT"]:
        g.edge(ent, "PG", style="dotted", color="#bbbbbb", arrowhead="none")
    g.edge("SKILL_ENT", "S3", label="文件包", style="dashed")

    # 实时推送
    g.edge("WF_EXE", "SR_WF")
    g.edge("INC", "SR_INC")

    # 输出
    g.edge("SR_WF", "OUT1")
    g.edge("SR_INC", "OUT1")
    g.edge("QRY", "OUT1")
    g.edge("INC", "OUT2")
    g.edge("INC", "OUT3")

    g.render(os.path.join(OUTPUT_DIR, "03_data_flow"), cleanup=True)
    print("✅ 03_data_flow.png")


# ═══════════════════════════════════════════════════════════════
# 图 4: 系统架构图 — 分层架构
# ═══════════════════════════════════════════════════════════════
def gen_architecture():
    g = graphviz.Digraph(
        "architecture",
        format="png",
        engine="dot",
        graph_attr={
            "label": "CoreSRE 系统架构图 — Clean Architecture 分层",
            "labelloc": "t",
            "fontsize": "32",
            "rankdir": "TB",
            "bgcolor": "#fafafa",
            "pad": "0.6",
            "nodesep": "0.4",
            "ranksep": "0.9",
            "dpi": "200",
            "compound": "true",
        },
    )
    set_chinese_font(g)
    g.node_attr.update(shape="box", style="rounded,filled", fontsize="14")
    g.edge_attr.update(fontsize="12", color="#888888")

    # ── 客户端层 ──
    with g.subgraph(name="cluster_client") as c:
        c.attr(label="客户端层 (React + TypeScript + Vite + shadcn/ui)", style="filled", fillcolor="#e0f2f1", fontsize="18", color="#26a69a")
        c.node("FE", "React SPA\n32 页面 · 11 模块", fillcolor="#80cbc4")
        c.node("AGUI", "AG-UI Protocol\nSSE 流式对话", fillcolor="#80cbc4")
        c.node("SRC", "SignalR Client\n实时推送", fillcolor="#80cbc4")

    # ── API 层 ──
    with g.subgraph(name="cluster_api_layer") as c:
        c.attr(label="API 网关层 (.NET 9 Minimal API)", style="filled", fillcolor="#e8eaf6", fontsize="18", color="#5c6bc0")
        c.node("EP", "16 组 Endpoints\nMinimal API Routes", fillcolor="#9fa8da")
        c.node("MW", "ExceptionHandling\nMiddleware", fillcolor="#9fa8da")
        c.node("WS", "WebSocket Handler\n沙箱终端", fillcolor="#9fa8da")
        c.node("HUBS", "SignalR Hubs\nWorkflow + Incident", fillcolor="#7986cb", fontcolor="white")

    # ── 应用层 ──
    with g.subgraph(name="cluster_app_layer") as c:
        c.attr(label="应用层 (CQRS + MediatR)", style="filled", fillcolor="#f3e5f5", fontsize="18", color="#8e24aa")
        c.node("MR", "MediatR Pipeline", fillcolor="#ce93d8")
        c.node("VB", "ValidationBehavior\nFluentValidation 前置", fillcolor="#ce93d8")
        c.node("CMD_H", "Command Handlers\n写操作 (15 模块)", fillcolor="#ba68c8", fontcolor="white")
        c.node("QRY_H", "Query Handlers\n读操作", fillcolor="#ba68c8", fontcolor="white")
        c.node("AM_M", "AutoMapper\nDTO ↔ Entity", fillcolor="#ce93d8")

    # ── 领域层 ──
    with g.subgraph(name="cluster_domain_layer") as c:
        c.attr(label="领域层 (DDD)", style="filled", fillcolor="#fff8e1", fontsize="18", color="#f9a825")
        c.node("ENT", "16 实体 (聚合根)\nAgentRegistration · Incident\nWorkflowDefinition · AlertRule ...", fillcolor="#ffd54f", shape="component")
        c.node("VO_N", "39 值对象 (JSONB)\nLlmConfigVO · TeamConfigVO\nWorkflowGraphVO · SopStepDefinition ...", fillcolor="#ffecb3", shape="component")
        c.node("ENUM_N", "33 枚举\nAgentType · IncidentStatus\nWorkflowNodeType ...", fillcolor="#fff9c4", shape="component")
        c.node("REPO_IF", "18 仓储接口\nIAgentRegistrationRepository\nIIncidentRepository ...", fillcolor="#ffe0b2", shape="component")

    # ── 基础设施层 ──
    with g.subgraph(name="cluster_infra_layer") as c:
        c.attr(label="基础设施层", style="filled", fillcolor="#fce4ec", fontsize="18", color="#e91e63")

        with c.subgraph(name="cluster_agent_rt_arch") as s:
            s.attr(label="Agent 运行时", style="filled", fillcolor="#f8bbd0")
            s.node("ARS", "AgentResolverService", fillcolor="#f48fb1")
            s.node("TOS", "TeamOrchestratorService", fillcolor="#f48fb1")
            s.node("ACS", "AgentCallerService", fillcolor="#f48fb1")

        with c.subgraph(name="cluster_tool_gw") as s:
            s.attr(label="工具网关", style="filled", fillcolor="#f8bbd0")
            s.node("REST_I", "RestApiToolInvoker", fillcolor="#f48fb1")
            s.node("MCP_I", "McpToolInvoker", fillcolor="#f48fb1")
            s.node("TFF_A", "ToolFunctionFactory", fillcolor="#f48fb1")
            s.node("OAP_A", "OpenApiParserService", fillcolor="#f48fb1")

        with c.subgraph(name="cluster_ds_int") as s:
            s.attr(label="数据源集成 (8 Queriers)", style="filled", fillcolor="#f8bbd0")
            s.node("DSF_A", "DataSourceQuerierFactory", fillcolor="#f48fb1")
            s.node("Q_LIST", "Prometheus · Loki · Jaeger\nK8s · ArgoCD · GitHub\nGitLab · Alertmanager", fillcolor="#f48fb1")

        with c.subgraph(name="cluster_sre_eng") as s:
            s.attr(label="SRE 告警引擎", style="filled", fillcolor="#f8bbd0")
            s.node("IDS_A", "IncidentDispatcher", fillcolor="#f48fb1")
            s.node("AIST_A", "ActiveIncidentTracker", fillcolor="#f48fb1")
            s.node("SOP_PARSE", "SopParser + Validator", fillcolor="#f48fb1")

        with c.subgraph(name="cluster_wf_eng_arch") as s:
            s.attr(label="工作流引擎", style="filled", fillcolor="#f8bbd0")
            s.node("WE_A", "WorkflowEngine (DAG)", fillcolor="#f48fb1")
            s.node("V8_A", "V8 ClearScript\nExpression Evaluator", fillcolor="#f48fb1")
            s.node("BG_W", "Background Worker\nChannel<T>", fillcolor="#f48fb1")

        with c.subgraph(name="cluster_sandbox") as s:
            s.attr(label="K8s 沙箱", style="filled", fillcolor="#f8bbd0")
            s.node("K8SC_A", "Kubernetes Client", fillcolor="#f48fb1")
            s.node("SPP_A", "SandboxPodPool", fillcolor="#f48fb1")
            s.node("PSM_A", "PersistentSandboxManager", fillcolor="#f48fb1")

        with c.subgraph(name="cluster_persist_arch") as s:
            s.attr(label="持久化", style="filled", fillcolor="#f8bbd0")
            s.node("EF_A", "EF Core + Npgsql\n15 仓储实现", fillcolor="#f48fb1")
            s.node("MINIO_A", "MinioFileStorage\n+ S3 SkillsProvider", fillcolor="#f48fb1")

    # ── Aspire 编排 ──
    with g.subgraph(name="cluster_aspire") as c:
        c.attr(label="基础设施 (Aspire 编排)", style="filled", fillcolor="#e3f2fd", fontsize="18", color="#1565c0")
        c.node("PG_A", "PostgreSQL\npgvector:pg17", shape="cylinder", fillcolor="#1e88e5", fontcolor="white")
        c.node("S3_A", "MinIO\nS3 对象存储", shape="cylinder", fillcolor="#1e88e5", fontcolor="white")

    # ── 外部 ──
    with g.subgraph(name="cluster_ext_arch") as c:
        c.attr(label="外部依赖", style="filled", fillcolor="#efebe9", fontsize="18")
        c.node("LLM_A", "LLM Providers\n(OpenAI 兼容)", fillcolor="#845ef7", fontcolor="white")
        c.node("EXT_MCP_A", "MCP Servers", fillcolor="#d7ccc8")
        c.node("EXT_REST_A", "REST APIs", fillcolor="#d7ccc8")
        c.node("EXT_OBS", "可观测性栈\nPrometheus/Loki/Jaeger", fillcolor="#d7ccc8")
        c.node("EXT_K8S", "K8s Cluster", fillcolor="#d7ccc8")
        c.node("EXT_GIT", "Git Platforms", fillcolor="#d7ccc8")

    # ── 连线（层间调用）──
    g.edge("FE", "EP")
    g.edge("AGUI", "EP")
    g.edge("SRC", "HUBS")
    g.edge("EP", "MW")
    g.edge("MW", "MR")
    g.edge("MR", "VB")
    g.edge("VB", "CMD_H")
    g.edge("VB", "QRY_H")
    g.edge("CMD_H", "ENT")
    g.edge("QRY_H", "ENT")
    g.edge("ENT", "REPO_IF")
    g.edge("REPO_IF", "EF_A")
    g.edge("EF_A", "PG_A")
    g.edge("MINIO_A", "S3_A")
    g.edge("ARS", "LLM_A")
    g.edge("TOS", "ARS")
    g.edge("REST_I", "EXT_REST_A")
    g.edge("MCP_I", "EXT_MCP_A")
    g.edge("DSF_A", "Q_LIST")
    g.edge("Q_LIST", "EXT_OBS")
    g.edge("K8SC_A", "EXT_K8S")

    g.render(os.path.join(OUTPUT_DIR, "04_architecture"), cleanup=True)
    print("✅ 04_architecture.png")


# ═══════════════════════════════════════════════════════════════
# 图 5: 概念图 — 领域模型关系
# ═══════════════════════════════════════════════════════════════
def gen_concept_map():
    g = graphviz.Digraph(
        "concept_map",
        format="png",
        engine="neato",
        graph_attr={
            "label": "CoreSRE 概念图 — 领域模型关系",
            "labelloc": "t",
            "fontsize": "32",
            "bgcolor": "#fafafa",
            "pad": "1.2",
            "overlap": "false",
            "splines": "true",
            "dpi": "200",
        },
    )
    set_chinese_font(g)
    g.node_attr.update(shape="ellipse", style="filled", fontsize="14")
    g.edge_attr.update(fontsize="12", color="#888888")

    # ── 核心概念 ──
    g.node("AGENT", "Agent\n智能体", fillcolor="#845ef7", fontcolor="white", width="2.2", height="1.5", fontsize="18")
    g.node("TOOL", "Tool\n工具", fillcolor="#fab005", fontcolor="black", width="1.8")
    g.node("SKILL", "Skill / SOP\n技能 · 标准操作程序", fillcolor="#339af0", fontcolor="white", width="2.4")
    g.node("DS", "DataSource\n数据源", fillcolor="#20c997", fontcolor="white", width="1.8")
    g.node("WF", "Workflow\n工作流", fillcolor="#fd7e14", fontcolor="white", width="1.8")
    g.node("LLM_C", "LLM Provider\n大模型服务", fillcolor="#845ef7", fontcolor="white", width="1.8")
    g.node("SANDBOX_C", "Sandbox\n沙箱环境", fillcolor="#80deea", width="1.8")

    # ── 事故响应 ──
    g.node("ALERT_C", "Alert\n告警", fillcolor="#ff6b6b", fontcolor="white", width="1.5")
    g.node("RULE_C", "AlertRule\n路由规则", fillcolor="#e64980", fontcolor="white", width="1.5")
    g.node("INC_C", "Incident\n故障事故", fillcolor="#ff6b6b", fontcolor="white", width="1.8", fontsize="16")
    g.node("SOP_EXEC_C", "SOP 执行\n标准流程", fillcolor="#c8e6c9", width="1.5")
    g.node("RCA_C", "RCA\n根因分析", fillcolor="#bbdefb", width="1.5")
    g.node("HITL_C", "HITL\n人工介入", fillcolor="#ffd54f", width="1.5")
    g.node("CANARY_C", "Canary\n金丝雀验证", fillcolor="#80deea", width="1.5")

    # ── 协作 ──
    g.node("CONV_C", "Conversation\n对话", fillcolor="#ffd54f", width="1.8")
    g.node("TEAM_C", "Team\n多Agent协作", fillcolor="#845ef7", fontcolor="white", width="1.5")

    # ── 评估 ──
    g.node("EVAL_C", "Evaluation\n效能评估", fillcolor="#51cf66", fontcolor="white", width="1.8")
    g.node("PM_C", "Post-Mortem\n事后复盘", fillcolor="#c5cae9", width="1.5")
    g.node("PROMPT_C", "Prompt 优化\n提示词改进", fillcolor="#d1c4e9", width="1.5")

    # ── Agent 为中心的关系 ──
    g.edge("AGENT", "TOOL", label="使用")
    g.edge("AGENT", "SKILL", label="装备")
    g.edge("AGENT", "DS", label="查询")
    g.edge("AGENT", "LLM_C", label="调用")
    g.edge("AGENT", "SANDBOX_C", label="操作")
    g.edge("AGENT", "CONV_C", label="参与")
    g.edge("AGENT", "WF", label="编排为")
    g.edge("TEAM_C", "AGENT", label="编排多个")

    # ── Agent 类型 ──
    g.edge("AGENT", "TEAM_C", label="类型: Team", style="dashed")

    # ── 告警处置 ──
    g.edge("ALERT_C", "RULE_C", label="匹配")
    g.edge("RULE_C", "INC_C", label="创建")
    g.edge("RULE_C", "SKILL", label="绑定 SOP")
    g.edge("INC_C", "SOP_EXEC_C", label="链路A")
    g.edge("INC_C", "RCA_C", label="链路B")
    g.edge("SOP_EXEC_C", "SKILL", label="执行")
    g.edge("SOP_EXEC_C", "RCA_C", label="失败降级", style="dashed")
    g.edge("RCA_C", "SKILL", label="链路C 生成", style="dashed")
    g.edge("SOP_EXEC_C", "HITL_C", label="需要时")
    g.edge("RULE_C", "CANARY_C", label="验证")

    # ── 评估闭环 ──
    g.edge("INC_C", "EVAL_C", label="产生")
    g.edge("INC_C", "PM_C", label="标注")
    g.edge("PM_C", "PROMPT_C", label="驱动")
    g.edge("PROMPT_C", "AGENT", label="优化", style="dashed")
    g.edge("EVAL_C", "SKILL", label="衡量")

    # ── 对话 ──
    g.edge("INC_C", "CONV_C", label="关联")

    # ── 数据源类别(注释节点) ──
    g.node("DS_NOTE", "Metrics: Prometheus/VictoriaMetrics/Mimir\nLogs: Loki/ES\nTracing: Jaeger/Tempo\nAlerting: Alertmanager/PagerDuty\nDeployment: K8s/ArgoCD\nGit: GitHub/GitLab",
           shape="note", fillcolor="#e0f2f1", fontsize="11", width="3.0")
    g.edge("DS", "DS_NOTE", style="dotted", arrowhead="none")

    g.render(os.path.join(OUTPUT_DIR, "05_concept_map"), cleanup=True)
    print("✅ 05_concept_map.png")


# ═══════════════════════════════════════════════════════════════
# 图 6: 完整 AgentLoop — 框架原生 + CoreSRE 增强
# ═══════════════════════════════════════════════════════════════
def gen_full_agent_loop():
    g = graphviz.Digraph(
        "full_agent_loop",
        format="png",
        engine="dot",
        graph_attr={
            "label": "CoreSRE 完整 AgentLoop — 框架原生流程 + 8 大增强",
            "labelloc": "t",
            "fontsize": "34",
            "rankdir": "TB",
            "bgcolor": "#fafafa",
            "pad": "0.7",
            "nodesep": "0.45",
            "ranksep": "0.65",
            "dpi": "200",
            "compound": "true",
        },
    )
    set_chinese_font(g)
    g.node_attr.update(shape="box", style="rounded,filled", fontsize="13", height="0.5")
    g.edge_attr.update(fontsize="11", color="#666666")

    # ========================== 色彩约定 ==========================
    # 框架原生: 紫色系 (#845ef7 / #ede7f6)
    # CoreSRE 增强: 各子系统独立颜色，外框用虚线 + ★ 标记
    # ==============================================================

    # ━━━━━━━━━ 阶段 0: 外层调度 (CoreSRE 增强) ━━━━━━━━━
    with g.subgraph(name="cluster_outer") as c:
        c.attr(label="★ 阶段 0 — CoreSRE 外层调度 (框架之上)", style="dashed,filled", fillcolor="#e3f2fd", fontsize="18", color="#1565c0", penwidth="2")

        # ── 触发入口 ──
        with c.subgraph(name="cluster_trigger") as s:
            s.attr(label="触发入口", style="filled", fillcolor="#bbdefb", fontsize="14", color="#1976d2")
            s.node("ALERT_WH", "Alertmanager\nWebhook", fillcolor="#ff6b6b", fontcolor="white")
            s.node("USER_CHAT", "用户对话\nAG-UI SSE", fillcolor="#80cbc4")
            s.node("WF_TRIGGER", "工作流触发\nDAG 执行", fillcolor="#ffcc80")

        # ── AgentResolver (增强①) ──
        with c.subgraph(name="cluster_resolver") as s:
            s.attr(label="★① AgentResolver — 动态构建", style="filled", fillcolor="#e3f2fd", fontsize="14", color="#1565c0")
            s.node("RESOLVE", "AgentResolverService\n.ResolveAsync(agentId)", fillcolor="#42a5f5", fontcolor="white")
            s.node("DB_REG", "AgentRegistration\n(DB 配置)", fillcolor="#90caf9", shape="cylinder")
            s.node("BUILD_CLIENT", "构建 IChatClient\n+ LlmProvider 配置", fillcolor="#64b5f6", fontcolor="white")

        # ── 能力注入 ──
        with c.subgraph(name="cluster_inject") as s:
            s.attr(label="★ 能力注入 (Resolver 内部)", style="filled", fillcolor="#e8f5e9", fontsize="14", color="#2e7d32")
            s.node("INJ_TOOL", "④ ToolFunctionFactory\nREST API + MCP", fillcolor="#ef5350", fontcolor="white")
            s.node("INJ_DS", "④ DataSourceFunction\nFactory · 8种数据源", fillcolor="#e57373", fontcolor="white")
            s.node("INJ_SB", "④ KubernetesSandbox\nToolProvider · 6函数", fillcolor="#ef9a9a")
            s.node("INJ_SKILL", "② S3AgentSkills\nProvider (AIContext)", fillcolor="#81c784")
            s.node("INJ_SOP", "② SopContextInit\nProvider (AIContext)", fillcolor="#66bb6a", fontcolor="white")
            s.node("INJ_MEM", "② FixedChatHistory\nMemory pgvector", fillcolor="#a5d6a7")
            s.node("INJ_HIST", "③ PostgresChatHistory\nProvider (JSONB)", fillcolor="#ffd54f")

        # ── HITL 调度 (增强⑥) ──
        with c.subgraph(name="cluster_hitl_dispatch") as s:
            s.attr(label="★⑥ HITL 事故调度", style="filled", fillcolor="#fff3e0", fontsize="14", color="#e65100")
            s.node("INC_DISP", "IncidentDispatcher\nService", fillcolor="#ff9800", fontcolor="white")
            s.node("HITL_LOOP", "RunAgentWith\nInterventionAsync()", fillcolor="#ffa726")

        # ── Team 编排 (增强⑤) ──
        with c.subgraph(name="cluster_team") as s:
            s.attr(label="★⑤ 多 Agent 编排", style="filled", fillcolor="#f3e5f5", fontsize="14", color="#6a1b9a")
            s.node("TEAM_ORCH", "TeamOrchestrator\nService", fillcolor="#ce93d8")
            s.node("GCM_SEL", "LlmSelector\nGroupChatMgr", fillcolor="#ba68c8", fontcolor="white")
            s.node("GCM_MAG", "MagneticOne\nGroupChatMgr", fillcolor="#9c27b0", fontcolor="white")

        # ── DAG 工作流 (增强⑧) ──
        with c.subgraph(name="cluster_dag") as s:
            s.attr(label="★⑧ DAG 工作流引擎", style="filled", fillcolor="#eceff1", fontsize="14", color="#37474f")
            s.node("WF_ENGINE", "WorkflowEngine\nDAG 执行", fillcolor="#78909c", fontcolor="white")
            s.node("WF_NODES", "Agent·Tool·Condition\nFanOut·FanIn", fillcolor="#b0bec5", fontsize="11")

    # 外层调度连线
    g.edge("ALERT_WH", "INC_DISP", label="告警触发")
    g.edge("USER_CHAT", "RESOLVE", label="对话")
    g.edge("WF_TRIGGER", "WF_ENGINE", label="DAG")
    g.edge("INC_DISP", "RESOLVE", label="解析 Agent")

    g.edge("DB_REG", "RESOLVE")
    g.edge("RESOLVE", "BUILD_CLIENT")

    # 能力注入连线
    g.edge("BUILD_CLIENT", "INJ_TOOL", style="dotted", arrowhead="none")
    g.edge("BUILD_CLIENT", "INJ_DS", style="dotted", arrowhead="none")
    g.edge("BUILD_CLIENT", "INJ_SB", style="dotted", arrowhead="none")
    g.edge("BUILD_CLIENT", "INJ_SKILL", style="dotted", arrowhead="none")
    g.edge("BUILD_CLIENT", "INJ_SOP", style="dotted", arrowhead="none")
    g.edge("BUILD_CLIENT", "INJ_MEM", style="dotted", arrowhead="none")
    g.edge("BUILD_CLIENT", "INJ_HIST", style="dotted", arrowhead="none")

    # Team 连线
    g.edge("RESOLVE", "TEAM_ORCH", label="Team Agent", style="dashed")
    g.edge("TEAM_ORCH", "GCM_SEL", style="dotted", arrowhead="none")
    g.edge("TEAM_ORCH", "GCM_MAG", style="dotted", arrowhead="none")

    # HITL 连线
    g.edge("INC_DISP", "HITL_LOOP")

    # WF 连线
    g.edge("WF_ENGINE", "WF_NODES", style="dotted", arrowhead="none")

    # ━━━━━━━━━ 产出: ChatClientAgent (AIAgent) ━━━━━━━━━
    g.node("AGENT_INST", "ChatClientAgent 实例\n(携带完整能力栈)", fillcolor="#845ef7", fontcolor="white", width="3.8", fontsize="15", shape="box3d")

    g.edge("BUILD_CLIENT", "AGENT_INST", label="chatClient.AsAIAgent(options)", color="#845ef7", penwidth="2")

    # ━━━━━━━━━ 阶段 1: 调用入口 (框架原生) ━━━━━━━━━
    with g.subgraph(name="cluster_entry") as c:
        c.attr(label="阶段 1 — 调用入口 (AIAgent 基类)", style="filled", fillcolor="#ede7f6", fontsize="18", color="#673ab7")
        c.node("RUN_STR", "RunAsync(string)", fillcolor="#d1c4e9")
        c.node("RUN_MSG", "RunAsync(ChatMessage)", fillcolor="#d1c4e9")
        c.node("RUN_ENUM", "RunAsync(IEnumerable)", fillcolor="#b39ddb", fontcolor="white")
        c.node("RUN_STREAM", "RunStreamingAsync()", fillcolor="#b39ddb", fontcolor="white")

    g.edge("RUN_STR", "RUN_MSG", label="包装 User", fontsize="10")
    g.edge("RUN_MSG", "RUN_ENUM", label="→ []", fontsize="10")

    # 外层→入口
    g.edge("HITL_LOOP", "RUN_STREAM", label="★⑥ 流式调用\n+ 介入检查", color="#e65100", penwidth="2")
    g.edge("AGENT_INST", "RUN_ENUM", style="invis")  # layout hint
    g.edge("TEAM_ORCH", "RUN_STREAM", label="编排调用", style="dashed", color="#6a1b9a")
    g.edge("WF_ENGINE", "RUN_STREAM", label="Agent 节点\n调用", style="dashed", color="#37474f")
    g.edge("USER_CHAT", "RUN_STREAM", label="直接对话", style="dashed", color="#26a69a")

    # 设置 RunContext
    g.node("SET_CTX", "CurrentRunContext = new AgentRunContext(...)\n(AsyncLocal 跨异步流转)", shape="parallelogram", fillcolor="#fff9c4", fontsize="12")
    g.edge("RUN_ENUM", "SET_CTX")
    g.edge("RUN_STREAM", "SET_CTX")

    # ━━━━━━━━━ 阶段 2: PrepareSessionAndMessages (框架原生 + 增强②③) ━━━━━━━━━
    with g.subgraph(name="cluster_prepare") as c:
        c.attr(label="阶段 2 — PrepareSessionAndMessagesAsync()                    框架原生, ★②③ 在此阶段注入", style="filled", fillcolor="#e8eaf6", fontsize="16", color="#5c6bc0")

        c.node("MERGE_OPTS", "2a. CreateConfiguredChatOptions()\n合并 AgentOptions + RunOptions\n(Instructions/Tools/Model/Temperature)", fillcolor="#c5cae9")
        c.node("ENSURE_SESS", "2b. session ?? CreateSessionAsync()\n确保会话存在", fillcolor="#c5cae9")

        # ChatHistoryProvider
        with c.subgraph(name="cluster_hist_step") as s:
            s.attr(label="2c. ChatHistoryProvider.InvokingAsync()", style="filled", fillcolor="#fff8e1", fontsize="13", color="#f9a825")
            s.node("HIST_LOAD", "ProvideChatHistoryAsync()\n加载历史消息", fillcolor="#fff9c4")
            s.node("HIST_MERGE", "历史消息 + 输入消息\n→ 合并后的完整消息列表", fillcolor="#fff9c4")

        c.node("HIST_PG_TAG", "★③ PostgresChatHistoryProvider\nJSONB 持久化 + IChatReducer", fillcolor="#ffd54f", shape="note", fontsize="11")

        # AIContextProvider loop
        with c.subgraph(name="cluster_ctx_loop") as s:
            s.attr(label="2d. foreach AIContextProvider.InvokingAsync()", style="filled", fillcolor="#e8f5e9", fontsize="13", color="#2e7d32")
            s.node("CTX_FILTER", "过滤: 只传 External 消息\n→ InvokingContext", fillcolor="#e8f5e9", shape="note", fontsize="11")
            s.node("CTX_PROVIDE", "ProvideAIContextAsync()\n→ AIContext{Instructions,\nMessages, Tools}", fillcolor="#81c784")
            s.node("CTX_MERGE", "累积合并:\nInstructions 拼接\nMessages 拼接\nTools 拼接", fillcolor="#66bb6a", fontcolor="white")

        c.node("CTX_PROVIDERS_TAG", "★② CoreSRE 注入了 3 个 Provider:\n· SopContextInitProvider (事故预查询)\n· S3AgentSkillsProvider (MinIO 技能包)\n· FixedChatHistoryMemory (pgvector 语义)", fillcolor="#c8e6c9", shape="note", fontsize="10")

    g.edge("SET_CTX", "MERGE_OPTS", label="RunCoreAsync()")
    g.edge("MERGE_OPTS", "ENSURE_SESS")
    g.edge("ENSURE_SESS", "HIST_LOAD")
    g.edge("HIST_LOAD", "HIST_MERGE")
    g.edge("HIST_MERGE", "CTX_FILTER")
    g.edge("CTX_FILTER", "CTX_PROVIDE")
    g.edge("CTX_PROVIDE", "CTX_MERGE")
    g.edge("CTX_MERGE", "CTX_FILTER", style="dashed", label="下一个 Provider", constraint="false", fontsize="10")
    g.edge("HIST_PG_TAG", "HIST_LOAD", style="dotted", arrowhead="none", constraint="false")
    g.edge("CTX_PROVIDERS_TAG", "CTX_PROVIDE", style="dotted", arrowhead="none", constraint="false")

    # ━━━━━━━━━ 阶段 3: IChatClient 中间件管道 (框架原生 + 增强④⑦) ━━━━━━━━━
    with g.subgraph(name="cluster_chatclient") as c:
        c.attr(label="阶段 3 — IChatClient 中间件管道                    框架原生, ★④⑦ 在此阶段生效", style="filled", fillcolor="#fce4ec", fontsize="16", color="#c62828")

        c.node("XFORM", "ApplyRunOptionsTransformations()\nChatClientFactory 装饰器链", fillcolor="#f8bbd0")

        # 装饰器管道
        c.node("NORM_TAG", "★⑦ ToolCallNormalizing\nChatClient\nnull args → {}\n(Bedrock/Anthropic 兼容)", fillcolor="#4dd0e1", shape="note", fontsize="11")

        c.node("FUNC_INVOKE", "FunctionInvokingChatClient\n(框架自动注入)", fillcolor="#f48fb1")

        c.node("LLM_CALL", "底层 IChatClient\n→ LLM API 调用\n(OpenAI Compatible)", fillcolor="#ec407a", fontcolor="white")

    g.edge("CTX_MERGE", "XFORM", ltail="cluster_ctx_loop")
    g.edge("XFORM", "FUNC_INVOKE")
    g.edge("NORM_TAG", "FUNC_INVOKE", style="dotted", arrowhead="none", constraint="false")
    g.edge("FUNC_INVOKE", "LLM_CALL")

    # ━━━━━━━━━ 阶段 4: Tool-Call 自动循环 (框架原生 + 增强④ 提供工具) ━━━━━━━━━
    with g.subgraph(name="cluster_toolloop") as c:
        c.attr(label="阶段 4 — Tool-Call Auto Loop                    ★④ 的 AIFunction 在此被调用", style="filled", fillcolor="#fff3e0", fontsize="16", color="#ff9800")

        c.node("LLM_RESP", "LLM 返回 ChatResponse", fillcolor="#ffe0b2")
        c.node("HAS_TOOL", "包含\nFunctionCallContent?", shape="diamond", fillcolor="#ffcc80", width="1.3")
        c.node("INVOKE_FN", "调用 AIFunction (执行工具)", fillcolor="#ffb74d")

        # 工具来源标注
        c.node("TOOL_SRC", "★④ 工具来源:\n· ToolRegistrationAIFunction (REST)\n· McpToolAIFunction (MCP)\n· DataSource Query 函数\n· Sandbox run_command/read_file\n· S3 load_skill/read_resource", fillcolor="#fff3e0", shape="note", fontsize="10")

        c.node("APPEND_RES", "附加工具结果到消息\n→ 再次调用 LLM", fillcolor="#ffa726")

    g.edge("LLM_CALL", "LLM_RESP")
    g.edge("LLM_RESP", "HAS_TOOL")
    g.edge("HAS_TOOL", "INVOKE_FN", label="是")
    g.edge("INVOKE_FN", "APPEND_RES")
    g.edge("APPEND_RES", "LLM_CALL", constraint="false", style="dashed", label="循环", color="#ff9800")
    g.edge("TOOL_SRC", "INVOKE_FN", style="dotted", arrowhead="none", constraint="false")

    # ━━━━━━━━━ 阶段 5: 后处理 (框架原生 + 增强③②) ━━━━━━━━━
    with g.subgraph(name="cluster_post") as c:
        c.attr(label="阶段 5 — 后处理 & 持久化", style="filled", fillcolor="#e8f5e9", fontsize="16", color="#2e7d32")

        c.node("POST", "UpdateSessionConversationId\n+ 设置 AuthorName", fillcolor="#a5d6a7")
        c.node("NOTIFY_HIST", "ChatHistoryProvider.InvokedAsync()\n★③ → PostgreSQL 持久化新消息", fillcolor="#81c784")
        c.node("NOTIFY_CTX", "AIContextProvider[].InvokedAsync()\n★② → 通知 3 个上下文提供者", fillcolor="#81c784")
        c.node("RETURN", "return AgentResponse\n(Messages + Usage + AgentId)", fillcolor="#4caf50", fontcolor="white")

    g.edge("HAS_TOOL", "POST", label="否 (最终回复)")
    g.edge("POST", "NOTIFY_HIST")
    g.edge("NOTIFY_HIST", "NOTIFY_CTX")
    g.edge("NOTIFY_CTX", "RETURN")

    # ━━━━━━━━━ 阶段 6: HITL 外层循环 (增强⑥) ━━━━━━━━━
    with g.subgraph(name="cluster_hitl_outer") as c:
        c.attr(label="★阶段 6 — HITL 外层循环 (框架之上)", style="dashed,filled", fillcolor="#fff3e0", fontsize="16", color="#e65100", penwidth="2")

        c.node("HITL_STREAM", "SignalR 推送\n流式 token → 前端", fillcolor="#ffe0b2")
        c.node("HITL_CHECK", "检测 Agent 是否\n请求人工介入?", shape="diamond", fillcolor="#ffcc80", width="1.4")
        c.node("HITL_REQ", "RequestIntervention\n发送结构化请求\n阻塞等待人类响应", fillcolor="#ffa726")
        c.node("HITL_RESP", "人类审批/输入/选择\n→ Respond 解除阻塞", fillcolor="#ffcc80")
        c.node("HITL_INJECT", "TryInjectMessage\n人类主动注入消息", fillcolor="#ffe0b2")
        c.node("HITL_DONE", "事故处理完成\nUpdate Incident\n记录 MTTR", fillcolor="#4caf50", fontcolor="white")

    g.edge("RETURN", "HITL_STREAM", label="★⑥ 流式 token")
    g.edge("HITL_STREAM", "HITL_CHECK")
    g.edge("HITL_CHECK", "HITL_REQ", label="需要介入")
    g.edge("HITL_REQ", "HITL_RESP")
    g.edge("HITL_RESP", "RUN_STREAM", label="继续 Agent Loop", style="dashed", color="#e65100", constraint="false")
    g.edge("HITL_INJECT", "RUN_STREAM", label="注入消息", style="dashed", color="#e65100", constraint="false")
    g.edge("HITL_CHECK", "HITL_DONE", label="完成")

    g.render(os.path.join(OUTPUT_DIR, "06_full_agent_loop"), cleanup=True)
    print("✅ 06_full_agent_loop.png")


# ═══════════════════════════════════════════════════════════════
if __name__ == "__main__":
    print(f"输出目录: {OUTPUT_DIR}\n")
    gen_business_flow()
    gen_interaction_map()
    gen_data_flow()
    gen_architecture()
    gen_concept_map()
    gen_full_agent_loop()
    print(f"\n🎉 全部 6 张图已生成到 {OUTPUT_DIR}")
