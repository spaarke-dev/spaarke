/**
 * Derives a display title for a memo.
 *
 * Precedence (per spec FR-16):
 *   1. memo.sprk_name if non-empty and not the default "Untitled" (unless that's the actual title)
 *   2. First non-empty line of memo.sprk_memobody, trimmed and truncated
 *   3. Fallback: "Untitled"
 *
 * @param memo - Memo object with sprk_name and/or sprk_memobody
 * @param maxLen - Max characters for the derived title (default: 60)
 * @returns Human-readable title string, never empty
 */
export function deriveTitle(
  memo: { sprk_name?: string | null; sprk_memobody?: string | null },
  maxLen: number = 60
): string {
  const name = memo.sprk_name?.trim();

  // If sprk_name is set AND not equal to the default "Untitled", use it verbatim (truncated)
  if (name && name.length > 0 && name.toLowerCase() !== "untitled") {
    return name.length > maxLen ? name.slice(0, maxLen - 1) + "…" : name;
  }

  // Fall back to first non-empty line of body
  const body = memo.sprk_memobody ?? "";
  const firstLine = body
    .split(/\r?\n/)
    .map(line => line.trim())
    .find(line => line.length > 0);

  if (!firstLine) {
    return "Untitled";
  }

  return firstLine.length > maxLen
    ? firstLine.slice(0, maxLen - 1) + "…"
    : firstLine;
}
