/**
 * Provenance parsing + humanization for the association review surface (W4 / FR-17).
 *
 * The Association Engine (task 015) writes a JSON trail to `sprk_associationprovenance`.
 * This module parses it and translates the machine trail into review-surface-ready
 * pieces: a confidence band, a plain-English rationale, and friendly entity labels —
 * so a reviewer can confirm/correct a filing without reading raw JSON.
 */

export interface ProvenanceContributor {
  rung: string;
  confidence: number;
  provenance: string;
}

export interface ProvenanceCandidate {
  field: string;
  targetEntity: string;
  targetId: string;
  /** Display name — resolved by the engine/catalog in production; seeded in the prototype. */
  targetName?: string;
  reinforcedConfidence: number;
  deterministicConfidence: number;
  written: boolean;
  conflict: boolean;
  contributors: ProvenanceContributor[];
}

export interface ProvenanceDecision {
  status: string;
  autoFiled: boolean;
  killSwitchEnabled: boolean;
  autoFileThreshold: number;
  topDeterministicConfidence: number;
  topConfidence: number;
  aiInvolved: boolean;
  reason: string;
}

export interface ProvenanceSignal {
  category: string;
  confidence: number;
  provenance: string;
  obligations: string[];
}

export interface ProvenanceDoc {
  version: number;
  direction: string;
  decision: ProvenanceDecision;
  rungsFired: string[];
  candidates: ProvenanceCandidate[];
  signals: ProvenanceSignal[];
}

export function parseProvenance(json: string | null | undefined): ProvenanceDoc | null {
  if (!json) return null;
  try {
    return JSON.parse(json) as ProvenanceDoc;
  } catch {
    return null;
  }
}

export type ConfidenceBand = 'high' | 'medium' | 'low';

export function confidenceBand(value: number): ConfidenceBand {
  if (value >= 0.85) return 'high';
  if (value >= 0.5) return 'medium';
  return 'low';
}

export const CONFIDENCE_BAND_LABEL: Record<ConfidenceBand, string> = {
  high: 'High confidence',
  medium: 'Medium confidence',
  low: 'Low confidence',
};

/** Rung → plain-English phrase for the "why" rationale. */
const RUNG_PHRASES: Record<string, string> = {
  ExplicitReference: 'the subject explicitly references this record',
  ThreadContinuity: 'it continues an email thread already filed here',
  ParticipantCorrelation: 'the sender and recipients are known participants',
  StructuralDetector: 'the message structure matched a known pattern',
  SemanticMatch: 'AI found a strong content match',
  AiClassification: 'AI classified the content',
};

export function contributorPhrase(c: ProvenanceContributor): string {
  return RUNG_PHRASES[c.rung] ?? c.provenance;
}

export function isAiRung(rung: string): boolean {
  return rung === 'SemanticMatch' || rung === 'AiClassification';
}

const ENTITY_LABELS: Record<string, string> = {
  sprk_matter: 'Matter',
  sprk_project: 'Project',
  contact: 'Contact',
  sprk_organization: 'Organization',
  account: 'Account',
  sprk_invoice: 'Invoice',
  sprk_event: 'Event',
  sprk_servicerequest: 'Service Request',
  sprk_workassignment: 'Work Assignment',
  sprk_budget: 'Budget',
};

export function entityLabel(entity: string): string {
  return ENTITY_LABELS[entity] ?? entity;
}

/**
 * One-sentence rationale for a candidate, e.g.:
 * "Suggested because it continues an email thread already filed here and the
 *  sender and recipients are known participants."
 */
export function rationaleSentence(candidate: ProvenanceCandidate): string {
  const phrases = candidate.contributors.map(contributorPhrase);
  if (phrases.length === 0) return 'No supporting signals were recorded.';
  const joined =
    phrases.length === 1 ? phrases[0] : `${phrases.slice(0, -1).join(', ')} and ${phrases[phrases.length - 1]}`;
  return `Suggested because ${joined}.`;
}

/** Highest-confidence non-conflict candidate (the one to accept). */
export function topCandidate(doc: ProvenanceDoc): ProvenanceCandidate | null {
  const usable = doc.candidates.filter(c => !c.conflict);
  if (usable.length === 0) return null;
  return usable.reduce((a, b) => (b.reinforcedConfidence > a.reinforcedConfidence ? b : a));
}

/** The competing candidates for an Ambiguous decision. */
export function conflictCandidates(doc: ProvenanceDoc): ProvenanceCandidate[] {
  return doc.candidates.filter(c => c.conflict);
}

// ── Multi-association model (an email is regarding MANY records at once) ────────

/** One typed association slot on the communication (Matter, Organization, Contact, …). */
export interface Connection {
  field: string;
  entity: string;
  slotLabel: string;
  targetName: string;
  targetId: string;
  confidence: number;
  status: 'confirmed' | 'suggested' | 'ambiguous';
  /** Competing candidates when the slot is ambiguous. */
  alternatives?: ProvenanceCandidate[];
  order: number;
}

/** A "create a related record from this email" affordance (some engine-suggested). */
export interface CreateAction {
  kind: 'event' | 'todo' | 'invoice';
  label: string;
  reason?: string;
  suggested: boolean;
}

const SLOT_META: Record<string, { label: string; order: number }> = {
  sprk_regardingmatter: { label: 'Matter', order: 1 },
  sprk_regardingproject: { label: 'Project', order: 2 },
  sprk_regardingorganization: { label: 'Organization', order: 3 },
  sprk_regardingaccount: { label: 'Account', order: 4 },
  sprk_regardingperson: { label: 'Contact', order: 5 },
  sprk_regardinginvoice: { label: 'Invoice', order: 6 },
  sprk_regardingservicerequest: { label: 'Service Request', order: 7 },
  sprk_regardingevent: { label: 'Event', order: 8 },
  sprk_regardingworkassignment: { label: 'Work Assignment', order: 9 },
};

/** Group candidates by field into typed connection slots. */
export function deriveConnections(doc: ProvenanceDoc, isResolved: boolean): Connection[] {
  const byField = new Map<string, ProvenanceCandidate[]>();
  for (const c of doc.candidates) {
    const list = byField.get(c.field) ?? [];
    list.push(c);
    byField.set(c.field, list);
  }
  const out: Connection[] = [];
  byField.forEach((cands, field) => {
    const meta = SLOT_META[field] ?? { label: entityLabel(cands[0].targetEntity), order: 99 };
    const conflict = cands.length >= 2 && cands.some(c => c.conflict);
    const primary = cands.reduce((a, b) => (b.reinforcedConfidence > a.reinforcedConfidence ? b : a));
    out.push({
      field,
      entity: primary.targetEntity,
      slotLabel: meta.label,
      targetName: primary.targetName ?? primary.targetId,
      targetId: primary.targetId,
      confidence: primary.reinforcedConfidence,
      status: conflict ? 'ambiguous' : isResolved ? 'confirmed' : 'suggested',
      alternatives: conflict ? cands : undefined,
      order: meta.order,
    });
  });
  return out.sort((a, b) => a.order - b.order);
}

/** Turn structural signals/obligations into "create from this email" suggestions. */
export function deriveCreateActions(doc: ProvenanceDoc): CreateAction[] {
  const suggested = new Map<string, CreateAction>();
  for (const sig of doc.signals) {
    if (sig.category === 'invoice') {
      suggested.set('invoice', {
        kind: 'invoice',
        label: 'Link or create Invoice',
        reason: 'invoice number detected',
        suggested: true,
      });
    }
    if (sig.category === 'event' || sig.obligations.includes('calendar-response')) {
      suggested.set('event', {
        kind: 'event',
        label: 'Create Event',
        reason: 'calendar invite detected',
        suggested: true,
      });
    }
    if (sig.obligations.includes('deadline-response') || sig.obligations.includes('payment-review')) {
      suggested.set('todo', {
        kind: 'todo',
        label: 'Create To Do',
        reason: 'response deadline detected',
        suggested: true,
      });
    }
  }
  // Always offer Event + To Do as manual options (not marked suggested unless the engine flagged them).
  const result: CreateAction[] = [];
  for (const key of ['event', 'todo', 'invoice'] as const) {
    result.push(
      suggested.get(key) ?? {
        kind: key,
        label: key === 'event' ? 'Create Event' : key === 'todo' ? 'Create To Do' : 'Link Invoice',
        suggested: false,
      }
    );
  }
  return result;
}
