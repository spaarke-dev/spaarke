/**
 * EmailAssociationsAndTracking — unit tests (email-communication-solution-r5
 * task 035, FR-13/FR-14/FR-19). Covers the task's closed acceptance set:
 *
 *  - Interactive confirm persists ADDITIVELY (sibling regarding preserved).
 *  - Reply-chain auto-association displays as "Filed automatically" (display
 *    only — no client recompute).
 *  - Ambiguous/low-confidence matches render in a "Needs your decision" group
 *    distinct from "Suggested" (FR-19 uncertainty state).
 *  - Dismissing a FILED association removes ONLY that one — siblings intact
 *    (the additive model's inverse-of-add, never a bulk clear).
 *  - Tracking flags (monitor/high-priority/access) read + write.
 *  - Dark mode (ADR-021).
 *  - Negative: the view is built on the task-020 PRODUCTION `logic/connections`
 *    extraction, not the stale `CommunicationPage` `ConnectionsEditor` stub.
 */
import * as fs from 'fs';
import * as path from 'path';
import * as React from 'react';
import { render, screen, fireEvent, waitFor, within } from '@testing-library/react';
import { FluentProvider, webDarkTheme, webLightTheme } from '@fluentui/react-components';
import { EmailConnectionsReview } from '../EmailConnectionsReview';
import { EmailTrackingPanel } from '../EmailTrackingPanel';
import { _resetNavPropCacheForTests, type FiledAssociation, type IResolverWriteContext } from '../../../logic/connections';
import type { EmailConnectionsReviewProps, EmailTrackingPanelProps } from '../EmailAssociationsAndTracking.types';

function renderWithProvider(ui: React.ReactElement, theme = webLightTheme) {
  return render(<FluentProvider theme={theme}>{ui}</FluentProvider>);
}

// jsdom has no `ResizeObserver` implementation; Fluent's `MessageBar` reflow
// hook needs one. Stubbed locally (not in the package-wide jest.setup.ts) to
// avoid touching shared test infra outside this task's scope.
beforeAll(() => {
  if (typeof (global as unknown as { ResizeObserver?: unknown }).ResizeObserver === 'undefined') {
    class ResizeObserverStub {
      observe(): void {}
      unobserve(): void {}
      disconnect(): void {}
    }
    (global as unknown as { ResizeObserver: unknown }).ResizeObserver = ResizeObserverStub;
  }
});

const HOST_ID = '22222222-2222-2222-2222-222222222222';

const NAV_PROPS_RESPONSE = {
  ok: true,
  json: async () => ({
    value: [
      {
        ReferencingAttribute: 'sprk_regardingmatter',
        ReferencingEntityNavigationPropertyName: 'sprk_RegardingMatter',
        ReferencedEntity: 'sprk_matter',
      },
      {
        ReferencingAttribute: 'sprk_regardingcontact',
        ReferencingEntityNavigationPropertyName: 'sprk_RegardingContact',
        ReferencedEntity: 'contact',
      },
      {
        ReferencingAttribute: 'sprk_regardingorganization',
        ReferencingEntityNavigationPropertyName: 'sprk_RegardingOrganization',
        ReferencedEntity: 'sprk_organization',
      },
    ],
  }),
};

function makeWriteContext(): IResolverWriteContext {
  return {
    webApi: {
      updateRecord: jest.fn().mockResolvedValue({}),
      retrieveMultipleRecords: jest.fn().mockResolvedValue({ entities: [] }),
    } as unknown as IResolverWriteContext['webApi'],
    hostEntity: 'sprk_communication',
    hostRecordId: HOST_ID,
  };
}

/**
 * A provenance doc with all three review states represented:
 *  - sprk_matter: written:true, ThreadContinuity rung (the reply-inheritance
 *    case) — lands in "Filed automatically".
 *  - contact: soft match, not written — lands in "Suggested".
 *  - sprk_organization: TWO conflicting candidates — lands in "Needs your
 *    decision".
 */
function buildProvenanceJson(): string {
  return JSON.stringify({
    version: 1,
    direction: 'inbound',
    decision: {
      status: 'PartiallyResolved',
      autoFiled: true,
      killSwitchEnabled: false,
      autoFileThreshold: 0.85,
      topDeterministicConfidence: 0.9,
      topConfidence: 0.95,
      aiInvolved: true,
      reason: 'thread continuity + AI org match',
    },
    rungsFired: ['ThreadContinuity', 'ParticipantCorrelation', 'SemanticMatch'],
    candidates: [
      {
        field: 'sprk_regardingmatter',
        targetEntity: 'sprk_matter',
        targetId: 'mtr-1',
        targetName: 'Acme v Beta',
        reinforcedConfidence: 0.95,
        deterministicConfidence: 0.9,
        written: true,
        conflict: false,
        contributors: [{ rung: 'ThreadContinuity', confidence: 0.95, provenance: 'thread-continuity' }],
      },
      {
        field: 'sprk_regardingcontact',
        targetEntity: 'contact',
        targetId: 'con-1',
        targetName: 'Jane Doe',
        reinforcedConfidence: 0.4,
        deterministicConfidence: 0.3,
        written: false,
        conflict: false,
        contributors: [{ rung: 'ParticipantCorrelation', confidence: 0.4, provenance: 'participant-correlation' }],
      },
      {
        field: 'sprk_regardingorganization',
        targetEntity: 'sprk_organization',
        targetId: 'org-1',
        targetName: 'Acme Corp',
        reinforcedConfidence: 0.6,
        deterministicConfidence: 0.5,
        written: false,
        conflict: true,
        contributors: [{ rung: 'SemanticMatch', confidence: 0.6, provenance: 'semantic-match' }],
      },
      {
        field: 'sprk_regardingorganization',
        targetEntity: 'sprk_organization',
        targetId: 'org-2',
        targetName: 'Acme Corp International',
        reinforcedConfidence: 0.55,
        deterministicConfidence: 0.5,
        written: false,
        conflict: true,
        contributors: [{ rung: 'SemanticMatch', confidence: 0.55, provenance: 'semantic-match' }],
      },
    ],
    signals: [],
  });
}

function baseProps(overrides: Partial<EmailConnectionsReviewProps> = {}): EmailConnectionsReviewProps {
  return {
    communicationId: HOST_ID,
    associationStatus: 100000001, // PendingReview
    associationProvenanceJson: buildProvenanceJson(),
    filedAssociations: [] as FiledAssociation[],
    writeContext: makeWriteContext(),
    ...overrides,
  };
}

describe('EmailConnectionsReview', () => {
  const originalFetch = global.fetch;

  beforeEach(() => {
    _resetNavPropCacheForTests();
    global.fetch = jest.fn().mockResolvedValue(NAV_PROPS_RESPONSE) as unknown as typeof fetch;
  });

  afterAll(() => {
    global.fetch = originalFetch;
  });

  it('groups an already-filed reply-inherited association into "Filed automatically" — display only', () => {
    renderWithProvider(<EmailConnectionsReview {...baseProps()} />);

    expect(screen.getByText('Filed automatically')).toBeInTheDocument();
    expect(screen.getByText('Acme v Beta')).toBeInTheDocument();
  });

  it('groups low-confidence/ambiguous matches into "Needs your decision", distinct from "Suggested" (FR-19)', () => {
    renderWithProvider(<EmailConnectionsReview {...baseProps()} />);

    expect(screen.getByText('Needs your decision')).toBeInTheDocument();
    expect(screen.getByRole('radio', { name: 'Acme Corp' })).toBeInTheDocument();
    expect(screen.getByRole('radio', { name: 'Acme Corp International' })).toBeInTheDocument();

    expect(screen.getByText('Suggested')).toBeInTheDocument();
    expect(screen.getByText('Jane Doe')).toBeInTheDocument();
  });

  it('confirming a suggested match persists ADDITIVELY via applyRegardingSelection — the existing sibling regarding is preserved', async () => {
    const props = baseProps();
    renderWithProvider(<EmailConnectionsReview {...props} />);

    // Scope to the "Suggested" section — "Needs your decision" ALSO has a
    // "Confirm" button (the ambiguous-conflict confirm), so an unscoped query
    // is ambiguous.
    const suggestedSection = screen.getByText('Suggested').closest('section') as HTMLElement;
    fireEvent.click(within(suggestedSection).getByRole('button', { name: 'Confirm' }));

    await waitFor(() => expect(props.writeContext.webApi.updateRecord).toHaveBeenCalled());

    const call = (props.writeContext.webApi.updateRecord as jest.Mock).mock.calls[0];
    expect(call[0]).toBe('sprk_communication');
    expect(call[1]).toBe(HOST_ID);
    const payload = call[2] as Record<string, unknown>;
    // ADDITIVE: no sibling typed lookup is nulled by this write.
    const nulledBinds = Object.entries(payload).filter(([k, v]) => k.endsWith('@odata.bind') && v === null);
    expect(nulledBinds).toHaveLength(0);

    // The pre-existing sibling (the filed Matter) is still rendered — untouched.
    expect(screen.getByText('Filed automatically')).toBeInTheDocument();
    expect(screen.getByText('Acme v Beta')).toBeInTheDocument();
  });

  it('dismissing a FILED association removes ONLY that one via unlinkRegarding — siblings (and other pending suggestions) are left intact', async () => {
    const props = baseProps();
    renderWithProvider(<EmailConnectionsReview {...props} />);

    // "Dismiss" on the Filed-automatically row (Acme v Beta / Matter). The
    // Suggested section ALSO has a "Dismiss" button (Jane Doe), so scope to
    // the "Filed automatically" section.
    const filedSection = screen.getByText('Filed automatically').closest('section') as HTMLElement;
    fireEvent.click(within(filedSection).getByRole('button', { name: 'Dismiss' }));

    await waitFor(() => expect(props.writeContext.webApi.updateRecord).toHaveBeenCalled());

    const payload = (props.writeContext.webApi.updateRecord as jest.Mock).mock.calls[0][2] as Record<string, unknown>;
    expect(payload).toEqual({ 'sprk_RegardingMatter@odata.bind': null });
    const nulledBinds = Object.entries(payload).filter(([k, v]) => k.endsWith('@odata.bind') && v === null);
    expect(nulledBinds).toHaveLength(1);

    // The untouched suggested contact sibling is still present.
    expect(screen.getByText('Jane Doe')).toBeInTheDocument();
    expect(screen.getByText('Suggested')).toBeInTheDocument();
  });

  it('renders correctly under a dark FluentProvider theme (ADR-021) with no console errors', () => {
    const errorSpy = jest.spyOn(console, 'error').mockImplementation(() => {});
    renderWithProvider(<EmailConnectionsReview {...baseProps()} />, webDarkTheme);

    expect(screen.getByText('Filed automatically')).toBeInTheDocument();
    expect(errorSpy).not.toHaveBeenCalled();
    errorSpy.mockRestore();
  });

  it('renders a read-only empty state when there is nothing to review', () => {
    renderWithProvider(
      <EmailConnectionsReview
        {...baseProps({
          associationProvenanceJson: null,
          associationStatus: null,
          readOnly: true,
        })}
      />
    );
    expect(screen.getByText('No connections yet.')).toBeInTheDocument();
  });
});

describe('EmailTrackingPanel', () => {
  const ACCESS_OPTIONS = [
    { value: 100000000, label: 'Standard' },
    { value: 100000001, label: 'Limited' },
    { value: 100000002, label: 'Restricted' },
  ];

  function baseTrackingProps(overrides: Partial<EmailTrackingPanelProps> = {}): EmailTrackingPanelProps {
    return {
      monitor: false,
      highPriority: true,
      accessPermission: 100000001,
      accessPermissionOptions: ACCESS_OPTIONS,
      onMonitorChange: jest.fn(),
      onHighPriorityChange: jest.fn(),
      onAccessPermissionChange: jest.fn(),
      ...overrides,
    };
  }

  it('reads current monitor/high-priority/access-permission values from the record', () => {
    renderWithProvider(<EmailTrackingPanel {...baseTrackingProps()} />);

    const switches = screen.getAllByRole('switch');
    expect(switches[0]).not.toBeChecked(); // monitor: false
    expect(switches[1]).toBeChecked(); // highPriority: true
    expect(screen.getByRole('radio', { name: 'Limited' })).toHaveAttribute('aria-checked', 'true');
  });

  it('writes back a monitor toggle', () => {
    const onMonitorChange = jest.fn();
    renderWithProvider(<EmailTrackingPanel {...baseTrackingProps({ onMonitorChange })} />);

    fireEvent.click(screen.getAllByRole('switch')[0]);
    expect(onMonitorChange).toHaveBeenCalledWith(true);
  });

  it('writes back an access-permission change', () => {
    const onAccessPermissionChange = jest.fn();
    renderWithProvider(<EmailTrackingPanel {...baseTrackingProps({ onAccessPermissionChange })} />);

    fireEvent.click(screen.getByRole('radio', { name: 'Restricted' }));
    expect(onAccessPermissionChange).toHaveBeenCalledWith(100000002);
  });

  it('surfaces an inline error when the write callback rejects', async () => {
    const onMonitorChange = jest.fn().mockRejectedValue(new Error('save failed'));
    renderWithProvider(<EmailTrackingPanel {...baseTrackingProps({ onMonitorChange })} />);

    fireEvent.click(screen.getAllByRole('switch')[0]);
    await waitFor(() => expect(screen.getByText('save failed')).toBeInTheDocument());
  });

  it('renders correctly under a dark FluentProvider theme (ADR-021) with no console errors', () => {
    const errorSpy = jest.spyOn(console, 'error').mockImplementation(() => {});
    renderWithProvider(<EmailTrackingPanel {...baseTrackingProps()} />, webDarkTheme);

    expect(screen.getAllByRole('switch')).toHaveLength(2);
    expect(errorSpy).not.toHaveBeenCalled();
    errorSpy.mockRestore();
  });
});

describe('Negative: production logic, not the stale stub', () => {
  it('imports from the task-020 shared logic/connections extraction, never the CommunicationPage stub', () => {
    const reviewSrc = fs.readFileSync(path.join(__dirname, '../EmailConnectionsReview.tsx'), 'utf8');
    const trackingSrc = fs.readFileSync(path.join(__dirname, '../EmailTrackingPanel.tsx'), 'utf8');

    expect(reviewSrc).toMatch(/from '\.\.\/\.\.\/logic\/connections'/);
    // Neither file IMPORTS from the stale CommunicationPage stub (a doc-comment
    // mentioning it by name, to explain what NOT to use, is fine — an import is not).
    expect(reviewSrc).not.toMatch(/from ['"][^'"]*CommunicationPage[^'"]*['"]/);
    expect(trackingSrc).not.toMatch(/from ['"][^'"]*CommunicationPage[^'"]*['"]/);
    // NFR-05: no PCF-boundary React.ComponentType cast anywhere in this view
    // (an actual cast always parametrizes the type, e.g. `as React.ComponentType<Props>`
    // — matching the generic bracket excludes this file's own prose ABOUT the ban).
    expect(reviewSrc).not.toMatch(/as React\.ComponentType</);
    expect(trackingSrc).not.toMatch(/as React\.ComponentType</);
  });
});
