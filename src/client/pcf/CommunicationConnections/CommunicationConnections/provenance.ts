/**
 * Provenance parsing + humanization for the association review surface (W4 / FR-17).
 *
 * The Association Engine (task 015) writes a JSON trail to `sprk_associationprovenance`.
 * This module parses it and translates the machine trail into review-surface-ready
 * pieces: a confidence band, a plain-English rationale, and friendly entity labels —
 * so a reviewer can confirm/correct a filing without reading raw JSON.
 *
 * Ported verbatim from the converged prototype
 * `code-pages/CommunicationPage/src/components/provenance.ts` (W4 pivot — task 042).
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
  /**
   * Feedback signal (task 042) — set when a reviewer overrides the engine's
   * suggestion. Persisted back to `sprk_associationprovenance`; NO learning loop
   * consumes it (out of R4 scope).
   */
  overrideReason?: string;
  overriddenField?: string;
  overriddenAt?: string;
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
  /** Competing candidates when the slot is ambiguous (conflict). */
  alternatives?: ProvenanceCandidate[];
  /**
   * Runner-up candidates for a NON-ambiguous slot (the engine had a clear top
   * pick but also recorded lower-confidence alternatives). Surfaced behind an
   * "other candidates" expander so a reviewer can file a different one. Empty/
   * undefined when the slot had a single candidate.
   */
  otherCandidates?: ProvenanceCandidate[];
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
    // Non-conflict runners-up (everything but the primary), highest-confidence first.
    const others = conflict
      ? undefined
      : cands.filter(c => c !== primary).sort((a, b) => b.reinforcedConfidence - a.reinforcedConfidence);
    // Per-candidate `written` is authoritative for filed state — the engine may mark the
    // communication Resolved on a deterministic (contact) auto-file while an AI-suggested
    // matter/project on ANOTHER field is NOT written; that one must still read as "suggested"
    // (to review), not "confirmed". Fall back to the global isResolved only if `written` is absent.
    const isWritten = primary.written ?? isResolved;
    out.push({
      field,
      entity: primary.targetEntity,
      slotLabel: meta.label,
      targetName: primary.targetName ?? primary.targetId,
      targetId: primary.targetId,
      confidence: primary.reinforcedConfidence,
      status: conflict ? 'ambiguous' : isWritten ? 'confirmed' : 'suggested',
      alternatives: conflict ? cands : undefined,
      otherCandidates: others && others.length > 0 ? others : undefined,
      order: meta.order,
    });
  });
  return out.sort((a, b) => a.order - b.order);
}

/** entityType → display slot (reverse of SLOT_META) so filed lookups map to a labelled slot. */
const ENTITY_TO_SLOT: Record<string, { field: string; label: string; order: number }> = {
  sprk_matter: { field: 'sprk_regardingmatter', label: 'Matter', order: 1 },
  sprk_project: { field: 'sprk_regardingproject', label: 'Project', order: 2 },
  sprk_organization: { field: 'sprk_regardingorganization', label: 'Organization', order: 3 },
  account: { field: 'sprk_regardingaccount', label: 'Account', order: 4 },
  contact: { field: 'sprk_regardingperson', label: 'Contact', order: 5 },
  sprk_invoice: { field: 'sprk_regardinginvoice', label: 'Invoice', order: 6 },
  sprk_servicerequest: { field: 'sprk_regardingservicerequest', label: 'Service Request', order: 7 },
  sprk_event: { field: 'sprk_regardingevent', label: 'Event', order: 8 },
  sprk_workassignment: { field: 'sprk_regardingworkassignment', label: 'Work Assignment', order: 9 },
};

/**
 * The actual `sprk_communication` regarding lookup fields (field → entity type). NOTE these
 * are the COMMUNICATION regarding columns (`sprk_regardingperson` for Contact, etc.) — NOT the
 * `sprk_todo` TODO_REGARDING_CATALOG names (`sprk_regardingcontact`), which do not exist on
 * `sprk_communication`. Reading with the wrong names makes the whole $select throw.
 */
export const COMMUNICATION_REGARDING_FIELDS: { field: string; entityType: string }[] = Object.entries(
  ENTITY_TO_SLOT
).map(([entityType, meta]) => ({ entityType, field: meta.field }));

/** A regarding lookup that is actually populated on the host record (a filed association). */
export interface FiledAssociation {
  entityType: string;
  recordId: string;
  recordName: string;
}

/**
 * Fold the record's actually-filed regarding lookups into the engine-derived slots
 * so the surface is authoritative — it shows EVERY association, not just what the
 * engine suggested. For a filed entity type that already has a slot, the filed
 * record is the truth (mark confirmed, adopt its identity, drop review affordances);
 * a filed type with no slot (e.g. a manual "Link another") becomes a new confirmed row.
 */
export function mergeFiledConnections(connections: Connection[], filed: FiledAssociation[]): Connection[] {
  if (!filed || filed.length === 0) return connections;
  const out = connections.map(c => ({ ...c }));
  for (const f of filed) {
    const existing = out.find(c => c.entity === f.entityType);
    if (existing) {
      existing.status = 'confirmed';
      existing.targetName = f.recordName;
      existing.targetId = f.recordId;
      existing.alternatives = undefined;
      existing.otherCandidates = undefined;
    } else {
      const slot = ENTITY_TO_SLOT[f.entityType] ?? {
        field: `sprk_regarding_${f.entityType}`,
        label: entityLabel(f.entityType),
        order: 99,
      };
      out.push({
        field: slot.field,
        entity: f.entityType,
        slotLabel: slot.label,
        targetName: f.recordName,
        targetId: f.recordId,
        confidence: 1,
        status: 'confirmed',
        order: slot.order,
      });
    }
  }
  return out.sort((a, b) => a.order - b.order);
}

/** A record type the AI classifier flagged (e.g. "looks like a new Matter") with no matching record yet. */
export interface AiSuggestedType {
  entityType: string;
  label: string;
  reason: string;
}

/**
 * Parse the AI-classification signals for suggested record TYPES that aren't already
 * represented by a candidate/filed slot — e.g. an email about a brand-new matter that
 * doesn't exist yet. The engine embeds these as `types=[sprk_matter]` in the signal's
 * provenance string (there is no dedicated structured field today). Surfaced in the grid
 * as a "Create <Type>" affordance so the reviewer can act on the AI's intent.
 */
export function deriveAiSuggestedTypes(
  doc: ProvenanceDoc,
  existingEntityTypes: ReadonlySet<string>
): AiSuggestedType[] {
  const out: AiSuggestedType[] = [];
  const seen = new Set<string>();
  for (const sig of doc.signals ?? []) {
    const match = /types=\[([^\]]*)\]/.exec(sig.provenance ?? '');
    if (!match) continue;
    const types = match[1]
      .split(',')
      .map(t => t.trim())
      .filter(Boolean);
    for (const et of types) {
      if (existingEntityTypes.has(et) || seen.has(et)) continue;
      seen.add(et);
      out.push({
        entityType: et,
        label: entityLabel(et),
        reason: `The AI classifier flagged this email as relating to a ${entityLabel(et)}.`,
      });
    }
  }
  return out;
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

/** Map a connection slot to the target that would be written on confirm. */
export function connectionTarget(conn: Connection): { entityType: string; recordId: string; recordName: string } {
  return { entityType: conn.entity, recordId: conn.targetId, recordName: conn.targetName };
}
