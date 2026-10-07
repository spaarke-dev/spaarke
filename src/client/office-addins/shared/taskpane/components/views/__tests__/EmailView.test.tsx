/**
 * EmailView — the Word pane's Email tab (task 096, owner UAT round 4 items 6-8).
 *
 * Renders the REAL shared compose engine (`EmailComposer`, `@spaarke/ui-components`, through its pane wrapper
 * `SendEmailPane`), resolved through the package's exact alias — never a local copy — and drives a real send through it. The only double is the
 * network (`global.fetch`); the request the engine actually sends to `POST /api/communications/send` is
 * asserted field by field.
 *
 * Pins: the form is the shared engine (To/Cc, Subject, Attachments, Related to, body, Send) pre-filled from the
 * document and its record; Send posts `sendMode: "user"`, the document as `attachmentDocumentIds`, the record as
 * `associations`, `archiveToSpe: false`, with the pane's bearer token; success shows "Email sent" + "Open in
 * Spaarke" for the communication and resets the form; a refused send (403/400/500) shows the server's reason and
 * nothing is reported as sent; a 2xx without a record id says "sent, but not recorded"; a document not yet in
 * Spaarke offers "Save to Spaarke first" and no Send at all.
 */
import { render, screen, fireEvent, waitFor, within, act } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { EmailView, type EmailViewProps } from '../EmailView';
import { openRecord } from '../../../services/openRecordLauncher';

jest.mock('../../../services/openRecordLauncher', () => {
  const actual = jest.requireActual('../../../services/openRecordLauncher');
  return { ...actual, openRecord: jest.fn(() => ({ opened: true })) };
});

// jsdom gaps the shared rich-text editor (lexical) touches — same polyfills the shared library's own jest setup
// installs (`Spaarke.UI.Components/jest.setup.js`).
beforeAll(() => {
  if (!window.matchMedia) {
    Object.defineProperty(window, 'matchMedia', {
      writable: true,
      value: (query: string) => ({
        matches: false,
        media: query,
        onchange: null,
        addListener: () => undefined,
        removeListener: () => undefined,
        addEventListener: () => undefined,
        removeEventListener: () => undefined,
        dispatchEvent: () => false,
      }),
    });
  }
  if (!Element.prototype.scrollIntoView) {
    Element.prototype.scrollIntoView = () => undefined;
  }
  const rangeProto = Range.prototype as unknown as Record<string, unknown>;
  if (typeof rangeProto['getBoundingClientRect'] !== 'function') {
    rangeProto['getBoundingClientRect'] = () => ({
      x: 0,
      y: 0,
      top: 0,
      left: 0,
      right: 0,
      bottom: 0,
      width: 0,
      height: 0,
    });
  }
  if (typeof rangeProto['getClientRects'] !== 'function') {
    rangeProto['getClientRects'] = () => [];
  }
});

const DOC = '11111111-1111-1111-1111-111111111111';
const REC = '22222222-2222-2222-2222-222222222222';
const COMM = '33333333-3333-3333-3333-333333333333';
const ORG = 'https://contoso.crm.dynamics.com';
const BFF = 'https://bff.contoso.test';

function fakeResponse(status: number, body: unknown): Response {
  const text = JSON.stringify(body);
  const make = (): Response =>
    ({
      ok: status >= 200 && status < 300,
      status,
      headers: { get: (h: string) => (h.toLowerCase() === 'content-type' ? 'application/problem+json' : null) },
      json: async () => JSON.parse(text),
      text: async () => text,
      clone: () => make(),
    }) as unknown as Response;
  return make();
}

let fetchMock: jest.Mock;

beforeEach(() => {
  fetchMock = jest.fn();
  (global as unknown as { fetch: unknown }).fetch = fetchMock;
  (openRecord as jest.Mock).mockClear();
});

function renderView(overrides: Partial<EmailViewProps> = {}) {
  const props: EmailViewProps = {
    document: { documentId: DOC, documentName: 'Master Agreement', fileName: 'Master Agreement.docx' },
    relatedRecord: { entityType: 'sprk_matter', id: REC, typeLabel: 'Matter', displayName: 'Smith v. Jones' },
    orgUrl: ORG,
    bffBaseUrl: BFF,
    fromMailbox: 'ralph@contoso.test',
    getAccessToken: async () => 'pane-token',
    onSearchContacts: async () => [],
    canOpenRecord: true,
    onGoToSave: jest.fn(),
    ...overrides,
  };
  const utils = render(
    <FluentProvider theme={webLightTheme}>
      <EmailView {...props} />
    </FluentProvider>
  );
  return { ...utils, props };
}

/** Type an address into the engine's To field and commit it (Enter), as a user would. */
function addToRecipient(address: string) {
  const to = screen.getByRole('group', { name: 'To' });
  const input = within(to).getByRole('textbox');
  fireEvent.change(input, { target: { value: address } });
  fireEvent.keyDown(input, { key: 'Enter' });
}

async function clickSend() {
  const from = screen.getByRole('group', { name: 'From' });
  await act(async () => {
    fireEvent.click(within(from).getByRole('button', { name: /send/i }));
  });
}

describe('EmailView renders the SHARED compose engine', () => {
  it('resolves the wrapper to the shared library source — not a local copy', () => {
    expect(require.resolve('@spaarke/ui-components/send-email-pane').replace(/\\/g, '/')).toMatch(
      /\/shared\/Spaarke\.UI\.Components\/src\/components\/EmailComposer\/wrappers\/SendEmailPane\.tsx$/
    );
  });

  it('shows the engine’s composer: From (my mailbox), To, Cc, Subject, Attachments (1), Related to (1), body', async () => {
    renderView();

    expect(await screen.findByRole('region', { name: 'Email composer' })).toBeInTheDocument();
    const from = screen.getByRole('group', { name: 'From' });
    expect(within(from).getByText('ralph@contoso.test')).toBeInTheDocument();
    expect(screen.getByRole('group', { name: 'To' })).toBeInTheDocument();
    expect(screen.getByRole('group', { name: 'Cc' })).toBeInTheDocument();
    expect(screen.getByRole('textbox', { name: 'Subject' })).toHaveValue('Master Agreement');
    expect(screen.getByText('Attachments (1)')).toBeInTheDocument();
    expect(screen.getByText('Related to (1)')).toBeInTheDocument();
    // The related record chip (engine's AssociationChips) and the body's record link both name the matter.
    expect(screen.getAllByText(/Smith v\. Jones/).length).toBeGreaterThan(0);
    expect(screen.getByText('Attached: Master Agreement.docx')).toBeInTheDocument();
  });
});

describe('Send', () => {
  it('posts the exact request to POST /api/communications/send as the user, with the document and the record', async () => {
    fetchMock.mockResolvedValue(fakeResponse(200, { communicationId: COMM }));
    renderView();
    await screen.findByRole('region', { name: 'Email composer' });

    addToRecipient('jane@acme.test');
    await clickSend();

    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(1));
    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit & { headers: Record<string, string> }];
    expect(url).toBe(`${BFF}/api/communications/send`);
    expect(init.method).toBe('POST');
    expect(init.headers.Authorization).toBe('Bearer pane-token');

    const body = JSON.parse(String(init.body));
    expect(body).toMatchObject({
      to: ['jane@acme.test'],
      subject: 'Master Agreement',
      bodyFormat: 'HTML',
      communicationType: 'Email',
      sendMode: 'User',
      archiveToSpe: false,
      attachmentDocumentIds: [DOC],
      associations: [
        {
          entityType: 'sprk_matter',
          entityId: REC,
          entityName: 'Smith v. Jones',
          entityUrl: `${ORG}/main.aspx?etn=sprk_matter&id=${REC}&pagetype=entityrecord&navbar=off`,
        },
      ],
    });
    expect(body.body).toContain('Please see the attached document.');
    expect(body.body).toContain(`etn=sprk_matter`);
    expect(body.body).not.toMatch(/sharepoint|share-link/i);
    // Nothing secret travels in the payload.
    expect(String(init.body)).not.toContain('pane-token');
  });

  it('success → "Email sent" with "Open in Spaarke" for the communication, and the form resets', async () => {
    fetchMock.mockResolvedValue(fakeResponse(200, { communicationId: COMM }));
    renderView();
    await screen.findByRole('region', { name: 'Email composer' });

    addToRecipient('jane@acme.test');
    expect(within(screen.getByRole('group', { name: 'To' })).getByText(/jane@acme\.test/)).toBeInTheDocument();
    await clickSend();

    expect(await screen.findByText('Email sent')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: /open in spaarke/i }));
    expect(openRecord).toHaveBeenCalledWith({ orgUrl: ORG, entityType: 'sprk_communication', recordId: COMM });

    // Reset: the engine is remounted with its pre-fill — the typed recipient is gone, the subject is back.
    await waitFor(() =>
      expect(within(screen.getByRole('group', { name: 'To' })).queryByText(/jane@acme\.test/)).not.toBeInTheDocument()
    );
    expect(screen.getByRole('textbox', { name: 'Subject' })).toHaveValue('Master Agreement');
  });

  it('no "Open in Spaarke" when the host cannot open a browser window (NFR-10)', async () => {
    fetchMock.mockResolvedValue(fakeResponse(200, { communicationId: COMM }));
    renderView({ canOpenRecord: false });
    await screen.findByRole('region', { name: 'Email composer' });

    addToRecipient('jane@acme.test');
    await clickSend();

    expect(await screen.findByText('Email sent')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /open in spaarke/i })).not.toBeInTheDocument();
  });

  it.each([
    [403, 'You do not have access to this document.'],
    [400, 'At least one recipient is required.'],
    [500, 'Unexpected error (OBO): consent required'],
  ])('a refused send (%i) shows the server’s reason and nothing is reported as sent', async (status, detail) => {
    fetchMock.mockResolvedValue(fakeResponse(status, { title: 'Refused', detail, status }));
    renderView();
    await screen.findByRole('region', { name: 'Email composer' });

    addToRecipient('jane@acme.test');
    await clickSend();

    expect(await screen.findByText(`Email not sent: ${detail}`)).toBeInTheDocument();
    expect(screen.queryByText('Email sent')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /open in spaarke/i })).not.toBeInTheDocument();
    // The form is kept so the user can fix and retry.
    expect(within(screen.getByRole('group', { name: 'To' })).getByText(/jane@acme\.test/)).toBeInTheDocument();
  });

  it('a 2xx without a record id → "Email sent, but not recorded" (never silent, never "Email sent")', async () => {
    fetchMock.mockResolvedValue(fakeResponse(200, { communicationId: null }));
    renderView();
    await screen.findByRole('region', { name: 'Email composer' });

    addToRecipient('jane@acme.test');
    await clickSend();

    expect(await screen.findByText('Email sent, but not recorded')).toBeInTheDocument();
    expect(screen.queryByText('Email sent')).not.toBeInTheDocument();
  });

  it('no response at all → "may not have been sent — check Sent Items" (never "sent", never a flat "not sent")', async () => {
    fetchMock.mockRejectedValue(new TypeError('Failed to fetch'));
    renderView();
    await screen.findByRole('region', { name: 'Email composer' });

    addToRecipient('jane@acme.test');
    await clickSend();

    expect(
      await screen.findByText(/Email may not have been sent — no response from Spaarke \(Failed to fetch\)/)
    ).toBeInTheDocument();
    expect(screen.queryByText('Email sent')).not.toBeInTheDocument();
  });
});

describe('a document not yet in Spaarke', () => {
  it('explains, offers "Save to Spaarke first", and offers no Send at all', () => {
    const { props } = renderView({ document: null });

    expect(screen.getByText('This document is not in Spaarke yet.', { exact: false })).toBeInTheDocument();
    expect(screen.queryByRole('region', { name: 'Email composer' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /^send/i })).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Save to Spaarke first' }));
    expect(props.onGoToSave).toHaveBeenCalledTimes(1);
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it('while the pane is still checking, shows progress instead of either state', () => {
    renderView({ document: null, identityStatus: 'checking' });

    expect(screen.getByText(/checking whether this document is in spaarke/i)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Save to Spaarke first' })).not.toBeInTheDocument();
  });

  it('a FAILED check never claims the document is new — it points to the Save tab, with no Send', () => {
    const { props } = renderView({ document: null, identityStatus: 'unconfirmed' });

    expect(screen.getByText('Could not confirm this document')).toBeInTheDocument();
    expect(screen.queryByText(/not in Spaarke yet/i)).not.toBeInTheDocument();
    expect(screen.queryByRole('region', { name: 'Email composer' })).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Go to Save' }));
    expect(props.onGoToSave).toHaveBeenCalledTimes(1);
  });
});

describe('recipient search', () => {
  it('searches the add-in’s contacts and shows each contact’s email', async () => {
    const onSearchContacts = jest.fn(async () => [
      { id: 'c1', name: 'Jane Cooper', email: 'jane.cooper@acme.test' },
      { id: 'c2', name: 'Jane Cooper', email: 'jane.cooper@beta.test' },
    ]);
    renderView({ onSearchContacts });
    await screen.findByRole('region', { name: 'Email composer' });

    const input = within(screen.getByRole('group', { name: 'To' })).getByRole('textbox');
    fireEvent.change(input, { target: { value: 'Jane' } });

    expect(await screen.findByText(/jane\.cooper@acme\.test/, {}, { timeout: 3000 })).toBeInTheDocument();
    expect(screen.getByText(/jane\.cooper@beta\.test/)).toBeInTheDocument();
    expect(onSearchContacts).toHaveBeenCalledWith('Jane');
  });
});
