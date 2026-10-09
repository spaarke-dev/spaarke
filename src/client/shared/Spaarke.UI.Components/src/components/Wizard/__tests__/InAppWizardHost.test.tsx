/**
 * InAppWizardHost.test.tsx — the in-app launch contract for the five Create wizards
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
import { InAppWizardHost } from '../InAppWizardHost';
import {
  launchAssignWorkWizard,
  launchCreateEventWizard,
  launchCreateMatterWizard,
  launchCreateProjectWizard,
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

// ---------------------------------------------------------------------------
// Xrm stub — present so a navigateTo call WOULD succeed; the tests assert it is not made.
// ---------------------------------------------------------------------------

let navigateTo: jest.Mock;

beforeEach(() => {
  navigateTo = jest.fn().mockResolvedValue(undefined);
  (window as unknown as { Xrm?: unknown }).Xrm = { WebApi: {}, Navigation: { navigateTo } };
  sessionStorage.clear();
  for (const k of Object.keys(mockWizardProps)) delete mockWizardProps[k];
});

afterEach(() => {
  delete (window as unknown as { Xrm?: unknown }).Xrm;
});

const BFF = 'https://bff.example';
const authFetch = jest.fn();

function mountHost(uiScale?: number) {
  return renderWithProviders(
    <InAppWizardHost authenticatedFetch={authFetch} bffBaseUrl={BFF} tenantId="tenant-1" uiScale={uiScale} />
  );
}

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
    await act(async () => {
      await navigateToWebResourceSurfaceAsync({ webresourceName: 'sprk_createprojectwizard', data: '' }).then(() => {
        secondSettled = true;
      });
    });
    expect(secondSettled).toBe(true);
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

  it('a wizard outside the task-112 scope (Summarize Files) still uses navigateTo with the host mounted', async () => {
    mountHost();
    launchSummarizeFilesWizard({ bffBaseUrl: BFF });
    await waitFor(() => expect(navigateTo).toHaveBeenCalledTimes(1));
    expect(navigateTo.mock.calls[0][0].webresourceName).toBe('sprk_summarizefileswizard');
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
