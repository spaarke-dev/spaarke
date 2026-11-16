/**
 * summaryStream.test.ts - spaarke-ontology-platform-r1 task 195. The Summary step's stream parser, on its own: which JSON
 * object it takes, and what it does with text that is not (complete) JSON.
 */
import { extractLastJsonObject, parseSummaryStream } from '../summaryStream';

describe('extractLastJsonObject', () => {
  it('takes the last complete object when the same object arrives twice, back to back', () => {
    const one = JSON.stringify({ tldr: 'a', summary: 'b' });
    expect(extractLastJsonObject(one + one)).toEqual({ tldr: 'a', summary: 'b' });
  });

  it('is not fooled by braces, quotes and escaped quotes inside a JSON string', () => {
    const text = JSON.stringify({ tldr: 'He said "stop {now}" and left }', summary: 'ok' });
    expect(extractLastJsonObject(text)).toEqual({ tldr: 'He said "stop {now}" and left }', summary: 'ok' });
  });

  it('skips prose braces and a trailing partial object', () => {
    const text = `Result {not json} ${JSON.stringify({ tldr: 'kept' })} then {"tldr":"half`;
    expect(extractLastJsonObject(text)).toEqual({ tldr: 'kept' });
  });

  it('returns null when there is no complete object', () => {
    expect(extractLastJsonObject('plain words')).toBeNull();
    expect(extractLastJsonObject('{"tldr":"half')).toBeNull();
  });
});

describe('parseSummaryStream', () => {
  const result = JSON.stringify({ tldr: 'Short.', summary: 'Longer.', confidence: 0.9 });

  it('is structured: tldr and summary from the last complete object', () => {
    expect(parseSummaryStream(result + result, true)).toEqual({
      kind: 'structured',
      tldr: 'Short.',
      summary: 'Longer.',
    });
  });

  it('falls back to shortSummary when the object has no summary', () => {
    expect(parseSummaryStream(JSON.stringify({ shortSummary: 'Condensed.' }), true)).toEqual({
      kind: 'structured',
      tldr: '',
      summary: 'Condensed.',
    });
  });

  it('is pending for nothing yet, and for a half-received object while streaming', () => {
    expect(parseSummaryStream('', false)).toEqual({ kind: 'pending' });
    expect(parseSummaryStream('{"tldr":"Sho', false)).toEqual({ kind: 'pending' });
  });

  it('shows a half-received object as text once the stream has ended (nothing else to show)', () => {
    expect(parseSummaryStream('{"tldr":"Sho', true)).toEqual({ kind: 'text', text: '{"tldr":"Sho' });
  });

  it('collapses a verbatim replay of plain text, but not a short repeated phrase', () => {
    const prose = 'The documents confirm the renewal terms.';
    expect(parseSummaryStream(prose + prose, true)).toEqual({ kind: 'text', text: prose });
    expect(parseSummaryStream('No No', true)).toEqual({ kind: 'text', text: 'No No' });
  });

  it('is empty for a complete object with no summary text', () => {
    expect(parseSummaryStream(JSON.stringify({ confidence: 0.2 }), true)).toEqual({ kind: 'empty' });
  });
});
