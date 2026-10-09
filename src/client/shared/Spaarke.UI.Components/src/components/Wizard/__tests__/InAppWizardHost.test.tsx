/**
 * InAppWizardHost.test.tsx — the in-app launch contract for the five Create wizards (task 112) and
 * for Summarize Files, Upload Documents, Find Similar and the Workspace layout wizard (task 113)
 * (spaarke-ontology-platform-r1 task 112; D-26; ADR-050 as amended 2026-10-07, launch rule (a)).
 *
 * The five wizard components are replaced by recording stubs: this file proves the ROUTING
 * (which launches mount which wizard, with which props, and that `Xrm.Navigation.navigateTo` is
 * not called), the close/settle semantics the launchers' promises rely on, and the Assistant
 * hand-off round trip. `InAppWizardHost.render.test.tsx` mounts a REAL wizard in SprkModal.
 *
 * Classification (ADR-038 §7): MAINTAIN — the shared launch seam every Console Create entry uses.
 */
import * as React from 'react';
import { act, fireEvent, screen, waitFor } from '@testing-library/react';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import { InAppWizardHost, type InAppWizardRenderer, type InAppWizardRenderers } from '../InAppWizardHost';
import {
  canOpenInApp,
  launchAssignWorkWizard,
  launchCreateEventWizard,
  launchCreateMatterWizard,
  launchCreateProjectWizard,
  launchFindSimilarWizard,
  launchPlaybookIntent,
  launchSummarizeFilesWizard,
  navigateToWebResourceSurfaceAsync,
} from '../../WorkspaceShell/wizardLaunchers';
import { launchSurface } from '../../../services/surfaceHandoff/launchSurface';

// ---------------------------------------------------------------------------
// Recording stubs for the five wizards
// ---------------------------------------------------------------------------

const mockWizardProps: Record<string, any> = {};

function mockWizardStub(id: string, props: any): React.ReactElement {
  mockWizardProps[id] = props;
  return (
    <div data-testid={`wizard-${id}`}>
      <button onClick={props.onClose}>stub-cancel</button>
      {props.onComplete ? <button onClick={() => props.onComplete('rec-1')}>stub-complete</button> : null}
    </div>
  );
}

jest.mock('../../CreateMatterWizard/CreateMatterWizard', () => ({
  CreateMatterWizard: (p: any) => mockWizardStub('matter', p),
}));
jest.mock('../../CreateProjectWizard/CreateProjectWizard', () => ({
  CreateProjectWizard: (p: any) => mockWizardStub('project', p),
}));
jest.mock('../../CreateEventWizard/CreateEventWizard', () => ({
  CreateEventWizard: (p: any) => mockWizardStub('event', p),
}));
jest.mock('../../CreateTodoWizard/TodoWizardDialog', () => ({
  __esModule: true,
  default: (p: any) => mockWizardStub('todo', p),
}));
jest.mock('../../CreateWorkAssignmentWizard/WorkAssignmentWizardDialog', () => ({
  __esModule: true,
  default: (p: any) => mockWizardStub('workassignment', p),
}));
jest.mock('../../SummarizeFilesWizard/SummarizeFilesDialog', () => ({
  __esModule: true,
  SummarizeFilesDialog: (p: any) => mockWizardStub('summarize', p),
  default: (p: any) => mockWizardStub('summarize', p),
}));

// ---------------------------------------------------------------------------
// Xrm stub — present so a navigateTo call WOULD succeed; the tests assert it is not made.
// ---------------------------------------------------------------------------

let navigateTo: jest.Mock;

beforeEach(() => {
  navigateTo = jest.fn().mockResolvedValue(undefined);
  (window as unknown as { Xrm?: unknown }).Xrm = { WebApi: {}, Navigation: { navigateTo } };
  sessionStorage.clear();
  for (const k of Object.keys(mockWizardProps)) delete mockWizardProps[k];
  for (const k of Object.keys(mockRenderCalls)) delete mockRenderCalls[k];
});

afterEach(() => {
  delete (window as unknown as { Xrm?: unknown }).Xrm;
});

const BFF = 'https://bff.example';
const authFetch = jest.fn();

function mountHost(uiScale?: number, renderers?: InAppWizardRenderers) {
  return renderWithProviders(
    <InAppWizardHost
      authenticatedFetch={authFetch}
      bffBaseUrl={BFF}
      tenantId="tenant-1"
      uiScale={uiScale}
      renderers={renderers}
    />
  );
}

// Renderers a mounting app supplies for the code-page wizards (task 113): recording stubs.
const mockRenderCalls: Record<string, any> = {};
function rendererFor(id: string): InAppWizardRenderer {
  return ctx => {
    mockRenderCalls[id] = ctx;
    return (
      <div data-testid={`wizard-${id}`}>
        <button onClick={ctx.onClose}>stub-cancel</button>
      </div>
    );
  };
}
const ALL_RENDERERS: InAppWizardRenderers = {
  sprk_documentuploadwizard: rendererFor('upload'),
  sprk_findsimilar: rendererFor('findsimilar'),
  sprk_workspacelayoutwizard: rendererFor('layout'),
};

// ---------------------------------------------------------------------------
// Every Create launch mounts in-app; navigateTo is never called
// ---------------------------------------------------------------------------

describe('InAppWizardHost — the five Create launches open in-app', () => {
  const cases: Array<[string, () => unknown, string]> = [
    ['launchCreateMatterWizard', () => launchCreateMatterWizard({ bffBaseUrl: BFF }), 'matter'],
    ['launchCreateProjectWizard', () => launchCreateProjectWizard({ bffBaseUrl: BFF }), 'project'],
    ['launchCreateEventWizard', () => launchCreateEventWizard({ bffBaseUrl: BFF }), 'event'],
    ['launchAssignWorkWizard', () => launchAssignWorkWizard({ bffBaseUrl: BFF }), 'workassignment'],
    [
      'navigateToWebResourceSurfaceAsync(sprk_createtodowizard)',
      () => navigateToWebResourceSurfaceAsync({ webresourceName: 'sprk_createtodowizard', data: `bffBaseUrl=${BFF}` }),
      'todo',
    ],
  ];

  it.each(cases)('%s mounts the wizard non-embedded with uiScale, and calls no navigateTo', async (_n, launch, id) => {
    mountHost(1.5);
    act(() => {
      void launch();
    });
    expect(await screen.findByTestId(`wizard-${id}`)).toBeInTheDocument();
    expect(navigateTo).not.toHaveBeenCalled();
    const props = mockWizardProps[id];
    expect(props.open).toBe(true);
    expect(props.embedded).toBe(false); // WizardShell inside SprkModal, not the code-page embed
    expect(props.uiScale).toBe(1.5);
    // Named size + explicit dismiss are WizardShell's defaults; the host must not override them
    // (and must not use the deprecated maxWidth/height carry-over, D-70).
    expect(props).not.toHaveProperty('dismiss');
    expect(props).not.toHaveProperty('size');
    expect(props).not.toHaveProperty('maxWidth');
    expect(props).not.toHaveProperty('height');
    expect(props.authenticatedFetch).toBe(authFetch);
    expect(props.bffBaseUrl).toBe(BFF);
  });

  it('closes on the wizard onClose and resolves the launcher promise only then', async () => {
    mountHost();
    let settled = false;
    let outcome: unknown;
    act(() => {
      void navigateToWebResourceSurfaceAsync({
        webresourceName: 'sprk_createeventwizard',
        data: `bffBaseUrl=${BFF}`,
      }).then(o => {
        settled = true;
        outcome = o;
      });
    });
    await screen.findByTestId('wizard-event');
    await act(async () => {
      await Promise.resolve();
    });
    expect(settled).toBe(false); // the wizard is still open

    fireEvent.click(screen.getByText('stub-cancel'));
    await waitFor(() => expect(settled).toBe(true));
    expect(outcome).toEqual({ launched: true });
    expect(screen.queryByTestId('wizard-event')).toBeNull();
    expect(navigateTo).not.toHaveBeenCalled();
  });

  it('ignores a second launch while a wizard is open (no stacking, no replacement)', async () => {
    mountHost();
    act(() => launchCreateMatterWizard({ bffBaseUrl: BFF }));
    await screen.findByTestId('wizard-matter');

    let secondSettled = false;
    let secondOutcome: unknown;
    await act(async () => {
      await navigateToWebResourceSurfaceAsync({ webresourceName: 'sprk_createprojectwizard', data: '' }).then(o => {
        secondSettled = true;
        secondOutcome = o;
      });
    });
    expect(secondSettled).toBe(true);
    // Honest outcome: nothing opened because another wizard is open (not "opened and closed").
    expect(secondOutcome).toEqual({ launched: false, busy: true });
    expect(screen.getByTestId('wizard-matter')).toBeInTheDocument();
    expect(screen.queryByTestId('wizard-project')).toBeNull();
    expect(navigateTo).not.toHaveBeenCalled();
  });
});

// ---------------------------------------------------------------------------
// Assistant hand-off (launchSurface) round trip, in-app
// ---------------------------------------------------------------------------

describe('InAppWizardHost — launchSurface hand-off', () => {
  it('pre-seeds the wizard from the envelope and reports a committed create', async () => {
    mountHost();
    let outcomePromise!: ReturnType<typeof launchSurface>;
    act(() => {
      outcomePromise = launchSurface({
        consumerType: 'create-matter',
        bffBaseUrl: BFF,
        draftValues: { sprk_mattername: 'Acme v Widget' },
        fileIds: ['file-1'],
        source: { sessionId: 'session-9' },
        provenance: { sourceFiles: ['brief.pdf'] },
      });
    });
    await screen.findByTestId('wizard-matter');
    const props = mockWizardProps.matter;
    expect(props.initialFormValues).toMatchObject({ matterName: 'Acme v Widget' });
    expect(props.initialFileRefs).toEqual({ sessionId: 'session-9', fileIds: ['file-1'], fileNames: ['brief.pdf'] });

    fireEvent.click(screen.getByText('stub-complete'));
    const outcome = await outcomePromise;
    expect(outcome.launched).toBe(true);
    expect(outcome.result).toEqual({ committed: true, recordId: 'rec-1' });
    expect(navigateTo).not.toHaveBeenCalled();
    expect(sessionStorage.length).toBe(0); // envelope + result cleaned up
  });

  it('reports a cancelled launch when the wizard closes without creating', async () => {
    mountHost();
    let outcomePromise!: ReturnType<typeof launchSurface>;
    act(() => {
      outcomePromise = launchSurface({ consumerType: 'create-work-assignment', bffBaseUrl: BFF });
    });
    await screen.findByTestId('wizard-workassignment');
    fireEvent.click(screen.getByText('stub-cancel'));
    const outcome = await outcomePromise;
    expect(outcome.result).toEqual({ committed: false, cancelled: true });
    expect(navigateTo).not.toHaveBeenCalled();
  });
});

// ---------------------------------------------------------------------------
// Negative cases: out-of-scope wizards and hostless contexts keep navigateTo
// ---------------------------------------------------------------------------

describe('InAppWizardHost — navigateTo is kept where it belongs', () => {
  it('with no host mounted, the launchers call navigateTo exactly as before', async () => {
    launchCreateMatterWizard({ bffBaseUrl: BFF });
    await waitFor(() => expect(navigateTo).toHaveBeenCalledTimes(1));
    const [pageInput, navOptions] = navigateTo.mock.calls[0];
    expect(pageInput).toEqual({
      pageType: 'webresource',
      webresourceName: 'sprk_creatematterwizard',
      data: `bffBaseUrl=${encodeURIComponent(BFF)}`,
    });
    expect(navOptions).toMatchObject({ target: 2, title: 'Create New Matter' });
  });

  it('a wizard outside the in-app scope (Playbook Library) still uses navigateTo with the host mounted', async () => {
    mountHost(undefined, ALL_RENDERERS);
    launchPlaybookIntent({ bffBaseUrl: BFF, intent: 'email-compose' });
    await waitFor(() => expect(navigateTo).toHaveBeenCalledTimes(1));
    expect(navigateTo.mock.calls[0][0].webresourceName).toBe('sprk_playbooklibrary');
    expect(screen.queryByTestId(/^wizard-/)).toBeNull();
  });

  it('a title-less launch sends no title navOption on the fallback', async () => {
    await navigateToWebResourceSurfaceAsync({ webresourceName: 'sprk_createtodowizard', data: 'x=1' });
    expect(navigateTo.mock.calls[0][1]).not.toHaveProperty('title');
  });

  it('unmounting the host settles an open launch and returns later launches to navigateTo', async () => {
    const { unmount } = mountHost();
    let settled = false;
    act(() => {
      void navigateToWebResourceSurfaceAsync({ webresourceName: 'sprk_createtodowizard', data: '' }).then(() => {
        settled = true;
      });
    });
    await screen.findByTestId('wizard-todo');
    unmount();
    await waitFor(() => expect(settled).toBe(true));

    launchCreateProjectWizard({ bffBaseUrl: BFF });
    await waitFor(() => expect(navigateTo).toHaveBeenCalledTimes(1));
    expect(navigateTo.mock.calls[0][0].webresourceName).toBe('sprk_createprojectwizard');
  });
});

// ---------------------------------------------------------------------------
// Task 113: Summarize Files (built in) opens in-app
// ---------------------------------------------------------------------------

describe('InAppWizardHost — Summarize Files opens in-app (task 113)', () => {
  it('launchSummarizeFilesWizard mounts it non-embedded with uiScale and calls no navigateTo', async () => {
    mountHost(1.25);
    act(() => launchSummarizeFilesWizard({ bffBaseUrl: BFF }));
    expect(await screen.findByTestId('wizard-summarize')).toBeInTheDocument();
    expect(navigateTo).not.toHaveBeenCalled();
    const props = mockWizardProps.summarize;
    expect(props.open).toBe(true);
    expect(props.embedded).toBe(false);
    expect(props.uiScale).toBe(1.25);
    expect(props).not.toHaveProperty('dismiss');
    expect(props).not.toHaveProperty('size');
    expect(props).not.toHaveProperty('maxWidth');
    expect(props).not.toHaveProperty('height');
    expect(props.bffBaseUrl).toBe(BFF);
  });

  it('the Console launchSurface route pre-seeds the session files and reads back cancelled (parity: no record)', async () => {
    mountHost();
    let outcomePromise!: ReturnType<typeof launchSurface>;
    act(() => {
      outcomePromise = launchSurface({
        consumerType: 'summarize-files',
        bffBaseUrl: BFF,
        fileIds: ['f1'],
        source: { sessionId: 's1' },
        provenance: { sourceFiles: ['a.pdf'] },
      });
    });
    await screen.findByTestId('wizard-summarize');
    expect(mockWizardProps.summarize.initialFileRefs).toEqual({
      sessionId: 's1',
      fileIds: ['f1'],
      fileNames: ['a.pdf'],
    });
    fireEvent.click(screen.getByText('stub-cancel'));
    const outcome = await outcomePromise;
    expect(outcome.launched).toBe(true);
    expect(outcome.result).toEqual({ committed: false, cancelled: true });
    expect(navigateTo).not.toHaveBeenCalled();
  });
});

// ---------------------------------------------------------------------------
// Task 113: Upload Documents / Find Similar / Workspace layout - supplied renderers
// ---------------------------------------------------------------------------

describe('InAppWizardHost — code-page wizards open through supplied renderers (task 113)', () => {
  const cases: Array<[string, string, string]> = [
    ['sprk_documentuploadwizard', 'upload', 'parentEntityType=sprk_document'],
    ['sprk_findsimilar', 'findsimilar', 'documentId=&containerId=&bffBaseUrl=x'],
    ['sprk_workspacelayoutwizard', 'layout', 'mode=edit&layoutId=l1'],
  ];

  it.each(cases)(
    '%s mounts its renderer with the launch data, onClose and uiScale; navigateTo is not called',
    async (name, id, data) => {
      mountHost(2, ALL_RENDERERS);
      act(() => {
        void navigateToWebResourceSurfaceAsync({ webresourceName: name, data });
      });
      expect(await screen.findByTestId(`wizard-${id}`)).toBeInTheDocument();
      expect(navigateTo).not.toHaveBeenCalled();
      const ctx = mockRenderCalls[id];
      expect(ctx.data).toBe(data);
      expect(ctx.uiScale).toBe(2);
      expect(ctx.bffBaseUrl).toBe(BFF);
      expect(ctx.tenantId).toBe('tenant-1');
      expect(ctx.authenticatedFetch).toBe(authFetch);
      expect(typeof ctx.onClose).toBe('function');
    }
  );

  it('closing through the renderer onClose resolves the launcher and unmounts the wizard', async () => {
    mountHost(undefined, ALL_RENDERERS);
    let outcome: unknown;
    act(() => {
      void navigateToWebResourceSurfaceAsync({
        webresourceName: 'sprk_workspacelayoutwizard',
        data: 'mode=create',
      }).then(o => {
        outcome = o;
      });
    });
    await screen.findByTestId('wizard-layout');
    expect(outcome).toBeUndefined();
    fireEvent.click(screen.getByText('stub-cancel'));
    await waitFor(() => expect(outcome).toEqual({ launched: true }));
    expect(screen.queryByTestId('wizard-layout')).toBeNull();
    expect(navigateTo).not.toHaveBeenCalled();
  });

  it('launchFindSimilarWizard (the fire-and-forget launcher) opens in-app too', async () => {
    mountHost(undefined, ALL_RENDERERS);
    act(() => launchFindSimilarWizard({ bffBaseUrl: BFF }));
    expect(await screen.findByTestId('wizard-findsimilar')).toBeInTheDocument();
    expect(navigateTo).not.toHaveBeenCalled();
  });

  it('forwards the Assistant hand-off file refs to the renderer (Find Similar pre-selects the first file)', async () => {
    mountHost(undefined, ALL_RENDERERS);
    act(() => {
      void launchSurface({
        consumerType: 'find-similar',
        bffBaseUrl: BFF,
        fileIds: ['f9'],
        source: { sessionId: 's9' },
        provenance: { sourceFiles: ['x.pdf'] },
      });
    });
    await screen.findByTestId('wizard-findsimilar');
    expect(mockRenderCalls.findsimilar.initialFileRefs).toEqual({
      sessionId: 's9',
      fileIds: ['f9'],
      fileNames: ['x.pdf'],
    });
    expect(navigateTo).not.toHaveBeenCalled();
  });

  it('a host mounted WITHOUT a renderer keeps that wizard on navigateTo (no swallowed launch)', async () => {
    mountHost(undefined, { sprk_findsimilar: rendererFor('findsimilar') }); // upload + layout have none
    await navigateToWebResourceSurfaceAsync({ webresourceName: 'sprk_documentuploadwizard', data: 'x=1' });
    await navigateToWebResourceSurfaceAsync({ webresourceName: 'sprk_workspacelayoutwizard', data: 'mode=create' });
    expect(navigateTo).toHaveBeenCalledTimes(2);
    expect(navigateTo.mock.calls.map(c => c[0].webresourceName)).toEqual([
      'sprk_documentuploadwizard',
      'sprk_workspacelayoutwizard',
    ]);
    expect(canOpenInApp('sprk_findsimilar')).toBe(true);
    expect(canOpenInApp('sprk_documentuploadwizard')).toBe(false);
    expect(canOpenInApp('sprk_createtodowizard')).toBe(true);
    expect(canOpenInApp('sprk_playbooklibrary')).toBe(false);
  });

  it('with no host mounted every one of the four keeps the navigateTo shape unchanged', async () => {
    expect(canOpenInApp('sprk_summarizefileswizard')).toBe(false);
    await navigateToWebResourceSurfaceAsync({ webresourceName: 'sprk_documentuploadwizard', data: 'enc%3D1' });
    await navigateToWebResourceSurfaceAsync({
      webresourceName: 'sprk_workspacelayoutwizard',
      data: 'mode=create',
      title: 'Create New Workspace',
    });
    expect(navigateTo.mock.calls[0][0]).toEqual({
      pageType: 'webresource',
      webresourceName: 'sprk_documentuploadwizard',
      data: 'enc%3D1',
    });
    expect(navigateTo.mock.calls[0][1]).toMatchObject({ target: 2 });
    expect(navigateTo.mock.calls[0][1]).not.toHaveProperty('title');
    expect(navigateTo.mock.calls[1][1]).toMatchObject({ target: 2, title: 'Create New Workspace' });
  });

  it('unmounting the host returns the code-page wizards to navigateTo', async () => {
    const { unmount } = mountHost(undefined, ALL_RENDERERS);
    expect(canOpenInApp('sprk_workspacelayoutwizard')).toBe(true);
    unmount();
    expect(canOpenInApp('sprk_workspacelayoutwizard')).toBe(false);
  });
});

// ---------------------------------------------------------------------------
// #1420: Create Work Assignment reaches launchSurface as committed
// ---------------------------------------------------------------------------

describe('InAppWizardHost — Create Work Assignment completion (#1420)', () => {
  it('passes onComplete to the wizard and a finished create reads back committed with the record id', async () => {
    mountHost();
    let outcomePromise!: ReturnType<typeof launchSurface>;
    act(() => {
      outcomePromise = launchSurface({ consumerType: 'create-work-assignment', bffBaseUrl: BFF });
    });
    await screen.findByTestId('wizard-workassignment');
    expect(typeof mockWizardProps.workassignment.onComplete).toBe('function');

    fireEvent.click(screen.getByText('stub-complete'));
    const outcome = await outcomePromise;
    expect(outcome.launched).toBe(true);
    expect(outcome.result).toEqual({ committed: true, recordId: 'rec-1' });
    expect(navigateTo).not.toHaveBeenCalled();
    expect(sessionStorage.length).toBe(0);
  });
});
