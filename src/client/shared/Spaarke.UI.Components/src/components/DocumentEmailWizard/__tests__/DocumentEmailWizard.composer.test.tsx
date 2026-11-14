/**
 * DocumentEmailWizard.composer.test.tsx - spaarke-ontology-platform-r1 task 183 (owner D-219, review section 2.2).
 *
 * The wizard's Compose step is the shared composer (`SendEmailFollowOnStep`, compose-only) and no longer the old
 * `EmailStep`; Finish sends through `sendComposedEmail`, the composer's own request path. Contract under test:
 *
 * - the Compose step gets the host's composer handlers (template picker, AI draft, record pane...), the recipient search,
 *   the shared mailbox as From, the default subject/body, and the kept documents as attachment rows;
 * - the request carries the chosen mailbox, To/Cc/Bcc, the ticked documents, the "Linked documents" block, the record as
 *   association, and no archive request of its own (as before);
 * - the step-1 toggles and "remove" keep the composer's attachment rows in step; uploads still running keep Finish off;
 * - a failed send keeps the wizard open with the composer's wording (an unconfirmed one says "may have been sent");
 * - a host with no authenticated fetch shows a warning and cannot finish.
 *
 * The wizard shell and the Compose step are stubs that hand over their props (the real composer is covered by its own
 * suites and by task 182's step tests); assertions are on what the wizard gives them and on the request it sends.
 */
import * as fs from 'fs';
import * as path from 'path';
import * as React from 'react';
import { act, fireEvent, screen } from '@testing-library/react';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import type { IWizardShellProps } from '../../Wizard/wizardShellTypes';
import type { IComposedEmail, IEmailComposeHandlers } from '../../EmailComposer/composeOnlySend';
import type { ISendEmailFollowOnStepProps } from '../../WizardFollowOns/steps/SendEmailFollowOnStep';
import type { IDataService } from '../../../types/serviceInterfaces';
import { DocumentEmailWizard } from '../DocumentEmailWizard';
import type { IDocumentEmailWizardItem, IDocumentEmailWizardProps } from '../DocumentEmailWizard';

const mockShell: { props?: IWizardShellProps } = {};
jest.mock('../../Wizard/WizardShell', () => {
  const ReactActual = jest.requireActual('react');
  return {
    WizardShell: ReactActual.forwardRef((props: IWizardShellProps, ref: unknown) => {
      mockShell.props = props;
      ReactActual.useImperativeHandle(ref, () => ({ addDynamicStep: () => {}, removeDynamicStep: () => {} }));
      // Confirm Selection and Compose; the summary step would call the BFF and is not under test.
      return ReactActual.createElement(
        'div',
        null,
        props.steps[0].renderContent({} as never),
        props.steps[2].renderContent({} as never)
      );
    }),
  };
});

const mockStep: { props?: ISendEmailFollowOnStepProps } = {};
jest.mock('../../WizardFollowOns/steps/SendEmailFollowOnStep', () => {
  const ReactActual = jest.requireActual('react');
  return {
    SendEmailFollowOnStep: (props: ISendEmailFollowOnStepProps) => {
      mockStep.props = props;
      return ReactActual.createElement('div', { 'data-testid': 'compose-step' }, props.headerContent);
    },
  };
});

const BFF = 'https://bff.example.test';
const ORG = 'https://org.crm.example';

const DOCS: IDocumentEmailWizardItem[] = [
  { documentId: 'doc-1', name: 'Master Agreement.pdf', fileSizeBytes: 2048 },
  { documentId: 'doc-2', name: 'Side Letter.docx', fileSizeBytes: 1024 },
];

const handlers: IEmailComposeHandlers = {
  onListEmailTemplates: jest.fn(),
  onRenderEmailTemplate: jest.fn(),
  onDraftWithAi: jest.fn(),
  onLookupRecipients: jest.fn(),
  onResolveShareLink: jest.fn(
    async (id: string) => `${ORG}/main.aspx?appname=sprk_MatterManagement&etn=sprk_document&id=${id}`
  ),
};

const okFetch = () =>
  jest.fn().mockResolvedValue({ ok: true, status: 200, json: async () => ({ communicationId: 'comm-1' }) });

function renderWizard(overrides: Partial<IDocumentEmailWizardProps> = {}) {
  const props: IDocumentEmailWizardProps = {
    open: true,
    onClose: jest.fn(),
    selectedDocuments: DOCS,
    parentEntityType: 'sprk_matter',
    parentEntityId: 'matter-1',
    authenticatedFetch: okFetch() as never,
    bffBaseUrl: BFF,
    clientUrl: ORG,
    dataService: {} as IDataService,
    emailComposeHandlers: handlers,
    ...overrides,
  };
  return { props, ...renderWithProviders(<DocumentEmailWizard {...props} />) };
}

const lift = (patch: Partial<IComposedEmail>) =>
  act(() => mockStep.props!.onChange({ ...mockStep.props!.value, ...patch }));
const addressed = (email: string) => ({ email, resolved: false });
const composeStep = () => mockShell.props!.steps[2];
const sent = (fetchMock: jest.Mock) => {
  const call = fetchMock.mock.calls.find(([url]: [string]) => url.includes('/api/communications/send'));
  return call ? (JSON.parse(call[1].body as string) as Record<string, unknown>) : undefined;
};

describe('DocumentEmailWizard — Compose step is the shared composer (task 183)', () => {
  beforeEach(() => {
    mockShell.props = undefined;
    mockStep.props = undefined;
    jest.clearAllMocks();
  });

  it('no longer imports the old EmailStep', () => {
    const source = fs.readFileSync(path.resolve(__dirname, '..', 'DocumentEmailWizard.tsx'), 'utf-8');
    expect(source).not.toMatch(/from\s+['"][^'"]*EmailStep/);
    expect(source).toMatch(/SendEmailFollowOnStep/);
  });

  it('gives the composer the host handlers, the recipient search, the shared mailbox and the documents as attachments', () => {
    renderWizard();
    const step = mockStep.props!;
    // Template picker, AI draft, record pane: the host's handlers reach the composer unchanged.
    expect(step.composeHandlers).toBe(handlers);
    expect(typeof step.onSearchRecipients).toBe('function');
    // From starts on the shared mailbox (what this wizard has always sent from); plain text, subject and body pre-filled.
    expect(step.value.sendMode).toBe('sharedMailbox');
    expect(step.value.bodyFormat).toBe('PlainText');
    expect(step.value.subject).toBe('Documents (2) for review');
    expect(step.value.body).toContain('Master Agreement.pdf');
    expect(step.value.body).toContain('Side Letter.docx');
    // The documents are attachment rows: attached and linked by default (the toggles' defaults).
    expect(step.value.attachments).toEqual([
      expect.objectContaining({
        id: 'doc:doc-1',
        documentId: 'doc-1',
        fileName: 'Master Agreement.pdf',
        selected: true,
        linkSelected: true,
      }),
      expect.objectContaining({
        id: 'doc:doc-2',
        documentId: 'doc-2',
        fileName: 'Side Letter.docx',
        selected: true,
        linkSelected: true,
      }),
    ]);
  });

  it('Finish sends the chosen mailbox, To/Cc/Bcc, the documents and the record, with no archive request of its own', async () => {
    const fetchMock = okFetch();
    const onSent = jest.fn();
    renderWizard({ authenticatedFetch: fetchMock as never, onSent });
    lift({
      to: [addressed('client@acme.example')],
      cc: [addressed('partner@firm.example')],
      bcc: [addressed('records@firm.example')],
      sendMode: 'user',
    });

    let result: Awaited<ReturnType<IWizardShellProps['onFinish']>> | undefined;
    await act(async () => {
      result = await mockShell.props!.onFinish();
    });

    const body = sent(fetchMock)!;
    expect(body.to).toEqual(['client@acme.example']);
    expect(body.cc).toEqual(['partner@firm.example']);
    expect(body.bcc).toEqual(['records@firm.example']);
    expect(body.sendMode).toBe('User');
    expect(body.attachmentDocumentIds).toEqual(['doc-1', 'doc-2']);
    expect(body.associations).toEqual([expect.objectContaining({ entityType: 'sprk_matter', entityId: 'matter-1' })]);
    expect(body.subject).toBe('Documents (2) for review');
    expect(body.bodyFormat).toBe('PlainText');
    // Ticked links become the composer's "Linked documents" block, built by the host's record-link resolver.
    expect(String(body.body)).toContain('Linked documents:');
    expect(String(body.body)).toContain(
      'Master Agreement.pdf: https://org.crm.example/main.aspx?appname=sprk_MatterManagement'
    );
    expect(body.archiveToSpe).toBe(false);
    expect(onSent).toHaveBeenCalledWith('comm-1');
    expect(result?.title).toBe('Email sent');
  });

  it('sends from the shared mailbox unless the user chooses their own', async () => {
    const fetchMock = okFetch();
    renderWizard({ authenticatedFetch: fetchMock as never });
    lift({ to: [addressed('client@acme.example')] });
    await act(async () => {
      await mockShell.props!.onFinish();
    });
    expect(sent(fetchMock)!.sendMode).toBe('SharedMailbox');
  });

  it('without a parent record the request has no association', async () => {
    const fetchMock = okFetch();
    renderWizard({ authenticatedFetch: fetchMock as never, parentEntityType: undefined, parentEntityId: undefined });
    lift({ to: [addressed('client@acme.example')] });
    await act(async () => {
      await mockShell.props!.onFinish();
    });
    expect(sent(fetchMock)!.associations ?? []).toEqual([]);
  });

  describe('the step-1 toggles and Remove keep the composer rows in step', () => {
    it('"Attach files" off leaves the documents out of the request; "Send document links" off leaves the block out', async () => {
      const fetchMock = okFetch();
      renderWizard({ authenticatedFetch: fetchMock as never });
      fireEvent.click(screen.getByRole('checkbox', { name: 'Attach files' }));
      fireEvent.click(screen.getByRole('checkbox', { name: 'Send document links' }));
      expect(mockStep.props!.value.attachments.every(a => a.selected === false && a.linkSelected === false)).toBe(true);

      lift({ to: [addressed('client@acme.example')] });
      await act(async () => {
        await mockShell.props!.onFinish();
      });
      const body = sent(fetchMock)!;
      expect(body.attachmentDocumentIds ?? []).toEqual([]);
      expect(String(body.body)).not.toContain('Linked documents');
    });

    it("a toggle changes only the wizard's own rows, not a file the user added in the composer", () => {
      renderWizard();
      const mine = {
        id: 'local-1',
        source: 'local' as const,
        fileName: 'extra.pdf',
        sizeBytes: 1,
        documentId: 'doc-9',
        selected: true,
      };
      lift({ attachments: [...mockStep.props!.value.attachments, mine] });
      fireEvent.click(screen.getByRole('checkbox', { name: 'Attach files' }));
      const rows = mockStep.props!.value.attachments;
      expect(rows.find(a => a.id === 'local-1')!.selected).toBe(true);
      expect(rows.find(a => a.id === 'doc:doc-1')!.selected).toBe(false);
    });

    it('removing a document drops its row and rewrites the default subject and body', async () => {
      const fetchMock = okFetch();
      renderWizard({ authenticatedFetch: fetchMock as never });
      fireEvent.click(screen.getByRole('button', { name: 'Remove Side Letter.docx' }));

      const value = mockStep.props!.value;
      expect(value.attachments.map(a => a.documentId)).toEqual(['doc-1']);
      expect(value.subject).toBe('Document: Master Agreement.pdf');
      expect(value.body).toContain('Master Agreement.pdf');
      expect(value.body).not.toContain('Side Letter.docx');
      expect(value.bodyFormat).toBe('PlainText');

      lift({ to: [addressed('client@acme.example')] });
      await act(async () => {
        await mockShell.props!.onFinish();
      });
      expect(sent(fetchMock)!.attachmentDocumentIds).toEqual(['doc-1']);
    });

    it('removing a document keeps the subject and body the user wrote', () => {
      renderWizard();
      lift({ subject: 'Signed copies', body: 'My own words.' });
      fireEvent.click(screen.getByRole('button', { name: 'Remove Side Letter.docx' }));
      expect(mockStep.props!.value.subject).toBe('Signed copies');
      expect(mockStep.props!.value.body).toBe('My own words.');
      expect(mockStep.props!.value.attachments.map(a => a.documentId)).toEqual(['doc-1']);
    });

    it('without a client URL the link tick is off (a relative link would be dead)', () => {
      renderWizard({ clientUrl: '' });
      expect(mockStep.props!.value.attachments.every(a => a.linkSelected === false && !a.linkUrl)).toBe(true);
    });
  });

  describe('Finish stays off until the email can be sent', () => {
    it('needs a recipient, a subject, a body and valid addresses', () => {
      renderWizard();
      expect(composeStep().canAdvance()).toBe(false); // no To yet
      lift({ to: [addressed('client@acme.example')] });
      expect(composeStep().canAdvance()).toBe(true);
      lift({ subject: '  ' });
      expect(composeStep().canAdvance()).toBe(false);
      lift({ subject: 'Hello', body: '' });
      expect(composeStep().canAdvance()).toBe(false);
      lift({ body: 'Hi', cc: [addressed('not-an-address')] });
      expect(composeStep().canAdvance()).toBe(false);
    });

    it('an attachment still uploading keeps it off', () => {
      renderWizard();
      lift({ to: [addressed('client@acme.example')] });
      expect(composeStep().canAdvance()).toBe(true);
      const uploading = { id: 'local-1', source: 'local' as const, fileName: 'big.pdf', sizeBytes: 5, selected: true };
      lift({ attachments: [...mockStep.props!.value.attachments, uploading] });
      expect(composeStep().canAdvance()).toBe(false);
      lift({
        attachments: mockStep.props!.value.attachments.map(a =>
          a.id === 'local-1' ? { ...a, documentId: 'doc-9' } : a
        ),
      });
      expect(composeStep().canAdvance()).toBe(true);
    });
  });

  describe('a failed send', () => {
    it('is reported in the composer wording, with no developer text, and the email stays as it was', async () => {
      const fetchMock = jest.fn().mockResolvedValue({
        ok: false,
        status: 500,
        statusText: 'Internal Server Error',
        headers: { get: () => null },
        json: async () => ({}),
        text: async () => '',
      });
      const onSent = jest.fn();
      renderWizard({ authenticatedFetch: fetchMock as never, onSent });
      lift({ to: [addressed('client@acme.example')], subject: 'Keep me' });

      const failure = await mockShell.props!.onFinish().then(
        () => undefined,
        (err: Error) => err
      );
      expect(failure).toBeInstanceOf(Error);
      expect(failure!.message).toMatch(/^The email was not sent\./);
      expect(failure!.message).not.toMatch(/HTTP 500|sendCommunication/);
      expect(onSent).not.toHaveBeenCalled();
      expect(mockStep.props!.value.subject).toBe('Keep me');
      expect(mockStep.props!.value.to).toHaveLength(1);
    });

    it('an unconfirmed send says the email "may have been sent"', async () => {
      const fetchMock = jest.fn().mockResolvedValue({
        ok: true,
        status: 200,
        json: async () => {
          throw new Error('Unexpected end of JSON input');
        },
      });
      renderWizard({ authenticatedFetch: fetchMock as never });
      lift({ to: [addressed('client@acme.example')] });

      const failure = await mockShell.props!.onFinish().then(
        () => undefined,
        (err: Error) => err
      );
      expect(failure!.message).toMatch(/may have been sent/);
      expect(failure!.message).toMatch(/Sent Items/);
    });

    it('a document link the host cannot build refuses the send and says which document', async () => {
      const fetchMock = okFetch();
      const noLinks: IEmailComposeHandlers = { ...handlers, onResolveShareLink: jest.fn(async () => null) };
      renderWizard({ authenticatedFetch: fetchMock as never, emailComposeHandlers: noLinks });
      lift({ to: [addressed('client@acme.example')] });

      const failure = await mockShell.props!.onFinish().then(
        () => undefined,
        (err: Error) => err
      );
      expect(failure!.message).toMatch(/not sent/);
      expect(failure!.message).toContain('Master Agreement.pdf');
      expect(sent(fetchMock)).toBeUndefined();
    });
  });

  describe('a host with no authenticated fetch', () => {
    it('shows a visible warning, cannot finish and sends nothing', async () => {
      renderWizard({ authenticatedFetch: undefined as never });
      expect(screen.getByTestId('email-service-unavailable')).toHaveTextContent(
        /no connection to the Spaarke email service/
      );
      lift({ to: [addressed('client@acme.example')] });
      expect(composeStep().canAdvance()).toBe(false);
      await expect(mockShell.props!.onFinish()).rejects.toThrow(/no connection to the Spaarke email service/);
    });
  });

  it('a host that passes no handlers still renders the step (the composer hides what it cannot do)', () => {
    renderWizard({ emailComposeHandlers: undefined });
    expect(screen.getByTestId('compose-step')).toBeInTheDocument();
    expect(mockStep.props!.composeHandlers).toBeUndefined();
  });
});
