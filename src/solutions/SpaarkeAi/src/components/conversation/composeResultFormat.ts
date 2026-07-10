/**
 * composeResultFormat.ts — per-action PROSE formatters for the 5 Compose AI
 * action output schemas (spaarkeai-compose-r2 task 112, UAT-R3 defect #3b/#3c).
 *
 * ROOT CAUSE (uat-r2-defect-triage-2026-07-10.md, R3): `formatEventOutputMarkdown`
 * (./DocumentUploadedEventStream.ts) understands ONLY the Event-path summarize
 * schema (`tldr`/`summary`/`keywords`); when neither field is present it falls
 * into a ```json``` fence. Every Compose action returns a DIFFERENT schema
 * shape (`infra/dataverse/outputschemas/compose-*.schema.json`), so all five
 * hit that fallback — an inline AI toolbar click rendered an unreadable JSON
 * blob instead of readable prose.
 *
 * This module maps each of the 5 Compose action result shapes to grounded
 * markdown — every field rendered comes directly from the payload; nothing is
 * invented or paraphrased beyond structural labels (ADR-039 grounded output,
 * "render only what came back").
 *
 * KEYING NOTE (deviation from the task's literal phrasing, documented per
 * CLAUDE.md §11): the task's guidance was to key the formatter on the action
 * id / bindingId carried by `ComposeActionRequest`. In the shipped wiring
 * (`useSerialActionQueue.ts`, `ComposeAiToolbar.tsx`), `bindingId` is the
 * `sprk_playbookconsumer` Binding row GUID — MINTED PER-ENVIRONMENT at catalog
 * seed time (ComposeAiToolbar.tsx's "PHASE-4 STUB BOUNDARY" note) with NO
 * client-side GUID→action-code resolver, and the request's `id` field is a
 * per-click correlation id, not the action's stable code. Neither is a
 * reliable, build-time-knowable discriminant a `switch` could key on.
 * Instead this module keys on the RESULT'S OWN SHAPE: the 5
 * `compose-*.schema.json` documents declare `additionalProperties:false` with
 * MUTUALLY EXCLUSIVE required-field sets (verified: no field name is shared
 * across two schemas' required sets), so exact structural detection is
 * deterministic and unambiguous — the same "never sniff a single ambiguous
 * field" complaint the task raised about the OLD tldr/summary heuristic is
 * avoided by requiring the FULL discriminating field combination per shape,
 * not a single loosely-typed field.
 *
 * @see infra/dataverse/outputschemas/compose-explain-clause.schema.json
 * @see infra/dataverse/outputschemas/compose-compare-to-playbook.schema.json
 * @see infra/dataverse/outputschemas/compose-draft-alternative.schema.json
 * @see infra/dataverse/outputschemas/compose-summarize-word-changes.schema.json
 * @see infra/dataverse/outputschemas/compose-defined-terms.schema.json
 * @see ./DocumentUploadedEventStream.ts (formatEventOutputMarkdown — the
 *      last-resort JSON-fence fallback for genuinely unknown shapes; this
 *      module's dispatcher returns `null` for anything it does not
 *      recognize, so callers keep that fallback fully intact)
 * @see projects/spaarkeai-compose-r2/notes/uat-r2-defect-triage-2026-07-10.md (R3)
 * @see ./ConversationPane.tsx (dispatchComposeAction — the injection seam)
 */

// ---------------------------------------------------------------------------
// Shared field-level helpers (pure, total, defensive against malformed input)
// ---------------------------------------------------------------------------

function asRecord(payload: unknown): Record<string, unknown> | null {
  return payload !== null && typeof payload === 'object' && !Array.isArray(payload)
    ? (payload as Record<string, unknown>)
    : null;
}

function asNonEmptyString(value: unknown): string | null {
  return typeof value === 'string' && value.length > 0 ? value : null;
}

function asStringArray(value: unknown): string[] {
  return Array.isArray(value)
    ? value.filter((v): v is string => typeof v === 'string' && v.length > 0)
    : [];
}

// ---------------------------------------------------------------------------
// compose-explain-clause — { explanation, keyConcepts, relatedPlaybookIds }
// ---------------------------------------------------------------------------

/** Detects + renders a `compose-explain-clause` result, or `null` if the shape doesn't match. */
export function formatExplainClauseResult(payload: unknown): string | null {
  const record = asRecord(payload);
  if (record === null) return null;
  const explanation = asNonEmptyString(record.explanation);
  // `keyConcepts` may legitimately be an EMPTY array ("insufficient selection"
  // per the schema) — detect on the FIELD being an array, not its length.
  if (explanation === null || !Array.isArray(record.keyConcepts)) return null;

  const keyConcepts = asStringArray(record.keyConcepts);
  const parts: string[] = [`**Explanation:** ${explanation}`];
  if (keyConcepts.length > 0) {
    parts.push(`**Key concepts:** ${keyConcepts.join(', ')}`);
  }
  const relatedPlaybookIds = asStringArray(record.relatedPlaybookIds);
  if (relatedPlaybookIds.length > 0) {
    parts.push(`**Related playbook references:** ${relatedPlaybookIds.join(', ')}`);
  }
  return parts.join('\n\n');
}

// ---------------------------------------------------------------------------
// compose-compare-to-playbook — { matches[], overallRisk }
// ---------------------------------------------------------------------------

/** Detects + renders a `compose-compare-to-playbook` result, or `null` if the shape doesn't match. */
export function formatCompareToPlaybookResult(payload: unknown): string | null {
  const record = asRecord(payload);
  if (record === null) return null;
  const overallRisk = asNonEmptyString(record.overallRisk);
  if (overallRisk === null || !Array.isArray(record.matches)) return null;

  const parts: string[] = [`**Overall risk:** ${overallRisk}`];
  const matches = record.matches as unknown[];
  if (matches.length === 0) {
    parts.push('No relevant playbook entries were supplied for comparison.');
    return parts.join('\n\n');
  }

  const lines = matches
    .map((entry) => {
      const m = asRecord(entry);
      if (m === null) return null;
      const id = asNonEmptyString(m.playbookEntryId) ?? 'unspecified entry';
      const riskScore = typeof m.riskScore === 'number' ? m.riskScore.toFixed(2) : 'n/a';
      const rationale = asNonEmptyString(m.rationale) ?? '';
      const deviations = asStringArray(m.deviations);
      const deviationsPart = deviations.length > 0 ? ` Deviations: ${deviations.join('; ')}.` : '';
      return `- **${id}** (risk ${riskScore}): ${rationale}${deviationsPart}`;
    })
    .filter((line): line is string => line !== null);

  parts.push(['**Matches:**', ...lines].join('\n'));
  return parts.join('\n\n');
}

// ---------------------------------------------------------------------------
// compose-draft-alternative — { target_text, new_text, match_mode, rationale, sources[] }
// ---------------------------------------------------------------------------

/** Detects + renders a `compose-draft-alternative` result, or `null` if the shape doesn't match. */
export function formatDraftAlternativeResult(payload: unknown): string | null {
  const record = asRecord(payload);
  if (record === null) return null;
  const newText = asNonEmptyString(record.new_text);
  const targetText = asNonEmptyString(record.target_text);
  const matchMode = asNonEmptyString(record.match_mode);
  if (newText === null || targetText === null || matchMode === null) return null;

  const rationale = asNonEmptyString(record.rationale);
  const parts: string[] = ['**Drafted an alternative clause.**'];
  if (rationale !== null) {
    parts.push(`**Rationale:** ${rationale}`);
  }
  parts.push(`**Proposed text:**\n\n> ${newText.replace(/\n/g, '\n> ')}`);

  const sources = Array.isArray(record.sources) ? (record.sources as unknown[]) : [];
  const sourceLines = sources
    .map((s) => {
      const src = asRecord(s);
      if (src === null) return null;
      const type = asNonEmptyString(src.type) ?? 'source';
      const id = asNonEmptyString(src.id);
      return id !== null ? `- ${type}: ${id}` : null;
    })
    .filter((line): line is string => line !== null);
  if (sourceLines.length > 0) {
    parts.push(['**Sources:**', ...sourceLines].join('\n'));
  }
  return parts.join('\n\n');
}

// ---------------------------------------------------------------------------
// compose-summarize-word-changes — { summary, changes[] }
// ---------------------------------------------------------------------------

/**
 * Detects + renders a `compose-summarize-word-changes` result, or `null` if
 * the shape doesn't match. Requires BOTH `summary` (string) AND `changes`
 * (array) so this never misfires on the unrelated Event-path summarize schema
 * (`tldr`/`summary`/`keywords` — no `changes` field).
 */
export function formatSummarizeWordChangesResult(payload: unknown): string | null {
  const record = asRecord(payload);
  if (record === null) return null;
  const summary = asNonEmptyString(record.summary);
  if (summary === null || !Array.isArray(record.changes)) return null;

  const parts: string[] = [summary];
  const changes = record.changes as unknown[];
  if (changes.length > 0) {
    const lines = changes
      .map((c) => {
        const change = asRecord(c);
        if (change === null) return null;
        const kind = asNonEmptyString(change.kind) ?? 'change';
        const location = asNonEmptyString(change.location);
        const description = asNonEmptyString(change.description) ?? '';
        const locationPart = location !== null ? ` ${location}:` : '';
        return `- **[${kind}]**${locationPart} ${description}`;
      })
      .filter((line): line is string => line !== null);
    parts.push(['**Changes:**', ...lines].join('\n'));
  }
  return parts.join('\n\n');
}

// ---------------------------------------------------------------------------
// compose-defined-terms — { terms[], inconsistencies[] }
// ---------------------------------------------------------------------------

/** Detects + renders a `compose-defined-terms` result, or `null` if the shape doesn't match. */
export function formatDefinedTermsResult(payload: unknown): string | null {
  const record = asRecord(payload);
  if (record === null) return null;
  if (!Array.isArray(record.terms) || !Array.isArray(record.inconsistencies)) return null;

  const terms = record.terms as unknown[];
  const inconsistencies = record.inconsistencies as unknown[];
  const parts: string[] = [];

  if (terms.length > 0) {
    const lines = terms
      .map((t) => {
        const term = asRecord(t);
        if (term === null) return null;
        const name = asNonEmptyString(term.term) ?? 'unnamed term';
        const usages = Array.isArray(term.usages) ? (term.usages as unknown[]) : [];
        const inconsistentCount = usages.filter((u) => {
          const usage = asRecord(u);
          return usage !== null && usage.consistent === false;
        }).length;
        const usageNote =
          inconsistentCount > 0
            ? ` (${inconsistentCount} inconsistent usage${inconsistentCount === 1 ? '' : 's'})`
            : '';
        return `- **${name}**${usageNote}`;
      })
      .filter((line): line is string => line !== null);
    parts.push(['**Defined terms:**', ...lines].join('\n'));
  } else {
    parts.push('No defined terms were found.');
  }

  if (inconsistencies.length > 0) {
    const lines = inconsistencies
      .map((i) => {
        const item = asRecord(i);
        if (item === null) return null;
        const term = asNonEmptyString(item.term) ?? 'unknown term';
        const detail = asNonEmptyString(item.detail) ?? '';
        return `- **${term}:** ${detail}`;
      })
      .filter((line): line is string => line !== null);
    parts.push(['**Inconsistencies:**', ...lines].join('\n'));
  }

  return parts.join('\n\n');
}

// ---------------------------------------------------------------------------
// Dispatcher — tries each of the 5 shapes; `null` means "not a recognized
// Compose action result" so the caller falls back to the JSON-fence formatter.
// ---------------------------------------------------------------------------

const COMPOSE_RESULT_FORMATTERS: ReadonlyArray<(payload: unknown) => string | null> = [
  formatExplainClauseResult,
  formatCompareToPlaybookResult,
  formatDefinedTermsResult,
  formatSummarizeWordChangesResult,
  formatDraftAlternativeResult,
];

/**
 * Render a Compose action's terminal result as grounded prose, trying each of
 * the 5 shipped Compose output schemas in turn. Returns `null` when the
 * payload matches none of them (the general Event-path summarize schema, a
 * genuinely unknown shape, or a non-object payload) — the caller (
 * `ConversationPane.dispatchComposeAction`) falls back to
 * `formatEventOutputMarkdown`, which keeps its own tldr/summary handling and
 * ```json``` last-resort fence intact.
 */
export function formatComposeActionResultMarkdown(payload: unknown): string | null {
  for (const formatter of COMPOSE_RESULT_FORMATTERS) {
    const formatted = formatter(payload);
    if (formatted !== null) return formatted;
  }
  return null;
}
