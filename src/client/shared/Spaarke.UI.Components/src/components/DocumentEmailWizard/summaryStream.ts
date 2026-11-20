/**
 * summaryStream.ts - turns the Summary step's streamed playbook output into the text the user reads.
 *
 * `POST /api/ai/analysis/execute` streams the playbook node's tokens as `chunk` events and then sends the completed node
 * text as one more `chunk` (AnalysisEndpoints.cs, other consumers read that stream, so the server is not changed). The
 * "Summarize New File(s)" playbook's output is a JSON object (`{"tldr":..., "summary":..., "confidence":0.9}`), so the
 * concatenation of every chunk is that object TWICE. Showing the concatenation verbatim put raw JSON on screen, twice.
 *
 * Rule (ontology task 195): take the last complete JSON object in the accumulated text, normalise it with the same
 * `normalizeResult` the Summarize Files wizard uses, and show `tldr` and `summary`. Output that is not JSON is shown as
 * text, once (a verbatim repeat of itself is collapsed).
 */
import { normalizeResult } from '../SummarizeFilesWizard/summarizeService';

/** What the Summary step should show for the text received so far. */
export type StreamedSummary =
  /** Nothing readable yet: a JSON object is still arriving, or nothing has arrived. Show the "generating" line. */
  | { kind: 'pending' }
  /** A complete JSON object that carries no summary text. Show "(no summary available)". */
  | { kind: 'empty' }
  /** The playbook's structured result: the TL;DR and the summary, each once. */
  | { kind: 'structured'; tldr: string; summary: string }
  /** Output that is not JSON, once. */
  | { kind: 'text'; text: string };

/** Shortest repeated unit that counts as the completed text replaying the streamed tokens (avoids "No No" -> "No"). */
const MIN_REPLAY_UNIT_LENGTH = 20;

/**
 * The top-level `{...}` spans in `text`, in order. String-aware: braces and quotes inside a JSON string (and escaped
 * quotes) do not count. A span still open at the end of the text (a partial object) is not returned.
 */
function topLevelObjectSpans(text: string): Array<[number, number]> {
  const spans: Array<[number, number]> = [];
  let depth = 0;
  let start = -1;
  let inString = false;
  let escaped = false;

  for (let i = 0; i < text.length; i++) {
    const ch = text[i];
    if (inString) {
      if (escaped) escaped = false;
      else if (ch === '\\') escaped = true;
      else if (ch === '"') inString = false;
      continue;
    }
    if (ch === '"' && depth > 0) {
      inString = true;
    } else if (ch === '{') {
      if (depth === 0) start = i;
      depth++;
    } else if (ch === '}' && depth > 0) {
      depth--;
      if (depth === 0) spans.push([start, i + 1]);
    }
  }
  return spans;
}

/** `json` as a plain object, or `null` when it is not JSON (prose braces) or not an object. */
function parseObject(json: string): Record<string, unknown> | null {
  try {
    const parsed: unknown = JSON.parse(json);
    return parsed && typeof parsed === 'object' && !Array.isArray(parsed) ? (parsed as Record<string, unknown>) : null;
  } catch {
    return null;
  }
}

/** The last top-level object in `text` that parses as JSON, or `null`. */
export function extractLastJsonObject(text: string): Record<string, unknown> | null {
  const spans = topLevelObjectSpans(text);
  for (let i = spans.length - 1; i >= 0; i--) {
    const object = parseObject(text.slice(spans[i][0], spans[i][1]));
    if (object) return object;
  }
  return null;
}

/** `X` + `X` (the tokens, then the completed text, in any whitespace between) collapses to `X`; anything else is unchanged. */
function collapseReplay(text: string): string {
  const trimmed = text.trim();
  const match = /^([\s\S]+?)\s*\1$/.exec(trimmed);
  return match && match[1].length >= MIN_REPLAY_UNIT_LENGTH ? match[1] : trimmed;
}

const asText = (value: unknown): string => (typeof value === 'string' ? value.trim() : '');

/**
 * @param accumulated every chunk's text, concatenated in arrival order.
 * @param finished    `true` once the stream has ended (a half-received JSON object will then never complete).
 */
export function parseSummaryStream(accumulated: string, finished: boolean): StreamedSummary {
  if (!accumulated.trim()) return { kind: 'pending' };

  const object = extractLastJsonObject(accumulated);
  if (object) {
    const result = normalizeResult(object) as unknown as Record<string, unknown>;
    const tldr = asText(result.tldr);
    const summary = asText(result.summary) || asText(result.shortSummary);
    if (!tldr && !summary) return { kind: 'empty' };
    return { kind: 'structured', tldr, summary };
  }

  // No complete object. While streaming, text that has begun an object is JSON still arriving: show nothing rather than
  // a half-written `{"tldr":"...`. Once the stream has ended it is shown as it is.
  if (!finished && accumulated.includes('{')) return { kind: 'pending' };
  return { kind: 'text', text: collapseReplay(accumulated) };
}
