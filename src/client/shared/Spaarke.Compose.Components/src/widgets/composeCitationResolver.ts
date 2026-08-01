/**
 * composeCitationResolver.ts — WS-4 CLIENT CITATION RESOLVER
 * (ai-advanced-capabilities-agreements-r1 task 011, spec FR-03).
 *
 * A client-side mirror of the server `CitationResolver`
 * (`src/server/api/Sprk.Bff.Api/Services/Compose/CitationResolver.cs`, spaarkeai-compose-fidelity-r4.5
 * task 042) — resolves a human legal citation string ("Section 4.2" / "4.2(b)(iii)" / "Sections 4–7")
 * to the paragraph(s) it names, by reading the ALREADY-COMPUTED `computedNumber`/`listPath` fields the
 * WS-3/WS-4 server engine (`ComposeDocxProjectionBuilder.NumberingComputationEngine`) attaches to each
 * `ParaIdMapEntry` on the Load response — threaded to `ComposeEditor`'s `paraIdMap` prop via
 * `ComposeWorkspace.tsx` (`state.paraIdMap` ← `LoadComposeDocumentResult.ParaIdMap`, unchanged end to
 * end). Pure string-parse + integer-chain matching over that array — no editor, no I/O, no new network
 * surface, same as the server twin.
 *
 * WHY A CLIENT MIRROR, NOT A SERVER CALL (CLAUDE.md §11 reuse-first three-question test — also this
 * task's escalation trigger, resolved here rather than fired):
 *  1. Existing — no client module parses a legal citation string into an ordinal path; the server
 *     `CitationResolver` does this, but it is a static, pure C# function with zero I/O.
 *  2. Extension — there is no in-process way to "call" a C# static function from a browser runtime.
 *     The alternative (a new `POST /api/compose/.../resolve-citation` endpoint) is exactly the
 *     NEW-BFF-ENDPOINT path this task's escalation trigger warns against — and unnecessary, because
 *     the projection payload ALREADY carries every field the resolver needs (`computedNumber` +
 *     `listPath` on `paraIdMap`, confirmed present end-to-end — see `ParaIdMapEntry`'s task-011 doc
 *     comment in `compose-contracts.ts`). A network round trip would add per-finding latency for data
 *     already in hand client-side.
 *  3. Cost of doing nothing — without this, `ComposeEditor.placeAdvisoryComments` keeps anchoring
 *     EVERY finding by fuzzy text/position match, including ones the review model precisely cited by
 *     section — the exact ambiguity class DEF-01 (task 012) diagnoses (a should-be-unique target
 *     matching more than one location in the document).
 *
 * PARITY: `composeCitationResolver.test.ts`'s "structured map" cases are ported verbatim (same input
 * maps, same citations, same expected paraIds) from
 * `tests/integration/seam/Compose/ComposeCitationResolverSeamTests.cs` — the letter/roman sub-item +
 * decoy-neighbor, section-prefix tolerance, and bullet-exclusion cases (the ones that construct an
 * in-memory `ParaIdMapEntry[]` rather than loading a real corpus `.docx`, so they translate directly
 * without a server-only fixture loader).
 *
 * SCOPE: forward resolution only (citation string → paraId(s)). The server's reverse
 * (`ResolveCitation`, paraId → canonical number) is NOT ported — nothing in this project's acceptance
 * criteria needs it, and `clauseLocation.ts`'s `computedNumberAt` already answers "what number does
 * this resolved position carry" without a citation string.
 *
 * @see src/server/api/Sprk.Bff.Api/Services/Compose/CitationResolver.cs — the server twin this mirrors.
 * @see ./clauseLocation.ts — the sibling WS-3/WS-4 client utility (resolved position → display label;
 *      audited for this task and left unchanged — it is a different concern, position→label, not
 *      string→paraId; see `notes/011-execution-notes.md`).
 * @see ./ComposeEditor.tsx — `placeAdvisoryComments` (the sole consumer; deterministic-first ordering).
 */
import type { ParaIdMapEntry } from '../types/compose-contracts';

/** The three legal-citation shapes this resolver matches, plus the unrecognized/malformed sentinel —
 *  mirrors the server `CitationShape` enum (lower-camel here per TS convention, not a wire type). */
export type CitationShape = 'single' | 'subItem' | 'range' | 'unrecognized';

/** One resolved paragraph target — mirrors the server `CitationTarget` value type. */
export interface CitationTarget {
  readonly paraId: string;
  readonly index: number;
  readonly computedNumber?: string;
  readonly listPath: readonly number[];
}

/** The outcome of resolving one citation string — mirrors the server `CitationResolution`. Zero
 *  `matches` is a VALID, explicit not-found answer, never a fabricated paraId. */
export interface CitationResolution {
  readonly query: string;
  readonly shape: CitationShape;
  readonly matches: readonly CitationTarget[];
}

// ---------------------------------------------------------------------------
// Citation string parsing (mirrors `CitationParser` in the server file)
// ---------------------------------------------------------------------------

/** Leading citation words/marks tolerated + stripped: "Section(s)"/"Article(s)"/"Clause(s)"/
 *  "Paragraph(s)"/"Para", case-insensitive. The section sign `§`/`§§` is stripped separately. */
const LEADING_LABEL_WORDS = [
  'sections',
  'section',
  'articles',
  'article',
  'clauses',
  'clause',
  'paragraphs',
  'paragraph',
  'paras',
  'para',
];

const ASCII_DIGIT = /^[0-9]+$/;
const ASCII_LETTERS = /^[a-zA-Z]+$/;

function collapseWhitespace(s: string): string {
  return s.replace(/\s+/g, ' ').trim();
}

/** Strips leading `§`/`§§` and any of {@link LEADING_LABEL_WORDS}, repeatedly (handles "Sections §4"
 *  style stacking, matching the server's repeat-until-no-change loop). NOTE: the letter-boundary check
 *  uses ASCII `[a-zA-Z]` (the server's `char.IsLetter` is Unicode-aware) — an acceptable, documented
 *  simplification for English legal-citation text. */
function stripLeadingLabel(input: string): string {
  let s = input;
  let changed = true;
  while (changed) {
    changed = false;
    let t = s.replace(/^\s+/, '');
    while (t.startsWith('§')) {
      t = t.slice(1).replace(/^\s+/, '');
      changed = true;
    }
    for (const word of LEADING_LABEL_WORDS) {
      if (
        t.length >= word.length &&
        t.slice(0, word.length).toLowerCase() === word &&
        (t.length === word.length || !/[a-zA-Z]/.test(t[word.length]))
      ) {
        t = t.slice(word.length).replace(/^\s+/, '');
        changed = true;
        break;
      }
    }
    s = t;
  }
  return s;
}

function isAllDigits(s: string): boolean {
  return s.length > 0 && ASCII_DIGIT.test(s);
}

function isAllAsciiLetters(s: string): boolean {
  return s.length > 0 && ASCII_LETTERS.test(s);
}

/** Contiguous top-level RANGE — "4-7" / "4–7" / "4—7" (hyphen / en dash / em dash), digits only on
 *  both sides. A descending "7-4" normalizes to an ascending span. */
function tryParseRange(s: string): { low: number; high: number } | null {
  let dashIndex = -1;
  for (let i = 0; i < s.length; i++) {
    if (s[i] === '-' || s[i] === '–' || s[i] === '—') {
      dashIndex = i;
      break;
    }
  }
  if (dashIndex <= 0 || dashIndex >= s.length - 1) return null;

  const left = s.slice(0, dashIndex).trim();
  const right = s.slice(dashIndex + 1).trim();
  if (!isAllDigits(left) || !isAllDigits(right)) return null;

  let low = parseInt(left, 10);
  let high = parseInt(right, 10);
  if (low > high) [low, high] = [high, low];
  return { low, high };
}

/** Bijective base-26: "a"→1, "z"→26, "aa"→27. Case-insensitive. */
function letterToOrdinal(token: string): number {
  let value = 0;
  for (const ch of token.toLowerCase()) {
    value = value * 26 + (ch.charCodeAt(0) - 'a'.charCodeAt(0) + 1);
  }
  return value;
}

const ROMAN_VALUES: Readonly<Record<string, number>> = {
  i: 1,
  v: 5,
  x: 10,
  l: 50,
  c: 100,
  d: 500,
  m: 1000,
};

const ROMAN_TABLE: ReadonlyArray<readonly [number, string]> = [
  [1000, 'm'],
  [900, 'cm'],
  [500, 'd'],
  [400, 'cd'],
  [100, 'c'],
  [90, 'xc'],
  [50, 'l'],
  [40, 'xl'],
  [10, 'x'],
  [9, 'ix'],
  [5, 'v'],
  [4, 'iv'],
  [1, 'i'],
];

function toRoman(value: number): string {
  let v = value;
  let out = '';
  for (const [num, sym] of ROMAN_TABLE) {
    while (v >= num) {
      out += sym;
      v -= num;
    }
  }
  return out;
}

/** Parses a canonical (case-insensitive) roman numeral, or `null` if invalid. Validates by
 *  re-encoding and comparing, so "iiii"/"vv" reject — mirrors the server's canonical-form check. */
function tryRomanToOrdinal(token: string): number | null {
  const lower = token.toLowerCase();
  let total = 0;
  let prevValue = 0;
  for (let i = lower.length - 1; i >= 0; i--) {
    const value = ROMAN_VALUES[lower[i]];
    if (value === undefined) return null;
    if (value < prevValue) total -= value;
    else {
      total += value;
      prevValue = value;
    }
  }
  if (total <= 0) return null;
  return toRoman(total) === lower ? total : null;
}

/**
 * Expands one parenthesized sub-item token into its candidate ordinal value(s) — mirrors
 * `SubItemTokenOrdinals`: all-digits → that integer; a single ambiguous letter (e.g. "i") yields
 * BOTH the letter (a=1..z=26) and roman readings when they differ; a multi-char token reads as
 * roman when valid, else as a bijective base-26 letter run.
 */
function subItemTokenOrdinals(token: string): number[] {
  if (token.length === 0) return [];
  if (isAllDigits(token)) return [parseInt(token, 10)];
  if (!isAllAsciiLetters(token)) return [];

  const letter = letterToOrdinal(token);
  const roman = tryRomanToOrdinal(token);

  if (token.length === 1) {
    return roman !== null && roman !== letter ? [letter, roman] : [letter];
  }
  return roman !== null ? [roman] : [letter];
}

/** Cartesian-product expansion of the running candidate paths with a new token's candidates. */
function expandPaths(
  paths: ReadonlyArray<readonly number[]>,
  candidates: readonly number[]
): Array<readonly number[]> {
  const out: Array<readonly number[]> = [];
  for (const path of paths) {
    for (const candidate of candidates) {
      out.push([...path, candidate]);
    }
  }
  return out;
}

interface ParsedCitation {
  readonly shape: CitationShape;
  readonly paths: ReadonlyArray<readonly number[]>;
  readonly rangeLow: number;
  readonly rangeHigh: number;
}

const UNRECOGNIZED: ParsedCitation = { shape: 'unrecognized', paths: [], rangeLow: 0, rangeHigh: 0 };

/** Leading dotted-decimal base ("4", "4.2", "1.1.1", optional trailing dot) + zero-or-more
 *  parenthesized sub-item tokens, expanded via cartesian product. Mirrors `ParseOrdinalPath`. */
function parseOrdinalPath(s: string): ParsedCitation {
  let i = 0;
  const basePath: number[] = [];
  while (i < s.length && /[0-9]/.test(s[i])) {
    const start = i;
    while (i < s.length && /[0-9]/.test(s[i])) i++;
    basePath.push(parseInt(s.slice(start, i), 10));
    if (i < s.length && s[i] === '.') i++; // consume the level separator; trailing dot tolerated.
  }
  if (basePath.length === 0) return UNRECOGNIZED; // no leading number ⇒ not a citation we resolve.

  let hadSubItem = false;
  let paths: ReadonlyArray<readonly number[]> = [basePath];

  while (i < s.length) {
    if (/\s/.test(s[i])) {
      i++;
      continue;
    }
    if (s[i] !== '(') return UNRECOGNIZED; // trailing junk ⇒ not a clean citation.
    const close = s.indexOf(')', i + 1);
    if (close < 0) return UNRECOGNIZED; // unbalanced parenthesis.

    const token = s.slice(i + 1, close).trim();
    const candidates = subItemTokenOrdinals(token);
    if (candidates.length === 0) return UNRECOGNIZED; // neither number, letter, nor roman.

    hadSubItem = true;
    paths = expandPaths(paths, candidates);
    i = close + 1;
  }

  return { shape: hadSubItem ? 'subItem' : 'single', paths, rangeLow: 0, rangeHigh: 0 };
}

/** Parses a raw citation string into a range or a set of candidate ordinal paths. Mirrors
 *  `CitationParser.Parse`. Total — never throws; an unparseable input returns `UNRECOGNIZED`. */
function parseCitation(raw: string): ParsedCitation {
  let s = collapseWhitespace(raw);
  if (s.length === 0) return UNRECOGNIZED;

  s = stripLeadingLabel(s);
  if (s.length === 0) return UNRECOGNIZED;

  const range = tryParseRange(s);
  if (range) return { shape: 'range', paths: [], rangeLow: range.low, rangeHigh: range.high };

  return parseOrdinalPath(s);
}

function pathEquals(a: readonly number[], b: readonly number[]): boolean {
  return a.length === b.length && a.every((v, idx) => v === b[idx]);
}

// ---------------------------------------------------------------------------
// Matching (mirrors `CitationResolver.ResolveCore` + `IsCitable`)
// ---------------------------------------------------------------------------

/**
 * A paragraph is CITABLE by number only when it carries a computed NUMERIC legal label — a bullet
 * carries a non-numeric `computedNumber` (a glyph) but can still carry a numeric `listPath`; it must
 * NOT be reachable by a section/range citation. Mirrors the server `IsCitable` gate exactly (gate on
 * the label's first character being an ASCII digit, after trimming LEADING whitespace only).
 */
function isCitable(target: CitationTarget): boolean {
  if (target.listPath.length === 0) return false;
  const label = (target.computedNumber ?? '').replace(/^\s+/, '');
  return label.length > 0 && /^[0-9]/.test(label[0]);
}

/**
 * Resolves `citation` against `referenceMap` — the `ComposeEditor` `paraIdMap` prop (or any
 * WS-4-extended `ParaIdMapEntry[]`). Total — never throws for any input; an unrecognized or
 * zero-match citation returns `{ matches: [] }`, never a fabricated paraId. `matches` is ordered by
 * `index` (document order), mirroring the server's `.OrderBy(t => t.Index)`.
 */
export function resolveCitation(
  citation: string | null | undefined,
  referenceMap: readonly ParaIdMapEntry[]
): CitationResolution {
  const query = (citation ?? '').trim();
  if (query.length === 0) {
    return { query, shape: 'unrecognized', matches: [] };
  }

  const parsed = parseCitation(query);
  if (parsed.shape === 'unrecognized') {
    return { query, shape: 'unrecognized', matches: [] };
  }

  const targets: CitationTarget[] = referenceMap.map(e => ({
    paraId: e.paraId,
    index: e.index,
    computedNumber: e.computedNumber,
    listPath: e.listPath ?? [],
  }));

  if (parsed.shape === 'range') {
    const matches = targets
      .filter(
        t =>
          isCitable(t) &&
          t.listPath.length >= 1 &&
          t.listPath[0] >= parsed.rangeLow &&
          t.listPath[0] <= parsed.rangeHigh
      )
      .sort((a, b) => a.index - b.index);
    return { query, shape: 'range', matches };
  }

  // Single / SubItem — a paragraph matches when its raw listPath equals ANY candidate path exactly.
  const matches = targets
    .filter(t => isCitable(t) && parsed.paths.some(path => pathEquals(t.listPath, path)))
    .sort((a, b) => a.index - b.index);
  return { query, shape: parsed.shape, matches };
}
