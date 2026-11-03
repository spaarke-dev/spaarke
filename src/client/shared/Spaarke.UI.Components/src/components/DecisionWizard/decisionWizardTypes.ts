/**
 * decisionWizardTypes — the contract the decision wizard is built against.
 *
 * Three sources, each named so a reader knows which one wins when they disagree:
 *  1. The BFF routes that exist today (the code is the contract): the plan route `GET /api/v1/signals/{id}/decision-plan`
 *     (task 036, `DecisionPlanEndpoints.cs`), the suppression route `GET /api/v1/signals/{id}/suppression` (task 034) and
 *     the commit route `POST /api/v1/signals/{id}/decision` (task 043, `DecisionCommitEndpoints.cs`). Their types below
 *     (`DecisionPlan`, `SuppressionStatus`, `DecisionReview`, `DecisionCommitResult`, `DecisionCommitFailure`) mirror the C#
 *     records field for field.
 *  2. The item the wizard shows (`DecisionWizardItem`): the Signal's display columns, its evidence lines and, for a decided
 *     item, the Decision Record. No BFF route returns this yet (the Signal read route is task 038, the record read is task 045),
 *     so the HOST supplies it through `loadItem`; the wizard is built and tested against fixtures of exactly this shape.
 *  3. The lookup search the host provides for Lookup / Person parameters (`DecisionLookupSearch`).
 *
 * Context-agnostic (ADR-012): no Dataverse, PCF or Xrm dependency. Task: spaarke-ontology-platform-r1, task 058.
 */

import type { ILookupItem } from '../../types/LookupTypes';
import type { DecisionOutcome, EvidenceTier, RecordClass, ResolutionType } from '../ConsoleKit';
import type { WorklistCore, WorklistItem } from '../Worklist/types';

// ---------------------------------------------------------------------------
// The plan (task 036) - mirrors DecisionPlanResponse
// ---------------------------------------------------------------------------

export type DecisionLaneName = 'Decide' | 'Do';

/** The class a Decision Record takes (the catalog's, never the user's). A dismissal is not an action's class. */
export type CatalogRecordClass = 'Routine' | 'Judgement';

/** `DecisionParameterKind` of the catalog, serialised by name. */
export type DecisionParameterKind = 'Text' | 'TextArea' | 'Choice' | 'Money' | 'Date' | 'Person' | 'Lookup' | 'Boolean';

export interface DecisionOption {
  value: string;
  label: string;
}

export interface DecisionParameter {
  code: string;
  label: string;
  kind: DecisionParameterKind;
  required: boolean;
  hint?: string | null;
  lookupEntity?: string | null;
  options?: DecisionOption[] | null;
  optionsSource?: string | null;
  /** Task 167 (D-193): the server's starting value (a Choice's option value), shown as a prefilled default and never a choice. Task 168
   * (D-194): a Boolean's fixed default ("true": Assign Task's Send email notification starts ticked). */
  defaultValue?: string | null;
  /** Task 167 (D-193): the code of a Date parameter of the same step this one must not be earlier than (End date after Start date). */
  notBefore?: string | null;
  /** Task 167 (D-193, review F4-4): for a Text, the most characters the target column holds; the step flags a longer value before Record decision. */
  maxLength?: number | null;
}

/** One catalog action (or Next-step creator) as the plan route returns it. */
export interface DecisionAction {
  code: string;
  label: string;
  workType?: string | null;
  recordClass: CatalogRecordClass;
  /** True for the first plan action only. */
  isRecommended: boolean;
  parameters: DecisionParameter[];
  /** "What it does", one line each - taken from the catalog, never composed here. */
  effectLines: string[];
  /** Codes that cannot be combined with this action (mutual). */
  excludes: string[];
  /**
   * Task 160 (D-179): `'email'` renders the step's `to`, `subject` and `body` in the shared EmailComposer, compose-only (the commit
   * route sends at Record decision). Absent or null: one field per parameter.
   */
  composer?: 'email' | null;
  /** Task 160 (D-179): the recipients To starts with (the outside firm's contact, read as the caller). Absent or null: To starts empty. */
  prefillTo?: DecisionRecipient[] | null;
}

/** A recipient the plan pre-fills To with (task 160): a contact the caller can read, with an email address. */
export interface DecisionRecipient {
  contactId: string;
  name?: string | null;
  email: string;
}

export interface DismissalReason {
  code: string;
  label: string;
  countsTowardSuppression: boolean;
  requiresDetail: boolean;
}

export interface RuleClauseDescription {
  clauseId: string;
  kind: string;
  text: string;
  formal: string;
}

export interface DecisionPlan {
  signalId: string;
  lane: DecisionLaneName;
  policyVersionId: string;
  /** In plan order; the first is the recommendation. */
  actions: DecisionAction[];
  nextSteps: DecisionAction[];
  dismissalReasons: DismissalReason[];
  /** The rule body in words ("How this was determined"); null when the body could not be described. */
  ruleDescription?: { clauses: RuleClauseDescription[] } | null;
}

/** Task 034: the (policy, core record) dismissal count the dismissal step shows. */
export interface SuppressionStatus {
  signalId: string;
  /** Dismissals that count toward the threshold, in the trailing window. */
  dismissalCount: number;
  dismissalsRecorded: number;
  threshold: number;
  windowDays: number;
  suppressedUntil?: string | null;
  /** False when the policy's dismissals never count. */
  countsTowardSuppression: boolean;
}

// ---------------------------------------------------------------------------
// The review and the commit result (task 043) - mirrors DecisionReview / DecisionCommitResult
// ---------------------------------------------------------------------------

/** A value the commit route accepts: a single scalar (strings, numbers, booleans). */
export type DecisionValue = string | number | boolean;

export interface DecisionReviewAction {
  code: string;
  taken: boolean;
  values?: Record<string, DecisionValue>;
}

export interface DecisionReviewNextStep {
  code: string;
  values?: Record<string, DecisionValue>;
}

export interface DecisionReviewDismissal {
  code: string;
  detail?: string;
}

/** The ONE body the wizard posts, with a client-generated review id. */
export interface DecisionReview {
  reviewId: string;
  /** True only when the Confirm step ran (D-52). */
  confirmed: boolean;
  alsoResolve?: string[];
  /** Every action the plan offered, each exactly once, taken or skipped. */
  actions: DecisionReviewAction[];
  nextSteps?: DecisionReviewNextStep[];
  dismissal?: DecisionReviewDismissal;
  because?: string;
  /** Resume a review whose first attempt failed before its record. */
  retry?: boolean;
}

export interface DecisionCommitRef {
  entity: string;
  id: string;
}

export interface DecisionCommitStep {
  key: string;
  code: string;
  phase: number;
  /** `Done`, `Refused` or `Failed`. */
  status: string;
  written: DecisionCommitRef[];
  possiblyWritten: DecisionCommitRef[];
  reasonCode?: string | null;
  detail?: string | null;
}

export interface DecisionCommitSignal {
  signalId: string;
  /** `Acted`, `Dismissed`, `InquirySent` (left open, Acknowledged, linked to the record) or `Unchanged`. */
  state: string;
  warning?: string | null;
}

/** The 200 body: the record was written and the Signals closed (or warnings say which could not be). */
export interface DecisionCommitResult {
  reviewId: string;
  decisionRecordId?: string | null;
  recordClass?: string | null;
  gateTier?: string | null;
  steps: DecisionCommitStep[];
  followOns: DecisionCommitRef[];
  signals: DecisionCommitSignal[];
  warnings: string[];
  replayed: boolean;
}

/**
 * What a non-200 answer carries (the ProblemDetails extensions of `DecisionCommitEndpoints.Problem`). A `Failed` outcome
 * (HTTP 500) is the D-17 case: no record was written and `landed` lists the writes that did.
 */
export interface DecisionCommitFailure {
  httpStatus: number;
  title: string;
  detail: string;
  reasonCode?: string | null;
  correlationId?: string | null;
  actionCode?: string | null;
  /** The D-17 step that failed (2, 3, 4 or 5); present only for a `Failed` outcome. */
  failedStep?: number | null;
  landed: DecisionCommitRef[];
  possiblyLanded: DecisionCommitRef[];
  steps: DecisionCommitStep[];
  /** A step-5 failure that did not get a clear answer: the record may exist. */
  recordMayExist: boolean;
  /** A message went out and the record was not written. */
  sentButNotRecorded: boolean;
  /** A failed first attempt that `retry: true` may resume. */
  resumable: boolean;
  replayed: boolean;
}

/** Thrown by `DecisionWizardService.commit` for every non-200 answer and for a request that could not be made. */
export class DecisionCommitError extends Error {
  readonly failure: DecisionCommitFailure;

  constructor(failure: DecisionCommitFailure) {
    super(failure.detail);
    this.name = 'DecisionCommitError';
    this.failure = failure;
  }
}

/** The three BFF calls the wizard makes. The wizard writes nothing itself: `commit` is the only write. */
export interface DecisionWizardService {
  getPlan(signalId: string): Promise<DecisionPlan>;
  getSuppression(signalId: string): Promise<SuppressionStatus>;
  /** Resolves with the 200 body; rejects with `DecisionCommitError` for anything else. */
  commit(signalId: string, review: DecisionReview): Promise<DecisionCommitResult>;
}

// ---------------------------------------------------------------------------
// The item (supplied by the host; the Signal read route is task 038, the record read task 045)
// ---------------------------------------------------------------------------

/** One `sprk_evidencerefs` entry as FR-48 writes it: the witness, the clause it tested (or none) and where it came from. */
export interface DecisionEvidenceRef {
  kind: string;
  ref: string;
  tier: EvidenceTier;
  /** Null when the fact is missing: it renders as Missing, never as zero (H-5). */
  text: string | null;
  /** What is missing, shown when `text` is null. */
  subject?: string;
  source?: string;
  asOf?: string;
  clause?: string;
  /**
   * True when the Signal read route withheld this line's text and source because its witness is on a record the reader may
   * not see (task 038, read-time witness rule). The line then reads "Details withheld (restricted record)", not "No value recorded".
   */
  withheld?: boolean;
}

/** One offered action as the Decision Record's `sprk_steps` holds it: taken or skipped, with the values actually sent. */
export interface DecidedStep {
  code: string;
  taken: boolean;
  values?: Record<string, unknown> | null;
  status?: string | null;
}

/** A row a Next step created, listed on the record (`sprk_followons`). */
export interface DecidedFollowOn {
  code: string;
  entity?: string | null;
  id?: string | null;
}

/** How a closed item ended, and (when a person decided it) the Decision Record's content. */
export interface DecisionResolution {
  resolutionType: ResolutionType;
  recordClass?: RecordClass | null;
  /** The record's autonumber, for example `DR-00042`. */
  recordNumber?: string | null;
  decidedByName?: string | null;
  decidedOn?: string | null;
  /** The dismissal reason as stored ("code - detail" rendered by the host). */
  reason?: string | null;
  /** The record that superseded this item. */
  supersededBy?: string | null;
  /** Stored outcome of the record; only Routine vs not matters to the display (R-4). */
  outcome?: DecisionOutcome | null;
  /** Every action the rule offered, in order, with taken / skipped and the values sent. */
  steps?: DecidedStep[];
  followOns?: DecidedFollowOn[];
  /**
   * True when a record was written but the item stays open (an inquiry was sent: Acknowledged and linked to the record). Set
   * only for an item recorded in this session; the status bar then says "Inquiry sent, still open".
   */
  leftOpen?: boolean;
  /**
   * What the status bar says after the state when `leftOpen` is set. Defaults to "Inquiry sent, still open"; the worklist's
   * Suppressed disclosure (task 059) sets "Not raised again until <date>".
   */
  note?: string | null;
  /** The evidence as it stood when the record was written. Shown INSTEAD of `evidence` for a decided item. */
  factSnapshot?: DecisionEvidenceRef[];
}

export interface DecisionWizardItem {
  /** The Signal's display columns (the same shape the worklist row renders). `row.signalId` is the item id. */
  row: WorklistItem;
  /** The CORE record (matter, project ...) the item groups under; null for a Not filed item (D-35). */
  core: WorklistCore | null;
  /** The rule's literal sentence (`sprk_sentence`). Witness values are NOT in it; they are evidence lines. */
  sentence: string | null;
  evidence: DecisionEvidenceRef[];
  /** Other OPEN items on the same core record in the SAME lane. Empty for a Not filed item. */
  otherOpenItems: WorklistItem[];
  /** Present when the item is already closed; absent while it is open. */
  resolution?: DecisionResolution | null;
}

// ---------------------------------------------------------------------------
// Lookups (provided by the host)
// ---------------------------------------------------------------------------

export interface DecisionLookupRequest {
  /** `Person` is a contact pick; `Lookup` names a table in `entity`. */
  kind: 'Person' | 'Lookup';
  /** The catalog's `lookupEntity` for a Lookup parameter. */
  entity?: string | null;
  /** The parameter's code (for example `budget`), so a host may scope its search. */
  parameterCode: string;
  /** What the user typed. An empty query asks for the candidates themselves (the item's own budgets, firms). */
  query: string;
  /** The item's core record, for scoping (the matter's budgets). Null for a Not filed item. */
  core: { recordType: string; recordId: string } | null;
}

export type DecisionLookupSearch = (request: DecisionLookupRequest) => Promise<ILookupItem[]>;

/**
 * What the host needs to open the platform's side-pane lookup for one Person / Lookup field (D-182). The host builds the row
 * filter from this (a budget: active, on the item's own record), exactly as its inline search scopes today; the wizard never
 * names a filter.
 */
export interface DecisionOpenLookupRequest {
  kind: 'Person' | 'Lookup';
  /** The table to pick from: `contact` for a Person, the catalog's `lookupEntity` for a Lookup. */
  entity: string;
  /** The parameter's code (for example `budget`), so a host may scope the pane. */
  parameterCode: string;
  /** The item's core record, for scoping (the matter's budgets). Null for a Not filed item. */
  core: { recordType: string; recordId: string } | null;
}

/**
 * The host's side-pane lookup (`INavigationService.openLookup`, single select). Resolves to the picked record, or null when the
 * user cancelled or the host could not open the pane. The shared library has no Xrm (ADR-012), so only the host calls it.
 */
export type DecisionOpenLookup = ((
  request: DecisionOpenLookupRequest
) => Promise<{ id: string; name: string; email?: string | null } | null>) & {
  /**
   * The tables this host's pane can open (K2). The wizard uses the pane only for these and keeps the inline field for the rest,
   * so a table the host cannot open is never a dead field. Absent: the pane is used for every table (the host handles all).
   */
  supportedEntities?: readonly string[];
};

/** The host's "open this record in a modal above the wizard" (`INavigationService.openRecordModal`). */
export type DecisionOpenRecord = (entityName: string, entityId: string) => Promise<void> | void;

/** The host's loader for one item. Rejects when the item cannot be read. */
export type DecisionItemLoader = (signalId: string) => Promise<DecisionWizardItem>;
