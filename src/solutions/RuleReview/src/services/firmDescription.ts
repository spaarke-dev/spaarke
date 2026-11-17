/**
 * The firm description of a rule (ebilling-r1 task 130e-2, DEF-060, EB-D-212 / EB-D-213): the short text a firm sees when the rule rejects
 * its invoice. The BFF is the authority (`FirmDescriptionRules.cs`, task 130e-1); this module mirrors its checks, in its order, so the
 * admin gets an early field error, and decides what the save path sends. Free of React so it can be tested.
 *
 * The values pinned to the C# source (the reason codes, the limit, the request member) are checked by `__tests__/firmDescriptionPin.test.ts`.
 *
 * Known limits (also in notes/130e-2-rule-review-firm-description.md): look-alike letters from other alphabets are not caught by the name
 * check (the server does not catch them either); the name check lower-cases per character, which can differ from the server's ordinal
 * comparison for a few letters (the server stays authoritative).
 */

import type { ApiFailure } from './ruleSetApi';

/** The most Unicode scalars a firm description may hold, after trimming (`FirmDescriptionRules.MaxRunes`). */
export const MAX_FIRM_DESCRIPTION = 200;

/** The request member of the PATCH body, the member of the GET view and the `field` of a refusal (`RuleSetService.FirmDescriptionField`). */
export const FIRM_DESCRIPTION_FIELD = 'firmDescription';

/** The six reason codes of a refused description (`FirmDescriptionRules`). */
export const REASON_TOO_LONG = 'firm_description_too_long';
export const REASON_BRACES = 'firm_description_braces';
export const REASON_MARKUP = 'firm_description_markup';
export const REASON_INVISIBLE = 'firm_description_invisible_character';
export const REASON_NAMES_RULE = 'firm_description_names_rule';
export const REASON_DUPLICATE = 'firm_description_duplicate';

/** The `errorCode`s of a 422 that can carry a firm description refusal (an edit, a publish). */
export const REFUSAL_ERROR_CODES: readonly string[] = ['ruleset.invalid', 'ruleset.publish_invalid'];

/** One message per reason code, in the server's wording (`RuleSetService.FirmDescriptionMessage`). The text is never echoed. */
export const FIRM_DESCRIPTION_MESSAGES: Readonly<Record<string, string>> = {
  [REASON_DUPLICATE]: 'A Duplicate rule cannot carry a firm description: firms always see the standard duplicate wording.',
  [REASON_TOO_LONG]: `The firm description is longer than ${MAX_FIRM_DESCRIPTION} characters.`,
  [REASON_BRACES]: 'The firm description cannot contain { or }.',
  [REASON_MARKUP]: 'The firm description cannot contain < or >.',
  [REASON_INVISIBLE]: 'The firm description cannot contain line breaks, tabs or invisible characters.',
  [REASON_NAMES_RULE]: 'The firm description cannot name the rule set or the rule: firms see it even when the rule set is hidden from them.',
};

const GENERIC_MESSAGE = 'The firm description is not valid.';

/** The message for a reason code; an unknown code falls back to the server's own message, then to a generic line. */
export const firmDescriptionMessage = (reason: string | null, serverMessage?: string | null): string =>
  (reason ? FIRM_DESCRIPTION_MESSAGES[reason] : undefined) ?? (serverMessage || GENERIC_MESSAGE);

// ─────────────────────────────────────────────────────────────────────────────
// The checks
// ─────────────────────────────────────────────────────────────────────────────

// .NET `char.IsWhiteSpace` (what the server's Trim and name comparison use): U+0009-000D, U+0020, U+0085, U+00A0, U+1680, U+2000-200A,
// U+2028, U+2029, U+202F, U+205F, U+3000. JavaScript's `\s` differs (it holds U+FEFF and lacks U+0085), so the set is spelled out.
const WS = '\\t-\\r \\u0085\\u00A0\\u1680\\u2000-\\u200A\\u2028\\u2029\\u202F\\u205F\\u3000';
const TRIM = new RegExp(`^[${WS}]+|[${WS}]+$`, 'g');
const WS_RUN = new RegExp(`[${WS}]+`, 'g');

/** Trim as the server does. */
export const trimLikeServer = (text: string): string => text.replace(TRIM, '');

// A UTF-16 surrogate with no partner (no `u` flag: the pattern works on code units).
const UNPAIRED_SURROGATE = /[\uD800-\uDBFF](?![\uDC00-\uDFFF])|(?<![\uD800-\uDBFF])[\uDC00-\uDFFF]/;
// Unicode categories Control, Format, LineSeparator, ParagraphSeparator (`FirmDescriptionRules.IsInvisibleControl`).
const INVISIBLE = /[\p{Cc}\p{Cf}\p{Zl}\p{Zp}]/u;

/** NFKC, then every run of white space as one space, trimmed, simple-upper-cased per character (the server's `Comparable` + OrdinalIgnoreCase). */
export function comparable(value: string): string {
  const folded = value.normalize('NFKC').replace(WS_RUN, ' ').replace(/^ | $/g, '');
  return [...folded]
    .map(ch => {
      const upper = ch.toUpperCase();
      return [...upper].length === 1 ? upper : ch; // a mapping that grows ("ß" to "SS") is not a simple mapping: keep the letter
    })
    .join('');
}

const names = (text: string, name: string | null | undefined): boolean => {
  if (!name || !trimLikeServer(name)) return false;
  const needle = comparable(name);
  return needle.length > 0 && comparable(text).includes(needle);
};

export interface FirmDescriptionContext {
  /** The rule set's name (`RuleSetSummary.name`). */
  ruleSetName?: string | null;
  /** The rule's name (`ProposalView.name`, the policy name). */
  ruleName?: string | null;
  /** A Duplicate rule takes none. */
  isDuplicate?: boolean;
}

export interface FirmDescriptionCheck {
  /** The trimmed text; null when blank. */
  text: string | null;
  /** Unicode scalars in the trimmed text (a character outside the Basic Multilingual Plane counts once). */
  count: number;
  /** The first check it fails, as the server's reason code; null when it passes. */
  reason: string | null;
}

/** The server's checks in the server's order: trim, blank is valid, Duplicate, unpaired surrogate, invisible, length, braces, markup, names. */
export function checkFirmDescription(raw: string, ctx: FirmDescriptionContext = {}): FirmDescriptionCheck {
  const text = trimLikeServer(raw);
  if (!text) return { text: null, count: 0, reason: null };

  const count = [...text].length;
  const refused = (reason: string): FirmDescriptionCheck => ({ text, count, reason });

  if (ctx.isDuplicate) return refused(REASON_DUPLICATE);
  if (UNPAIRED_SURROGATE.test(text) || INVISIBLE.test(text)) return refused(REASON_INVISIBLE);
  if (count > MAX_FIRM_DESCRIPTION) return refused(REASON_TOO_LONG);
  if (/[{}]/.test(text)) return refused(REASON_BRACES);
  if (/[<>]/.test(text)) return refused(REASON_MARKUP);
  if (names(text, ctx.ruleSetName) || names(text, ctx.ruleName)) return refused(REASON_NAMES_RULE);
  return { text, count, reason: null };
}

// ─────────────────────────────────────────────────────────────────────────────
// What the save path sends
// ─────────────────────────────────────────────────────────────────────────────

/**
 * The `firmDescription` member of the PATCH body, or `undefined` for "leave the key out". Only a change is sent (130e-1 contract):
 * a changed text as typed (the server trims it), a cleared field as `""`, an untouched field not at all, so the member is never null by
 * accident. A blank field over a rule with no text is not a change. A Duplicate rule never sends one.
 */
export function firmDescriptionToSend(stored: string | null | undefined, current: string, isDuplicate = false): string | undefined {
  if (isDuplicate) return undefined;
  const now = trimLikeServer(current);
  if (now === trimLikeServer(stored ?? '')) return undefined;
  return now === '' ? '' : current; // a blank field is a clear: say it as "", whatever white space was left in it
}

// ─────────────────────────────────────────────────────────────────────────────
// Server refusals
// ─────────────────────────────────────────────────────────────────────────────

/** One refusal of the firm description: the server's reason code and the message to show for it. */
export interface FirmRefusal {
  reason: string;
  message: string;
}

/**
 * The refusals of a 422 (`ruleset.invalid`, `ruleset.publish_invalid`) that name the firm description, by rule (`policyId`). An entry with
 * no `policyId` belongs to `fallbackPolicyId` (the rule the call was about). One message per reason code per rule, in the order sent.
 */
export function firmDescriptionRefusals(failure: ApiFailure, fallbackPolicyId?: string): Record<string, FirmRefusal[]> {
  const out: Record<string, FirmRefusal[]> = {};
  if (failure.status !== 422 || !failure.errorCode || !REFUSAL_ERROR_CODES.includes(failure.errorCode)) return out;
  for (const e of failure.errors) {
    if (e.field !== FIRM_DESCRIPTION_FIELD) continue;
    const policyId = e.policyId ?? fallbackPolicyId;
    if (!policyId) continue;
    const reason = e.reason ?? '';
    const list = (out[policyId] ??= []);
    if (!list.some(r => r.reason === reason)) list.push({ reason, message: firmDescriptionMessage(reason || null, e.message) });
  }
  return out;
}
