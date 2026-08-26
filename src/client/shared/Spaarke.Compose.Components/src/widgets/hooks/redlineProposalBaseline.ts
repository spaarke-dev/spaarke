/**
 * redlineProposalBaseline.ts — the DETECTION half of FR-C05's stale-target outcome
 * (spaarkeai-compose-r8 task 052, made durable beyond the tab by task 052b).
 *
 * THE PROBLEM. "This clause changed since the suggestion" needs two texts: the clause NOW, and the
 * clause AS THE MODEL SAW IT. The first is in the editor. The second is nowhere on the wire — task
 * 052 stopped asking the model to echo `target_text` back (it is a lossy paraphrase and a placement
 * hazard, ADR-049 I-7), `ParaIdMapEntry` carries ids + numbering but no text, and the compose-outputs
 * read projection returns only `{key, bindingId, turn, disposition, payload}`. The capture-time datum
 * must therefore be a COPY THE SYSTEM TAKES, never a reproduction the model makes (project invariant 7).
 *
 * WHAT THIS RECORDS. On the LIVE materialize of a `ledgerRef` — the model produced its proposal
 * against this document seconds earlier, so the anchored paragraph as it reads now IS the capture-time
 * text — that text is recorded here, keyed by `{scope}` (the document session) + `{ledgerRef}`. Every
 * later REPLAY of the same key compares against it.
 *
 * ------------------------------------------------------------------------------------------------
 * TASK 052b — WHY THERE ARE TWO TIERS, AND WHAT EACH ONE IS FOR
 * ------------------------------------------------------------------------------------------------
 * Task 052 kept only the text, in `sessionStorage`. That covers a same-tab refresh (the O-5 acceptance
 * scenario) and nothing beyond it: a reopen in a DIFFERENT TAB or a new window sees an empty store, and
 * so does a session that evicted the entry. In each of those cases the check went inert and the
 * suggestion applied silently — which is the pre-052 behaviour, i.e. the silent overwrite FR-C05 exists
 * to close. The fix has two independent parts, and only the SECOND one lives in this module:
 *
 *   1. The GATE's correctness comes from the CALLER, not from this store. `usePendingRedline` now
 *      distinguishes a LIVE materialize (nothing can have drifted) from a REPLAY, and a replay with
 *      {@link ProposalBaselineComparison} `'unrecorded'` asks before it places. That makes the
 *      undeterminable case honest no matter what any store does — including a different DEVICE, a
 *      cleared browser, and private browsing, none of which this module can ever reach.
 *   2. This module's job is therefore reduced to NOISE: keep the question from being asked when we
 *      genuinely do know the clause is unchanged. That is what the durable tier buys.
 *
 * | tier | store | holds | scope | why |
 * |---|---|---|---|---|
 * | durable | `localStorage` | a one-way {@link fingerprintParagraph} | browser origin | answers "did it change?" across tabs, windows and restarts |
 * | per-tab | `sessionStorage` | the paragraph TEXT | this tab | additionally answers "changed FROM WHAT?", so the confirmation can quote the capture-time wording |
 *
 * The DECISION is single-sourced: whichever tier answers, it answers the same question by comparing
 * the same capture-time text (the per-tab tier compares it directly; the durable tier compares its
 * fingerprint). The per-tab tier is consulted first only because it is strictly richer — it can also
 * supply {@link ProposalBaselineComparison.proposedAgainst}. A tier that is absent is never a "no";
 * it is silence, and the caller treats silence as `'unrecorded'`.
 *
 * ADR-015. Paragraph text is Tier-3 content. The DURABLE tier therefore stores a fingerprint and
 * never the text: nothing recoverable is left at rest after the tab closes. The per-tab tier keeps
 * the text exactly as task 052 shipped it — `sessionStorage` dies with the tab, which is the same
 * lifetime as the editor buffer holding that paragraph anyway.
 *
 * CONTRACT: pure module-level functions, best-effort, NEVER throws (private browsing, quota, SSR),
 * never logs the text it stores (Tier 3).
 *
 * @see ./usePendingRedline.ts — the consumer; records on a LIVE materialize, compares on every replay
 * @see projects/spaarkeai-compose-r8/notes/adr-043-041-assessment.md §4.4 (O-1…O-6)
 * @see projects/spaarkeai-compose-r8/notes/052b-stale-detection-decisions.md (carrier choice + evidence)
 */

/** One sessionStorage entry per document session; the value is a `{ledgerRef: text}` JSON map. */
const TEXT_KEY_PREFIX = 'spaarke.compose.redline-proposal-baseline.';

/**
 * ONE localStorage entry for every document session this browser has seen; the value is a flat
 * `{durableKey: fingerprint}` JSON map. A single key (rather than one per scope) is deliberate: it
 * makes the total footprint boundable by {@link MAX_FINGERPRINT_ENTRIES} instead of leaving one
 * orphan key per document the user ever opened.
 */
const FINGERPRINT_KEY = 'spaarke.compose.redline-proposal-fingerprint';

/**
 * Cap on TEXT entries per scope. A long editing session accumulates one entry per materialized
 * suggestion; the cap bounds the stored blob and drops the OLDEST first (insertion order). Since
 * task 052b an evicted text entry costs only the "when suggested" quote — the durable tier below
 * still answers whether the clause changed.
 */
const MAX_TEXT_ENTRIES = 200;

/**
 * Cap on DURABLE fingerprint entries, across all scopes. A fingerprint is ~20 characters against a
 * paragraph's ~1 KB, so this tier can hold an order of magnitude more history in a fraction of the
 * space (~110 KB fully populated, against a typical 5 MB origin quota). Eviction is oldest-first and
 * degrades to the `'unrecorded'` outcome — a question, never a wrong answer.
 */
const MAX_FINGERPRINT_ENTRIES = 1000;

/** What the recorded baseline says about the clause as it reads right now. */
export type ProposalBaselineComparison =
  /** Nothing was ever recorded for this key in this browser — detection cannot be established here. */
  | { readonly status: 'unrecorded' }
  /** The clause is byte-identical to the text the suggestion was proposed against. */
  | { readonly status: 'unchanged' }
  /**
   * The clause is NOT the text the suggestion was proposed against.
   * `proposedAgainst` is the capture-time wording when the per-tab tier still has it, and `null`
   * when only the durable fingerprint survives — a fingerprint proves the change, it cannot
   * reproduce the words, and inventing them is the thing this whole path refuses to do.
   */
  | { readonly status: 'changed'; readonly proposedAgainst: string | null };

type StorageKind = 'session' | 'local';

function storage(kind: StorageKind): Storage | null {
  try {
    if (typeof window === 'undefined') return null;
    const store = kind === 'session' ? window.sessionStorage : window.localStorage;
    return store ?? null;
  } catch {
    return null;
  }
}

function readJsonMap(kind: StorageKind, key: string): Record<string, string> {
  const store = storage(kind);
  if (!store) return {};
  try {
    const raw = store.getItem(key);
    if (!raw) return {};
    const parsed: unknown = JSON.parse(raw);
    return parsed && typeof parsed === 'object' && !Array.isArray(parsed) ? (parsed as Record<string, string>) : {};
  } catch {
    return {};
  }
}

function writeJsonMap(kind: StorageKind, key: string, map: Record<string, string>): void {
  const store = storage(kind);
  if (!store) return;
  try {
    store.setItem(key, JSON.stringify(map));
  } catch {
    /* quota / private browsing — detection degrades to the caller's honest `'unrecorded'` outcome. */
  }
}

/** Insert at the END (so eviction stays oldest-first) and trim to `max`. */
function insertBounded(map: Record<string, string>, key: string, value: string, max: number): void {
  delete map[key];
  map[key] = value;
  const keys = Object.keys(map);
  if (keys.length > max) {
    for (const stale of keys.slice(0, keys.length - max)) delete map[stale];
  }
}

/**
 * The durable tier's composite key. Length-prefixing the scope makes the join UNAMBIGUOUS: there is
 * exactly one way to split `12|abc…|b1@t1` back into its two parts, so no pair of (scope, ledgerRef)
 * values can ever address the same entry as a different pair.
 */
function durableKey(scope: string, ledgerRef: string): string {
  return `${scope.length}|${scope}|${ledgerRef}`;
}

/**
 * A one-way fingerprint of a paragraph's text — the DURABLE tier's stored datum (ADR-015: a digest,
 * not Tier-3 content).
 *
 * Two independent 32-bit lanes (FNV-1a, and a second multiply/xorshift mix with different constants)
 * plus the character length, base-36 encoded. It answers exactly one question — "is this the same
 * text?" — which is all the stale GATE asks: `redlineLocalDiff` computes the edit range from the LIVE
 * paragraph and the model's `new_text`, never from the baseline, so no consumer needs the capture-time
 * characters to decide anything (only to QUOTE them, which the per-tab tier covers).
 *
 * Deliberately synchronous and dependency-free: `crypto.subtle` is async and would turn a
 * synchronous placement decision into a promise, and a hashing library would be a new dependency for
 * a check whose failure mode is bounded (see below). NOT a security primitive — it authenticates
 * nothing and guards no boundary.
 *
 * RESIDUAL, stated rather than hidden: a text of a DIFFERENT length can never collide (the length is
 * part of the value). A different text of the SAME length collides at a nominal 2^-64; even assuming
 * the two lanes are less than fully independent, the rate stays far below anything that matters here,
 * and the consequence is bounded to ONE paragraph of ONE suggestion reverting to the pre-052
 * behaviour — not to a systematic loss of detection.
 *
 * Total: never throws, defined for every string including the empty one.
 */
export function fingerprintParagraph(text: string): string {
  let h1 = 0x811c9dc5; // FNV-1a 32 offset basis
  let h2 = 0x01000193; // second lane, seeded with FNV's prime so the two never start equal
  for (let i = 0; i < text.length; i++) {
    const code = text.charCodeAt(i);
    h1 = Math.imul(h1 ^ code, 0x01000193);
    h2 = Math.imul(h2 ^ code, 0x85ebca6b);
    h2 ^= h2 >>> 13;
  }
  return `${text.length.toString(36)}.${(h1 >>> 0).toString(36)}.${(h2 >>> 0).toString(36)}`;
}

/**
 * Compare the live clause against what this suggestion was proposed against.
 *
 * The per-tab tier is consulted first because it is strictly richer (it can also say WHAT the clause
 * used to read); the durable tier answers when the tab has no record — the cross-tab, new-window and
 * post-restart cases task 052b exists to close. `'unrecorded'` means neither tier knows, which the
 * caller must treat as "detection could not be established", NOT as "nothing changed".
 */
export function compareProposalBaseline(
  scope: string,
  ledgerRef: string,
  currentText: string
): ProposalBaselineComparison {
  if (!scope || !ledgerRef) return { status: 'unrecorded' };

  const recordedText = readJsonMap('session', TEXT_KEY_PREFIX + scope)[ledgerRef];
  if (typeof recordedText === 'string') {
    return recordedText === currentText
      ? { status: 'unchanged' }
      : { status: 'changed', proposedAgainst: recordedText };
  }

  const recordedFingerprint = readJsonMap('local', FINGERPRINT_KEY)[durableKey(scope, ledgerRef)];
  if (typeof recordedFingerprint === 'string') {
    return recordedFingerprint === fingerprintParagraph(currentText)
      ? { status: 'unchanged' }
      : { status: 'changed', proposedAgainst: null };
  }

  return { status: 'unrecorded' };
}

/**
 * Record (or re-record) the capture-time text for `ledgerRef` into BOTH tiers.
 *
 * Re-recording is how "apply anyway" stops a later re-render from re-asking a question the user
 * already answered; the DURABLE answer is still the ledger supersession, not this. Writing both tiers
 * from the same string in the same call is what keeps them from ever disagreeing about a key they
 * both hold: they are two encodings of one datum, not two data.
 */
export function recordProposalBaseline(scope: string, ledgerRef: string, text: string): void {
  if (!scope || !ledgerRef) return;

  const textMap = readJsonMap('session', TEXT_KEY_PREFIX + scope);
  insertBounded(textMap, ledgerRef, text, MAX_TEXT_ENTRIES);
  writeJsonMap('session', TEXT_KEY_PREFIX + scope, textMap);

  const fingerprintMap = readJsonMap('local', FINGERPRINT_KEY);
  insertBounded(fingerprintMap, durableKey(scope, ledgerRef), fingerprintParagraph(text), MAX_FINGERPRINT_ENTRIES);
  writeJsonMap('local', FINGERPRINT_KEY, fingerprintMap);
}

/** Test/reset helper — drops every recorded baseline for a scope, in BOTH tiers. */
export function clearProposalBaselines(scope: string): void {
  if (!scope) return;

  const sessionStore = storage('session');
  if (sessionStore) {
    try {
      sessionStore.removeItem(TEXT_KEY_PREFIX + scope);
    } catch {
      /* best effort */
    }
  }

  const fingerprintMap = readJsonMap('local', FINGERPRINT_KEY);
  const prefix = `${scope.length}|${scope}|`;
  let removed = false;
  for (const key of Object.keys(fingerprintMap)) {
    if (key.startsWith(prefix)) {
      delete fingerprintMap[key];
      removed = true;
    }
  }
  if (removed) writeJsonMap('local', FINGERPRINT_KEY, fingerprintMap);
}
