/**
 * CommandRouter.ts — R6 task 080 / D-D-01 (Pillar 8 foundation).
 *
 * Pure TypeScript parser that classifies user chat input into a structured
 * `Intent { command, references[], rawText, isHardSlash, isSoftSlash }` shape.
 *
 * Per spec FR-48 the parser runs BEFORE agent invocation; downstream tasks
 * (081 hard-slash executor, 082 soft-slash agent routing, 083 reference
 * resolver) consume the Intent. This module does NOT execute commands and
 * does NOT resolve references — it only tokenizes.
 *
 * Per Q6 + spec FR-49/FR-50/FR-51 the vocabulary is CLOSED at:
 *   - 6 hard slashes (deterministic, bypass LLM):
 *       `/clear`, `/new-session`, `/help`, `/export`, `/save-to-matter`, `/pin`
 *   - 4 soft slashes (intent shortcuts via agent):
 *       `/summarize`, `/draft`, `/extract-entities`, `/analyze`
 *   - 3 reference types (data identifiers):
 *       `#scope`, `@<entity>`, `#<filename>`
 *
 * Do NOT extend this vocabulary without spec FR sign-off.
 *
 * NFR-11 binding: when no slash is detected, parse() returns
 * `{ command: null, references: [], rawText, isHardSlash: false, isSoftSlash: false }`
 * so the downstream `handleBeforeSendMessage` path in `ConversationPane.tsx`
 * can fall through to the existing `CapabilityRouter` natural-language path
 * UNCHANGED. The parser is non-blocking and side-effect-free.
 *
 * NFR-01 binding: parser does NOT interrupt the LLM's conversational ability.
 * It only classifies; it does not block.
 *
 * This module is PURE — no IO, no network, no React, no state writes, no async.
 * Deterministic given inputs. Trivially testable with plain values.
 *
 * @see projects/spaarke-ai-platform-unification-r6/spec.md FR-48..FR-54
 * @see projects/spaarke-ai-platform-unification-r6/CLAUDE.md §Pillar 8
 * @see ADR-029 — frontend-only; BFF publish-size delta = 0 MB
 * @see ADR-030 — PaneEventBus stays at 4 channels; parser is NOT an emitter
 */

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

/**
 * Hard slash commands — deterministic, bypass LLM, <100ms latency target.
 * Execution lands in task 081 (D-D-02). Do NOT extend.
 */
export type HardSlashCommand =
  | '/clear'
  | '/new-session'
  | '/help'
  | '/export'
  | '/save-to-matter'
  | '/pin'
  | '/playbooks';

/**
 * Soft slash commands — intent shortcuts; routed through the agent.
 * Routing lands in task 082 (D-D-03). Do NOT extend.
 */
export type SoftSlashCommand =
  | '/summarize'
  | '/draft'
  | '/extract-entities'
  | '/analyze';

/**
 * Union of all recognized slash commands. The parser narrows to this set;
 * unrecognized slashes return `command: null` (NFR-11 passthrough).
 */
export type SlashCommand = HardSlashCommand | SoftSlashCommand;

/**
 * Reference kinds per spec FR-51:
 *   - `scope`    — `#scope` shorthand; resolves to scope reference at task 083
 *   - `entity`   — `@<entity>` ; resolves to matter/person/organization
 *   - `filename` — `#<filename>`; resolves to session file or workspace tab
 *
 * Disambiguation rule: `#scope` (literal token) is the scope shorthand;
 * any other `#<value>` is a filename reference. `@<value>` is always entity.
 */
export type ReferenceKind = 'scope' | 'entity' | 'filename';

/**
 * Single reference token extracted from the input.
 *
 * Fields:
 *   - `kind`     — `scope` | `entity` | `filename`
 *   - `value`    — the literal token AFTER the sigil
 *                  (e.g., for `@opposing-counsel` → `opposing-counsel`;
 *                   for `#engagement-letter.docx` → `engagement-letter.docx`;
 *                   for `#scope` → `scope` — kept for round-trip serialization)
 *   - `raw`      — the literal token AS IT APPEARED in the input
 *                  (sigil + value), used by downstream re-injection /
 *                  highlighting affordances
 */
export interface Reference {
  kind: ReferenceKind;
  value: string;
  raw: string;
}

/**
 * Structured intent emitted by `parse()`. Consumed by downstream Phase D tasks:
 *   - 081 (hard-slash executor) — reads `isHardSlash` + `command`
 *   - 082 (soft-slash agent routing) — reads `isSoftSlash` + `command`
 *   - 083 (reference resolver) — reads `references[]`
 *   - 084 (`/help` UI affordance) — reads `command === '/help'`
 *   - 086 (composition test harness) — exercises full Intent shape
 *
 * Invariants:
 *   - `isHardSlash` && `isSoftSlash` are mutually exclusive
 *   - `command === null` ⇔ `isHardSlash === false && isSoftSlash === false`
 *   - `command !== null` ⇒ either `isHardSlash` OR `isSoftSlash` is true
 *   - `rawText` is the input AS-RECEIVED (no trimming applied to the field)
 *   - `references[]` may be empty; never null
 *
 * The `null` command on no-slash input is THE NFR-11 contract — downstream
 * code MUST fall through to the existing `CapabilityRouter` path.
 */
export interface Intent {
  command: SlashCommand | null;
  references: Reference[];
  rawText: string;
  isHardSlash: boolean;
  isSoftSlash: boolean;
}

// ---------------------------------------------------------------------------
// Vocabulary tables (closed)
// ---------------------------------------------------------------------------

const HARD_SLASHES: readonly HardSlashCommand[] = [
  '/clear',
  '/new-session',
  '/help',
  '/export',
  '/save-to-matter',
  '/pin',
  '/playbooks',
] as const;

const SOFT_SLASHES: readonly SoftSlashCommand[] = [
  '/summarize',
  '/draft',
  '/extract-entities',
  '/analyze',
] as const;

const HARD_SLASH_SET: ReadonlySet<string> = new Set(HARD_SLASHES);
const SOFT_SLASH_SET: ReadonlySet<string> = new Set(SOFT_SLASHES);

/**
 * Token characters allowed AFTER a sigil (`#`, `@`). Matches typical
 * identifier shapes: alphanumeric, hyphen, underscore, dot (for file
 * extensions). Whitespace terminates the token. Surrounding punctuation
 * (e.g., trailing `.` or `,`) is dropped via a post-match trim.
 *
 * Note: this is deliberately permissive — the parser tokenizes; the
 * downstream resolver (task 083) validates against actual data.
 */
const REFERENCE_TOKEN_RE = /([#@])([A-Za-z0-9][A-Za-z0-9._\-]*)/g;

// ---------------------------------------------------------------------------
// Public API
// ---------------------------------------------------------------------------

/**
 * Parse user chat input into a structured {@link Intent}.
 *
 * Behavior:
 *   1. Tokenize the FIRST whitespace-separated token. If it starts with `/`:
 *      - If it matches a known hard slash → command + isHardSlash=true
 *      - If it matches a known soft slash → command + isSoftSlash=true
 *      - Unrecognized slash (e.g., `/foobar`) → command=null (passthrough
 *        per NFR-11; downstream LLM handles literal text)
 *   2. Scan the FULL input for reference tokens (`#…`, `@…`) and emit
 *      `references[]`. Reference tokens INSIDE the slash command itself
 *      are NOT extracted (e.g., the leading `/` is consumed as the command).
 *      References anywhere AFTER the leading command are extracted.
 *   3. If input has no leading slash, references are still extracted from
 *      the body (so natural-language `"Tell me about @opposing-counsel"`
 *      surfaces the entity reference for downstream context priming, even
 *      though command=null and the existing CapabilityRouter handles
 *      routing).
 *
 * Pure / synchronous / no side effects. Safe to call on every keystroke.
 *
 * @param rawText  the user's literal input text (untrimmed)
 * @returns        Intent — see {@link Intent} invariants
 */
export function parse(rawText: string): Intent {
  // Defensive: handle non-string inputs in JS callers gracefully.
  // TypeScript callers won't hit this, but ConversationPane.tsx is .tsx in a
  // mixed codebase and the runtime guarantee is worth the 2 lines.
  const text = typeof rawText === 'string' ? rawText : '';

  // Empty / whitespace-only → passthrough (NFR-11 baseline case).
  const trimmed = text.trim();
  if (trimmed.length === 0) {
    return {
      command: null,
      references: [],
      rawText: text,
      isHardSlash: false,
      isSoftSlash: false,
    };
  }

  // ── Step 1: classify the leading token ──────────────────────────────────
  //
  // We only treat a leading `/<token>` (whitespace-terminated) as a command
  // candidate. Slashes appearing later in the message (e.g., `URL paths`)
  // are NOT commands. Lowercase-fold the first token for matching; the
  // canonical vocabulary is lowercase by construction.
  let command: SlashCommand | null = null;
  let isHardSlash = false;
  let isSoftSlash = false;

  if (trimmed.startsWith('/')) {
    // Extract the first whitespace-bounded token. Lowercase for case
    // insensitivity (`/Summarize` should match `/summarize`).
    const firstToken = trimmed.split(/\s+/, 1)[0].toLowerCase();

    if (HARD_SLASH_SET.has(firstToken)) {
      command = firstToken as HardSlashCommand;
      isHardSlash = true;
    } else if (SOFT_SLASH_SET.has(firstToken)) {
      command = firstToken as SoftSlashCommand;
      isSoftSlash = true;
    }
    // else: unrecognized slash → command stays null → NFR-11 passthrough.
    // The literal text (including the unrecognized `/foobar`) flows to the
    // existing CapabilityRouter via the unchanged downstream path.
  }

  // ── Step 2: extract references from the full input ──────────────────────
  //
  // Reference extraction scans the WHOLE input (not just post-command body)
  // so power-user composition like `/draft response to @opposing-counsel
  // about #motion-to-dismiss` (FR-52) and natural-language `"Tell me about
  // @opposing-counsel"` (NFR-11) both surface references.
  //
  // Note: the leading slash command itself does NOT match the reference
  // regex because the regex requires `#` or `@` as the sigil, not `/`.
  const references: Reference[] = extractReferences(text);

  return {
    command,
    references,
    rawText: text,
    isHardSlash,
    isSoftSlash,
  };
}

/**
 * Extract reference tokens from input. Pure helper.
 *
 * Disambiguation:
 *   - `#scope` (literal) → kind: 'scope'
 *   - `#<other>`         → kind: 'filename'
 *   - `@<other>`         → kind: 'entity'
 *
 * Token shape: sigil + identifier (alphanum, hyphen, underscore, dot).
 * Whitespace terminates a token. Trailing punctuation outside the
 * identifier class is NOT consumed (handled by the regex character class).
 */
function extractReferences(text: string): Reference[] {
  const refs: Reference[] = [];
  // Reset the regex's lastIndex (the regex is shared via the module-level
  // const and is stateful when /g is set).
  REFERENCE_TOKEN_RE.lastIndex = 0;

  let m: RegExpExecArray | null;
  while ((m = REFERENCE_TOKEN_RE.exec(text)) !== null) {
    const sigil = m[1];
    const value = m[2];
    const raw = `${sigil}${value}`;

    let kind: ReferenceKind;
    if (sigil === '@') {
      kind = 'entity';
    } else {
      // sigil === '#'
      kind = value.toLowerCase() === 'scope' ? 'scope' : 'filename';
    }

    refs.push({ kind, value, raw });
  }

  return refs;
}

// ---------------------------------------------------------------------------
// Read-only exports for downstream tasks
// ---------------------------------------------------------------------------

/**
 * Read-only vocabulary tables exposed for downstream consumers:
 *   - Task 084 `/help` UI affordance enumerates commands from these
 *   - Task 081 / 082 dispatchers iterate to register handlers
 *
 * Do NOT mutate these arrays. Vocabulary is closed (Q6).
 */
export const HardSlashes: readonly HardSlashCommand[] = HARD_SLASHES;
export const SoftSlashes: readonly SoftSlashCommand[] = SOFT_SLASHES;
