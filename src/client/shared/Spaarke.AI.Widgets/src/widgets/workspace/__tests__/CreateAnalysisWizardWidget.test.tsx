/**
 * CreateAnalysisWizardWidget — unit tests
 *
 * Exercises the real `CreateRecordWizard` shell (not mocked — a small, already-tested
 * shared component) end-to-end. As of the 2026-07-30 UAT rework the step sequence is
 * FOUR steps (Associate To is now a dedicated first step, files are REQUIRED):
 *
 *   1. Associate To     (built-in `config.associateToStep`; skip-able)
 *   2. Add file(s)       (REQUIRED — `config.requireFilesStep`; no Skip, Next gated on
 *                         an uploaded file OR a picked existing Document)
 *   3. Analysis Details  (name + description + Assigned Attorney/Paralegal contact lookups;
 *                         'Access' removed)
 *   4. Next Steps        (follow-on cards)
 *
 * Covered:
 *   1. The four steps render in order.
 *   2. Add file(s) is REQUIRED — Next is disabled with no document (negative).
 *   3. Finish creates a valid `sprk_analysis` and loads the file to a workspace tab.
 *   4. Associate-to (step 1): the create names the chosen parent as the BFF `mappingSource`; the payload holds only
 *      what the user entered (field-mapping-server-write-path-r1 task 028, FR-16). No client mapping request.
 *   5. Send Email next-step: the request carries the user-typed subject/body as they are (task 028, FR-17).
 *   6. The server's create warnings reach the success panel.
 *   7. ADR-021: renders under both light and dark Fluent themes.
 *
 * Everything (CreateRecordWizard, AssociateToStep, WizardFollowOns, the BFF create adapter) is the REAL shared-lib impl;
 * only the BFF itself is faked, at the `authenticatedFetch` boundary.
 */

import '@testing-library/jest-dom';
import * as React from 'react';
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { FluentProvider, webDarkTheme, webLightTheme } from '@fluentui/react-components';

// jsdom does not implement ResizeObserver; Fluent's MessageBar uses it for reflow.
class ResizeObserverStub {
  observe() {}
  unobserve() {}
  disconnect() {}
}
(global as unknown as { ResizeObserver: typeof ResizeObserverStub }).ResizeObserver = ResizeObserverStub;

import type { IDataService, INavigationService, LookupResult } from '@spaarke/ui-components';

import CreateAnalysisWizardWidget from '../CreateAnalysisWizardWidget';
import type { CreateAnalysisWizardData } from '../CreateAnalysisWizardWidget';
import { useDispatchPaneEvent } from '../../../events/useDispatchPaneEvent';
import { ApiError } from '../../../../../Spaarke.Auth/src/errors';
import type { OkResponse } from '../../../../../Spaarke.Auth/src/types';

jest.mock('../../../events/useDispatchPaneEvent');

// ---------------------------------------------------------------------------
// Test doubles
// ---------------------------------------------------------------------------

function buildDataService(): IDataService {
  return {
    createRecord: jest.fn().mockResolvedValue('new-analysis-id'),
    retrieveRecord: jest.fn().mockResolvedValue({}),
    retrieveMultipleRecords: jest.fn().mockResolvedValue({ entities: [] }),
    updateRecord: jest.fn().mockResolvedValue(undefined),
    deleteRecord: jest.fn().mockResolvedValue(undefined),
  };
}

function buildNavigationService(lookupResult: LookupResult[] = []): INavigationService {
  return {
    openRecord: jest.fn().mockResolvedValue(undefined),
    openDialog: jest.fn().mockResolvedValue({ confirmed: true }),
    closeDialog: jest.fn(),
    openLookup: jest.fn().mockResolvedValue(lookupResult),
  };
}

/**
 * UAC-r2 task 147 r1: the analysis (and its follow-on to-do) is created through the BFF's
 * `POST /api/v1/child-records/{table}`, not through `dataService.createRecord`. This fake BFF answers that route by
 * handing the SAME payload to the mock data service (so the payload assertions below still describe what reaches the
 * server) and passes every other request to the suite's own fetch.
 */
/** The tables created through the fake BFF route, in order (reset per test). */
const childRecordPosts: string[] = [];
/** The full URL of each create (query string included), in order (reset per test). */
const childRecordUrls: string[] = [];
/** Every URL the widget asked the fake BFF for, in order (reset per test). */
const requestedUrls: string[] = [];
/** The `warnings` the fake BFF answers each create with (reset per test). */
let childRecordWarnings: string[] | undefined;
beforeEach(() => {
  childRecordPosts.length = 0;
  childRecordUrls.length = 0;
  requestedUrls.length = 0;
  childRecordWarnings = undefined;
});

function routeChildRecords(
  dataService: IDataService,
  inner: CreateAnalysisWizardData['authenticatedFetch']
): CreateAnalysisWizardData['authenticatedFetch'] {
  return (async (input: RequestInfo, init?: RequestInit) => {
    const url = String(input);
    requestedUrls.push(url);
    const create = /\/api\/v1\/child-records\/([a-z_]+)(\?.*)?$/.exec(url);
    if (create && init?.method === 'POST') {
      childRecordPosts.push(create[1]);
      childRecordUrls.push(url);
      const id = await dataService.createRecord(create[1], JSON.parse(String(init.body)));
      return {
        ok: true,
        status: 201,
        json: async () => ({ id, ...(childRecordWarnings ? { warnings: childRecordWarnings } : {}) }),
        text: async () => '',
      } as unknown as Response;
    }
    return inner(input as string, init);
  }) as CreateAnalysisWizardData['authenticatedFetch'];
}

function buildData(overrides: Partial<CreateAnalysisWizardData> = {}): CreateAnalysisWizardData {
  const dataService = overrides.dataService ?? buildDataService();
  const authenticatedFetch =
    overrides.authenticatedFetch ??
    jest.fn().mockResolvedValue({ ok: true, json: async () => ({ documentIds: ['uploaded-doc-id'] }) });
  return {
    wizardId: 'create-analysis-test',
    bffBaseUrl: 'https://bff.example.com',
    navigationService: buildNavigationService([
      { id: 'existing-doc-id', name: 'MSA Draft', entityType: 'sprk_document' },
    ]),
    searchUsers: jest.fn().mockResolvedValue([]),
    ...overrides,
    dataService,
    authenticatedFetch: routeChildRecords(dataService, authenticatedFetch),
  };
}

function renderWidget(data: CreateAnalysisWizardData, theme: 'light' | 'dark' = 'light') {
  return render(
    <FluentProvider theme={theme === 'dark' ? webDarkTheme : webLightTheme}>
      <CreateAnalysisWizardWidget data={data} widgetType="create-analysis-wizard" />
    </FluentProvider>
  );
}

/** Step 1 "Associate To" is skip-able — bypass it via the footer Skip button. */
function skipAssociateTo() {
  fireEvent.click(screen.getByRole('button', { name: 'Skip' }));
}

/** On the "Add file(s)" step, pick an existing Document (satisfies the required-file gate). */
async function selectExistingDocument() {
  const button = await screen.findByTestId('select-existing-record-button');
  await act(async () => {
    fireEvent.click(button);
    await Promise.resolve();
  });
  await screen.findByTestId('selected-existing-record');
}

function clickPrimary(label: 'Next' | 'Finish') {
  fireEvent.click(screen.getByRole('button', { name: label }));
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('CreateAnalysisWizardWidget', () => {
  beforeEach(() => {
    (useDispatchPaneEvent as jest.Mock).mockReturnValue(jest.fn());
  });

  it('renders the four-step flow: Associate To -> Add file(s) -> Analysis Details -> Next Steps', () => {
    renderWidget(buildData());

    // All four steps appear in the stepper sidebar.
    expect(screen.getAllByText('Associate To').length).toBeGreaterThan(0);
    expect(screen.getAllByText('Add file(s)').length).toBeGreaterThan(0);
    expect(screen.getAllByText('Analysis Details').length).toBeGreaterThan(0);
    expect(screen.getAllByText('Next Steps').length).toBeGreaterThan(0);
  });

  it('requires a document: Add file(s) Next is disabled and offers no Skip until a doc is provided (negative)', () => {
    renderWidget(buildData());

    // Step 1: skip Associate To to reach the required Add file(s) step.
    skipAssociateTo();

    // Add file(s) is required — Next disabled, and there is no Skip button here.
    const nextButton = screen.getByRole('button', { name: 'Next' });
    expect(nextButton).toBeDisabled();
    // The only remaining Skip on the page would be from a skippable step — none here.
    expect(screen.queryByRole('button', { name: 'Skip' })).not.toBeInTheDocument();
    // The existing-document picker is available to satisfy the requirement.
    expect(screen.getByTestId('select-existing-record-button')).toBeInTheDocument();
  });

  it('on finish, creates a valid sprk_analysis (name/description/work-type) and loads the file to a workspace tab', async () => {
    const dispatchMock = jest.fn();
    (useDispatchPaneEvent as jest.Mock).mockReturnValue(dispatchMock);

    const dataService = buildDataService();
    const navigationService = buildNavigationService([
      { id: 'existing-doc-id', name: 'MSA Draft', entityType: 'sprk_document' },
    ]);
    const data = buildData({
      dataService,
      navigationService,
      workTypeValue: 100000000,
      workTypeLabel: 'Agreement Review',
    });

    renderWidget(data);

    // Step 1: skip Associate To.
    skipAssociateTo();
    // Step 2: select existing document.
    await selectExistingDocument();
    clickPrimary('Next');

    // Step 3: name + description.
    fireEvent.change(screen.getByLabelText('Analysis name'), { target: { value: 'MSA Review — Acme Corp' } });
    fireEvent.change(screen.getByLabelText('Analysis description'), { target: { value: 'Reviewing the MSA.' } });
    clickPrimary('Next');

    // Step 4: finish with no follow-on actions.
    await act(async () => {
      clickPrimary('Finish');
      await Promise.resolve();
      await Promise.resolve();
    });

    await waitFor(() => expect(dataService.createRecord).toHaveBeenCalled());
    // UAC-r2 task 147 r1: the analysis left through the BFF child-record route, not Xrm.WebApi.
    expect(childRecordPosts).toEqual(['sprk_analysis']);
    const [entityName, payload] = (dataService.createRecord as jest.Mock).mock.calls[0];
    expect(entityName).toBe('sprk_analysis');
    expect(payload).toMatchObject({
      sprk_name: 'MSA Review — Acme Corp',
      sprk_description: 'Reviewing the MSA.',
      sprk_worktype: 100000000,
      sprk_analysisstatus: 1,
      'sprk_documentid@odata.bind': '/sprk_documents(existing-doc-id)',
    });

    // File loaded to a workspace tab via the existing document-viewer widget.
    await waitFor(() =>
      expect(dispatchMock).toHaveBeenCalledWith(
        'workspace',
        expect.objectContaining({
          type: 'widget_load',
          widgetType: 'document-viewer',
          widgetData: expect.objectContaining({ documentId: 'existing-doc-id' }),
        })
      )
    );
  });

  it('on finish, binds the selected agreement type (sprk_agreementtype lookup) into the create payload', async () => {
    const dispatchMock = jest.fn();
    (useDispatchPaneEvent as jest.Mock).mockReturnValue(dispatchMock);

    const dataService = buildDataService();
    // The registry load returns one selectable row; `defaultSubDomain='nda'` auto-selects it.
    (dataService.retrieveMultipleRecords as jest.Mock).mockImplementation(async (entityName: string) =>
      entityName === 'sprk_agreementtype'
        ? {
            entities: [
              {
                sprk_agreementtypeid: 'nda-type-id',
                sprk_key: 'nda',
                sprk_name: 'NDA / Confidentiality',
                sprk_isfallback: false,
              },
            ],
          }
        : { entities: [] }
    );
    const navigationService = buildNavigationService([
      { id: 'existing-doc-id', name: 'MSA Draft', entityType: 'sprk_document' },
    ]);
    const data = buildData({ dataService, navigationService, defaultSubDomain: 'nda' });

    renderWidget(data);

    skipAssociateTo();
    await selectExistingDocument();
    clickPrimary('Next');
    fireEvent.change(screen.getByLabelText('Analysis name'), { target: { value: 'NDA Review' } });
    clickPrimary('Next');
    await act(async () => {
      clickPrimary('Finish');
      await Promise.resolve();
      await Promise.resolve();
    });

    await waitFor(() => expect(dataService.createRecord).toHaveBeenCalled());
    const [, payload] = (dataService.createRecord as jest.Mock).mock.calls[0];
    // The lookup is bound via the discovered nav-prop, or the PascalCase fallback
    // (`sprk_AgreementType`) when metadata discovery is unavailable in jsdom. Assert on
    // the target set/id so the test is robust to which nav-prop name resolves.
    const bindValues = Object.entries(payload)
      .filter(([k]) => k.endsWith('@odata.bind'))
      .map(([, v]) => v);
    expect(bindValues).toContain('/sprk_agreementtypes(nda-type-id)');
  });

  // -------------------------------------------------------------------------
  // field-mapping-server-write-path-r1 task 028 (FR-16, FR-17): the client maps nothing.
  // -------------------------------------------------------------------------
  describe('task 028: server-side field mapping (FR-16, FR-17)', () => {
    /** Every key a create may carry: what the user entered or picked, the document bind, the wizard's own run status, and the regarding keys. */
    const USER_AND_ROUTE_KEYS = [
      'sprk_name',
      'sprk_description',
      'sprk_worktype',
      'sprk_analysisstatus',
      'sprk_documentid@odata.bind',
    ];

    function mattersAndDocumentNav(): INavigationService {
      return {
        openRecord: jest.fn().mockResolvedValue(undefined),
        openDialog: jest.fn().mockResolvedValue({ confirmed: true }),
        closeDialog: jest.fn(),
        openLookup: jest
          .fn()
          .mockResolvedValueOnce([{ id: 'matter-id', name: 'Smith v. Jones', entityType: 'sprk_matter' }])
          .mockResolvedValueOnce([{ id: 'existing-doc-id', name: 'MSA Draft', entityType: 'sprk_document' }]),
      };
    }

    /** Step 1: pick the regarding matter; step 2: the document; step 3: name + description; step 4: Finish. */
    async function driveAssociatedToFinish(extraOnDetailsStep?: () => void) {
      const selectRecordButton = screen.getByTestId('associate-to-step-select-record-button');
      await act(async () => {
        fireEvent.click(selectRecordButton);
        await Promise.resolve();
      });
      await screen.findByText('Smith v. Jones');
      clickPrimary('Next');
      await selectExistingDocument();
      clickPrimary('Next');
      fireEvent.change(screen.getByLabelText('Analysis name'), { target: { value: 'MSA Review' } });
      fireEvent.change(screen.getByLabelText('Analysis description'), { target: { value: 'Reviewing the MSA.' } });
      extraOnDetailsStep?.();
      clickPrimary('Next');
    }

    async function finish() {
      await act(async () => {
        clickPrimary('Finish');
        for (let i = 0; i < 8; i++) await Promise.resolve();
      });
    }

    it('with an association: creates through the BFF route naming the parent as the mapping source; the payload holds only what the user entered', async () => {
      const dataService = buildDataService();
      renderWidget(buildData({ dataService, navigationService: mattersAndDocumentNav(), workTypeValue: 100000001 }));

      await driveAssociatedToFinish();
      await finish();

      await waitFor(() => expect(dataService.createRecord).toHaveBeenCalled());
      // The parent is named as the mapping source on the create's own URL.
      expect(childRecordUrls).toEqual([
        'https://bff.example.com/api/v1/child-records/sprk_analysis?mappingSourceEntity=sprk_matter&mappingSourceId=matter-id',
      ]);
      // No mapped, default or owner value: the user's values, the document bind, the wizard's run status, the regarding keys.
      const [, payload] = (dataService.createRecord as jest.Mock).mock.calls[0] as [string, Record<string, unknown>];
      expect(payload).toMatchObject({
        sprk_name: 'MSA Review',
        sprk_description: 'Reviewing the MSA.',
        sprk_worktype: 100000001,
        sprk_analysisstatus: 1,
        'sprk_documentid@odata.bind': '/sprk_documents(existing-doc-id)',
      });
      const extraKeys = Object.keys(payload).filter(k => !USER_AND_ROUTE_KEYS.includes(k));
      expect(extraKeys.every(k => /regard/i.test(k))).toBe(true);
      expect(Object.keys(payload).some(k => /owner|^sprk_searchindex|AI_Search_Index|subject/i.test(k))).toBe(false);
      // The client makes no mapping request of its own.
      expect(requestedUrls.filter(u => u.includes('/field-mappings'))).toEqual([]);
    });

    it('without an association: creates on the plain route and names no mapping source', async () => {
      const dataService = buildDataService();
      renderWidget(buildData({ dataService }));

      skipAssociateTo();
      await selectExistingDocument();
      clickPrimary('Next');
      fireEvent.change(screen.getByLabelText('Analysis name'), { target: { value: 'Standalone' } });
      clickPrimary('Next');
      await finish();

      await waitFor(() => expect(dataService.createRecord).toHaveBeenCalled());
      expect(childRecordUrls).toEqual(['https://bff.example.com/api/v1/child-records/sprk_analysis']);
      const [, payload] = (dataService.createRecord as jest.Mock).mock.calls[0] as [string, Record<string, unknown>];
      expect(Object.keys(payload).sort()).toEqual([...USER_AND_ROUTE_KEYS].sort());
      expect(requestedUrls.filter(u => u.includes('/field-mappings'))).toEqual([]);
    });

    it('shows the warnings the server sends with the create on the success panel', async () => {
      childRecordWarnings = ['The related record is not available to you, so no fields were copied from it.'];
      renderWidget(buildData({ navigationService: mattersAndDocumentNav() }));

      await driveAssociatedToFinish();
      await finish();

      expect(
        await screen.findByText('The related record is not available to you, so no fields were copied from it.')
      ).toBeInTheDocument();
      expect(await screen.findByText('Analysis created with warnings')).toBeInTheDocument();
    });

    it('Send Email next-step: the request carries the user-typed subject and body as they are, with the analysis as its association', async () => {
      const dataService = buildDataService();
      const authenticatedFetch = jest
        .fn()
        .mockResolvedValue({ ok: true, json: async () => ({ documentIds: ['uploaded-doc-id'] }) });
      const searchUsers = jest
        .fn()
        .mockResolvedValue([{ id: 'user-1', name: 'Jordan Lee', email: 'jordan.lee@firm.example' }]);
      const data = buildData({ dataService, authenticatedFetch, searchUsers });

      renderWidget(data);

      // Step 1: skip Associate To. Step 2: existing document. Step 3: name.
      skipAssociateTo();
      await selectExistingDocument();
      clickPrimary('Next');
      fireEvent.change(screen.getByLabelText('Analysis name'), { target: { value: 'MSA Review' } });
      clickPrimary('Next');

      // Step 4: select the Send Email follow-on card, then advance into its step.
      fireEvent.click(screen.getByRole('checkbox', { name: /Send Notification Email/i }));
      clickPrimary('Next');

      // Send Email step (task 182: the shared EmailComposer, compose-only): pick a "To" user via the recipient search.
      const toInput = await screen.findByRole('textbox', { name: 'To' });
      fireEvent.change(toInput, { target: { value: 'jo' } });
      fireEvent.click(await screen.findByText(/Jordan Lee/));

      fireEvent.change(screen.getByRole('textbox', { name: 'Subject' }), { target: { value: 'User-typed subject' } });
      fireEvent.click(screen.getByRole('button', { name: 'Switch to plain text' }));
      fireEvent.change(await screen.findByRole('textbox', { name: 'Message body' }), {
        target: { value: 'User-typed body' },
      });

      await finish();

      await waitFor(() => expect(dataService.createRecord).toHaveBeenCalled());
      await waitFor(() =>
        expect(authenticatedFetch).toHaveBeenCalledWith(
          expect.stringContaining('/api/communications/send'),
          expect.objectContaining({ method: 'POST' })
        )
      );
      const sendCall = authenticatedFetch.mock.calls.find(([url]: [string]) =>
        url.includes('/api/communications/send')
      );
      const sentBody = JSON.parse(sendCall[1].body as string);
      // Exactly what was typed; the send path fills an EMPTY one server-side and never replaces a typed one.
      expect(sentBody.subject).toBe('User-typed subject');
      expect(sentBody.body).toBe('User-typed body');
      expect(sentBody.bodyFormat).toBe('PlainText');
      // Task 182: sent through the composer's request path - To from the composer, filed under the analysis.
      expect(sentBody.to).toEqual(['jordan.lee@firm.example']);
      expect(sentBody.associations).toEqual([expect.objectContaining({ entityType: 'sprk_analysis' })]);
      // The client computes no mapped text: no mapping request at all.
      const allUrls = [...requestedUrls, ...authenticatedFetch.mock.calls.map(([url]: [string]) => String(url))];
      expect(allUrls.filter(u => u.includes('/field-mappings'))).toEqual([]);
    });

    it('Send Email next-step: an empty subject and body do not block Finish and are sent empty, for the server to fill (owner 2026-11-25)', async () => {
      const dataService = buildDataService();
      const authenticatedFetch = jest
        .fn()
        .mockResolvedValue({ ok: true, json: async () => ({ documentIds: ['uploaded-doc-id'] }) });
      const searchUsers = jest
        .fn()
        .mockResolvedValue([{ id: 'user-1', name: 'Jordan Lee', email: 'jordan.lee@firm.example' }]);
      renderWidget(buildData({ dataService, authenticatedFetch, searchUsers }));

      skipAssociateTo();
      await selectExistingDocument();
      clickPrimary('Next');
      fireEvent.change(screen.getByLabelText('Analysis name'), { target: { value: 'MSA Review' } });
      clickPrimary('Next');
      fireEvent.click(screen.getByRole('checkbox', { name: /Send Notification Email/i }));
      clickPrimary('Next');

      // A recipient only: no subject, no body.
      const toInput = await screen.findByRole('textbox', { name: 'To' });
      fireEvent.change(toInput, { target: { value: 'jo' } });
      fireEvent.click(await screen.findByText(/Jordan Lee/));

      await waitFor(() => expect(screen.getByRole('button', { name: 'Finish' })).toBeEnabled());
      await finish();

      await waitFor(() =>
        expect(authenticatedFetch).toHaveBeenCalledWith(
          expect.stringContaining('/api/communications/send'),
          expect.objectContaining({ method: 'POST' })
        )
      );
      const sendCall = authenticatedFetch.mock.calls.find(([url]: [string]) =>
        url.includes('/api/communications/send')
      );
      const sentBody = JSON.parse(sendCall[1].body as string);
      expect(String(sentBody.subject ?? '').trim()).toBe('');
      expect(sentBody.associations).toEqual([expect.objectContaining({ entityType: 'sprk_analysis' })]);
    });

    it('Send Email next-step: with no recipient, Finish stays disabled (negative)', async () => {
      const dataService = buildDataService();
      const authenticatedFetch = jest
        .fn()
        .mockResolvedValue({ ok: true, json: async () => ({ documentIds: ['uploaded-doc-id'] }) });
      renderWidget(buildData({ dataService, authenticatedFetch }));

      skipAssociateTo();
      await selectExistingDocument();
      clickPrimary('Next');
      fireEvent.change(screen.getByLabelText('Analysis name'), { target: { value: 'MSA Review' } });
      clickPrimary('Next');
      fireEvent.click(screen.getByRole('checkbox', { name: /Send Notification Email/i }));
      clickPrimary('Next');

      await screen.findByRole('textbox', { name: 'To' });
      fireEvent.change(screen.getByRole('textbox', { name: 'Subject' }), { target: { value: 'A subject' } });
      expect(screen.getByRole('button', { name: 'Finish' })).toBeDisabled();
    });
  });

  it('Create To Do follow-on: Assigned To opens the Dataverse side pane over active contacts (ontology 180, D-219)', async () => {
    const navigationService = buildNavigationService([
      { id: 'existing-doc-id', name: 'MSA Draft', entityType: 'sprk_document' },
    ]);
    renderWidget(buildData({ navigationService }));

    skipAssociateTo();
    await selectExistingDocument();
    clickPrimary('Next');
    fireEvent.change(screen.getByLabelText('Analysis name'), { target: { value: 'MSA Review' } });
    clickPrimary('Next');
    fireEvent.click(screen.getByRole('checkbox', { name: /Create To Do/i }));
    clickPrimary('Next');

    // A side-pane field: a Select button, no inline contact search.
    const select = await screen.findByRole('button', { name: 'Select Assigned To' });
    expect(screen.queryByPlaceholderText('Search contacts...')).not.toBeInTheDocument();

    (navigationService.openLookup as jest.Mock).mockClear();
    (navigationService.openLookup as jest.Mock).mockResolvedValue([
      { id: 'contact-1', name: 'Alex Contact', entityType: 'contact' },
    ]);
    fireEvent.click(select);

    await screen.findByRole('button', { name: 'Clear Assigned To' });
    expect(navigationService.openLookup).toHaveBeenCalledWith(
      expect.objectContaining({
        entityType: 'contact',
        filters: [expect.objectContaining({ entityLogicalName: 'contact' })],
      })
    );
  });

  it('ADR-021: renders correctly under both light and dark Fluent themes', () => {
    const { unmount } = renderWidget(buildData(), 'light');
    expect(screen.getAllByText('Associate To').length).toBeGreaterThan(0);
    unmount();

    renderWidget(buildData(), 'dark');
    expect(screen.getAllByText('Associate To').length).toBeGreaterThan(0);
  });

  // -------------------------------------------------------------------------
  // task 033 (FR-17) — wizard→review auto-run hand-off: the Analysis-OWNED
  // compose-session mint (path B-i: HostContext sentinel → create-time durable
  // sprk_analysis FK) + the compose-seed hand-off fields.
  // -------------------------------------------------------------------------
  describe('task 033 (FR-17): Analysis-owned session mint + auto-run hand-off', () => {
    /** dataService whose existing-document pick resolves a FULL SPE pointer (the compose door). */
    function buildSpeDataService(): IDataService {
      const dataService = buildDataService();
      (dataService.retrieveRecord as jest.Mock).mockResolvedValue({
        sprk_filename: 'MSA Draft.docx',
        sprk_graphitemid: 'spe-item-1',
        sprk_graphdriveid: 'spe-drive-1',
      });
      // The agreement-type registry (one selectable row; `defaultSubDomain` auto-selects it).
      (dataService.retrieveMultipleRecords as jest.Mock).mockImplementation(async (entityName: string) =>
        entityName === 'sprk_agreementtype'
          ? {
              entities: [
                {
                  sprk_agreementtypeid: 'nda-type-id',
                  sprk_key: 'nda',
                  sprk_name: 'NDA / Confidentiality',
                  sprk_isfallback: false,
                },
              ],
            }
          : { entities: [] }
      );
      return dataService;
    }

    /** Routed authenticatedFetch: session create → configurable; everything else generic-ok. */
    function buildRoutedAuthFetch(sessionCreate: { ok: boolean; sessionId?: string }) {
      return jest.fn(async (input: string, init?: RequestInit) => {
        const url = String(input);
        if (url.endsWith('/api/ai/chat/sessions') && init?.method === 'POST') {
          // authenticatedFetch THROWS ApiError for a non-2xx; it never resolves { ok: false }.
          if (!sessionCreate.ok) throw new ApiError('HTTP 500', 500, null);
          return {
            ok: true,
            status: 201,
            json: async () => ({ sessionId: sessionCreate.sessionId }),
          } as unknown as OkResponse;
        }
        return { ok: true, json: async () => ({}) } as unknown as OkResponse;
      });
    }

    async function driveToFinish(name: string) {
      skipAssociateTo();
      await selectExistingDocument();
      clickPrimary('Next');
      fireEvent.change(screen.getByLabelText('Analysis name'), { target: { value: name } });
      clickPrimary('Next');
      await act(async () => {
        clickPrimary('Finish');
        for (let i = 0; i < 8; i++) await Promise.resolve();
      });
    }

    it('mints the ANALYSIS-OWNED session (documentId anchor + HostContext sentinel) and hands off composeSessionId/analysisId/subDomain/autoRunReview on the compose seed', async () => {
      const dispatchMock = jest.fn();
      (useDispatchPaneEvent as jest.Mock).mockReturnValue(dispatchMock);
      const authenticatedFetch = buildRoutedAuthFetch({ ok: true, sessionId: 'wizard-session-1' });
      const data = buildData({
        dataService: buildSpeDataService(),
        authenticatedFetch,
        workTypeValue: 100000000, // Agreement Review — the FR-17 flow
        workTypeLabel: 'Agreement Review',
        defaultSubDomain: 'nda',
      });

      renderWidget(data);
      await driveToFinish('FR-17 Handoff');

      // (1) BIND (wire-level HostContext assert): ONE session-create POST, document-anchored,
      // carrying the ANALYSIS-OWNED sentinel with the created sprk_analysis id — the create-time
      // durable-FK trigger (ChatDataverseRepository.CreateSessionAsync). Sentinel value comes from
      // the ONE client constant site (ANALYSIS_SESSION_HOST_ENTITY_TYPE), asserted literally here.
      const sessionCalls = authenticatedFetch.mock.calls.filter(
        ([url, init]: [RequestInfo, RequestInit?]) =>
          String(url).endsWith('/api/ai/chat/sessions') && init?.method === 'POST'
      );
      expect(sessionCalls).toHaveLength(1);
      const sessionBody = JSON.parse(sessionCalls[0][1]!.body as string);
      expect(sessionBody).toEqual({
        documentId: 'existing-doc-id',
        hostContext: {
          entityType: 'sprk_analysisoutput',
          entityId: 'new-analysis-id',
          entityName: 'FR-17 Handoff',
        },
      });

      // (2) HAND-OFF: the compose seed carries the session + analysis + sub-domain + auto-run arm
      // + (task 070) the default review depth (Thorough — untouched radio).
      await waitFor(() =>
        expect(dispatchMock).toHaveBeenCalledWith(
          'workspace',
          expect.objectContaining({
            type: 'widget_load',
            widgetType: 'compose',
            widgetData: expect.objectContaining({
              compose: expect.objectContaining({
                speDriveItemId: 'spe-item-1',
                speDriveId: 'spe-drive-1',
                activeWorkType: 'agreement-analysis',
                subDomain: 'nda',
                composeSessionId: 'wizard-session-1',
                analysisId: 'new-analysis-id',
                autoRunReview: true,
                reviewDepth: 'thorough',
              }),
            }),
          })
        )
      );
    });

    // task 070 (UAT2 review-depth selector): the "Analysis Details" step now carries a small,
    // additive Review Depth radio group alongside the Agreement Type picker.
    it('task 070: picking Quick on the Analysis Details step hands off reviewDepth:"quick" on the compose seed', async () => {
      const dispatchMock = jest.fn();
      (useDispatchPaneEvent as jest.Mock).mockReturnValue(dispatchMock);
      const authenticatedFetch = buildRoutedAuthFetch({ ok: true, sessionId: 'wizard-session-depth-quick' });
      const data = buildData({
        dataService: buildSpeDataService(),
        authenticatedFetch,
        workTypeValue: 100000000, // Agreement Review
        workTypeLabel: 'Agreement Review',
        defaultSubDomain: 'nda',
      });

      renderWidget(data);
      skipAssociateTo();
      await selectExistingDocument();
      clickPrimary('Next');
      fireEvent.change(screen.getByLabelText('Analysis name'), { target: { value: 'FR-17 Depth Quick' } });
      fireEvent.click(screen.getByLabelText('Quick (~20 sec)'));
      clickPrimary('Next');
      await act(async () => {
        clickPrimary('Finish');
        for (let i = 0; i < 8; i++) await Promise.resolve();
      });

      await waitFor(() =>
        expect(dispatchMock).toHaveBeenCalledWith(
          'workspace',
          expect.objectContaining({
            widgetData: expect.objectContaining({
              compose: expect.objectContaining({ reviewDepth: 'quick' }),
            }),
          })
        )
      );
    });

    it('non-agreement work type: NO session mint and NO hand-off fields on the compose seed (negative)', async () => {
      const dispatchMock = jest.fn();
      (useDispatchPaneEvent as jest.Mock).mockReturnValue(dispatchMock);
      const authenticatedFetch = buildRoutedAuthFetch({ ok: true, sessionId: 'should-never-mint' });
      const data = buildData({
        dataService: buildSpeDataService(),
        authenticatedFetch,
        workTypeValue: 100000001, // NOT Agreement Review
        workTypeLabel: 'Legal Research',
      });

      renderWidget(data);
      await driveToFinish('Non-Agreement Analysis');

      // No Analysis-owned session mint for a non-agreement analysis.
      const sessionCalls = authenticatedFetch.mock.calls.filter(
        ([url, init]: [RequestInfo, RequestInit?]) =>
          String(url).endsWith('/api/ai/chat/sessions') && init?.method === 'POST'
      );
      expect(sessionCalls).toHaveLength(0);

      // Compose still opens (SPE pointer present) with NONE of the hand-off fields.
      await waitFor(() =>
        expect(dispatchMock).toHaveBeenCalledWith(
          'workspace',
          expect.objectContaining({ type: 'widget_load', widgetType: 'compose' })
        )
      );
      const composeEvent = dispatchMock.mock.calls.find(
        ([, evt]: [string, { widgetType?: string }]) => evt.widgetType === 'compose'
      )![1] as { widgetData: { compose: Record<string, unknown> } };
      expect(composeEvent.widgetData.compose.composeSessionId).toBeUndefined();
      expect(composeEvent.widgetData.compose.analysisId).toBeUndefined();
      expect(composeEvent.widgetData.compose.autoRunReview).toBeUndefined();
    });

    it('session-mint failure: wizard still succeeds, Compose still opens WITHOUT the hand-off, and the distinct bind-failure warning surfaces (ADR-019)', async () => {
      const dispatchMock = jest.fn();
      (useDispatchPaneEvent as jest.Mock).mockReturnValue(dispatchMock);
      const authenticatedFetch = buildRoutedAuthFetch({ ok: false });
      const data = buildData({
        dataService: buildSpeDataService(),
        authenticatedFetch,
        workTypeValue: 100000000,
        workTypeLabel: 'Agreement Review',
        defaultSubDomain: 'nda',
      });

      renderWidget(data);
      await driveToFinish('Bind Failure Case');

      // The mint was ATTEMPTED (no silent skip) …
      const sessionCalls = authenticatedFetch.mock.calls.filter(
        ([url, init]: [RequestInfo, RequestInit?]) =>
          String(url).endsWith('/api/ai/chat/sessions') && init?.method === 'POST'
      );
      expect(sessionCalls).toHaveLength(1);

      // … Compose STILL opens (shipped behavior — the failure is non-fatal) with NO hand-off
      // fields (no half-armed auto-run pointing at a session that does not exist) …
      await waitFor(() =>
        expect(dispatchMock).toHaveBeenCalledWith(
          'workspace',
          expect.objectContaining({ type: 'widget_load', widgetType: 'compose' })
        )
      );
      const composeEvent = dispatchMock.mock.calls.find(
        ([, evt]: [string, { widgetType?: string }]) => evt.widgetType === 'compose'
      )![1] as { widgetData: { compose: Record<string, unknown> } };
      expect(composeEvent.widgetData.compose.composeSessionId).toBeUndefined();
      expect(composeEvent.widgetData.compose.autoRunReview).toBeUndefined();

      // … and the DISTINCT bind-failure warning is surfaced on the wizard success panel.
      expect(await screen.findByText(/The review could not be started automatically/)).toBeInTheDocument();
    });
  });
});
