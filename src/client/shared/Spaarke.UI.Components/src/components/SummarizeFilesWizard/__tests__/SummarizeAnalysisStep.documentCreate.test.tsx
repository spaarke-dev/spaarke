/**
 * SummarizeAnalysisStep.documentCreate.test.tsx — "Work on Analysis" creates its documents through the BFF child-record
 * route, sending the file name only (field-mapping-server-write-path-r1 task 029, spec FR-16, FR-09, FR-40).
 *
 * Before: the step read the user's business unit `sprk_containerid` in the browser and POSTed `{ name, containerId }` per file
 * to `/api/v1/documents`. Now it POSTs `{ sprk_documentname }` to `/api/v1/child-records/sprk_document`: the server decides
 * the owner (the caller's business-unit team) and applies any mapping, and the client reads no business unit.
 *
 * The real step runs against a stubbed fetch (no HTTP at this seam, ADR-038).
 *
 * Classification (ADR-038 §7): MAINTAIN — the wizard's document-create contract.
 */
import * as React from 'react';
import { render, screen, waitFor, fireEvent } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

// The playbook list is not under test: one card, selectable.
jest.mock('../../Playbook', () => ({
  loadPlaybooks: jest.fn(async () => [{ id: 'pb-1', name: 'Contract review' }]),
  PlaybookCardGrid: ({ playbooks, onSelect }: any) => (
    <div>
      {playbooks.map((p: any) => (
        <button key={p.id} onClick={() => onSelect(p)}>
          {p.name}
        </button>
      ))}
    </div>
  ),
}));

import { SummarizeAnalysisStep } from '../SummarizeAnalysisStep';

const BFF = 'https://bff.example';

const uploaded = (name: string): any => ({ id: `id-${name}`, name, sizeBytes: 10, fileType: 'pdf', file: {} });

type Call = { url: string; init: RequestInit };

/** The part of a fetch Response the BFF seam reads (jsdom has no `Response`). */
const reply = (status: number, body: unknown) => ({
  ok: status >= 200 && status < 300,
  status,
  text: async () => JSON.stringify(body),
  json: async () => body,
});

/** A BFF stub: creates answer 201 with an id per file; `refuse` names files the server refuses (ProblemDetails 403). */
function makeFetch(refuse: string[] = []) {
  const calls: Call[] = [];
  let n = 0;
  const fetchFn = jest.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = String(input);
    calls.push({ url, init: init ?? {} });
    const body = JSON.parse(String(init?.body ?? '{}')) as Record<string, unknown>;
    if (refuse.includes(String(body.sprk_documentname))) {
      return reply(403, { detail: 'You do not have permission to create a document.' });
    }
    n += 1;
    return reply(201, { id: `{0000000${n}-0000-0000-0000-000000000000}` });
  });
  return { fetchFn, calls };
}

function setup(files: string[], refuse: string[] = []) {
  const { fetchFn, calls } = makeFetch(refuse);
  const dataService = {
    retrieveRecord: jest.fn(),
    retrieveMultipleRecords: jest.fn(),
    createRecord: jest.fn(),
    updateRecord: jest.fn(),
    deleteRecord: jest.fn(),
  };
  const navigationService = { openDialog: jest.fn(async () => undefined) };
  render(
    <FluentProvider theme={webLightTheme}>
      <SummarizeAnalysisStep
        dataService={dataService as any}
        navigationService={navigationService as any}
        uploadedFiles={files.map(uploaded)}
        authenticatedFetch={fetchFn as any}
        bffBaseUrl={BFF}
      />
    </FluentProvider>
  );
  return { fetchFn, calls, dataService, navigationService };
}

async function pickPlaybook() {
  fireEvent.click(await screen.findByText('Contract review'));
}

describe('SummarizeAnalysisStep — documents are created through the BFF child-record route', () => {
  it('POSTs one create per file to /api/v1/child-records/sprk_document with the file name only', async () => {
    const { calls, navigationService } = setup(['a.pdf', 'b.pdf']);

    await pickPlaybook();
    await waitFor(() => expect(navigationService.openDialog).toHaveBeenCalledTimes(1));

    expect(calls).toHaveLength(2);
    for (const call of calls) {
      // No mapping source, no secure flag: the documents are unfiled, so there is no query string at all.
      expect(call.url).toBe(`${BFF}/api/v1/child-records/sprk_document`);
      expect(call.init.method).toBe('POST');
    }
    // Closed set: the body is the file name and nothing the user did not enter.
    expect(calls.map(c => JSON.parse(String(c.init.body)))).toEqual([
      { sprk_documentname: 'a.pdf' },
      { sprk_documentname: 'b.pdf' },
    ]);
  });

  it('never calls /api/v1/documents, and sends no container, owner, team, parent or search index key', async () => {
    const { calls, navigationService } = setup(['a.pdf']);

    await pickPlaybook();
    await waitFor(() => expect(navigationService.openDialog).toHaveBeenCalledTimes(1));

    expect(calls.some(c => /\/api\/v1\/documents(\/|\?|$)/.test(c.url))).toBe(false);
    const body = JSON.parse(String(calls[0].init.body)) as Record<string, unknown>;
    expect(Object.keys(body)).toEqual(['sprk_documentname']);
    expect(JSON.stringify(body)).not.toMatch(/container|owner|team|businessunit|searchindex|search_index|odata\.bind/i);
  });

  it('reads no business unit in the browser (no systemuser / businessunit lookup)', async () => {
    const { dataService, navigationService } = setup(['a.pdf']);

    await pickPlaybook();
    await waitFor(() => expect(navigationService.openDialog).toHaveBeenCalledTimes(1));

    expect(dataService.retrieveRecord).not.toHaveBeenCalled();
    expect(dataService.createRecord).not.toHaveBeenCalled();
  });

  it('opens the Playbook Library with the created document ids', async () => {
    const { navigationService } = setup(['a.pdf', 'b.pdf']);

    await pickPlaybook();
    await waitFor(() => expect(navigationService.openDialog).toHaveBeenCalledTimes(1));

    const [webResource, data] = navigationService.openDialog.mock.calls[0] as unknown as [string, string];
    expect(webResource).toBe('sprk_playbooklibrary');
    // The route answers a brace-wrapped id; the id given to the library is the cleaned one.
    expect(decodeURIComponent(data.split('&')[0])).toBe(
      'documentIds=00000001-0000-0000-0000-000000000000,00000002-0000-0000-0000-000000000000'
    );
  });

  it("carries on with the documents that were created and shows the server's own words for one it refused", async () => {
    const { navigationService } = setup(['a.pdf', 'b.pdf'], ['b.pdf']);

    await pickPlaybook();
    await waitFor(() => expect(navigationService.openDialog).toHaveBeenCalledTimes(1));

    const [, data] = navigationService.openDialog.mock.calls[0] as unknown as [string, string];
    expect(decodeURIComponent(data.split('&')[0])).toBe('documentIds=00000001-0000-0000-0000-000000000000');
    expect(await screen.findByText(/"b\.pdf": You do not have permission to create a document\./)).toBeTruthy();
  });

  it('opens nothing and says so when the server refuses every document', async () => {
    const { navigationService } = setup(['a.pdf'], ['a.pdf']);

    await pickPlaybook();

    expect(await screen.findByText(/All document records failed to create/)).toBeTruthy();
    expect(navigationService.openDialog).not.toHaveBeenCalled();
  });
});
