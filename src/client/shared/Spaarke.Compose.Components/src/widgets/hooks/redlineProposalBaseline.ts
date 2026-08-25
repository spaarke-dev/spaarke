/**
 * redlineProposalBaseline.ts — the DETECTION half of FR-C05's stale-target outcome
 * (spaarkeai-compose-r8 task 052).
 *
 * THE PROBLEM. "This clause changed since the suggestion" needs two texts: the clause NOW, and the
 * clause AS THE MODEL SAW IT. The first is in the editor. The second is nowhere durable — task 052
 * stops asking the model to echo `target_text` back (it is a lossy paraphrase and a placement hazard,
 * ADR-049 I-7), `ParaIdMapEntry` carries ids + numbering but no text, and the compose-outputs read
 * projection returns only `{key, bindingId, turn, disposition, payload}`. Without a second text the
 * check cannot exist, and a stored suggestion silently overwrites whatever the user typed since —
 * the silent data loss FR-C05 exists to close.
 *
 * WHAT THIS RECORDS. The FIRST time a given `ledgerRef` is materialized, the anchored paragraph's
 * text at that instant IS the capture-time text: the model produced its proposal against the live
 * document milliseconds earlier, so nothing can have drifted yet. That text is recorded here, keyed
 * by `{scope}` (the document session) + `{ledgerRef}`. Every LATER materialize of the same key — a
 * refresh replay, an untargeted reopen pass, an undo/try-another re-render — compares against it.
 *
 * WHY sessionStorage. This is DETECTION state, not the user's RESOLUTION. The resolution obligation
 * (task-050 assessment §4.4 O-2) is ledger durability, and it is met by the FR-17 supersession write
 * — see `ComposeWorkspace.supersedeComposeOutput`. A baseline that is missing after a cross-tab
 * reopen degrades to "no prompt", which is exactly the pre-task behaviour and never a wrong edit.
 * Same-tab refresh — the O-5 acceptance scenario — is covered. The precedent is
 * `writeReviewFindingsMarker` in `ComposeWorkspace.tsx`: a best-effort, same-tab durability marker
 * used to detect a state the server projection cannot express.
 *
 * CONTRACT: pure module-level functions, best-effort, NEVER throws (private browsing, quota, SSR),
 * never logs the text it stores (Tier 3).
 *
 * @see ./usePendingRedline.ts — the consumer; records on first materialize, compares on every later one
 * @see projects/spaarkeai-compose-r8/notes/adr-043-041-assessment.md §4.4 (O-1…O-6)
 */

/** One sessionStorage entry per document session; the value is a `{ledgerRef: text}` JSON map. */
const KEY_PREFIX = 'spaarke.compose.redline-proposal-baseline.';

/**
 * Cap on entries per scope. A long editing session accumulates one entry per materialized suggestion;
 * the cap bounds the stored blob and drops the OLDEST first (insertion order). An evicted key simply
 * stops being checkable — it degrades to the pre-task behaviour, never to a wrong answer.
 */
const MAX_ENTRIES = 200;

function storage(): Storage | null {
  try {
    if (typeof window === 'undefined' || !window.sessionStorage) return null;
    return window.sessionStorage;
  } catch {
    return null;
  }
}

function readMap(scope: string): Record<string, string> {
  const store = storage();
  if (!store || !scope) return {};
  try {
    const raw = store.getItem(KEY_PREFIX + scope);
    if (!raw) return {};
    const parsed: unknown = JSON.parse(raw);
    return parsed && typeof parsed === 'object' && !Array.isArray(parsed)
      ? (parsed as Record<string, string>)
      : {};
  } catch {
    return {};
  }
}

function writeMap(scope: string, map: Record<string, string>): void {
  const store = storage();
  if (!store || !scope) return;
  try {
    store.setItem(KEY_PREFIX + scope, JSON.stringify(map));
  } catch {
    /* quota / private browsing — detection degrades to "no prompt", never to a wrong edit. */
  }
}

/**
 * The text the suggestion `ledgerRef` was PROPOSED AGAINST, or `undefined` when this key has never
 * been materialized in this session/tab (i.e. this IS the first materialize).
 */
export function readProposalBaseline(scope: string, ledgerRef: string): string | undefined {
  if (!scope || !ledgerRef) return undefined;
  const value = readMap(scope)[ledgerRef];
  return typeof value === 'string' ? value : undefined;
}

/**
 * Record (or re-record) the capture-time text for `ledgerRef`. Re-recording is how "apply anyway"
 * stops a same-session re-render from re-asking a question the user already answered; the DURABLE
 * answer is the ledger supersession, not this.
 */
export function recordProposalBaseline(scope: string, ledgerRef: string, text: string): void {
  if (!scope || !ledgerRef) return;
  const map = readMap(scope);
  delete map[ledgerRef]; // re-insert at the end so eviction stays oldest-first
  map[ledgerRef] = text;
  const keys = Object.keys(map);
  if (keys.length > MAX_ENTRIES) {
    for (const stale of keys.slice(0, keys.length - MAX_ENTRIES)) delete map[stale];
  }
  writeMap(scope, map);
}

/** Test/reset helper — drops every recorded baseline for a scope. */
export function clearProposalBaselines(scope: string): void {
  const store = storage();
  if (!store || !scope) return;
  try {
    store.removeItem(KEY_PREFIX + scope);
  } catch {
    /* best effort */
  }
}
