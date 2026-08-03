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
 *   4. Associate-to (step 1) is Field-Mapping-driven (`applyFieldMappings` parent→analysis).
 *   5. Send Email next-step is Field-Mapping-driven (mapped subject/body win).
 *   6. ADR-021: renders under both light and dark Fluent themes.
 *
 * `applyFieldMappings` is mocked at the module boundary (BFF call); everything else
 * (CreateRecordWizard, AssociateToStep, WizardFollowOns) is the REAL shared-lib impl.
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

const mockApplyFieldMappings = jest.fn().mockResolvedValue({ profileFound: false, fieldsMapped: [], warnings: [] });

jest.mock('@spaarke/ui-components', () => {
  const actual = jest.requireActual('@spaarke/ui-components');
  return {
    ...actual,
    applyFieldMappings: (...args: unknown[]) => mockApplyFieldMappings(...args),
  };
});

import CreateAnalysisWizardWidget from '../CreateAnalysisWizardWidget';
import type { CreateAnalysisWizardData } from '../CreateAnalysisWizardWidget';
import { useDispatchPaneEvent } from '../../../events/useDispatchPaneEvent';

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

function buildData(overrides: Partial<CreateAnalysisWizardData> = {}): CreateAnalysisWizardData {
  return {
    wizardId: 'create-analysis-test',
    bffBaseUrl: 'https://bff.example.com',
    authenticatedFetch: jest
      .fn()
      .mockResolvedValue({ ok: true, json: async () => ({ documentIds: ['uploaded-doc-id'] }) }),
    dataService: buildDataService(),
    navigationService: buildNavigationService([
      { id: 'existing-doc-id', name: 'MSA Draft', entityType: 'sprk_document' },
    ]),
    searchUsers: jest.fn().mockResolvedValue([]),
    ...overrides,
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
    mockApplyFieldMappings.mockClear();
    mockApplyFieldMappings.mockResolvedValue({ profileFound: false, fieldsMapped: [], warnings: [] });
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

  it('is Field-Mapping-driven for associate-to: applyFieldMappings runs parent -> sprk_analysis when a regarding record is picked in step 1', async () => {
    const dataService = buildDataService();
    // Two openLookup calls: first the step-1 AssociateToStep (matter), then the
    // step-2 existing-document picker (document).
    const navigationService: INavigationService = {
      openRecord: jest.fn().mockResolvedValue(undefined),
      openDialog: jest.fn().mockResolvedValue({ confirmed: true }),
      closeDialog: jest.fn(),
      openLookup: jest
        .fn()
        .mockResolvedValueOnce([{ id: 'matter-id', name: 'Smith v. Jones', entityType: 'sprk_matter' }])
        .mockResolvedValueOnce([{ id: 'existing-doc-id', name: 'MSA Draft', entityType: 'sprk_document' }]),
    };
    const data = buildData({ dataService, navigationService });

    renderWidget(data);

    // Step 1: pick the regarding record via AssociateToStep's "Select Record" button.
    const selectRecordButton = screen.getByTestId('associate-to-step-select-record-button');
    await act(async () => {
      fireEvent.click(selectRecordButton);
      await Promise.resolve();
    });
    await screen.findByText('Smith v. Jones');
    clickPrimary('Next');

    // Step 2: select existing document.
    await selectExistingDocument();
    clickPrimary('Next');

    // Step 3: name.
    fireEvent.change(screen.getByLabelText('Analysis name'), { target: { value: 'MSA Review' } });
    clickPrimary('Next');

    // Step 4: finish.
    await act(async () => {
      clickPrimary('Finish');
      await Promise.resolve();
      await Promise.resolve();
    });

    await waitFor(() => expect(dataService.createRecord).toHaveBeenCalled());
    expect(mockApplyFieldMappings).toHaveBeenCalledWith(
      expect.objectContaining({ sourceEntity: 'sprk_matter', sourceId: 'matter-id', targetEntity: 'sprk_analysis' })
    );
  });

  it('is Field-Mapping-driven for the Send Email next-step: applyFieldMappings runs sprk_analysis -> sprk_communication and mapped subject/body win over user input', async () => {
    mockApplyFieldMappings.mockImplementation(
      async (args: { targetEntity: string; payload: Record<string, unknown> }) => {
        if (args.targetEntity === 'sprk_communication') {
          args.payload['subject'] = 'Mapped subject from profile';
          args.payload['description'] = 'Mapped body from profile';
          return { profileFound: true, fieldsMapped: ['subject', 'description'], warnings: [] };
        }
        return { profileFound: false, fieldsMapped: [], warnings: [] };
      }
    );

    const dataService = buildDataService();
    const authenticatedFetch = jest
      .fn()
      .mockResolvedValue({ ok: true, json: async () => ({ documentIds: ['uploaded-doc-id'] }) });
    const searchUsers = jest.fn().mockResolvedValue([{ id: 'user-1', name: 'Jordan Lee' }]);
    const data = buildData({ dataService, authenticatedFetch, searchUsers });

    renderWidget(data);

    // Step 1: skip Associate To.
    skipAssociateTo();
    // Step 2: existing document.
    await selectExistingDocument();
    clickPrimary('Next');

    // Step 3: name.
    fireEvent.change(screen.getByLabelText('Analysis name'), { target: { value: 'MSA Review' } });
    clickPrimary('Next');

    // Step 4: select the Send Email follow-on card, then advance into its step.
    fireEvent.click(screen.getByRole('checkbox', { name: /Send Notification Email/i }));
    clickPrimary('Next');

    // Send Email step: pick a "To" user via the LookupField search.
    const toInput = await screen.findByPlaceholderText('Search users...');
    fireEvent.change(toInput, { target: { value: 'jo' } });
    await screen.findByText('Jordan Lee');
    fireEvent.click(screen.getByText('Jordan Lee'));

    fireEvent.change(screen.getByLabelText('Subject'), { target: { value: 'User-typed subject' } });
    fireEvent.change(screen.getByLabelText('Message body'), { target: { value: 'User-typed body' } });

    await act(async () => {
      clickPrimary('Finish');
      await Promise.resolve();
      await Promise.resolve();
    });

    await waitFor(() => expect(dataService.createRecord).toHaveBeenCalled());
    expect(mockApplyFieldMappings).toHaveBeenCalledWith(
      expect.objectContaining({ sourceEntity: 'sprk_analysis', targetEntity: 'sprk_communication' })
    );

    await waitFor(() =>
      expect(authenticatedFetch).toHaveBeenCalledWith(
        expect.stringContaining('/api/communications/send'),
        expect.objectContaining({ method: 'POST' })
      )
    );
    const sendCall = authenticatedFetch.mock.calls.find(([url]: [string]) => url.includes('/api/communications/send'));
    const sentBody = JSON.parse(sendCall[1].body as string);
    expect(sentBody.subject).toBe('Mapped subject from profile');
    expect(sentBody.body).toBe('Mapped body from profile');
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
      return jest.fn(async (input: RequestInfo, init?: RequestInit) => {
        const url = String(input);
        if (url.endsWith('/api/ai/chat/sessions') && init?.method === 'POST') {
          return {
            ok: sessionCreate.ok,
            status: sessionCreate.ok ? 201 : 500,
            json: async () => (sessionCreate.ok ? { sessionId: sessionCreate.sessionId } : {}),
          } as unknown as Response;
        }
        return { ok: true, json: async () => ({}) } as unknown as Response;
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

      // (2) HAND-OFF: the compose seed carries the session + analysis + sub-domain + auto-run arm.
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
              }),
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
