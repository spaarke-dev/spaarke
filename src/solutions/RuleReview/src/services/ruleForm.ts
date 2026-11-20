/**
 * What the rule detail form would send and whether it may be saved (ebilling-r1 task 130e-2, fix round 1 F5): the edit-building the
 * component used to do inline, kept free of React so a test can read it. The component calls {@link computeRuleForm} on every render and
 * uses its result for the Save and Discard buttons, the dirty state, the field errors and the PATCH body.
 */

import type { Consequence, ProposalView, RuleEdit } from './ruleSetApi';
import {
  checkFirmDescription,
  firmDescriptionMessage,
  firmDescriptionToSend,
  trimLikeServer,
  type FirmRefusal,
} from './firmDescription';
import { buildBody, sameBody, type BuildResult, type ParamValues, type RuleTypeDef } from './ruleTypes';

/** What the admin has typed. */
export interface RuleFormValues {
  params: ParamValues;
  consequence: string;
  headline: string;
  template: string;
  quote: string;
  location: string;
  firmText: string;
}

export interface RuleFormContext {
  /** The rule set's name: the firm description may not contain it. */
  ruleSetName?: string | null;
  /** The server's refusals to show now (see {@link shownRefusals}). */
  shownRefusals?: readonly FirmRefusal[];
  /** A call is in flight. */
  busy: boolean;
}

export interface FirmFieldView {
  /** A Duplicate rule takes no firm description: its field is read-only. */
  isDuplicate: boolean;
  /** A Duplicate rule that still holds a stored text: the text is shown with a "Remove" action. */
  removable: boolean;
  /** The "Remove" was used: saving sends `""`. */
  removing: boolean;
  /** Unicode scalars in the trimmed text; null on a Duplicate rule (no counter). */
  count: number | null;
  /** More than the limit. */
  over: boolean;
  /** The `firmDescription` member of the PATCH body, or undefined to leave the key out. */
  send: string | undefined;
  /** A text that would be sent fails the client checks: it is not sent and Save is off. */
  blocked: boolean;
  /** The messages under the field: the client check's first, then one per server refusal, each once. */
  messages: string[];
  /**
   * The working version is in force and its stored text fails the checks now, and the admin has not touched it: the server leaves the text off
   * the new draft a save creates. Said by the field before the save.
   */
  carryWarning: boolean;
}

export interface RuleFormState {
  /** The result of the typed parameters; null for a rule type the page does not know. */
  built: BuildResult | null;
  /** Only the members that differ from the stored rule (an empty PATCH is refused by the server). */
  edit: RuleEdit;
  /** A text field emptied by hand: it cannot be sent (the server keeps the stored text), but it is an unsaved change. */
  blanked: Record<string, string>;
  dirty: boolean;
  clauseEdited: boolean;
  firm: FirmFieldView;
  /** The Save button is off: a call in flight, nothing changed, or a firm description that would be sent and fails the checks. */
  saveDisabled: boolean;
}

const isBlank = (text: string | null | undefined): boolean => !trimLikeServer(text ?? '');

export function computeRuleForm(proposal: ProposalView, def: RuleTypeDef | undefined, v: RuleFormValues, ctx: RuleFormContext): RuleFormState {
  const isDuplicate = (proposal.ruleType ?? '').toLowerCase() === 'duplicate';
  const names = { ruleSetName: ctx.ruleSetName, ruleName: proposal.name };

  // ── The firm description ───────────────────────────────────────────────────
  const check = isDuplicate ? null : checkFirmDescription(v.firmText, names);
  const send = firmDescriptionToSend(proposal.firmDescription, v.firmText, isDuplicate);
  const blocked = send !== undefined && !!check?.reason;
  const messages = [
    ...(check?.reason ? [firmDescriptionMessage(check.reason)] : []),
    ...(ctx.shownRefusals ?? []).map(r => r.message),
  ].filter((m, i, all) => all.indexOf(m) === i);
  const storedFails = !isBlank(proposal.firmDescription) && (isDuplicate || !!checkFirmDescription(proposal.firmDescription ?? '', names).reason);
  const firm: FirmFieldView = {
    isDuplicate,
    removable: isDuplicate && !isBlank(proposal.firmDescription),
    removing: isDuplicate && send === '',
    count: check ? check.count : null,
    over: !!check && check.reason === 'firm_description_too_long',
    send,
    blocked,
    messages,
    carryWarning: proposal.inForce && storedFails && send === undefined,
  };

  // ── The rest of the rule ───────────────────────────────────────────────────
  const built = def ? buildBody(def, v.params) : null;
  const edit: RuleEdit = {};
  if (built?.ok && !sameBody(built.body, proposal.ruleBody)) edit.ruleBody = built.body;
  if (v.consequence && v.consequence !== (proposal.consequence ?? '')) edit.consequence = v.consequence as Consequence;
  if (v.headline !== (proposal.shortHeadline ?? '') && v.headline.trim()) edit.shortHeadline = v.headline;
  if (v.template !== (proposal.messageTemplate ?? '') && v.template.trim()) edit.messageTemplate = v.template;
  if (v.quote !== (proposal.clauseQuote ?? '') && v.quote.trim()) edit.clauseReference = v.quote;
  if (v.location !== (proposal.clauseLocation ?? '') && v.location.trim()) edit.clauseLocation = v.location;
  if (send !== undefined) edit.firmDescription = send;

  const blanked: Record<string, string> = {};
  const emptied = (now: string, stored: string | null): boolean => !now.trim() && !!stored?.trim();
  if (emptied(v.headline, proposal.shortHeadline)) blanked.headline = 'The headline cannot be empty.';
  if (emptied(v.template, proposal.messageTemplate)) blanked.template = 'The message cannot be empty.';
  if (emptied(v.quote, proposal.clauseQuote)) blanked.quote = 'The quote cannot be empty.';
  if (emptied(v.location, proposal.clauseLocation)) blanked.location = 'The location cannot be empty.';

  const dirty = Object.keys(edit).length > 0 || Object.keys(blanked).length > 0;
  return {
    built,
    edit,
    blanked,
    dirty,
    clauseEdited: edit.clauseReference !== undefined || edit.clauseLocation !== undefined,
    firm,
    saveDisabled: ctx.busy || !dirty || blocked,
  };
}

// ─────────────────────────────────────────────────────────────────────────────
// The firm description field's own state: the text and which server refusals the admin has moved past
// ─────────────────────────────────────────────────────────────────────────────

export interface FirmFieldState {
  text: string;
  /** The refusals the admin has dismissed (by editing the text or discarding): not shown again unless a new set arrives. */
  dismissed: readonly FirmRefusal[] | undefined;
}

export type FirmFieldAction =
  /** The admin typed (or used Remove): the refusals about the old text no longer apply. */
  | { type: 'edit'; text: string; refusals: readonly FirmRefusal[] | undefined }
  /** Discard: back to the stored text; the refusals about the discarded text no longer apply (fix round 1 F4). */
  | { type: 'reset'; stored: string | null; refusals: readonly FirmRefusal[] | undefined };

export const initialFirmField = (stored: string | null | undefined): FirmFieldState => ({ text: stored ?? '', dismissed: undefined });

export function firmFieldReducer(_state: FirmFieldState, action: FirmFieldAction): FirmFieldState {
  switch (action.type) {
    case 'edit':
      return { text: action.text, dismissed: action.refusals };
    case 'reset':
      return { text: action.stored ?? '', dismissed: action.refusals };
  }
}

/** The server refusals to show under the field now: those that arrived after the last edit or discard. */
export const shownRefusals = (arrived: readonly FirmRefusal[] | undefined, state: FirmFieldState): readonly FirmRefusal[] =>
  arrived && arrived !== state.dismissed ? arrived : [];
