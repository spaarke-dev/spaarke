/**
 * intakeValidation - the pure helpers both intake wizards share (r3 task 052; design note
 * `notes/intake-form-design.md` section 5.3): trimmed length rules, ISO-date range rules, email format, and the
 * `compact` step that keeps empty and hidden keys out of the answers JSON.
 *
 * No React, no I/O, and "today" is always a parameter so a test pins it.
 */

/** A field's error message, or `null` when valid. */
export type FieldError = string | null;

/** `yyyy-mm-dd` for the LOCAL calendar day of `now` (what a date input shows). */
export function todayIso(now: Date = new Date()): string {
  const y = String(now.getFullYear()).padStart(4, '0');
  const m = String(now.getMonth() + 1).padStart(2, '0');
  const d = String(now.getDate()).padStart(2, '0');
  return `${y}-${m}-${d}`;
}

const ISO_DATE = /^(\d{4})-(\d{2})-(\d{2})$/;

/** The UTC midnight of a real ISO calendar date, or `null` for a malformed or impossible one (`2026-02-30`). */
function parseIso(value: string): number | null {
  const m = ISO_DATE.exec(value);
  if (!m) return null;
  const t = Date.UTC(Number(m[1]), Number(m[2]) - 1, Number(m[3]));
  const back = new Date(t);
  return back.getUTCFullYear() === Number(m[1]) &&
    back.getUTCMonth() === Number(m[2]) - 1 &&
    back.getUTCDate() === Number(m[3])
    ? t
    : null;
}

/** `iso` plus `days` calendar days, as `yyyy-mm-dd`. */
export function addDaysIso(iso: string, days: number): string {
  const t = parseIso(iso);
  if (t === null) throw new Error(`Not an ISO date: ${iso}`);
  const d = new Date(t + days * 86_400_000);
  return `${String(d.getUTCFullYear()).padStart(4, '0')}-${String(d.getUTCMonth() + 1).padStart(2, '0')}-${String(d.getUTCDate()).padStart(2, '0')}`;
}

/**
 * Working days (Monday to Friday) from after `todayIso` up to and including `dateIso`; `0` for today or a past date.
 * Used for the "within 3 business days" rule that reveals the reason field.
 */
export function businessDaysUntil(dateIso: string, today: string): number {
  const end = parseIso(dateIso);
  const start = parseIso(today);
  if (end === null || start === null || end <= start) return 0;
  let n = 0;
  for (let t = start + 86_400_000; t <= end; t += 86_400_000) {
    const day = new Date(t).getUTCDay();
    if (day !== 0 && day !== 6) n++;
  }
  return n;
}

/** Trimmed text length rule. `min` 0 means optional (an empty value is valid). */
export function textError(value: string, label: string, min: number, max: number): FieldError {
  const v = value.trim();
  if (v.length === 0) return min > 0 ? `Enter ${label}.` : null;
  if (v.length < min) return `${capitalize(label)} must be at least ${min} characters.`;
  if (v.length > max) return `${capitalize(label)} must be at most ${max} characters.`;
  return null;
}

/** A required code picked from `allowed`. */
export function choiceError(value: string, label: string, allowed: readonly string[]): FieldError {
  if (value === '') return `Choose ${label}.`;
  return allowed.includes(value) ? null : `Choose ${label} from the list.`;
}

/**
 * A date between `min` and `max` (inclusive ISO dates). Empty is valid unless `required`.
 * `rangeText` completes the message, e.g. "within the next 365 days".
 */
export function dateError(value: string, required: boolean, min: string, max: string, rangeText: string): FieldError {
  if (value === '') return required ? 'Choose a date.' : null;
  if (parseIso(value) === null) return 'Enter a valid date.';
  if (value < min || value > max) return `Choose a date ${rangeText}.`;
  return null;
}

const EMAIL = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

/** Required email address: format check, at most 254 characters. */
export function emailError(value: string, label: string): FieldError {
  const v = value.trim();
  if (v.length === 0) return `Enter ${label}.`;
  if (v.length > 254 || !EMAIL.test(v)) return `Enter a valid email address for ${label}.`;
  return null;
}

/**
 * Drops the keys whose value is empty: `undefined`, `null`, a blank string or an empty array. The wizards build only the
 * keys a visible field owns; this is the last guard so an empty optional answer never reaches the stored JSON.
 */
export function compact(answers: Record<string, unknown>): Record<string, unknown> {
  const out: Record<string, unknown> = {};
  for (const [key, value] of Object.entries(answers)) {
    if (value === undefined || value === null) continue;
    if (typeof value === 'string' && value.trim() === '') continue;
    if (Array.isArray(value) && value.length === 0) continue;
    out[key] = typeof value === 'string' ? value.trim() : value;
  }
  return out;
}

/** Truncates to `max` UTF-16 units (the unit the server counts). */
export function truncate(value: string, max: number): string {
  return value.length <= max ? value : value.slice(0, max);
}

function capitalize(s: string): string {
  return s.length === 0 ? s : s[0].toUpperCase() + s.slice(1);
}
