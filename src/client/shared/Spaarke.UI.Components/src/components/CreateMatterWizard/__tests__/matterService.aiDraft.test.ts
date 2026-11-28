/**
 * Shared AI-draft streaming helper (ontology task 239) — one helper serves the Create Matter and Create Project
 * wizards. Pinned:
 *   - a matter's request body is exactly what it was before task 239 (no recordKind);
 *   - a project sends recordKind "project" with projectName / projectType / practiceArea;
 *   - the streamed result is returned and progress steps are reported;
 *   - every failure (no host, HTTP failure, error chunk, empty stream) degrades to a fallback in the right wording.
 */
import { TextEncoder as NodeTextEncoder, TextDecoder as NodeTextDecoder } from 'util';
import { streamAiDraftDescription, streamAiDraftSummary } from '../matterService';

// jsdom does not always polyfill these — use Node's.
if (typeof (globalThis as any).TextEncoder === 'undefined') (globalThis as any).TextEncoder = NodeTextEncoder;
if (typeof (globalThis as any).TextDecoder === 'undefined') (globalThis as any).TextDecoder = NodeTextDecoder;

const BFF = 'https://bff.example.test';

function sse(...chunks: Array<Record<string, unknown> | '[DONE]'>): string {
  return chunks.map(c => `data: ${c === '[DONE]' ? c : JSON.stringify(c)}\n\n`).join('');
}

/** A fetch resolving to a streaming 200 whose body is the given SSE text. */
function streamingFetch(text: string, ok = true) {
  return jest.fn(async (_url: string, _init?: RequestInit) => {
    let sent = false;
    return {
      ok,
      status: ok ? 200 : 503,
      text: async () => 'unavailable',
      body: {
        getReader: () => ({
          read: async () => {
            if (sent) return { done: true, value: undefined };
            sent = true;
            return { done: false, value: new TextEncoder().encode(text) };
          },
          releaseLock: () => {},
        }),
      },
    } as unknown as Response;
  });
}

const resultChunk = (summary: string) => ({ type: 'result', content: JSON.stringify({ summary }) });

function postedBody(fetchMock: jest.Mock): Record<string, unknown> {
  return JSON.parse(fetchMock.mock.calls[0][1].body as string);
}

describe('streamAiDraftDescription / streamAiDraftSummary', () => {
  it('a matter request body is unchanged: matterName / matterType / practiceArea and no recordKind', async () => {
    const fetchMock = streamingFetch(sse(resultChunk('A matter draft.'), '[DONE]'));

    const result = await streamAiDraftSummary(
      'Acme v. Beta',
      'Litigation',
      'Employment',
      {},
      undefined,
      fetchMock,
      BFF
    );

    expect(result.summary).toBe('A matter draft.');
    expect(fetchMock.mock.calls[0][0]).toBe(`${BFF}/api/workspace/matters/ai-summary`);
    expect(postedBody(fetchMock)).toEqual({
      matterName: 'Acme v. Beta',
      matterType: 'Litigation',
      practiceArea: 'Employment',
    });
  });

  it('a project request carries recordKind "project" with projectName / projectType / practiceArea', async () => {
    const fetchMock = streamingFetch(sse(resultChunk('A project draft.'), '[DONE]'));

    const result = await streamAiDraftDescription(
      { kind: 'project', name: 'Data Room Migration', type: 'Transaction', practiceArea: 'Employment' },
      {},
      undefined,
      fetchMock,
      BFF
    );

    expect(result.summary).toBe('A project draft.');
    expect(fetchMock.mock.calls[0][0]).toBe(`${BFF}/api/workspace/matters/ai-summary`);
    expect(postedBody(fetchMock)).toEqual({
      recordKind: 'project',
      projectName: 'Data Room Migration',
      projectType: 'Transaction',
      practiceArea: 'Employment',
    });
    expect(postedBody(fetchMock)).not.toHaveProperty('matterName');
  });

  it('reports the progress steps the server streams', async () => {
    const fetchMock = streamingFetch(
      sse(
        { type: 'progress', step: 'analyzing', content: 'Generating summary...' },
        resultChunk('Draft.'),
        { type: 'progress', step: 'delivering', content: 'Delivering summary...' },
        '[DONE]'
      )
    );
    const steps: string[] = [];

    await streamAiDraftDescription(
      { kind: 'project', name: 'P', type: '', practiceArea: '' },
      { onProgress: s => steps.push(s) },
      undefined,
      fetchMock,
      BFF
    );

    expect(steps).toEqual(['analyzing', 'delivering']);
  });

  describe('failure degrades to the fallback in the right wording', () => {
    const project = {
      kind: 'project' as const,
      name: 'Data Room Migration',
      type: 'Transaction',
      practiceArea: 'Employment',
    };

    it.each([
      ['no authenticated fetch', undefined, undefined],
      ['no BFF base url', jest.fn(), undefined],
    ])('%s', async (_label, fetchFn, base) => {
      const result = await streamAiDraftDescription(project, {}, undefined, fetchFn as never, base);

      expect(result.summary).toContain('project, "Data Room Migration"');
      expect(result.summary).not.toMatch(/matter/i);
    });

    it('an error chunk from the server', async () => {
      const fetchMock = streamingFetch(
        sse({ type: 'error', content: 'An error occurred while generating the summary.' }, '[DONE]')
      );

      const result = await streamAiDraftDescription(project, {}, undefined, fetchMock, BFF);

      expect(result.summary).toContain('project, "Data Room Migration"');
    });

    it('an HTTP failure', async () => {
      const result = await streamAiDraftDescription(project, {}, undefined, streamingFetch('', false), BFF);

      expect(result.summary).toContain('project, "Data Room Migration"');
    });

    it('a stream that ends with no result', async () => {
      const result = await streamAiDraftDescription(project, {}, undefined, streamingFetch(sse('[DONE]')), BFF);

      expect(result.summary).toContain('project, "Data Room Migration"');
    });

    it('a matter keeps its original fallback wording', async () => {
      const result = await streamAiDraftSummary('Acme v. Beta', 'Litigation', 'Employment');

      expect(result.summary).toContain(
        'This litigation matter, "Acme v. Beta", has been created in the Employment practice area.'
      );
    });
  });
});
