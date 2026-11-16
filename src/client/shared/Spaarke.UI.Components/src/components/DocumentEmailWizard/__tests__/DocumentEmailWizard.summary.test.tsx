/**
 * DocumentEmailWizard.summary.test.tsx - spaarke-ontology-platform-r1 task 195 (owner 185 dev check C1).
 *
 * The Summary step ran the playbook from `renderContent`, which `WizardShell` calls while IT is rendering, so the run's
 * state updates made React warn "Cannot update a component while rendering a different component"; and it appended every
 * stream chunk and showed the concatenation: the playbook's raw JSON, twice (the streamed tokens, then the completed node
 * text, both as `chunk` events).
 *
 * Contract under test:
 * - opening the Summary step starts the run once and never sets state during render (a console.error spy catches the warning);
 * - tokens followed by the same completed JSON show the TL;DR and the summary once, with no braces or field names;
 * - output that is not JSON is shown once, as text (a verbatim replay collapses);
 * - a complete object with no summary text shows the empty line, never the JSON; a half-received object is not shown.
 *
 * The wizard shell is a stub that renders the Summary step during ITS render, as the real one does; the SSE stream is a
 * fake `Response` body feeding the real `readSseStream`.
 */
import * as React from 'react';
// jsdom has no TextDecoder, which readSseStream needs; Node's is the platform one.
const { TextDecoder: NodeTextDecoder } = require('node:util');
(globalThis as any).TextDecoder = NodeTextDecoder;
import { act, screen, waitFor } from '@testing-library/react';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import type { IWizardShellProps } from '../../Wizard/wizardShellTypes';
import { DocumentEmailWizard } from '../DocumentEmailWizard';
import type { IDocumentEmailWizardItem, IDocumentEmailWizardProps } from '../DocumentEmailWizard';
import type { IDataService } from '../../../types/serviceInterfaces';

jest.mock('../../Wizard/WizardShell', () => {
  const ReactActual = jest.requireActual('react');
  return {
    WizardShell: ReactActual.forwardRef((props: IWizardShellProps, ref: unknown) => {
      ReactActual.useImperativeHandle(ref, () => ({ addDynamicStep: () => {}, removeDynamicStep: () => {} }));
      // The Summary step, rendered during this component's render (what WizardShell does with `renderContent`).
      return ReactActual.createElement('div', null, props.steps[1].renderContent({} as never));
    }),
  };
});

jest.mock('../../WizardFollowOns/steps/SendEmailFollowOnStep', () => ({ SendEmailFollowOnStep: () => null }));

const BFF = 'https://bff.example.test';
const DOCS: IDocumentEmailWizardItem[] = [
  { documentId: 'doc-1', name: 'Master Agreement.pdf' },
  { documentId: 'doc-2', name: 'Side Letter.docx' },
];

const TLDR = 'The agreement renews in March.';
const SUMMARY = 'Both documents confirm the renewal terms and the notice period.';
const RESULT_JSON = JSON.stringify({ tldr: TLDR, summary: SUMMARY, confidence: 0.9 });

/** A fake streamed `Response`: the given SSE events, then the terminal `done`. */
function sseResponse(chunks: string[]): Response {
  const events = [
    'data: {"type":"metadata"}\n\n',
    ...chunks.map(content => `data: ${JSON.stringify({ type: 'chunk', content })}\n\n`),
    'data: {"type":"done","done":true}\n\n',
  ].join('');
  const bytes = Uint8Array.from(Buffer.from(events, 'utf-8'));
  let reads = 0;
  const reader = {
    read: jest.fn(() =>
      ++reads === 1 ? Promise.resolve({ done: false, value: bytes }) : Promise.resolve({ done: true, value: undefined })
    ),
    cancel: jest.fn().mockResolvedValue(undefined),
    releaseLock: jest.fn(),
  };
  return { ok: true, status: 200, body: { getReader: () => reader }, text: async () => '' } as unknown as Response;
}

function fetchStreaming(chunks: string[]): jest.Mock {
  return jest.fn(async (url: string) =>
    url.includes('/api/ai/playbooks/by-id/')
      ? ({ ok: true, status: 200, json: async () => ({ playbookId: 'pb-1' }) } as unknown as Response)
      : sseResponse(chunks)
  );
}

function renderSummaryStep(chunks: string[]) {
  const authenticatedFetch = fetchStreaming(chunks);
  const props: IDocumentEmailWizardProps = {
    open: true,
    onClose: jest.fn(),
    selectedDocuments: DOCS,
    authenticatedFetch: authenticatedFetch as never,
    bffBaseUrl: BFF,
    dataService: {} as IDataService,
  };
  return { authenticatedFetch, ...renderWithProviders(<DocumentEmailWizard {...props} />) };
}

const executeCalls = (fetchMock: jest.Mock) =>
  fetchMock.mock.calls.filter(([url]: [string]) => url.includes('/api/ai/analysis/execute'));

describe('DocumentEmailWizard — Summary step (task 195)', () => {
  let errorSpy: jest.SpyInstance;

  beforeEach(() => {
    errorSpy = jest.spyOn(console, 'error').mockImplementation(() => {});
  });
  afterEach(() => {
    errorSpy.mockRestore();
  });

  const renderWarnings = () =>
    errorSpy.mock.calls
      .map(args => args.map(String).join(' '))
      .filter(message => /Cannot update a component|while rendering a different component/.test(message));

  it('starts the run once, after the step has rendered, without setting state during render', async () => {
    const { authenticatedFetch } = renderSummaryStep([RESULT_JSON]);

    await waitFor(() => expect(executeCalls(authenticatedFetch)).toHaveLength(1));
    // Checked before anything else about the output, so this fails on the warning itself.
    expect(renderWarnings()).toEqual([]);
    await waitFor(() => expect(screen.getByTestId('summary-tldr')).toBeInTheDocument());
    expect(executeCalls(authenticatedFetch)).toHaveLength(1);
  });

  it('shows the TL;DR and the summary once for tokens followed by the same completed JSON', async () => {
    // The tokens as the model streams them, then the completed node text: the same object again.
    const tokens = [RESULT_JSON.slice(0, 20), RESULT_JSON.slice(20, 60), RESULT_JSON.slice(60)];
    renderSummaryStep([...tokens, RESULT_JSON]);

    await waitFor(() => expect(screen.getByTestId('summary-tldr')).toBeInTheDocument());
    expect(screen.getAllByText(TLDR)).toHaveLength(1);
    expect(screen.getAllByText(SUMMARY)).toHaveLength(1);
    const card = screen.getByTestId('summary-tldr').closest('div')!.parentElement!;
    expect(card.textContent).not.toMatch(/[{}]|"tldr"|"summary"|confidence/);
    expect(renderWarnings()).toEqual([]);
  });

  it('shows one object once when only the tokens arrive (no completed-text replay)', async () => {
    renderSummaryStep([RESULT_JSON.slice(0, 30), RESULT_JSON.slice(30)]);

    await waitFor(() => expect(screen.getByTestId('summary-tldr')).toBeInTheDocument());
    expect(screen.getAllByText(TLDR)).toHaveLength(1);
    expect(screen.getAllByText(SUMMARY)).toHaveLength(1);
  });

  it('unwraps the rawResponse envelope the generic handler can add', async () => {
    renderSummaryStep([JSON.stringify({ rawResponse: RESULT_JSON })]);

    await waitFor(() => expect(screen.getByTestId('summary-tldr')).toBeInTheDocument());
    expect(screen.getAllByText(SUMMARY)).toHaveLength(1);
  });

  it('shows output that is not JSON as text, once, even when the completed text replays the tokens', async () => {
    const prose = 'The documents confirm the renewal terms and the notice period.';
    renderSummaryStep([prose.slice(0, 25), prose.slice(25), prose]);

    await waitFor(() => expect(screen.getByTestId('summary-text')).toBeInTheDocument());
    expect(screen.getByTestId('summary-text').textContent).toBe(prose);
    expect(screen.queryByTestId('summary-tldr')).not.toBeInTheDocument();
  });

  it('shows the empty line, never JSON, for a complete object with no summary text', async () => {
    renderSummaryStep([JSON.stringify({ confidence: 0.4, practiceAreas: [] })]);

    await waitFor(() => expect(screen.getByText('(no summary available)')).toBeInTheDocument());
    expect(document.body.textContent).not.toMatch(/confidence/);
  });

  it('does not show a half-received object while the stream is still running', async () => {
    // The stream never ends: the object stays incomplete, so nothing readable (and no JSON fragment) is shown.
    const reader = {
      read: jest
        .fn()
        .mockResolvedValueOnce({
          done: false,
          value: Uint8Array.from(Buffer.from('data: {"type":"chunk","content":"{\\"tldr\\":\\"The agr"}\n\n', 'utf-8')),
        })
        .mockImplementation(() => new Promise(() => {})),
      cancel: jest.fn().mockResolvedValue(undefined),
      releaseLock: jest.fn(),
    };
    const authenticatedFetch = jest.fn(async (url: string) =>
      url.includes('/api/ai/playbooks/by-id/')
        ? ({ ok: true, status: 200, json: async () => ({ playbookId: 'pb-1' }) } as unknown as Response)
        : ({ ok: true, status: 200, body: { getReader: () => reader } } as unknown as Response)
    );
    renderWithProviders(
      <DocumentEmailWizard
        open
        onClose={jest.fn()}
        selectedDocuments={DOCS}
        authenticatedFetch={authenticatedFetch as never}
        bffBaseUrl={BFF}
        dataService={{} as IDataService}
      />
    );

    await waitFor(() => expect(reader.read).toHaveBeenCalled());
    await act(async () => {
      await Promise.resolve();
    });
    expect(screen.getByText('Generating combined summary…')).toBeInTheDocument();
    expect(document.body.textContent).not.toMatch(/tldr|The agr/);
  });
});
