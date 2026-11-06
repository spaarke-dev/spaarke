/**
 * The Invoice Review page's view logic (ebilling-r1 task 110), kept pure so it is tested without rendering.
 *
 * Nothing here is a server decision (ADR-002, I-1): no total, status, approver or authorization is computed. These are the page's
 * lenses on the read model the server returned: which lines a count card shows, how a line's badge reads, where a value sits on the
 * PDF, which line is next to decide, and the per-line amounts a suggestion asks the server to apply.
 */

import type { MetricCardConfig, IPdfHighlight, IPdfNormalisedBox, StatusBadgeTone } from '@spaarke/ui-components';
import type {
  InvoiceReview,
  ReviewFinding,
  ReviewHeader,
  ReviewLine,
  ReviewLineFinding,
  Severity,
} from './invoiceReviewApi';

// ─────────────────────────────────────────────────────────────────────────────
// Line status (F-1: stored Pending is displayed as Processing)
// ─────────────────────────────────────────────────────────────────────────────

/**
 * The words for why a line decision, an adjustment or a correction is not available while the invoice needs its firm (EB-D-185 / EB-D-197): it
 * has no firm, is held for one, or its billing rules are being checked again after the firm was set. The server refuses them (409) in that state.
 */
export const FIRM_FIRST_WORDS =
  'This invoice needs its firm first (or its billing rules are being checked again after the firm was set). Add the firm, then decide.';

export const DECIDED_STATUSES: ReadonlySet<string> = new Set(['Accepted', 'Reduced', 'Disallowed']);

/** The one table of what each stored line status shows (F-1). Pending is never labelled "Pending". */
export const LINE_STATUS: Readonly<Record<string, { label: string; tone: StatusBadgeTone; meaning: string }>> = {
  Pending: { label: 'Processing', tone: 'info', meaning: 'Still being read or checked. It cannot be decided yet.' },
  Flagged: { label: 'To decide', tone: 'warning', meaning: 'A finding needs your decision.' },
  Clean: { label: 'Clean', tone: 'neutral', meaning: 'No findings. Accept it, or decide it like any other line.' },
  Accepted: { label: 'Accepted', tone: 'success', meaning: 'Approved at the billed amount.' },
  Reduced: { label: 'Reduced', tone: 'warning', meaning: 'Approved at a lower amount.' },
  Disallowed: { label: 'Disallowed', tone: 'critical', meaning: 'Not approved.' },
};

export function lineStatusView(line: Pick<ReviewLine, 'status' | 'statusLabel'>): { label: string; tone: StatusBadgeTone; meaning: string } {
  const known = LINE_STATUS[line.status];
  if (known) return known;
  return { label: line.statusLabel || line.status, tone: 'neutral', meaning: '' };
}

export const isProcessing = (line: ReviewLine): boolean => line.status === 'Pending';
export const isDecided = (line: ReviewLine): boolean => DECIDED_STATUSES.has(line.status);
export const isAdjustment = (line: ReviewLine): boolean => line.type === 'Adjustment';

/**
 * A line the page offers a decision on: Clean or Flagged, never Processing, never an Adjustment line (computed, not decided; the server
 * refuses it with `ebilling.line.not_decidable`). Whether the caller may decide at all is the server's `flags.canDecide`.
 */
export function isDecidable(line: ReviewLine): boolean {
  return !isAdjustment(line) && (line.status === 'Flagged' || line.status === 'Clean');
}

/** A line whose values the page offers to correct: an undecided Clean or Flagged fee or expense line (101 refuses the rest). */
export const isCorrectable = isDecidable;

// ─────────────────────────────────────────────────────────────────────────────
// Count filters: MetricCardRow fed MetricCardConfig[] (copy of the buildLaneCountCards shape, D-24)
// ─────────────────────────────────────────────────────────────────────────────

export type LineFilterKey = 'all' | 'processing' | 'toDecide' | 'clean' | 'decided';

const FILTERS: ReadonlyArray<{ key: Exclude<LineFilterKey, 'all'>; label: string; test: (l: ReviewLine) => boolean }> = [
  { key: 'processing', label: 'Processing', test: l => l.status === 'Pending' },
  { key: 'toDecide', label: 'To decide', test: l => l.status === 'Flagged' },
  { key: 'clean', label: 'Clean', test: l => l.status === 'Clean' },
  { key: 'decided', label: 'Decided', test: isDecided },
];

export function filterLines(lines: readonly ReviewLine[], key: LineFilterKey): ReviewLine[] {
  if (key === 'all') return [...lines];
  const f = FILTERS.find(x => x.key === key);
  return f ? lines.filter(f.test) : [...lines];
}

/** The filter in force: `key` while it still shows a line, else `all` (a user is never left on an empty lens). */
export function resolveFilterKey(lines: readonly ReviewLine[], key: LineFilterKey): LineFilterKey {
  return key === 'all' || filterLines(lines, key).length > 0 ? key : 'all';
}

export interface BuildLineCountCardsOptions {
  lines: readonly ReviewLine[];
  selectedKey: LineFilterKey;
  /** Called with the NEW filter: the chosen key, or `all` when the selected card is chosen again. */
  onSelectKey: (key: LineFilterKey) => void;
}

/** `MetricCardConfig[]` for `<MetricCardRow layout="wide" ariaLabel="Line filters" cards={...} />`. Counts always cover the whole invoice. */
export function buildLineCountCards({ lines, selectedKey, onSelectKey }: BuildLineCountCardsOptions): MetricCardConfig[] {
  const resolved = resolveFilterKey(lines, selectedKey);
  const decided = lines.filter(isDecided).length;
  const all: MetricCardConfig = {
    id: 'lines-all',
    label: 'All lines',
    value: lines.length,
    ariaLabel: `All lines, ${lines.length} ${lines.length === 1 ? 'line' : 'lines'}`,
    selected: resolved === 'all',
    // All stays enabled even on an empty invoice.
    disableWhenEmpty: false,
    progress: { done: decided, total: lines.length },
    onClick: () => onSelectKey('all'),
  };
  return [
    all,
    ...FILTERS.map(f => {
      const count = lines.filter(f.test).length;
      const card: MetricCardConfig = {
        id: `lines-${f.key}`,
        label: f.label,
        value: count,
        ariaLabel: `${f.label}, ${count} ${count === 1 ? 'line' : 'lines'}`,
        selected: resolved === f.key,
        disableWhenEmpty: true,
        onClick: () => onSelectKey(resolved === f.key ? 'all' : f.key),
      };
      return card;
    }),
  ];
}

// ─────────────────────────────────────────────────────────────────────────────
// Findings: the badge, the reasons, the fixed fallback texts
// ─────────────────────────────────────────────────────────────────────────────

/** The fixed sentence for a line finding whose stored message is null (task 110 acceptance: one per code). */
export const LINE_FINDING_FALLBACK: Readonly<Record<string, string>> = {
  'coding.not_returned': 'The AI did not return codes for this line, so its codes were not checked.',
  'line.no_description': 'This line has no description, so the rules that read the narrative could not check it.',
  'rules.not_checked': 'The billing rules were not checked for this line.',
};

export const LINE_FINDING_FALLBACK_UNKNOWN = 'This line needs a review.';

export function lineFindingText(f: ReviewLineFinding): string {
  const message = f.message?.trim();
  if (message) return message;
  return LINE_FINDING_FALLBACK[f.code] ?? LINE_FINDING_FALLBACK_UNKNOWN;
}

/** A policy finding's reason: the stored sentence, else worded from its rule and reason code (D-123: secondary reasons carry none). */
export function findingText(f: ReviewFinding): string {
  const message = f.message?.trim();
  if (message) return message;
  return `${ruleLabel(f)}: ${humanize(f.reasonCode)}.`;
}

/** "rate.above_approved" -> "Rate above approved". */
export function humanize(code: string): string {
  const words = code.replace(/[._-]+/g, ' ').trim();
  return words.length ? words[0].toUpperCase() + words.slice(1) : code;
}

/** The rule's name as the screen shows it: the policy code, with the rule type when it differs. */
export function ruleLabel(f: Pick<ReviewFinding, 'policyCode' | 'ruleType'>): string {
  const code = f.policyCode?.trim();
  if (!code) return f.ruleType;
  return code;
}

const SEVERITY_RANK: Readonly<Record<string, number>> = { Critical: 3, Warning: 2, Info: 1 };

export function severityRank(s: Severity | string | null | undefined): number {
  return s ? (SEVERITY_RANK[s] ?? 0) : 0;
}

export interface FindingsBadge {
  /** Policy findings plus line findings (the server's findingCount counts policy findings only). */
  count: number;
  /** The highest policy severity; `Warning` when only line findings exist; null with nothing. */
  severity: Severity | null;
  /** Rule names (and line-finding sentences) for the hover list. */
  names: string[];
}

export function findingsBadge(line: ReviewLine): FindingsBadge {
  const policy = line.findingCount ?? line.findings.length;
  const count = policy + line.lineFindings.length;
  if (count === 0) return { count: 0, severity: null, names: [] };
  const names = Array.from(new Set(line.findings.map(ruleLabel)));
  for (const lf of line.lineFindings) names.push(lineFindingText(lf));
  const severity: Severity | null = line.highestSeverity ?? (line.lineFindings.length > 0 ? 'Warning' : null);
  return { count, severity, names };
}

/** True when an AI observation (block billing, vague narrative ...) fed one of the line's findings (field family `Observation.`). */
export function hasAiObservation(line: ReviewLine): boolean {
  return line.findings.some(f => f.valuesRead.some(v => (v.field ?? v.name).startsWith('Observation.')));
}

/** True when any extracted value of the line is below the reliability threshold (the server's `lowConfidence`). */
export function lowConfidenceFields(line: ReviewLine): string[] {
  return Object.entries(line.fields)
    .filter(([, f]) => f.lowConfidence)
    .map(([name]) => name);
}

// ─────────────────────────────────────────────────────────────────────────────
// By rule (prototype answer 4): grouped by rule, most severe first, then count
// ─────────────────────────────────────────────────────────────────────────────

export interface RuleGroup {
  key: string;
  label: string;
  ruleType: string;
  severity: Severity | null;
  /** Every line the rule found something on (from the filtered lines). */
  lines: ReviewLine[];
  /** The first finding (for the clause and the message). */
  sample: ReviewFinding;
}

export function groupByRule(lines: readonly ReviewLine[]): RuleGroup[] {
  const groups = new Map<string, RuleGroup>();
  for (const line of lines) {
    const seen = new Set<string>();
    for (const f of line.findings) {
      const key = `${f.policyCode}|${f.ruleType}`;
      let g = groups.get(key);
      if (!g) {
        g = { key, label: ruleLabel(f), ruleType: f.ruleType, severity: f.severity ?? null, lines: [], sample: f };
        groups.set(key, g);
      }
      if (severityRank(f.severity) > severityRank(g.severity)) g.severity = f.severity;
      if (!seen.has(key)) {
        g.lines.push(line);
        seen.add(key);
      }
    }
  }
  return [...groups.values()].sort(
    (a, b) => severityRank(b.severity) - severityRank(a.severity) || b.lines.length - a.lines.length || a.label.localeCompare(b.label)
  );
}

/** One line's part of "Apply suggestion to n lines": the server's per-line amount (Reduce) or a Disallow. */
export interface SuggestionPlan {
  action: 'reduce' | 'disallow';
  lines: Array<{ lineId: string; approvedAmount: number | null }>;
  /** Lines of the group the plan leaves out (decided, Processing, or with no usable suggestion). */
  skipped: number;
}

/**
 * The suggestion of a rule group as one multi-line request. Each Reduce line gets its own approved amount = billed − the rule's suggested
 * reduction (design.md 6.1), floored at 0; the server checks the range. A group whose suggestions disagree on Reduce vs Disallow has no single
 * plan (the multi-line route takes one action): null.
 */
export function planSuggestion(group: RuleGroup): SuggestionPlan | null {
  const candidates: Array<{ line: ReviewLine; kind: string; amount: number | null }> = [];
  let skipped = 0;
  for (const line of group.lines) {
    const f = line.findings.find(x => `${x.policyCode}|${x.ruleType}` === group.key && x.suggestedAdjustment);
    if (!isDecidable(line) || !f?.suggestedAdjustment) {
      skipped += 1;
      continue;
    }
    const kind = f.suggestedAdjustment.kind;
    if (kind === 'Reduce' && f.suggestedAdjustment.amount === null) {
      skipped += 1;
      continue;
    }
    candidates.push({ line, kind, amount: f.suggestedAdjustment.amount });
  }
  if (candidates.length === 0) return null;
  const kinds = new Set(candidates.map(c => c.kind));
  if (kinds.size !== 1) return null;
  const kind = candidates[0].kind;
  if (kind === 'Reduce') {
    return {
      action: 'reduce',
      lines: candidates.map(c => ({ lineId: c.line.id, approvedAmount: reducedAmount(c.line.amount, c.amount!) })),
      skipped,
    };
  }
  if (kind === 'Disallow') {
    return { action: 'disallow', lines: candidates.map(c => ({ lineId: c.line.id, approvedAmount: null })), skipped };
  }
  return null;
}

/** Billed − reduction, floored at 0 and rounded to cents (what the reviewer is asked to confirm; the server checks it). */
export function reducedAmount(billed: number, reduction: number): number {
  return Math.max(0, Math.round((billed - reduction) * 100) / 100);
}

/** The first suggestion on a line, for "suggestion first" in the decision. */
export function lineSuggestion(line: ReviewLine): { kind: 'Reduce' | 'Disallow'; amount: number | null; finding: ReviewFinding } | null {
  for (const f of line.findings) {
    const s = f.suggestedAdjustment;
    if (!s) continue;
    if (s.kind === 'Reduce' && s.amount !== null) return { kind: 'Reduce', amount: s.amount, finding: f };
    if (s.kind === 'Disallow') return { kind: 'Disallow', amount: s.amount, finding: f };
  }
  return null;
}

// ─────────────────────────────────────────────────────────────────────────────
// Selection: next line to decide
// ─────────────────────────────────────────────────────────────────────────────

/** The next Flagged line after `fromLineId` in sequence order, wrapping; the first one when nothing is selected; null when none is left. */
export function nextToDecide(lines: readonly ReviewLine[], fromLineId: string | null): ReviewLine | null {
  const ordered = [...lines].sort((a, b) => a.sequence - b.sequence);
  const toDecide = ordered.filter(l => l.status === 'Flagged');
  if (toDecide.length === 0) return null;
  if (!fromLineId) return toDecide[0];
  const from = ordered.find(l => l.id === fromLineId);
  if (!from) return toDecide[0];
  return toDecide.find(l => l.sequence > from.sequence && l.id !== fromLineId) ?? toDecide.find(l => l.id !== fromLineId) ?? null;
}

// ─────────────────────────────────────────────────────────────────────────────
// The PDF highlight (108's contract: {page, x, y, w, h}, normalised 0-1)
// ─────────────────────────────────────────────────────────────────────────────

/** The field names of a line value, as the read model keys them, with the screen's label. */
export const FIELD_LABEL: Readonly<Record<string, string>> = {
  Date: 'Date',
  TimekeeperAsBilled: 'Timekeeper',
  TitleAsBilled: 'Title',
  Hours: 'Hours',
  Rate: 'Rate',
  Quantity: 'Quantity',
  UnitCost: 'Unit cost',
  Amount: 'Amount',
  Narrative: 'Narrative',
  BilledTaskCode: 'Task code (billed)',
  BilledActivityCode: 'Activity code (billed)',
  BilledExpenseCode: 'Expense code (billed)',
};

export function fieldLabel(name: string): string {
  return FIELD_LABEL[name] ?? humanize(name);
}

function boxPage(f: { page: number | null; box: { page: number | null } | null }): number | null {
  return f.page ?? f.box?.page ?? null;
}

/**
 * Where the line (and one of its fields) sits on the PDF. The row box is the union of the line's field boxes on the field's page (or, with
 * no field chosen, on the page most of its boxes are on). Null when the line has no boxes at all.
 */
export function highlightFor(line: ReviewLine | null, field: string | null): IPdfHighlight | null {
  if (!line) return null;
  const boxed = Object.entries(line.fields).filter(([, f]) => f.box && boxPage(f) !== null);
  if (boxed.length === 0) return null;

  const chosen = field ? line.fields[field] : undefined;
  let page: number;
  if (chosen?.box && boxPage(chosen) !== null) {
    page = boxPage(chosen)!;
  } else {
    const byPage = new Map<number, number>();
    for (const [, f] of boxed) byPage.set(boxPage(f)!, (byPage.get(boxPage(f)!) ?? 0) + 1);
    page = [...byPage.entries()].sort((a, b) => b[1] - a[1] || a[0] - b[0])[0][0];
  }

  const onPage = boxed.filter(([, f]) => boxPage(f) === page).map(([, f]) => f.box!);
  const x1 = Math.min(...onPage.map(b => b.x));
  const y1 = Math.min(...onPage.map(b => b.y));
  const x2 = Math.max(...onPage.map(b => b.x + b.w));
  const y2 = Math.max(...onPage.map(b => b.y + b.h));
  const row: IPdfNormalisedBox = { x: x1, y: y1, w: x2 - x1, h: y2 - y1 };

  const fieldBox: IPdfNormalisedBox | null =
    chosen?.box && boxPage(chosen) === page ? { x: chosen.box.x, y: chosen.box.y, w: chosen.box.w, h: chosen.box.h } : null;
  return {
    page,
    row,
    field: fieldBox,
    label: fieldBox && field ? `Line ${line.sequence} · ${fieldLabel(field)}` : `Line ${line.sequence}`,
  };
}

// ─────────────────────────────────────────────────────────────────────────────
// Read-only and the header
// ─────────────────────────────────────────────────────────────────────────────

export interface ReviewAccess {
  /** Decisions and corrections are offered. */
  canWrite: boolean;
  /** Why not, when not (one sentence). */
  reason: string | null;
}

/**
 * Whether the page offers decisions and corrections: the server's `flags.canDecide` (Write on the invoice, D-77, and In Review), unless a
 * write was already refused with 403 in this session (the page then turns read-only and says so).
 */
export function reviewAccess(review: Pick<InvoiceReview, 'flags' | 'header'>, refusedWith403: boolean): ReviewAccess {
  if (refusedWith403) return { canWrite: false, reason: 'You do not have permission to change this invoice, so it is shown read-only.' };
  if (review.header.status !== 'InReview') {
    return { canWrite: false, reason: `This invoice is ${invoiceStatusView(review.header).label.toLowerCase()}; it is shown read-only.` };
  }
  if (!review.flags.canDecide) return { canWrite: false, reason: 'You can view this invoice. Decisions need Write access to it.' };
  return { canWrite: true, reason: null };
}

export const INVOICE_STATUS: Readonly<Record<string, { label: string; tone: StatusBadgeTone }>> = {
  InReview: { label: 'In review', tone: 'info' },
  Approved: { label: 'Approved', tone: 'success' },
  SentToAp: { label: 'Sent to AP', tone: 'success' },
  Paid: { label: 'Paid', tone: 'success' },
  Rejected: { label: 'Rejected', tone: 'critical' },
  ReturnedToFirm: { label: 'Returned to firm', tone: 'warning' },
  Cancelled: { label: 'Cancelled', tone: 'neutral' },
  Superseded: { label: 'Superseded', tone: 'neutral' },
};

export function invoiceStatusView(header: Pick<ReviewHeader, 'status'>): { label: string; tone: StatusBadgeTone } {
  return INVOICE_STATUS[header.status] ?? { label: humanize(header.status || 'Unknown'), tone: 'neutral' };
}

/** The model-driven entity of a core record type in the read model. */
export const CORE_ENTITY: Readonly<Record<string, string>> = {
  matter: 'sprk_matter',
  project: 'sprk_project',
  workAssignment: 'sprk_workassignment',
};

export const CORE_KIND_LABEL: Readonly<Record<string, string>> = {
  matter: 'Matter',
  project: 'Project',
  workAssignment: 'Work assignment',
};

// ─────────────────────────────────────────────────────────────────────────────
// Formatting
// ─────────────────────────────────────────────────────────────────────────────

const MONEY = new Intl.NumberFormat('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 });

export function money(n: number | null | undefined): string {
  if (n === null || n === undefined || !Number.isFinite(n)) return '—';
  return n < 0 ? `−${MONEY.format(-n)}` : MONEY.format(n);
}

export function signedMoney(n: number | null | undefined): string {
  if (n === null || n === undefined || !Number.isFinite(n)) return '—';
  if (n === 0) return MONEY.format(0);
  return n < 0 ? `−${MONEY.format(-n)}` : `+${MONEY.format(n)}`;
}

/** yyyy-mm-dd -> "12 Mar 2025" (read as a calendar date, never shifted by the time zone). */
export function shortDate(iso: string | null | undefined): string {
  if (!iso) return '—';
  const m = /^(\d{4})-(\d{2})-(\d{2})/.exec(iso);
  if (!m) return iso;
  const months = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
  return `${Number(m[3])} ${months[Number(m[2]) - 1] ?? m[2]} ${m[1]}`;
}

export function hours(n: number | null | undefined): string {
  if (n === null || n === undefined || !Number.isFinite(n)) return '—';
  return n.toFixed(2).replace(/0$/, '');
}
