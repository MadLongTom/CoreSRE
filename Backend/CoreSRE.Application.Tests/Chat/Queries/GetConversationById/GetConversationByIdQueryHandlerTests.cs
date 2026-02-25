using System.Text.Json;
using CoreSRE.Application.Chat.DTOs;
using CoreSRE.Application.Chat.Queries.GetConversationById;
using CoreSRE.Application.Common.Models;
using CoreSRE.Application.Interfaces;
using CoreSRE.Domain.Interfaces;
using AutoMapper;
using FluentAssertions;
using Moq;
using Xunit;

namespace CoreSRE.Application.Tests.Chat.Queries.GetConversationById;

/// <summary>
/// Tests for GetConversationByIdQueryHandler — specifically the ExtractMessages logic
/// which must handle both ChatClientAgentSession (top-level messages) and
/// WorkflowSession (checkpoint → stateData → PortableValue → AIAgentHostState → threadState).
///
/// Framework serialization format reference:
///   WorkflowSession → JsonMarshaller with custom converters:
///     ScopeKeyConverter:      "executorId||key"       (pipe-delimited, empty scopeName)
///     CheckpointInfoConverter: "runId|checkpointId"    (pipe-delimited)
///     PortableValue:           { typeId: {...}, value: {...} }
/// </summary>
public class GetConversationByIdQueryHandlerTests
{
    private static List<ChatMessageDto> InvokeExtractMessages(JsonElement sessionData)
    {
        var method = typeof(GetConversationByIdQueryHandler)
            .GetMethod("ExtractMessages", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        return (List<ChatMessageDto>)method.Invoke(null, [sessionData])!;
    }

    // ── Helper: builds a WorkflowSession JSON with the correct framework format ──

    /// <summary>
    /// Builds a realistic WorkflowSession JSON matching the framework's InMemoryCheckpointManager
    /// serialization with ScopeKeyConverter pipe-delimited keys and PortableValue wrappers.
    /// </summary>
    private static string BuildWorkflowSessionJson(
        string userMessage,
        Dictionary<string, string> executorMessages, // executorId → messages JSON array
        int checkpointStep = 3,
        string? runId = null)
    {
        runId ??= "run-" + Guid.NewGuid().ToString("N")[..8];
        var cpId = Guid.NewGuid().ToString("N");

        // Build stateData entries with ScopeKey pipe format: "executorId||AIAgentHostState"
        var stateDataEntries = string.Join(",\n", executorMessages.Select(kvp =>
            $$"""
                    "{{kvp.Key}}||AIAgentHostState": {
                      "typeId": { "assemblyName": "Microsoft.Agents.AI.Workflows", "typeName": "AIAgentHostState" },
                      "value": {
                        "threadState": {
                          "chatHistoryProviderState": {
                            "messages": {{kvp.Value}}
                          }
                        },
                        "currentTurnEmitEvents": true
                      }
                    }
            """));

        return $$"""
        {
          "runId": "{{runId}}",
          "lastCheckpoint": { "runId": "{{runId}}", "checkpointId": "{{cpId}}" },
          "chatHistoryProviderState": {
            "bookmark": 1,
            "messages": [
              { "role": "user", "contents": [{ "kind": "text", "text": "{{userMessage}}" }] }
            ]
          },
          "checkpointManager": {
            "store": {
              "{{runId}}": {
                "checkpointIndex": [
                  { "runId": "{{runId}}", "checkpointId": "{{cpId}}" }
                ],
                "cache": {
                  "{{runId}}|{{cpId}}": {
                    "stepNumber": {{checkpointStep}},
                    "stateData": {
                      {{stateDataEntries}}
                    }
                  }
                }
              }
            }
          }
        }
        """;
    }

    // ═══════════════════════════════════════════════════════════════════
    // ChatClientAgentSession tests
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void ExtractMessages_ChatClient_TopLevel_ReturnsFullConversation()
    {
        // ChatClientAgentSession: top-level chatHistoryProviderState has user + assistant
        var json = """
        {
            "chatHistoryProviderState": {
                "bookmark": 3,
                "messages": [
                    { "role": "user", "contents": [{ "kind": "text", "text": "Hello" }] },
                    { "role": "assistant", "contents": [{ "kind": "text", "text": "Hi there!" }] },
                    { "role": "user", "contents": [{ "kind": "text", "text": "How are you?" }] }
                ]
            }
        }
        """;
        var sessionData = JsonDocument.Parse(json).RootElement;

        var messages = InvokeExtractMessages(sessionData);

        messages.Should().HaveCount(3);
        messages[0].Role.Should().Be("user");
        messages[0].Content.Should().Be("Hello");
        messages[1].Role.Should().Be("assistant");
        messages[1].Content.Should().Be("Hi there!");
        messages[2].Role.Should().Be("user");
        messages[2].Content.Should().Be("How are you?");
    }

    [Fact]
    public void ExtractMessages_ChatClient_ToolCallsWithResults_MatchedCorrectly()
    {
        var json = """
        {
            "chatHistoryProviderState": {
                "bookmark": 3,
                "messages": [
                    { "role": "user", "contents": [{ "kind": "text", "text": "run diagnostics" }] },
                    {
                        "role": "assistant",
                        "contents": [
                            { "kind": "functionCall", "callId": "c1", "name": "kubectl_get", "arguments": "{\"resource\":\"pods\"}" },
                            { "kind": "functionCall", "callId": "c2", "name": "prometheus_query", "arguments": "{\"query\":\"up\"}" }
                        ]
                    },
                    {
                        "role": "tool",
                        "contents": [
                            { "kind": "functionResult", "callId": "c1", "result": "pod-1 Running" },
                            { "kind": "functionResult", "callId": "c2", "result": "1" }
                        ]
                    },
                    { "role": "assistant", "contents": [{ "kind": "text", "text": "All good" }] }
                ]
            }
        }
        """;
        var sessionData = JsonDocument.Parse(json).RootElement;

        var messages = InvokeExtractMessages(sessionData);

        messages.Should().HaveCount(3);
        var toolCallMsg = messages[1];
        toolCallMsg.ToolCalls.Should().HaveCount(2);
        toolCallMsg.ToolCalls![0].ToolCallId.Should().Be("c1");
        toolCallMsg.ToolCalls[0].Result.Should().Be("pod-1 Running");
        toolCallMsg.ToolCalls[1].ToolCallId.Should().Be("c2");
        toolCallMsg.ToolCalls[1].Result.Should().Be("1");
    }

    [Fact]
    public void ExtractMessages_ChatClient_SystemMemory_AttachedToNextUserMessage()
    {
        var json = """
        {
            "chatHistoryProviderState": {
                "messages": [
                    { "role": "system", "source": "memory", "contents": [{ "kind": "text", "text": "Previous K8s context" }] },
                    { "role": "user", "contents": [{ "kind": "text", "text": "status?" }] },
                    { "role": "assistant", "contents": [{ "kind": "text", "text": "Cluster is healthy" }] }
                ]
            }
        }
        """;
        var sessionData = JsonDocument.Parse(json).RootElement;

        var messages = InvokeExtractMessages(sessionData);

        messages.Should().HaveCount(2);
        messages[0].Role.Should().Be("user");
        messages[0].Content.Should().Be("status?");
        messages[0].MemoryContext.Should().Be("Previous K8s context");
    }

    // ═══════════════════════════════════════════════════════════════════
    // WorkflowSession tests (InMemoryCheckpointManager format)
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void ExtractMessages_WorkflowSession_ExtractsFromCheckpointStateData()
    {
        // WorkflowSession with correct framework JSON format:
        //   checkpointManager.store.<runId>.cache.<runId|cpId>.stateData
        //     .<executorId>||AIAgentHostState.value.threadState.chatHistoryProviderState.messages[]
        var agentMsgs = """
        [
            { "role": "user", "contents": [{ "kind": "text", "text": "测试" }] },
            { "role": "assistant", "contents": [{ "kind": "text", "text": "你好！我是运维Agent" }], "authorName": "ops-agent" },
            { "role": "assistant", "contents": [{ "kind": "functionCall", "callId": "call1", "name": "kubectl_get", "arguments": "{\"resource\":\"pods\"}" }], "authorName": "ops-agent" },
            { "role": "tool", "contents": [{ "kind": "functionResult", "callId": "call1", "result": "pod1 Running" }] },
            { "role": "assistant", "contents": [{ "kind": "text", "text": "诊断完成，系统正常。" }], "authorName": "ops-agent" }
        ]
        """;
        var json = BuildWorkflowSessionJson("测试", new() { ["ops_agent_abc123"] = agentMsgs });
        var sessionData = JsonDocument.Parse(json).RootElement;

        var messages = InvokeExtractMessages(sessionData);

        messages.Should().HaveCountGreaterThanOrEqualTo(3);
        messages[0].Role.Should().Be("user");
        messages[0].Content.Should().Be("测试");
        messages[1].Role.Should().Be("assistant");
        messages[1].Content.Should().Be("你好！我是运维Agent");
        messages[1].ParticipantAgentName.Should().Be("ops-agent");
        messages[2].ToolCalls.Should().NotBeNull();
        messages[2].ToolCalls![0].ToolName.Should().Be("kubectl_get");
        messages[2].ToolCalls[0].Result.Should().Be("pod1 Running");
        messages[3].Role.Should().Be("assistant");
        messages[3].Content.Should().Be("诊断完成，系统正常。");
    }

    [Fact]
    public void ExtractMessages_WorkflowSession_FallbackPicksExecutorWithMostMessages()
    {
        // When no executor has authorName attribution, fallback to message count
        var agentA = """
        [
            { "role": "user", "contents": [{ "kind": "text", "text": "check" }] },
            { "role": "assistant", "contents": [{ "kind": "text", "text": "short reply" }] }
        ]
        """;
        var agentB = """
        [
            { "role": "user", "contents": [{ "kind": "text", "text": "check" }] },
            { "role": "assistant", "contents": [{ "kind": "text", "text": "step 1" }] },
            { "role": "assistant", "contents": [{ "kind": "text", "text": "step 2" }] },
            { "role": "assistant", "contents": [{ "kind": "text", "text": "step 3" }] }
        ]
        """;
        var json = BuildWorkflowSessionJson("check", new()
        {
            ["agent_a"] = agentA,
            ["agent_b"] = agentB
        });
        var sessionData = JsonDocument.Parse(json).RootElement;

        var messages = InvokeExtractMessages(sessionData);

        // No authorName → score 0 for both → fallback to count: agent_b (4) > agent_a (2)
        messages.Should().HaveCount(4);
        messages[1].Content.Should().Be("step 1");
    }

    [Fact]
    public void ExtractMessages_WorkflowSession_PrefersAuthorNameOverMessageCount()
    {
        // The orchestrator's merge thread has MORE messages (6) but assistant messages lack authorName.
        // The participant thread has fewer messages (4) but assistant messages have authorName.
        // New scoring logic should prefer the participant thread.
        var orchestratorThread = """
        [
            { "role": "user", "contents": [{ "kind": "text", "text": "check cluster" }] },
            { "role": "user", "contents": [{ "kind": "text", "text": "ops-agent replied: pods look good" }] },
            { "role": "assistant", "contents": [{ "kind": "text", "text": "Let me check deeper" }] },
            { "role": "user", "contents": [{ "kind": "text", "text": "ops-agent replied: services are healthy" }] },
            { "role": "assistant", "contents": [{ "kind": "text", "text": "Task complete" }] },
            { "role": "user", "contents": [{ "kind": "text", "text": "ops-agent replied: final summary" }] }
        ]
        """;
        var participantThread = """
        [
            { "role": "user", "contents": [{ "kind": "text", "text": "check cluster" }] },
            { "role": "assistant", "contents": [{ "kind": "text", "text": "pods look good" }], "authorName": "ops-agent" },
            { "role": "assistant", "contents": [{ "kind": "text", "text": "services are healthy" }], "authorName": "ops-agent" },
            { "role": "assistant", "contents": [{ "kind": "text", "text": "final summary" }], "authorName": "ops-agent" }
        ]
        """;
        var json = BuildWorkflowSessionJson("check cluster", new()
        {
            ["orchestrator_abc"] = orchestratorThread,
            ["ops_agent_def"] = participantThread
        });
        var sessionData = JsonDocument.Parse(json).RootElement;

        var messages = InvokeExtractMessages(sessionData);

        // Should pick participant thread (score=3) over orchestrator (score=0 despite 6 messages)
        messages.Should().HaveCount(4);
        messages[0].Role.Should().Be("user");
        messages[0].Content.Should().Be("check cluster");
        messages[1].ParticipantAgentName.Should().Be("ops-agent");
        messages[1].Content.Should().Be("pods look good");
        messages[2].ParticipantAgentName.Should().Be("ops-agent");
        messages[3].ParticipantAgentName.Should().Be("ops-agent");
        messages[3].Content.Should().Be("final summary");
    }

    [Fact]
    public void ExtractMessages_WorkflowSession_FiltersOrchestratorMessages()
    {
        var agentMsgs = """
        [
            { "role": "user", "contents": [{ "kind": "text", "text": "测试" }] },
            { "role": "assistant", "contents": [{ "kind": "text", "text": "Hello!" }] },
            { "role": "user", "contents": [{ "kind": "text", "text": "Continue working on the task. Here's the plan: ## Facts\n1. User said hello\n## Plan\n1. Respond" }] },
            { "role": "assistant", "contents": [{ "kind": "text", "text": "Still working..." }] }
        ]
        """;
        var json = BuildWorkflowSessionJson("测试", new() { ["agent"] = agentMsgs });
        var sessionData = JsonDocument.Parse(json).RootElement;

        var messages = InvokeExtractMessages(sessionData);

        messages.Should().HaveCount(3);
        messages[0].Content.Should().Be("测试");
        messages[1].Content.Should().Be("Hello!");
        messages[2].Content.Should().Be("Still working...");
    }

    [Fact]
    public void ExtractMessages_WorkflowSession_FiltersOrchestratorByAuthorName()
    {
        // MagneticOne orchestrator injects user-role messages with authorName="Orchestrator"
        // These may contain arbitrary instructions that don't match heuristic patterns
        var agentMsgs = """
        [
            { "role": "user", "contents": [{ "kind": "text", "text": "你好" }] },
            { "role": "assistant", "contents": [{ "kind": "text", "text": "你好！" }], "authorName": "ops-agent" },
            { "role": "user", "contents": [{ "kind": "text", "text": "请检查集群中是否有问题需要处理。" }], "authorName": "Orchestrator" },
            { "role": "assistant", "contents": [{ "kind": "text", "text": "好的，正在检查..." }], "authorName": "ops-agent" },
            { "role": "user", "contents": [{ "kind": "text", "text": "请继续深入分析各服务的日志和指标。" }], "authorName": "Orchestrator" },
            { "role": "assistant", "contents": [{ "kind": "text", "text": "分析完成，一切正常。" }], "authorName": "ops-agent" }
        ]
        """;
        var json = BuildWorkflowSessionJson("你好", new() { ["agent"] = agentMsgs });
        var sessionData = JsonDocument.Parse(json).RootElement;

        var messages = InvokeExtractMessages(sessionData);

        // Should only have: user "你好" + 3 assistant messages (orchestrator messages filtered)
        messages.Should().HaveCount(4);
        messages[0].Role.Should().Be("user");
        messages[0].Content.Should().Be("你好");
        messages[1].Role.Should().Be("assistant");
        messages[1].Content.Should().Be("你好！");
        messages[2].Role.Should().Be("assistant");
        messages[2].Content.Should().Be("好的，正在检查...");
        messages[3].Role.Should().Be("assistant");
        messages[3].Content.Should().Be("分析完成，一切正常。");
    }

    [Fact]
    public void ExtractMessages_WorkflowSession_DedupConsecutiveAssistantMessages()
    {
        var agentMsgs = """
        [
            { "role": "user", "contents": [{ "kind": "text", "text": "hi" }] },
            { "role": "assistant", "contents": [{ "kind": "text", "text": "Hello there!" }] },
            { "role": "assistant", "contents": [{ "kind": "text", "text": "Hello there!" }] },
            { "role": "assistant", "contents": [{ "kind": "text", "text": "Different response" }] }
        ]
        """;
        var json = BuildWorkflowSessionJson("hi", new() { ["agent"] = agentMsgs });
        var sessionData = JsonDocument.Parse(json).RootElement;

        var messages = InvokeExtractMessages(sessionData);

        messages.Should().HaveCount(3);
        messages[0].Content.Should().Be("hi");
        messages[1].Content.Should().Be("Hello there!");
        messages[2].Content.Should().Be("Different response");
    }

    [Fact]
    public void ExtractMessages_WorkflowSession_RevertsReassignedOtherAgentRoles()
    {
        // Framework's AIAgentHostExecutor.ContinueTurnAsync uses ReassignOtherAgentsAsUsers
        // which converts assistant messages from OTHER agents to user role (but preserves authorName).
        // In a multi-agent MagneticOne team, monitor-agent's thread has ops-agent's responses
        // stored as user role with authorName="ops-agent".
        // The handler must detect this (user role + non-null authorName ≠ Orchestrator) and
        // revert to assistant role for display.
        var monitorAgentThread = """
        [
            { "role": "user", "contents": [{ "kind": "text", "text": "check cluster health" }] },
            { "role": "user", "contents": [{ "kind": "text", "text": "Pods are all running" }], "authorName": "ops-agent" },
            { "role": "assistant", "contents": [{ "kind": "text", "text": "Metrics look normal" }], "authorName": "monitor-agent" },
            { "role": "user", "contents": [{ "kind": "text", "text": "请继续深入分析" }], "authorName": "Orchestrator" },
            { "role": "user", "contents": [{ "kind": "text", "text": "Services are healthy" }], "authorName": "ops-agent" },
            { "role": "assistant", "contents": [{ "kind": "text", "text": "All metrics within thresholds" }], "authorName": "monitor-agent" }
        ]
        """;
        var json = BuildWorkflowSessionJson("check cluster health", new()
        {
            ["monitor_agent_xyz"] = monitorAgentThread
        });
        var sessionData = JsonDocument.Parse(json).RootElement;

        var messages = InvokeExtractMessages(sessionData);

        // Expected: user input, ops-agent's 2 messages reverted to assistant, monitor-agent's 2 assistant messages
        // Orchestrator message filtered out
        messages.Should().HaveCount(5);
        messages[0].Role.Should().Be("user");
        messages[0].Content.Should().Be("check cluster health");
        messages[1].Role.Should().Be("assistant"); // Reverted from user → assistant
        messages[1].Content.Should().Be("Pods are all running");
        messages[1].ParticipantAgentName.Should().Be("ops-agent");
        messages[2].Role.Should().Be("assistant");
        messages[2].Content.Should().Be("Metrics look normal");
        messages[2].ParticipantAgentName.Should().Be("monitor-agent");
        messages[3].Role.Should().Be("assistant"); // Reverted from user → assistant
        messages[3].Content.Should().Be("Services are healthy");
        messages[3].ParticipantAgentName.Should().Be("ops-agent");
        messages[4].Role.Should().Be("assistant");
        messages[4].Content.Should().Be("All metrics within thresholds");
        messages[4].ParticipantAgentName.Should().Be("monitor-agent");
    }

    [Fact]
    public void ExtractMessages_WorkflowSession_MultiTurn_CorrectRolesAfterSecondInput()
    {
        // Simulates a 2-turn conversation with a single participant agent.
        // After turn 2, the participant's thread has accumulated messages from both turns.
        // The checkpoint stateData has the participant executor's accumulated thread.
        var participantThread = """
        [
            { "role": "user", "contents": [{ "kind": "text", "text": "check cluster" }] },
            { "role": "user", "contents": [{ "kind": "text", "text": "Check pod status" }], "authorName": "Orchestrator" },
            { "role": "assistant", "contents": [{ "kind": "text", "text": "Pods are healthy" }], "authorName": "ops-agent" },
            { "role": "user", "contents": [{ "kind": "text", "text": "Summarize findings" }], "authorName": "Orchestrator" },
            { "role": "assistant", "contents": [{ "kind": "text", "text": "All systems nominal" }], "authorName": "ops-agent" },
            { "role": "user", "contents": [{ "kind": "text", "text": "what about memory usage?" }] },
            { "role": "user", "contents": [{ "kind": "text", "text": "Check memory metrics" }], "authorName": "Orchestrator" },
            { "role": "assistant", "contents": [{ "kind": "text", "text": "Memory at 72%" }], "authorName": "ops-agent" }
        ]
        """;
        // Use a bigger checkpoint step to indicate this is after turn 2
        var json = BuildWorkflowSessionJson("what about memory usage?", new()
        {
            ["ops_agent_def"] = participantThread
        }, checkpointStep: 6);
        var sessionData = JsonDocument.Parse(json).RootElement;

        var messages = InvokeExtractMessages(sessionData);

        // Expected: 2 user messages + 3 assistant messages (orchestrator messages filtered)
        messages.Should().HaveCount(5);
        messages[0].Role.Should().Be("user");
        messages[0].Content.Should().Be("check cluster");
        messages[1].Role.Should().Be("assistant");
        messages[1].Content.Should().Be("Pods are healthy");
        messages[1].ParticipantAgentName.Should().Be("ops-agent");
        messages[2].Role.Should().Be("assistant");
        messages[2].Content.Should().Be("All systems nominal");
        messages[2].ParticipantAgentName.Should().Be("ops-agent");
        messages[3].Role.Should().Be("user");
        messages[3].Content.Should().Be("what about memory usage?");
        messages[4].Role.Should().Be("assistant");
        messages[4].Content.Should().Be("Memory at 72%");
        messages[4].ParticipantAgentName.Should().Be("ops-agent");
    }

    [Fact]
    public void ExtractMessages_WorkflowSession_MultipleCheckpoints_UsesLast()
    {
        // Build JSON with 2 checkpoints in the same RunCheckpointCache
        var runId = "run-test";
        var cpId1 = "cp1";
        var cpId2 = "cp2";

        var json = $$"""
        {
          "runId": "{{runId}}",
          "lastCheckpoint": { "runId": "{{runId}}", "checkpointId": "{{cpId2}}" },
          "chatHistoryProviderState": {
            "bookmark": 1,
            "messages": [
              { "role": "user", "contents": [{ "kind": "text", "text": "start" }] }
            ]
          },
          "checkpointManager": {
            "store": {
              "{{runId}}": {
                "checkpointIndex": [
                  { "runId": "{{runId}}", "checkpointId": "{{cpId1}}" },
                  { "runId": "{{runId}}", "checkpointId": "{{cpId2}}" }
                ],
                "cache": {
                  "{{runId}}|{{cpId1}}": {
                    "stepNumber": 1,
                    "stateData": {
                      "agent||AIAgentHostState": {
                        "typeId": { "assemblyName": "fw", "typeName": "AIAgentHostState" },
                        "value": {
                          "threadState": {
                            "chatHistoryProviderState": {
                              "messages": [
                                { "role": "user", "contents": [{ "kind": "text", "text": "start" }] },
                                { "role": "assistant", "contents": [{ "kind": "text", "text": "first checkpoint" }] }
                              ]
                            }
                          }
                        }
                      }
                    }
                  },
                  "{{runId}}|{{cpId2}}": {
                    "stepNumber": 2,
                    "stateData": {
                      "agent||AIAgentHostState": {
                        "typeId": { "assemblyName": "fw", "typeName": "AIAgentHostState" },
                        "value": {
                          "threadState": {
                            "chatHistoryProviderState": {
                              "messages": [
                                { "role": "user", "contents": [{ "kind": "text", "text": "start" }] },
                                { "role": "assistant", "contents": [{ "kind": "text", "text": "first checkpoint" }] },
                                { "role": "assistant", "contents": [{ "kind": "text", "text": "second checkpoint added" }] }
                              ]
                            }
                          }
                        }
                      }
                    }
                  }
                }
              }
            }
          }
        }
        """;
        var sessionData = JsonDocument.Parse(json).RootElement;

        var messages = InvokeExtractMessages(sessionData);

        // Should use last checkpoint (cpId2) with 3 messages
        messages.Should().HaveCount(3);
        messages[2].Content.Should().Be("second checkpoint added");
    }

    [Fact]
    public void ExtractMessages_WorkflowSession_ScopeKeyPipeFormat_CorrectParsing()
    {
        // Verify that the ScopeKey pipe format "executorId||AIAgentHostState" is correctly matched
        // (double pipe = executorId + empty scopeName + key)
        var json = BuildWorkflowSessionJson("test", new()
        {
            ["my_complex_executor_id_123"] = """
            [
                { "role": "user", "contents": [{ "kind": "text", "text": "test" }] },
                { "role": "assistant", "contents": [{ "kind": "text", "text": "response" }] }
            ]
            """
        });
        var sessionData = JsonDocument.Parse(json).RootElement;

        var messages = InvokeExtractMessages(sessionData);

        messages.Should().HaveCount(2);
        messages[1].Content.Should().Be("response");
    }

    [Fact]
    public void ExtractMessages_EmptySessionData_ReturnsEmptyList()
    {
        var json = "{}";
        var sessionData = JsonDocument.Parse(json).RootElement;

        var messages = InvokeExtractMessages(sessionData);

        messages.Should().BeEmpty();
    }

    [Fact]
    public void ExtractMessages_WorkflowSession_FallbackWithoutPortableValueWrapper()
    {
        // Some configurations may not wrap in PortableValue — test direct threadState
        var runId = "run-fb";
        var cpId = "cp-fb";
        var json = $$"""
        {
          "runId": "{{runId}}",
          "chatHistoryProviderState": { "bookmark": 1, "messages": [{ "role": "user", "contents": [{ "kind": "text", "text": "x" }] }] },
          "checkpointManager": {
            "store": {
              "{{runId}}": {
                "checkpointIndex": [{ "runId": "{{runId}}", "checkpointId": "{{cpId}}" }],
                "cache": {
                  "{{runId}}|{{cpId}}": {
                    "stepNumber": 1,
                    "stateData": {
                      "agent||AIAgentHostState": {
                        "threadState": {
                          "chatHistoryProviderState": {
                            "messages": [
                              { "role": "user", "contents": [{ "kind": "text", "text": "x" }] },
                              { "role": "assistant", "contents": [{ "kind": "text", "text": "unwrapped" }] }
                            ]
                          }
                        }
                      }
                    }
                  }
                }
              }
            }
          }
        }
        """;
        var sessionData = JsonDocument.Parse(json).RootElement;

        var messages = InvokeExtractMessages(sessionData);

        messages.Should().HaveCount(2);
        messages[1].Content.Should().Be("unwrapped");
    }
}
