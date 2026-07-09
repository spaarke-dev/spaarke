/**
 * progressiveSectionReveal — client-side progressive reveal of an already-STORED
 * dispatch result (ADR-040 store-before-render; ADR-037 amended section-name-keyed
 * streaming contract).
 *
 * spaarke-ai-architecture-redesign-r2 task 039 (FR-A1-10 / D-F5).
 *
 * WHY THIS EXISTS (mechanism choice — full rationale in task notes
 * `projects/spaarke-ai-architecture-redesign-r2/notes/039-progressive-render-decision.md`):
 *
 * The terminal `complete` AnalysisChunk carries the FULL dispatched result — it is only
 * ever emitted by the BFF AFTER `IOutputRouter.RouteAsync` has written the SessionOutput
 * to the session ledger (see `SessionDispatchOrchestrator.DispatchAsync`, which now also
 * asserts this via `ProgressiveRenderGuard.EnsureStored` — ADR-040 D2/D8). This module
 * NEVER has access to pre-store state — it operates exclusively on the terminal result the
 * stream already delivered, so there is no code path by which it could render ahead of the
 * ledger write.
 *
 * What this module adds is PACING. `dispatchConsumer.ts` already synthesizes one
 * `section_started` / `section_completed` pair per top-level result key (task 046 / amended
 * ADR-037's section-name-keyed contract), but it previously did so in a single synchronous
 * loop — React 18/19 batches those state updates into ONE paint, so the existing
 * `StructuredOutputStreamWidget` still rendered everything at once, visually
 * indistinguishable from the single terminal blob D-F5 asks us to eliminate. This module
 * staggers the SAME section-keyed reveal with a small delay between sections so the output
 * arrives in ≥2 visible steps — the client-side progressive-reveal mechanism (the POML's
 * documented fallback), riding the EXISTING section-name-keyed wire vocabulary (no new SSE
 * event types, no second streaming protocol — ADR-039).
 */

/** One revealable (name, value) pair extracted from a terminal dispatch result. */
export interface RevealableSection {
  readonly name: string;
  readonly value: unknown;
}

/** Widget-internal metadata fields that are never independently revealable sections. */
const SKIPPED_KEYS = new Set(['parsedSuccessfully', 'rawResponse']);

/**
 * Extract the ordered, revealable (name, value) pairs from a terminal dispatch result.
 * Pure — no side effects, no timing. Declaration order is preserved (Object.entries
 * iterates own enumerable string keys in insertion order for a JSON-parsed object, which
 * mirrors the BFF action's output-schema property order — see
 * `StructuredOutputStreamWidget`'s `SUMMARIZE_SCHEMA` module doc).
 *
 * Filtering matches what `dispatchConsumer.ts` already applied inline before this module
 * existed: null/undefined values, empty strings, and the two widget-internal metadata keys
 * (`parsedSuccessfully`, `rawResponse`) are skipped.
 */
export function extractRevealableSections(result: Record<string, unknown>): RevealableSection[] {
  const sections: RevealableSection[] = [];
  for (const [name, value] of Object.entries(result)) {
    if (value === null || value === undefined) continue;
    if (SKIPPED_KEYS.has(name)) continue;
    if (typeof value === 'string' && value === '') continue;
    sections.push({ name, value });
  }
  return sections;
}

export interface ProgressiveRevealOptions {
  /**
   * Delay (ms) awaited between revealing successive sections. Default 120 — long enough to
   * be a perceptible, distinct step in the UI, short enough not to feel sluggish for the
   * common 2-4 section case. Pass 0 to reveal all sections back-to-back (e.g. tests that
   * don't care about pacing) — sections still publish in N separate calls, only the
   * inter-section delay is skipped.
   */
  readonly delayMs?: number;
}

/**
 * Reveal `sections` one at a time via `publishSection`, awaiting `delayMs` between each
 * (not before the first section, not after the last) — N sections produce N-1 visible
 * pauses. This is the ONLY place pacing is introduced; `publishSection` itself is a plain
 * synchronous callback (typically two `publishPaneEvent` calls — `section_started` then
 * `section_completed` — mirroring the existing per-key bridge in `dispatchConsumer.ts`).
 *
 * Store-before-render note: this function has no access to anything but `sections`, which
 * the caller MUST have derived from the terminal (already-stored) dispatch result — never
 * from an earlier ("text"/"delta"-shaped) chunk. See the module doc above.
 */
export async function revealSectionsProgressively(
  sections: readonly RevealableSection[],
  publishSection: (section: RevealableSection, index: number, total: number) => void,
  options: ProgressiveRevealOptions = {}
): Promise<void> {
  const delayMs = options.delayMs ?? 120;
  for (let i = 0; i < sections.length; i++) {
    publishSection(sections[i], i, sections.length);
    if (i < sections.length - 1 && delayMs > 0) {
      await sleep(delayMs);
    }
  }
}

function sleep(ms: number): Promise<void> {
  return new Promise(resolve => setTimeout(resolve, ms));
}
