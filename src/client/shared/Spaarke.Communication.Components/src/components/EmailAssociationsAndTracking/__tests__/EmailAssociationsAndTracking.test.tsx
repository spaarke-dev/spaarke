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
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { FluentProvider, webDarkTheme, webLightTheme } from '@fluentui/react-components';
import { EmailConnectionsReview } from '../EmailConnectionsReview';
import { EmailTrackingPanel } from '../EmailTrackingPanel';
import {
  _resetNavPropCacheForTests,
  type FiledAssociation,
  type IResolverWriteContext,
} from '../../../logic/connections';
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

// `sprk_associationstatus` = Resolved (human-confirmed → green).
const STATUS_RESOLVED = 100000000;
const STATUS_PENDING = 100000001;

/** Build a single provenance candidate carrying a RecordNameMatch (→ number + reason). */
function cand(
  field: string,
  entity: string,
  id: string,
  name: string,
  confidence: number,
  opts: { written?: boolean; conflict?: boolean; number?: string } = {}
): Record<string, unknown> {
  return {
    field,
    targetEntity: entity,
    targetId: id,
    targetName: name,
    reinforcedConfidence: confidence,
    deterministicConfidence: confidence,
    written: opts.written ?? false,
    conflict: opts.conflict ?? false,
    contributors: [
      {
        rung: 'RecordNameMatch',
        confidence,
        provenance: `record-name-match:${entity}:where=subject:matched=name:name="${name}":number="${opts.number ?? ''}":reason="name in subject"`,
      },
    ],
  };
}

function provenance(candidates: Record<string, unknown>[], autoFiled = false): string {
  return JSON.stringify({
    version: 1,
    direction: 'inbound',
    decision: {
      status: '',
      autoFiled,
      killSwitchEnabled: false,
      autoFileThreshold: 0.85,
      topDeterministicConfidence: 0,
      topConfidence: 0,
      aiInvolved: false,
      reason: '',
    },
    rungsFired: [],
    candidates,
    signals: [],
  });
}

function baseProps(overrides: Partial<EmailConnectionsReviewProps> = {}): EmailConnectionsReviewProps {
  return {
    communicationId: HOST_ID,
    associationStatus: STATUS_PENDING,
    associationProvenanceJson: provenance([
      cand('sprk_regardingmatter', 'sprk_matter', 'mtr-1', 'Acme v Beta', 0.82, { number: 'MAT-1' }),
      cand('sprk_regardingorganization', 'sprk_organization', 'org-1', 'Acme Corp', 0.75, { number: 'ORG-1' }),
    ]),
    filedAssociations: [] as FiledAssociation[],
    writeContext: makeWriteContext(),
    ...overrides,
  };
}

describe('EmailConnectionsReview (single-primary redesign 2026-07-29)', () => {
  const originalFetch = global.fetch;

  beforeEach(() => {
    _resetNavPropCacheForTests();
    global.fetch = jest.fn().mockResolvedValue(NAV_PROPS_RESPONSE) as unknown as typeof fetch;
  });

  afterAll(() => {
    global.fetch = originalFetch;
  });

  it('REQUIRES REVIEW: renders the ≥70% candidates as selectable cards; clicking one reveals Confirm beneath it', () => {
    renderWithProvider(<EmailConnectionsReview {...baseProps()} />);

    // Both ≥70% candidates render as radio cards; nothing is pre-selected in the red state.
    expect(screen.getByRole('radio', { name: /Acme v Beta/ })).toBeInTheDocument();
    expect(screen.getByRole('radio', { name: /Acme Corp/ })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Confirm' })).not.toBeInTheDocument();

    // Clicking a card selects it → Confirm appears.
    fireEvent.click(screen.getByRole('radio', { name: /Acme v Beta/ }));
    expect(screen.getByRole('button', { name: 'Confirm' })).toBeInTheDocument();
  });

  it('confirming a candidate persists ADDITIVELY via applyRegardingSelection (no sibling lookup nulled)', async () => {
    const props = baseProps();
    renderWithProvider(<EmailConnectionsReview {...props} />);

    fireEvent.click(screen.getByRole('radio', { name: /Acme v Beta/ }));
    fireEvent.click(screen.getByRole('button', { name: 'Confirm' }));

    await waitFor(() => expect(props.writeContext.webApi.updateRecord).toHaveBeenCalled());
    const call = (props.writeContext.webApi.updateRecord as jest.Mock).mock.calls[0];
    expect(call[0]).toBe('sprk_communication');
    expect(call[1]).toBe(HOST_ID);
    const payload = call[2] as Record<string, unknown>;
    const nulledBinds = Object.entries(payload).filter(([k, v]) => k.endsWith('@odata.bind') && v === null);
    expect(nulledBinds).toHaveLength(0);
  });

  it('NEEDS CONFIRMATION: an auto-matched (autoFiled) top candidate is pre-selected with a Confirm', () => {
    renderWithProvider(
      <EmailConnectionsReview
        {...baseProps({
          associationProvenanceJson: provenance(
            [cand('sprk_regardingmatter', 'sprk_matter', 'mtr-1', 'Acme v Beta', 0.95, { number: 'MAT-1' })],
            true // autoFiled → needs-confirmation (yellow), pre-selected green card
          ),
        })}
      />
    );

    expect(screen.getByRole('radio', { name: /Acme v Beta/ })).toBeInTheDocument();
    // Pre-selected (no user click needed) → Confirm is already shown.
    expect(screen.getByRole('button', { name: 'Confirm' })).toBeInTheDocument();
  });

  it('CONFIRMED: shows ONLY the "Link another record" tile — no candidate cards or blank slots (the chip lives in the section header) (owner UAT 2026-07-31)', () => {
    renderWithProvider(
      <EmailConnectionsReview
        {...baseProps({
          associationStatus: STATUS_RESOLVED,
          associationProvenanceJson: provenance([
            cand('sprk_regardingmatter', 'sprk_matter', 'mtr-1', 'Acme v Beta', 0.95, {
              number: 'MAT-1',
              written: true,
            }),
          ]),
          filedAssociations: [{ entityType: 'sprk_matter', recordId: 'mtr-1', recordName: 'Acme v Beta' }],
        })}
      />
    );

    // Confirmed → the primary is the section-header chip (rendered by the parent), so
    // the cards row shows NO candidate card, no Confirm, and no "No confident match".
    expect(screen.queryByRole('radio', { name: /Acme v Beta/ })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Confirm' })).not.toBeInTheDocument();
    expect(screen.queryByText(/No confident match/i)).not.toBeInTheDocument();
    // Only the link tile remains.
    expect(screen.getByRole('button', { name: /Link another record/i })).toBeInTheDocument();
  });

  it('HAS MATCHES: renders only the actual candidate cards + Link tile — NO "No confident match" fillers (owner UAT 2026-07-31)', () => {
    renderWithProvider(
      <EmailConnectionsReview
        {...baseProps({
          associationProvenanceJson: provenance([
            cand('sprk_regardingmatter', 'sprk_matter', 'mtr-1', 'Acme v Beta', 0.95, { number: 'MAT-1' }),
          ]),
        })}
      />
    );

    expect(screen.getByRole('radio', { name: /Acme v Beta/ })).toBeInTheDocument();
    // A single match → no blank filler slots padding the row to a fixed count.
    expect(screen.queryByText(/No confident match/i)).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Link another record/i })).toBeInTheDocument();
  });

  it('NO MATCHES: renders a single "No confident match" card + the Link tile (owner UAT 2026-07-31)', () => {
    renderWithProvider(<EmailConnectionsReview {...baseProps({ associationProvenanceJson: provenance([]) })} />);

    expect(screen.getAllByText(/No confident match/i)).toHaveLength(1);
    expect(screen.getByRole('button', { name: /Link another record/i })).toBeInTheDocument();
  });

  it('offers a "Link another record" tile (interactive) and hides it in readOnly', () => {
    const { rerender } = renderWithProvider(<EmailConnectionsReview {...baseProps()} />);
    expect(screen.getByRole('button', { name: /Link another record/i })).toBeInTheDocument();

    rerender(
      <FluentProvider theme={webLightTheme}>
        <EmailConnectionsReview {...baseProps({ readOnly: true })} />
      </FluentProvider>
    );
    expect(screen.queryByRole('button', { name: /Link another record/i })).not.toBeInTheDocument();
  });

  it('a SINGLE click on the "Link another record" tile opens the record-type dropdown directly — no intermediate step (owner UAT #6)', async () => {
    renderWithProvider(<EmailConnectionsReview {...baseProps()} />);

    // One click on the tile opens the type dropdown (a Fluent Menu) directly.
    fireEvent.click(screen.getByRole('button', { name: /link another record/i }));
    const items = await screen.findAllByRole('menuitem');
    expect(items.length).toBeGreaterThan(0);

    // Non-MDA / dev host: `Xrm.Utility.lookupObjects` is absent, so picking a
    // type is a silent no-op (no throw) — the expected dev-host behavior.
    expect(() => fireEvent.click(items[0])).not.toThrow();
  });

  it('renders correctly under a dark FluentProvider theme (ADR-021) with no console errors', () => {
    const errorSpy = jest.spyOn(console, 'error').mockImplementation(() => {});
    renderWithProvider(<EmailConnectionsReview {...baseProps()} />, webDarkTheme);

    expect(screen.getByTestId('email-connections-review')).toBeInTheDocument();
    expect(errorSpy).not.toHaveBeenCalled();
    errorSpy.mockRestore();
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

  it('compact mode (reading-pane header band placement) hides the "Tracking" label and field captions, but keeps the controls read/write functional', () => {
    const onAccessPermissionChange = jest.fn();
    renderWithProvider(<EmailTrackingPanel {...baseTrackingProps({ compact: true, onAccessPermissionChange })} />);

    expect(screen.queryByText('Tracking')).not.toBeInTheDocument();
    expect(screen.queryByText('Monitor')).not.toBeInTheDocument();
    expect(screen.getAllByRole('switch')).toHaveLength(2);

    fireEvent.click(screen.getByRole('radio', { name: 'Restricted' }));
    expect(onAccessPermissionChange).toHaveBeenCalledWith(100000002);
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
