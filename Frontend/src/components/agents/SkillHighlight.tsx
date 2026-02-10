interface SkillHighlightProps {
  text: string;
  query: string;
}

/**
 * Wraps matching substrings with <mark> for keyword highlighting.
 * Case-insensitive, escapes regex special chars.
 */
export default function SkillHighlight({ text, query }: SkillHighlightProps) {
  if (!query.trim()) {
    return <>{text}</>;
  }

  const escaped = query.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
  const regex = new RegExp(`(${escaped})`, "gi");
  const parts = text.split(regex);

  return (
    <>
      {parts.map((part, i) =>
        regex.test(part) ? (
          <mark key={i} className="bg-yellow-200 dark:bg-yellow-800 rounded-sm px-0.5">
            {part}
          </mark>
        ) : (
          <span key={i}>{part}</span>
        ),
      )}
    </>
  );
}
