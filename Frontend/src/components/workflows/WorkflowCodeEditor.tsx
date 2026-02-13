import { useRef, useCallback, useEffect } from "react";
import Editor, { loader, type OnMount, type BeforeMount } from "@monaco-editor/react";
import * as monaco from "monaco-editor";
import editorWorker from "monaco-editor/esm/vs/editor/editor.worker?worker";
import jsonWorker from "monaco-editor/esm/vs/language/json/json.worker?worker";
import tsWorker from "monaco-editor/esm/vs/language/typescript/ts.worker?worker";
import type { editor, IDisposable } from "monaco-editor";

// Configure Monaco workers locally (avoids CDN and getWorkerUrl errors)
self.MonacoEnvironment = {
  getWorker(_, label) {
    if (label === "json") return new jsonWorker();
    if (label === "typescript" || label === "javascript") return new tsWorker();
    return new editorWorker();
  },
};

loader.config({ monaco });

// ── V8 运行时类型声明 (.d.ts) ──────────────────────────────────
// 与 V8ExpressionEvaluator.InjectContext + CreateSandboxedEngine 完全对应。
// Monaco 的 TypeScript 语言服务会基于此做真正的类型推断和 IntelliSense。
const V8_RUNTIME_TYPES = `
/**
 * 工作流 V8 表达式引擎 — 运行时类型声明
 *
 * 这些全局变量和函数由 V8ExpressionEvaluator 在每次求值时注入。
 * 使用 {{ expr }} 模板语法时，expr 部分可直接使用以下 API。
 */

/** 节点输出条目 — 每个节点在 $node 字典中的值 */
interface NodeOutput {
  /** 节点输出的 JSON 解析结果 */
  json: any;
  /** 节点输出的原始文本 */
  text: string;
}

/** 执行元数据 */
interface ExecutionMeta {
  /** 当前执行 ID */
  id: string;
  /** 工作流 ID */
  workflowId: string;
}

// ── 内置变量 ──

/**
 * 当前节点的输入数据（JSON 对象）。
 * 由上游节点传入的数据，可通过属性路径访问字段。
 *
 * @example
 * $input.severity       // 访问顶层字段
 * $input.data.items[0]  // 嵌套访问
 */
declare var $input: any;

/**
 * $input 的快捷别名，与 $input 完全相同。
 */
declare var $json: any;

/**
 * 各节点输出字典，按 nodeId 索引。
 *
 * @example
 * $node["step1"].json.result  // 获取 step1 节点输出的 result 字段
 * $node["step1"].text         // 获取 step1 节点输出的原始文本
 */
declare var $node: Record<string, NodeOutput>;

/**
 * 工作流执行元数据。
 */
declare var $execution: ExecutionMeta;

/**
 * 全局用户变量字典。
 *
 * @example
 * $vars["apiKey"]    // 获取用户定义的变量
 * $vars["threshold"] // 获取阈值变量
 */
declare var $vars: Record<string, any>;

// ── 内置辅助函数 ──

/**
 * 将 JSON 字符串解析为对象。解析失败时返回原始字符串。
 *
 * @example
 * $parseJson('{"name":"Alice"}')  // { name: "Alice" }
 */
declare function $parseJson(s: string): any;

/**
 * 将对象序列化为 JSON 字符串。
 *
 * @example
 * $toJson($input)  // '{"severity":"high"}'
 */
declare function $toJson(o: any): string;

/**
 * 拼接多个参数为一个字符串。
 *
 * @example
 * $concat("Hello ", $input.name, "!")  // "Hello Alice!"
 */
declare function $concat(...args: any[]): string;

/**
 * 安全地通过点分路径获取嵌套属性值。
 * 支持数组索引语法 (如 "items[0].name")。
 * 属性不存在时返回 defaultValue。
 *
 * @example
 * $get($input, "a.b.c")              // 深层取值
 * $get($input, "x.y.z", "fallback")  // 不存在时返回 "fallback"
 * $get($input, "items[0].name")      // 数组索引
 */
declare function $get(obj: any, path: string, defaultValue?: any): any;
`;

let typesRegistered = false;

function registerV8Types(monacoInstance: typeof monaco) {
  if (typesRegistered) return;
  typesRegistered = true;

  // Configure JS defaults to use TS-level IntelliSense for real type inference
  const jsDefaults = monacoInstance.languages.typescript.javascriptDefaults;

  jsDefaults.setDiagnosticsOptions({
    // Don't show errors — user expressions are snippets, not full programs
    noSemanticValidation: true,
    noSyntaxValidation: true,
  });

  jsDefaults.setCompilerOptions({
    target: monacoInstance.languages.typescript.ScriptTarget.ESNext,
    allowJs: true,
    checkJs: false,
    allowNonTsExtensions: true,
    lib: ["esnext"],
  });

  // Inject V8 runtime type declarations — enables IntelliSense for
  // $input, $node, $execution, $vars, $get(), $toJson(), etc.
  // Also brings in all JS builtins: .length, .includes(), .map(), JSON.*, etc.
  jsDefaults.addExtraLib(V8_RUNTIME_TYPES, "ts:v8-workflow-runtime.d.ts");
}

// ── 编辑器语言 ──────────────────────────────────────────────

type EditorLanguage = "javascript" | "handlebars-like";

export interface WorkflowCodeEditorProps {
  /** 当前值 */
  value: string;
  /** 值变更回调 */
  onChange: (value: string) => void;
  /**
   * 编辑器语言模式
   * - `"javascript"` — 条件表达式 / 纯 JS 表达式（如 `$input.severity === 'high'`）
   * - `"handlebars-like"` — 模板字符串，含 `{{ expression }}` 插值（如提示词模板）
   */
  language?: EditorLanguage;
  /** 编辑器高度，默认 120 */
  height?: number | string;
  /** placeholder（编辑器为空时的灰色提示） */
  placeholder?: string;
  /** 是否只读 */
  readOnly?: boolean;
  /** 额外的 CSS className */
  className?: string;
}

/**
 * 工作流代码编辑器 — 基于 Monaco Editor + TypeScript 语言服务。
 *
 * 注入与后端 V8ExpressionEvaluator 完全对应的 .d.ts 类型声明，
 * 提供真正的 IntelliSense：变量补全、属性推断、函数签名、JS 内置方法等。
 */
export function WorkflowCodeEditor({
  value,
  onChange,
  language = "javascript",
  height = 120,
  placeholder,
  readOnly = false,
  className,
}: WorkflowCodeEditorProps) {
  const editorRef = useRef<editor.IStandaloneCodeEditor | null>(null);
  const disposablesRef = useRef<IDisposable[]>([]);

  const handleBeforeMount: BeforeMount = useCallback(
    (monacoInstance) => {
      // Dispose previous registrations (React StrictMode double-mount)
      disposablesRef.current.forEach((d) => d.dispose());
      disposablesRef.current = [];

      // Register V8 runtime types for JS IntelliSense (once globally)
      registerV8Types(monacoInstance);

      // For handlebars-like mode (plaintext), add a custom completion provider
      // since TS language service only works for JS/TS files
      if (language === "handlebars-like") {
        const completionDisposable =
          monacoInstance.languages.registerCompletionItemProvider("plaintext", {
            triggerCharacters: ["$", ".", "{"],
            provideCompletionItems: (model, position) => {
              const wordInfo = model.getWordUntilPosition(position);
              const range = {
                startLineNumber: position.lineNumber,
                endLineNumber: position.lineNumber,
                startColumn: wordInfo.startColumn,
                endColumn: wordInfo.endColumn,
              };

              const suggestions: monaco.languages.CompletionItem[] = [];

              // Variables
              const vars: [string, string][] = [
                ["$input", "当前节点的输入数据"],
                ["$json", "$input 的别名"],
                ["$node", "各节点输出字典"],
                ["$execution", "执行元数据"],
                ["$vars", "全局变量字典"],
              ];
              for (const [name, detail] of vars) {
                suggestions.push({
                  label: name,
                  kind: monacoInstance.languages.CompletionItemKind.Variable,
                  detail,
                  insertText: name,
                  range,
                });
              }

              // Functions
              const fns: [string, string, string][] = [
                ["$get", "(obj, path, default?) => any", '$get($input, "${1:path}", ${2:undefined})'],
                ["$toJson", "(obj) => string", "$toJson(${1:\\$input})"],
                ["$parseJson", "(str) => object", "$parseJson(${1:str})"],
                ["$concat", "(...args) => string", "$concat(${1:str1}, ${2:str2})"],
              ];
              for (const [name, detail, insert] of fns) {
                suggestions.push({
                  label: name,
                  kind: monacoInstance.languages.CompletionItemKind.Function,
                  detail,
                  insertText: insert,
                  insertTextRules:
                    monacoInstance.languages.CompletionItemInsertTextRule.InsertAsSnippet,
                  range,
                });
              }

              // {{ }} wrapper snippet
              suggestions.push({
                label: "{{ expression }}",
                kind: monacoInstance.languages.CompletionItemKind.Snippet,
                detail: "插入表达式插值",
                insertText: "{{ ${1:\\$input.${2:field}} }}",
                insertTextRules:
                  monacoInstance.languages.CompletionItemInsertTextRule.InsertAsSnippet,
                range,
              });

              return { suggestions };
            },
          });
        disposablesRef.current.push(completionDisposable);
      }
    },
    [language],
  );

  const handleMount: OnMount = useCallback((editorInstance) => {
    editorRef.current = editorInstance;
  }, []);

  const handleChange = useCallback(
    (val: string | undefined) => {
      onChange(val ?? "");
    },
    [onChange],
  );

  // Cleanup on unmount
  useEffect(() => {
    return () => {
      disposablesRef.current.forEach((d) => d.dispose());
      disposablesRef.current = [];
    };
  }, []);

  const monacoLanguage = language === "handlebars-like" ? "plaintext" : "javascript";

  return (
    <div
      className={`border rounded-md overflow-visible ${className ?? ""}`}
      onKeyDown={(e) => e.stopPropagation()}
    >
      <Editor
        height={height}
        language={monacoLanguage}
        value={value}
        onChange={handleChange}
        beforeMount={handleBeforeMount}
        onMount={handleMount}
        theme="vs-dark"
        options={{
          minimap: { enabled: false },
          lineNumbers: "off",
          glyphMargin: false,
          folding: false,
          lineDecorationsWidth: 8,
          lineNumbersMinChars: 0,
          scrollBeyondLastLine: false,
          wordWrap: "on",
          wrappingStrategy: "advanced",
          automaticLayout: true,
          fontSize: 12,
          tabSize: 2,
          renderLineHighlight: "none",
          overviewRulerLanes: 0,
          hideCursorInOverviewRuler: true,
          overviewRulerBorder: false,
          scrollbar: {
            vertical: "auto",
            horizontal: "hidden",
            verticalScrollbarSize: 6,
          },
          padding: { top: 6, bottom: 6 },
          suggestOnTriggerCharacters: true,
          quickSuggestions: true,
          fixedOverflowWidgets: true,
          readOnly,
          domReadOnly: readOnly,
          placeholder,
        }}
      />
    </div>
  );
}
