/**
 * What the Procedures panel shows, as pure functions of task 084's DTOs (ebilling-r1 task 084a): state badges, labels, the count-card filters
 * and which rows are dimmed. No React, no fetching, no resolution: every fact comes from the server's answer; this module only words it.
 *
 * Two mappings are written to be extended without touching the components:
 *   - {@link RULE_MARKS}: one entry per mark kind (label, tone). A kind not in the table renders as a NEUTRAL badge with the kind's own
 *     text (the mark array is open). Task 084e's `Outranked` and `CapOutranked` show the server's own `text` ("Outranked by X").
 *   - {@link DIMMED_MARK_KINDS}: the mark kinds whose rule does not run here and is shown dimmed. `Outranked` and `CapOutranked` are NOT
 *     in it: an outranked rule is still in force (it runs if every outranking rule is refused on an invoice), so it keeps "In force".
 * The word "displaced" is never shown (EB-D-219: it is "outranked", task 084e).
 */

import type { MetricCardConfig, StatusBadgeTone } from '@spaarke/ui-components';
import type { GapView, ProcedureItem, RuleItem, RuleMark, SourceView, WritePermissions } from './recordProceduresApi';

// ─────────────────────────────────────────────────────────────────────────────
// Write permissions (task 084g, EB-D-221)
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Whether the wizards open read-only, from the `permissions` of the answers the lists came from (both routes answer it). A wizard can plan an
 * assignment or exclusion and a removal (a switch is both), so it may write only when EVERY answer gives both `assignOrExclude` and `remove`;
 * `change` is task 084c's and opens nothing yet. No answer at all is read-only. A hint only: the write routes still decide.
 */
export function wizardReadOnly(...answers: ReadonlyArray<WritePermissions>): boolean {
  return answers.length === 0 || !answers.every(p => p.assignOrExclude === true && p.remove === true);
}

// ─────────────────────────────────────────────────────────────────────────────
// Words
// ─────────────────────────────────────────────────────────────────────────────

const MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];

/** "2026-11-12" -> "12 Nov 2026". Anything that is not a plain date is returned as it came. */
export function shortDate(iso: string | null | undefined): string {
  const m = /^(\d{4})-(\d{2})-(\d{2})/.exec(iso ?? '');
  if (!m) return iso ?? '';
  return `${Number(m[3])} ${MONTHS[Number(m[2]) - 1] ?? m[2]} ${m[1]}`;
}

/** Today as yyyy-MM-dd in the user's own calendar. */
export function todayIso(now: Date = new Date()): string {
  const p = (n: number) => String(n).padStart(2, '0');
  return `${now.getFullYear()}-${p(now.getMonth() + 1)}-${p(now.getDate())}`;
}

const TABLE_NOUN: Readonly<Record<string, string>> = { sprk_matter: 'matter', sprk_project: 'project', sprk_workassignment: 'work assignment' };

export const recordNoun = (table: string): string => TABLE_NOUN[table] ?? 'record';

export function recordWords(table: string, name: string | null): string {
  return `${TABLE_NOUN[table] ?? 'record'}${name ? ` ${name}` : ''}`;
}

/** How a Procedure reaches the record, in words (the source's own kind; a kind this page does not know is shown as it is). */
export function sourceLabel(source: SourceView, procedureFirm: string | null | undefined): string {
  switch (source.kind) {
    case 'AllRecords':
      return 'All records';
    case 'Firm':
      return procedureFirm ? `Firm · ${procedureFirm}` : 'Firm';
    case 'ManualOwn':
      return 'Assigned to this record';
    case 'ManualParent':
      return source.via ? `Assigned to ${recordWords(source.via.table, source.via.name)}` : 'Assigned to a parent record';
    default:
      return source.kind;
  }
}

/** The rule's source level, in words (rules carry the level and distance, not the parent's name). */
export function levelLabel(level: string | null, firm: string | null | undefined): string {
  switch (level) {
    case null:
    case '':
      return '';
    case 'AllRecords':
      return 'All records';
    case 'Firm':
      return firm ? `Firm · ${firm}` : 'Firm';
    case 'ManualOwn':
      return 'Assigned to this record';
    case 'ManualParent':
      return 'Assigned to a parent record';
    default:
      return level;
  }
}

export const versionLabel = (v: { number: number } | null | undefined): string => (v ? `v${v.number}` : '—');

// ─────────────────────────────────────────────────────────────────────────────
// Procedures
// ─────────────────────────────────────────────────────────────────────────────

export interface Badge {
  label: string;
  tone: StatusBadgeTone;
}

/** The gap that holds a Procedure, from the view's `gaps[]` (a gap Procedure also appears in `procedures` with status Gap). */
export function gapFor(p: ProcedureItem, gaps: readonly GapView[]): GapView | undefined {
  return gaps.find(g => g.procedureId.toLowerCase() === p.id.toLowerCase());
}

/** The override row that decided this Procedure on this record, when there is one. */
export function decisiveOverride(p: ProcedureItem) {
  return p.overrides.find(o => o.decisive);
}

/** One state badge per Procedure. A status or gap kind this page does not know renders neutral with its own text. */
export function procedureState(p: ProcedureItem, gaps: readonly GapView[]): Badge {
  switch (p.status) {
    case 'InForce':
      return p.pinned ? { label: 'Pinned', tone: 'info' } : { label: 'In force', tone: 'success' };
    case 'Excluded': {
      const row = decisiveOverride(p);
      return { label: row && row.distance > 0 ? 'Excluded on parent' : 'Excluded here', tone: 'neutral' };
    }
    case 'NotYetInForce':
      return { label: 'Not yet in force', tone: 'neutral' };
    case 'Gap': {
      switch (gapFor(p, gaps)?.kind) {
        case 'ConflictingParents':
          return { label: 'Conflicting pins', tone: 'critical' };
        case 'PinnedVersionRetired':
          return { label: 'Pinned version retired', tone: 'critical' };
        case 'PinnedVersionNotInForce':
          return { label: 'Pinned version not in force', tone: 'critical' };
        default:
          return { label: 'No version', tone: 'critical' };
      }
    }
    default:
      return { label: p.status, tone: 'neutral' };
  }
}

/** Excluded and not-yet-in-force Procedures are dimmed: they do not apply on this date. */
export const isProcedureDimmed = (p: ProcedureItem): boolean => p.status === 'Excluded' || p.status === 'NotYetInForce';

/** The version cell: the version in force, and under it "pinned to vN, vM available" or "vM available". */
export function versionLines(p: ProcedureItem): { main: string; note: string | null } {
  const main = versionLabel(p.versionInForce);
  const newer = p.newerVersion ? `${versionLabel(p.newerVersion)} available` : null;
  if (p.pinned && p.versionInForce) return { main, note: `pinned to ${main}${newer ? `, ${newer}` : ''}` };
  return { main, note: newer };
}

/** The line under "Applies through": why this Procedure is pinned, excluded or held. Null when there is nothing to add. */
export function overrideNote(p: ProcedureItem, gaps: readonly GapView[]): string | null {
  const gap = gapFor(p, gaps);
  if (gap?.kind === 'ConflictingParents') return 'Parents pin different versions';
  const row = decisiveOverride(p);
  if (!row) return null;
  const verb = row.mode === 'Pinned' ? 'Pinned' : row.mode === 'Excluded' ? 'Excluded' : null;
  if (!verb) return null;
  return `${verb} ${row.distance === 0 ? 'here' : `on ${recordWords(row.chainRecord.table, row.chainRecord.name)}`}`;
}

/** The blocking gaps' sentence for the gap bar. */
export function gapBarText(firmName: string | null, date: string, gaps: readonly GapView[]): string {
  const who = firmName ? `${firmName} invoices` : 'Invoices';
  const why = gaps.map(g => `${g.procedureName}: ${g.text}`).join(' ');
  return `${who} for work dated ${shortDate(date)} are held: "No procedure associated with this invoice". ${why}`.trim();
}

export type ProcedureFilter = 'all' | 'pinned' | 'excluded' | 'attention';

export function matchesProcedureFilter(f: ProcedureFilter, p: ProcedureItem): boolean {
  switch (f) {
    case 'all':
      return true;
    case 'pinned':
      return p.status === 'InForce' && p.pinned;
    case 'excluded':
      return p.status === 'Excluded';
    case 'attention':
      return p.status === 'Gap';
  }
}

// ─────────────────────────────────────────────────────────────────────────────
// Rules
// ─────────────────────────────────────────────────────────────────────────────

/** Label and tone per mark kind. Add a row here for a new kind; an unknown kind is neutral with its own text. */
export const RULE_MARKS: Readonly<Record<string, Badge>> = {
  Changed: { label: 'Changed here', tone: 'info' },
  Excluded: { label: 'Excluded', tone: 'neutral' },
  Stale: { label: 'Base rule updated', tone: 'warning' },
  BaseRetired: { label: 'Base rule retired', tone: 'warning' },
  // 084e: the label is the fallback only; the badge shows the mark's own text ("Outranked by X"), see markBadge.
  Outranked: { label: 'Outranked', tone: 'neutral' },
  CapOutranked: { label: 'Cap outranked', tone: 'neutral' },
};

/**
 * 084e (EB-D-219, R4): the precedence marks. The rule is still in force (it runs if every outranking rule is refused when an invoice is
 * checked), so it keeps its "In force" badge, is not dimmed, and its badge always names who outranks it: never a bare "Outranked".
 */
export const PRECEDENCE_MARK_KINDS: ReadonlySet<string> = new Set(['Outranked', 'CapOutranked']);

/** The mark kinds whose rule does not run on this date and is shown dimmed. The precedence kinds are never in it (see above). */
export const DIMMED_MARK_KINDS: ReadonlySet<string> = new Set(['Excluded']);

/** Marks that put a rule under "Needs attention". */
const ATTENTION_MARK_KINDS: ReadonlySet<string> = new Set(['Stale', 'BaseRetired']);

export const hasMark = (r: RuleItem, kind: string): boolean => r.marks.some(m => m.kind === kind);
export const isRuleDimmed = (r: RuleItem): boolean => r.marks.some(m => DIMMED_MARK_KINDS.has(m.kind));

/** "X"; "X and Y"; "X, Y and Z": the server's join rule for the outranking codes. */
export function joinCodes(codes: readonly string[]): string {
  return codes.length <= 1 ? codes.join('') : `${codes.slice(0, -1).join(', ')} and ${codes[codes.length - 1]}`;
}

/** The badge of one mark: its table entry, else neutral with the kind's own text. A precedence mark shows its own text, which names "by". */
export function markBadge(mark: RuleMark): Badge {
  const entry = RULE_MARKS[mark.kind] ?? { label: mark.kind, tone: 'neutral' };
  if (!PRECEDENCE_MARK_KINDS.has(mark.kind)) return entry;
  if (mark.text) return { ...entry, label: mark.text };
  const codes = (mark.by ?? []).map(b => b.code).filter(Boolean);
  return codes.length > 0 ? { ...entry, label: `${entry.label} by ${joinCodes(codes)}` } : entry;
}

/** The secondary line of a precedence mark: its note (when the rule can still run), else null. */
export function markNote(mark: RuleMark): string | null {
  return PRECEDENCE_MARK_KINDS.has(mark.kind) && mark.note ? mark.note : null;
}

/** The tooltip of a precedence mark: every outranking rule at the top rank, with its source level. Null for any other mark. */
export function markByHint(mark: RuleMark): string | null {
  if (!PRECEDENCE_MARK_KINDS.has(mark.kind) || !mark.by || mark.by.length === 0) return null;
  return `Outranked by: ${mark.by.map(b => `${b.code} (${levelLabel(b.level, null)}${b.source === 'Legacy' ? ', rule set' : ''})`).join('; ')}`;
}

/**
 * The badges of a rule: one per mark (a mark with no kind is dropped), or "In force" when it has none. A rule in force with a precedence
 * mark (084e) keeps "In force" first, so the mark never reads as "does not run".
 */
export function ruleBadges(r: RuleItem): Badge[] {
  const marks = r.marks.filter(m => typeof m.kind === 'string' && m.kind.length > 0);
  const status: Badge = r.inForce ? { label: 'In force', tone: 'success' } : { label: 'Not in force', tone: 'neutral' };
  if (marks.length === 0) return [status];
  const keepsInForce = r.inForce && marks.some(m => PRECEDENCE_MARK_KINDS.has(m.kind)) && !marks.some(m => DIMMED_MARK_KINDS.has(m.kind));
  return keepsInForce ? [status, ...marks.map(markBadge)] : marks.map(markBadge);
}

export type RuleFilter = 'all' | 'changed' | 'excluded' | 'attention';

export function matchesRuleFilter(f: RuleFilter, r: RuleItem): boolean {
  switch (f) {
    case 'all':
      return true;
    case 'changed':
      return hasMark(r, 'Changed');
    case 'excluded':
      return hasMark(r, 'Excluded');
    case 'attention':
      return r.marks.some(m => ATTENTION_MARK_KINDS.has(m.kind));
  }
}

/** The rules that run: the tab's count and the "n running" note. */
export const runningCount = (rules: readonly RuleItem[]): number => rules.filter(r => r.inForce).length;

/** The line under "Runs as": the published rule a record rule changed, or that it is retired. */
export function publishedNote(r: RuleItem): string | null {
  const changed = r.marks.find(m => m.kind === 'Changed');
  if (r.marks.some(m => m.kind === 'BaseRetired')) return 'Published rule retired';
  if (changed?.baseParameterSummary) return `Published: ${changed.baseParameterSummary}`;
  return null;
}

// ─────────────────────────────────────────────────────────────────────────────
// Count cards (WorkspaceShell MetricCardRow + MetricCard, as Worklist/laneCountFilters and InvoiceReview do: D-24)
// ─────────────────────────────────────────────────────────────────────────────

export interface FilterDef<K extends string, T> {
  key: K;
  label: string;
  test: (row: T) => boolean;
}

export const PROCEDURE_FILTERS: ReadonlyArray<FilterDef<ProcedureFilter, ProcedureItem>> = [
  { key: 'pinned', label: 'Pinned', test: p => matchesProcedureFilter('pinned', p) },
  { key: 'excluded', label: 'Excluded', test: p => matchesProcedureFilter('excluded', p) },
  { key: 'attention', label: 'Needs attention', test: p => matchesProcedureFilter('attention', p) },
];

export const RULE_FILTERS: ReadonlyArray<FilterDef<RuleFilter, RuleItem>> = [
  { key: 'changed', label: 'Changed here', test: r => matchesRuleFilter('changed', r) },
  { key: 'excluded', label: 'Excluded', test: r => matchesRuleFilter('excluded', r) },
  { key: 'attention', label: 'Needs attention', test: r => matchesRuleFilter('attention', r) },
];

/** The filter in force: `key` while its card still holds a row, else `all` (a card never leaves the user on an empty lens). */
export function resolveFilter<K extends string, T>(filters: ReadonlyArray<FilterDef<K, T>>, rows: readonly T[], key: K | 'all'): K | 'all' {
  if (key === 'all') return 'all';
  const f = filters.find(x => x.key === key);
  return f && rows.some(f.test) ? key : 'all';
}

export interface BuildCardsOptions<K extends string, T> {
  idPrefix: string;
  noun: { singular: string; plural: string };
  filters: ReadonlyArray<FilterDef<K, T>>;
  rows: readonly T[];
  selected: K | 'all';
  onSelect: (key: K | 'all') => void;
  /** The All card's own label and note (rules: "n running"). */
  allLabel: string;
  allNote?: string;
}

/** `MetricCardConfig[]` for `<MetricCardRow layout="wide" cards={...} />`: counts always cover the whole list; the selected card again returns to All. */
export function buildCountCards<K extends string, T>(o: BuildCardsOptions<K, T>): MetricCardConfig[] {
  const resolved = resolveFilter(o.filters, o.rows, o.selected);
  const word = (n: number) => (n === 1 ? o.noun.singular : o.noun.plural);
  const all: MetricCardConfig = {
    id: `${o.idPrefix}-all`,
    label: o.allLabel,
    value: o.rows.length,
    ariaLabel: `${o.allLabel}, ${o.rows.length} ${word(o.rows.length)}`,
    selected: resolved === 'all',
    // All stays enabled even when the list is empty.
    disableWhenEmpty: false,
    note: o.allNote,
    onClick: () => o.onSelect('all'),
  };
  return [
    all,
    ...o.filters.map(f => {
      const count = o.rows.filter(f.test).length;
      const card: MetricCardConfig = {
        id: `${o.idPrefix}-${f.key}`,
        label: f.label,
        value: count,
        ariaLabel: `${f.label}, ${count} ${word(count)}`,
        selected: resolved === f.key,
        disableWhenEmpty: true,
        onClick: () => o.onSelect(resolved === f.key ? 'all' : f.key),
      };
      return card;
    }),
  ];
}
