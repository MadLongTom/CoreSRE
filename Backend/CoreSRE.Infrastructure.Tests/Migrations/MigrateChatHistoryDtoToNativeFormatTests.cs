using System.Text.Json;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace CoreSRE.Infrastructure.Tests.Migrations;

/// <summary>
/// Integration tests for the MigrateChatHistoryDtoToNativeFormat migration.
/// Uses Testcontainers to spin up a real PostgreSQL instance and validates
/// the PL/pgSQL data-migration script transforms old DTO format → native MEAI format.
/// </summary>
public class MigrateChatHistoryDtoToNativeFormatTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("pgvector/pgvector:pg17")
        .Build();

    public Task InitializeAsync() => _postgres.StartAsync();
    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    // ─── DDL for the minimal table needed by migration ────────────────────────
    private const string CreateTableSql = """
        CREATE TABLE agent_sessions (
            agent_id       VARCHAR(255)  NOT NULL,
            conversation_id VARCHAR(255) NOT NULL,
            session_data   JSONB         NOT NULL,
            session_type   VARCHAR(100)  NOT NULL DEFAULT '',
            created_at     TIMESTAMPTZ   NOT NULL DEFAULT NOW(),
            updated_at     TIMESTAMPTZ   NOT NULL DEFAULT NOW(),
            PRIMARY KEY (agent_id, conversation_id)
        );
        """;

    // ─── Up migration SQL (extracted from 20260320055814) ─────────────────────
    private const string UpMigrationSql = """
        DO $$
        DECLARE
            rec RECORD;
            new_data jsonb;
            messages_path text;
            messages_arr jsonb;
            new_messages jsonb;
            msg jsonb;
            contents_arr jsonb;
            new_contents jsonb;
            content_item jsonb;
            new_content jsonb;
            source_val text;
            i int;
            j int;
            changed boolean;
        BEGIN
            FOR rec IN
                SELECT agent_id, conversation_id, session_data
                FROM agent_sessions
                WHERE session_data ? 'PostgresChatHistoryProvider'
                   OR (session_data ? 'stateBag'
                       AND session_data->'stateBag' ? 'PostgresChatHistoryProvider')
            LOOP
                new_data := rec.session_data;
                changed := false;

                IF new_data ? 'PostgresChatHistoryProvider' THEN
                    messages_path := 'top';
                    messages_arr := new_data->'PostgresChatHistoryProvider'->'messages';
                ELSIF new_data #> '{stateBag,PostgresChatHistoryProvider}' IS NOT NULL THEN
                    messages_path := 'stateBag';
                    messages_arr := new_data #> '{stateBag,PostgresChatHistoryProvider,messages}';
                ELSE
                    CONTINUE;
                END IF;

                IF messages_arr IS NULL OR jsonb_typeof(messages_arr) != 'array' THEN
                    CONTINUE;
                END IF;

                new_messages := '[]'::jsonb;

                FOR i IN 0..jsonb_array_length(messages_arr) - 1 LOOP
                    msg := messages_arr->i;
                    contents_arr := msg->'contents';

                    IF msg ? 'source' AND msg->>'source' IS NOT NULL THEN
                        source_val := msg->>'source';
                        msg := msg - 'source';
                        msg := jsonb_set(
                            msg,
                            '{additionalProperties}',
                            COALESCE(msg->'additionalProperties', '{}'::jsonb)
                                || jsonb_build_object('source', source_val)
                        );
                        changed := true;
                    END IF;

                    IF contents_arr IS NOT NULL AND jsonb_typeof(contents_arr) = 'array' THEN
                        new_contents := '[]'::jsonb;

                        FOR j IN 0..jsonb_array_length(contents_arr) - 1 LOOP
                            content_item := contents_arr->j;

                            IF content_item ? 'kind' THEN
                                new_content := jsonb_build_object('$type', content_item->'kind')
                                               || (content_item - 'kind');
                                new_contents := new_contents || jsonb_build_array(new_content);
                                changed := true;
                            ELSE
                                new_contents := new_contents || jsonb_build_array(content_item);
                            END IF;
                        END LOOP;

                        msg := jsonb_set(msg, '{contents}', new_contents);
                    END IF;

                    new_messages := new_messages || jsonb_build_array(msg);
                END LOOP;

                IF changed THEN
                    IF messages_path = 'top' THEN
                        new_data := jsonb_set(
                            new_data,
                            '{PostgresChatHistoryProvider,messages}',
                            new_messages
                        );
                    ELSE
                        new_data := jsonb_set(
                            new_data,
                            '{stateBag,PostgresChatHistoryProvider,messages}',
                            new_messages
                        );
                    END IF;

                    UPDATE agent_sessions
                    SET session_data = new_data,
                        updated_at = NOW() AT TIME ZONE 'UTC'
                    WHERE agent_id = rec.agent_id
                      AND conversation_id = rec.conversation_id;
                END IF;
            END LOOP;
        END $$;
        """;

    // ─── Down migration SQL (extracted from 20260320055814) ───────────────────
    private const string DownMigrationSql = """
        DO $$
        DECLARE
            rec RECORD;
            new_data jsonb;
            messages_path text;
            messages_arr jsonb;
            new_messages jsonb;
            msg jsonb;
            contents_arr jsonb;
            new_contents jsonb;
            content_item jsonb;
            new_content jsonb;
            additional_props jsonb;
            source_val text;
            i int;
            j int;
            changed boolean;
        BEGIN
            FOR rec IN
                SELECT agent_id, conversation_id, session_data
                FROM agent_sessions
                WHERE session_data ? 'PostgresChatHistoryProvider'
                   OR (session_data ? 'stateBag'
                       AND session_data->'stateBag' ? 'PostgresChatHistoryProvider')
            LOOP
                new_data := rec.session_data;
                changed := false;

                IF new_data ? 'PostgresChatHistoryProvider' THEN
                    messages_path := 'top';
                    messages_arr := new_data->'PostgresChatHistoryProvider'->'messages';
                ELSIF new_data #> '{stateBag,PostgresChatHistoryProvider}' IS NOT NULL THEN
                    messages_path := 'stateBag';
                    messages_arr := new_data #> '{stateBag,PostgresChatHistoryProvider,messages}';
                ELSE
                    CONTINUE;
                END IF;

                IF messages_arr IS NULL OR jsonb_typeof(messages_arr) != 'array' THEN
                    CONTINUE;
                END IF;

                new_messages := '[]'::jsonb;

                FOR i IN 0..jsonb_array_length(messages_arr) - 1 LOOP
                    msg := messages_arr->i;
                    contents_arr := msg->'contents';

                    additional_props := msg->'additionalProperties';
                    IF additional_props IS NOT NULL AND additional_props ? 'source' THEN
                        source_val := additional_props->>'source';
                        IF source_val IS NOT NULL THEN
                            additional_props := additional_props - 'source';
                            msg := jsonb_set(msg, '{source}', to_jsonb(source_val));
                            IF additional_props = '{}'::jsonb THEN
                                msg := msg - 'additionalProperties';
                            ELSE
                                msg := jsonb_set(msg, '{additionalProperties}', additional_props);
                            END IF;
                            changed := true;
                        END IF;
                    END IF;

                    IF contents_arr IS NOT NULL AND jsonb_typeof(contents_arr) = 'array' THEN
                        new_contents := '[]'::jsonb;

                        FOR j IN 0..jsonb_array_length(contents_arr) - 1 LOOP
                            content_item := contents_arr->j;

                            IF content_item ? '$type' THEN
                                new_content := jsonb_build_object('kind', content_item->'$type')
                                               || (content_item - '$type');
                                new_contents := new_contents || jsonb_build_array(new_content);
                                changed := true;
                            ELSE
                                new_contents := new_contents || jsonb_build_array(content_item);
                            END IF;
                        END LOOP;

                        msg := jsonb_set(msg, '{contents}', new_contents);
                    END IF;

                    new_messages := new_messages || jsonb_build_array(msg);
                END LOOP;

                IF changed THEN
                    IF messages_path = 'top' THEN
                        new_data := jsonb_set(
                            new_data,
                            '{PostgresChatHistoryProvider,messages}',
                            new_messages
                        );
                    ELSE
                        new_data := jsonb_set(
                            new_data,
                            '{stateBag,PostgresChatHistoryProvider,messages}',
                            new_messages
                        );
                    END IF;

                    UPDATE agent_sessions
                    SET session_data = new_data,
                        updated_at = NOW() AT TIME ZONE 'UTC'
                    WHERE agent_id = rec.agent_id
                      AND conversation_id = rec.conversation_id;
                END IF;
            END LOOP;
        END $$;
        """;

    // ─── Test Data ────────────────────────────────────────────────────────────

    /// <summary>Old DTO format: top-level PostgresChatHistoryProvider key with "kind" discriminator and "source" property.</summary>
    private static readonly string OldFormatTopLevel = JsonSerializer.Serialize(new
    {
        PostgresChatHistoryProvider = new
        {
            messages = new object[]
            {
                new
                {
                    role = "user",
                    source = "web-ui",
                    contents = new object[]
                    {
                        new { kind = "text", text = "Hello, what's wrong with pod xyz?" }
                    }
                },
                new
                {
                    role = "assistant",
                    contents = new object[]
                    {
                        new { kind = "text", text = "Let me check the pod status." },
                        new { kind = "functionCall", callId = "call_1", name = "kubectl_get_pod", arguments = "{}" }
                    }
                },
                new
                {
                    role = "tool",
                    contents = new object[]
                    {
                        new { kind = "functionResult", callId = "call_1", result = "CrashLoopBackOff" }
                    }
                }
            }
        }
    });

    /// <summary>Old DTO format: stateBag-wrapped layout.</summary>
    private static readonly string OldFormatStateBag = JsonSerializer.Serialize(new
    {
        stateBag = new
        {
            PostgresChatHistoryProvider = new
            {
                messages = new object[]
                {
                    new
                    {
                        role = "user",
                        source = "incident-dispatch",
                        contents = new object[]
                        {
                            new { kind = "text", text = "Alert: high CPU on node-03" }
                        }
                    }
                }
            }
        }
    });

    /// <summary>Unrelated session data — should NOT be touched by migration.</summary>
    private static readonly string UnrelatedSession = JsonSerializer.Serialize(new
    {
        chatHistoryProviderState = new
        {
            messages = new object[]
            {
                new { role = "user", contents = new object[] { new { kind = "text", text = "unrelated" } } }
            }
        }
    });

    /// <summary>Session already in new format — should NOT be changed.</summary>
    private static readonly string AlreadyNewFormat = JsonSerializer.Serialize(new
    {
        PostgresChatHistoryProvider = new
        {
            messages = new object[]
            {
                new
                {
                    role = "user",
                    additionalProperties = new { source = "web-ui" },
                    contents = new object[]
                    {
                        new { text = "already migrated" }  // no "kind" or "$type" — plain content
                    }
                }
            }
        }
    });

    // ─── Helper ───────────────────────────────────────────────────────────────

    private async Task<NpgsqlConnection> OpenConnectionAsync()
    {
        var conn = new NpgsqlConnection(_postgres.GetConnectionString());
        await conn.OpenAsync();
        return conn;
    }

    private async Task SeedAsync(NpgsqlConnection conn, string agentId, string conversationId, string sessionDataJson)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO agent_sessions (agent_id, conversation_id, session_data, session_type)
            VALUES (@aid, @cid, @data::jsonb, 'chat');
            """;
        cmd.Parameters.AddWithValue("aid", agentId);
        cmd.Parameters.AddWithValue("cid", conversationId);
        cmd.Parameters.AddWithValue("data", sessionDataJson);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<JsonDocument> ReadSessionDataAsync(NpgsqlConnection conn, string agentId, string conversationId)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT session_data::text FROM agent_sessions WHERE agent_id = @aid AND conversation_id = @cid";
        cmd.Parameters.AddWithValue("aid", agentId);
        cmd.Parameters.AddWithValue("cid", conversationId);
        var json = (string)(await cmd.ExecuteScalarAsync())!;
        return JsonDocument.Parse(json);
    }

    // ─── Tests ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Up_RenamesKindToType_AndMigratesSourceToAdditionalProperties_TopLevel()
    {
        await using var conn = await OpenConnectionAsync();
        await using (var ddl = conn.CreateCommand()) { ddl.CommandText = CreateTableSql; await ddl.ExecuteNonQueryAsync(); }
        await SeedAsync(conn, "agent-1", "conv-1", OldFormatTopLevel);

        // Act
        await using (var migrate = conn.CreateCommand()) { migrate.CommandText = UpMigrationSql; await migrate.ExecuteNonQueryAsync(); }

        // Assert
        using var doc = await ReadSessionDataAsync(conn, "agent-1", "conv-1");
        var messages = doc.RootElement.GetProperty("PostgresChatHistoryProvider").GetProperty("messages");

        // Message 0: user message with source → additionalProperties
        var msg0 = messages[0];
        Assert.False(msg0.TryGetProperty("source", out _), "source should have been removed from top level");
        Assert.Equal("web-ui", msg0.GetProperty("additionalProperties").GetProperty("source").GetString());
        var c0 = msg0.GetProperty("contents")[0];
        Assert.True(c0.TryGetProperty("$type", out var t0));
        Assert.Equal("text", t0.GetString());
        Assert.False(c0.TryGetProperty("kind", out _), "kind should have been renamed to $type");
        Assert.Equal("Hello, what's wrong with pod xyz?", c0.GetProperty("text").GetString());

        // Message 1: assistant with text + functionCall
        var msg1contents = messages[1].GetProperty("contents");
        Assert.Equal("text", msg1contents[0].GetProperty("$type").GetString());
        Assert.Equal("functionCall", msg1contents[1].GetProperty("$type").GetString());
        Assert.Equal("call_1", msg1contents[1].GetProperty("callId").GetString());
        Assert.Equal("kubectl_get_pod", msg1contents[1].GetProperty("name").GetString());

        // Message 2: tool with functionResult
        var msg2c0 = messages[2].GetProperty("contents")[0];
        Assert.Equal("functionResult", msg2c0.GetProperty("$type").GetString());
        Assert.Equal("call_1", msg2c0.GetProperty("callId").GetString());
    }

    [Fact]
    public async Task Up_RenamesKindToType_StateBagLayout()
    {
        await using var conn = await OpenConnectionAsync();
        await using (var ddl = conn.CreateCommand()) { ddl.CommandText = CreateTableSql; await ddl.ExecuteNonQueryAsync(); }
        await SeedAsync(conn, "agent-2", "conv-2", OldFormatStateBag);

        await using (var migrate = conn.CreateCommand()) { migrate.CommandText = UpMigrationSql; await migrate.ExecuteNonQueryAsync(); }

        using var doc = await ReadSessionDataAsync(conn, "agent-2", "conv-2");
        var msg0 = doc.RootElement.GetProperty("stateBag")
            .GetProperty("PostgresChatHistoryProvider")
            .GetProperty("messages")[0];

        Assert.False(msg0.TryGetProperty("source", out _));
        Assert.Equal("incident-dispatch", msg0.GetProperty("additionalProperties").GetProperty("source").GetString());
        Assert.Equal("text", msg0.GetProperty("contents")[0].GetProperty("$type").GetString());
    }

    [Fact]
    public async Task Up_DoesNotTouch_UnrelatedSessions()
    {
        await using var conn = await OpenConnectionAsync();
        await using (var ddl = conn.CreateCommand()) { ddl.CommandText = CreateTableSql; await ddl.ExecuteNonQueryAsync(); }
        await SeedAsync(conn, "agent-3", "conv-3", UnrelatedSession);

        await using (var migrate = conn.CreateCommand()) { migrate.CommandText = UpMigrationSql; await migrate.ExecuteNonQueryAsync(); }

        using var doc = await ReadSessionDataAsync(conn, "agent-3", "conv-3");
        // Still has "kind" — untouched
        var c = doc.RootElement.GetProperty("chatHistoryProviderState").GetProperty("messages")[0].GetProperty("contents")[0];
        Assert.True(c.TryGetProperty("kind", out var k));
        Assert.Equal("text", k.GetString());
        Assert.False(c.TryGetProperty("$type", out _));
    }

    [Fact]
    public async Task Up_Idempotent_NoDoubleTransform_WhenAlreadyMigrated()
    {
        await using var conn = await OpenConnectionAsync();
        await using (var ddl = conn.CreateCommand()) { ddl.CommandText = CreateTableSql; await ddl.ExecuteNonQueryAsync(); }
        await SeedAsync(conn, "agent-1", "conv-1", OldFormatTopLevel);

        // Run Up twice
        await using (var m1 = conn.CreateCommand()) { m1.CommandText = UpMigrationSql; await m1.ExecuteNonQueryAsync(); }
        await using (var m2 = conn.CreateCommand()) { m2.CommandText = UpMigrationSql; await m2.ExecuteNonQueryAsync(); }

        using var doc = await ReadSessionDataAsync(conn, "agent-1", "conv-1");
        var c0 = doc.RootElement.GetProperty("PostgresChatHistoryProvider").GetProperty("messages")[0].GetProperty("contents")[0];
        Assert.Equal("text", c0.GetProperty("$type").GetString());
        Assert.False(c0.TryGetProperty("kind", out _));
    }

    [Fact]
    public async Task Down_ReversesUpCompletely()
    {
        await using var conn = await OpenConnectionAsync();
        await using (var ddl = conn.CreateCommand()) { ddl.CommandText = CreateTableSql; await ddl.ExecuteNonQueryAsync(); }
        await SeedAsync(conn, "agent-1", "conv-1", OldFormatTopLevel);

        // Up then Down
        await using (var up = conn.CreateCommand()) { up.CommandText = UpMigrationSql; await up.ExecuteNonQueryAsync(); }
        await using (var down = conn.CreateCommand()) { down.CommandText = DownMigrationSql; await down.ExecuteNonQueryAsync(); }

        using var doc = await ReadSessionDataAsync(conn, "agent-1", "conv-1");
        var msg0 = doc.RootElement.GetProperty("PostgresChatHistoryProvider").GetProperty("messages")[0];

        // source should be back at top-level
        Assert.Equal("web-ui", msg0.GetProperty("source").GetString());
        // additionalProperties should have been cleaned up (had only source)
        Assert.False(msg0.TryGetProperty("additionalProperties", out _),
            "additionalProperties should be removed when empty after rollback");

        // "kind" restored
        var c0 = msg0.GetProperty("contents")[0];
        Assert.True(c0.TryGetProperty("kind", out var k));
        Assert.Equal("text", k.GetString());
        Assert.False(c0.TryGetProperty("$type", out _));
    }

    [Fact]
    public async Task Down_ReversesUp_StateBagLayout()
    {
        await using var conn = await OpenConnectionAsync();
        await using (var ddl = conn.CreateCommand()) { ddl.CommandText = CreateTableSql; await ddl.ExecuteNonQueryAsync(); }
        await SeedAsync(conn, "agent-2", "conv-2", OldFormatStateBag);

        await using (var up = conn.CreateCommand()) { up.CommandText = UpMigrationSql; await up.ExecuteNonQueryAsync(); }
        await using (var down = conn.CreateCommand()) { down.CommandText = DownMigrationSql; await down.ExecuteNonQueryAsync(); }

        using var doc = await ReadSessionDataAsync(conn, "agent-2", "conv-2");
        var msg0 = doc.RootElement.GetProperty("stateBag")
            .GetProperty("PostgresChatHistoryProvider")
            .GetProperty("messages")[0];

        Assert.Equal("incident-dispatch", msg0.GetProperty("source").GetString());
        Assert.Equal("text", msg0.GetProperty("contents")[0].GetProperty("kind").GetString());
    }
}
