/**
 * documentFileName.ts — the ONE rule for naming a Word document saved to Spaarke (spaarkeai-word-add-in-r1 task 089).
 *
 * Extracted, unchanged, from the two places the pane applied it inline — `SaveFlow.tsx` (the Document Name
 * field's default, task 020 / FR-06) and `useSaveFlow.ts` (the uploaded file name, UAT 2026-09-03) — so the ribbon's
 * Quick Save can call the SAME rule instead of keeping its own copy (it had one, with a different fallback). The
 * pane's behaviour is byte-for-byte what it was; its existing tests pin it.
 *
 * A file NAME is never identity (#1005): these functions only spell the name. Whether a save versions an
 * existing document is decided by the resolved identity (URL or stamp), never by a name match.
 */

/**
 * The Document Name default for an item name: a trailing `.docx`/`.doc` (case-insensitive) removed. A value without
 * one (e.g. Word's "Untitled Document" fallback) is returned unchanged — normalization, not a requirement.
 */
export function stripDocumentExtension(name: string): string {
  return name.replace(/\.docx?$/i, '');
}

/**
 * The file name a Document save uploads under: the name trimmed (blank → `document`) with `.docx` appended unless it
 * already ends in `.docx`. SPE preview, Word open, Compose mount and AI text extraction all key off the extension;
 * without it the file is stored extensionless and treated as an unsupported type (UAT 2026-09-03).
 */
export function toDocxFileName(name: string | null | undefined): string {
  const raw = (name || 'document').trim() || 'document';
  return /\.docx$/i.test(raw) ? raw : `${raw}.docx`;
}
