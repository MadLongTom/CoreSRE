using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoreSRE.Infrastructure.Migrations
{
    /// <summary>
    /// Data migration: converts old PostgresChatHistoryProvider DTO format (using "kind" discriminator)
    /// to MEAI native ChatMessage format (using "$type" discriminator).
    ///
    /// Old format:  { "kind": "text", "text": "hello" }
    /// New format:  { "$type": "text", "text": "hello" }
    ///
    /// Also migrates the "source" top-level DTO property into ChatMessage's "additionalProperties" dict.
    ///
    /// Covers both session_data layouts:
    ///   1. Top-level:   session_data -> PostgresChatHistoryProvider -> messages[] -> contents[]
    ///   2. StateBag:    session_data -> stateBag -> PostgresChatHistoryProvider -> messages[] -> contents[]
    /// </summary>
    public partial class MigrateChatHistoryDtoToNativeFormat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── Step 1: Rename "kind" → "$type" in contents arrays ────────────────────────
            // Uses a PL/pgSQL DO block to iterate all affected rows and transform each content object.
            // This handles both direct and stateBag-wrapped session layouts.
            migrationBuilder.Sql("""
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

                        -- Determine the JSON path to PostgresChatHistoryProvider
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

                            -- Migrate "source" from top-level DTO property into "additionalProperties"
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
                                        -- Rename "kind" → "$type"
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
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // ── Reverse: rename "$type" → "kind" in contents arrays ───────────────────────
            migrationBuilder.Sql("""
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

                            -- Reverse: migrate "source" from additionalProperties back to top-level
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
                                        -- Rename "$type" → "kind"
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
                """);
        }
    }
}
