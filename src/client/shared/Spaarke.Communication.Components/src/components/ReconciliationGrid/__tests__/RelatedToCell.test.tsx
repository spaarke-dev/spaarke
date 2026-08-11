/**
 * RelatedToCell.test.tsx — Pillar E "Related to" reconcile cell (task 052, FR-E3).
 *
 * Covers the closed acceptance set: an unassociated row shows blank + "Requires
 * review" + an open-picker icon; clicking it opens the REUSED `EmailConnectionsReview`
 * (same candidate cards the email-form review uses); Confirm writes THROUGH the
 * single `applyRegardingSelection`/`RegardingFieldMap` path with
 * `writeContext.hostRecordId` === `communicationId` (no second write path, no
 * forked picker); Create-new fires the host `onCreateNewRecord` intent; a confirmed
 * row shows the linked-record chip; and everything renders under the dark theme.
 *
 * Harness (candidate provenance + write-context mock) mirrors
 * EmailAssociationsAndTracking.test.tsx — the confirm path exercises the REAL
 * `applyRegardingSelection` over a mock `webApi.updateRecord` (the single writer).
 */
import * as React from 'react';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { FluentProvider, webDarkTheme, webLightTheme } from '@fluentui/react-components';
import { RelatedToCell } from '../RelatedToCell';
import { _resetNavPropCacheForTests, type IResolverWriteContext } from '../../../logic/connections';
import type { EmailConnectionsReviewProps } from '../../EmailAssociationsAndTracking/EmailAssociationsAndTracking.types';

const HOST_ID = '22222222-2222-2222-2222-222222222222';
const STATUS_PENDING = 100000001;
const STATUS_RESOLVED = 100000000;

// jsdom lacks ResizeObserver (Fluent MessageBar reflow needs it) — stub locally.
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

const NAV_PROPS_RESPONSE = {
  ok: true,
  json: async () => ({
    value: [
      {
        ReferencingAttribute: 'sprk_regardingmatter',
        ReferencingEntityNavigationPropertyName: 'sprk_RegardingMatter',
        ReferencedEntity: 'sprk_matter',
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

function provenance(): string {
  return JSON.stringify({
    version: 1,
    direction: 'inbound',
    decision: {
      status: '',
      autoFiled: false,
      killSwitchEnabled: false,
      autoFileThreshold: 0.85,
      topDeterministicConfidence: 0,
      topConfidence: 0,
      aiInvolved: false,
      reason: '',
    },
    rungsFired: [],
    candidates: [
      {
        field: 'sprk_regardingmatter',
        targetEntity: 'sprk_matter',
        targetId: 'mtr-1',
        targetName: 'Acme v Beta',
        reinforcedConfidence: 0.82,
        deterministicConfidence: 0.82,
        written: false,
        conflict: false,
        contributors: [
          {
            rung: 'RecordNameMatch',
            confidence: 0.82,
            provenance:
              'record-name-match:sprk_matter:where=subject:matched=name:name="Acme v Beta":number="MAT-1":reason="name in subject"',
          },
        ],
      },
    ],
    signals: [],
  });
}

function makeReview(overrides: Partial<EmailConnectionsReviewProps> = {}): EmailConnectionsReviewProps {
  return {
    communicationId: HOST_ID,
    associationStatus: STATUS_PENDING,
    associationProvenanceJson: provenance(),
    filedAssociations: [],
    writeContext: makeWriteContext(),
    ...overrides,
  };
}

function renderCell(ui: React.ReactElement, theme = webLightTheme) {
  return render(<FluentProvider theme={theme}>{ui}</FluentProvider>);
}

describe('RelatedToCell', () => {
  const originalFetch = global.fetch;
  beforeEach(() => {
    _resetNavPropCacheForTests();
    global.fetch = jest.fn().mockResolvedValue(NAV_PROPS_RESPONSE) as unknown as typeof fetch;
  });
  afterEach(() => {
    global.fetch = originalFetch;
    jest.restoreAllMocks();
  });

  it('shows blank + "Requires review" + an open-picker icon until confirmed', () => {
    renderCell(<RelatedToCell review={makeReview()} />);
    expect(screen.getByText('Requires review')).toBeInTheDocument();
    expect(screen.getByTestId('related-to-open-picker')).toBeInTheDocument();
    // Picker is closed initially.
    expect(screen.queryByTestId('email-connections-review')).not.toBeInTheDocument();
  });

  it('opens the reused EmailConnectionsReview (same candidate cards the form uses) on icon click', async () => {
    renderCell(<RelatedToCell review={makeReview()} />);
    fireEvent.click(screen.getByTestId('related-to-open-picker'));

    expect(await screen.findByTestId('email-connections-review')).toBeInTheDocument();
    expect(screen.getByRole('radio', { name: /Acme v Beta/ })).toBeInTheDocument();
  });

  it('Confirm writes via the single applyRegardingSelection path (hostRecordId===communicationId); fires onConfirmed', async () => {
    const review = makeReview();
    const onConfirmed = jest.fn();
    renderCell(<RelatedToCell review={review} onConfirmed={onConfirmed} />);

    fireEvent.click(screen.getByTestId('related-to-open-picker'));
    fireEvent.click(await screen.findByRole('radio', { name: /Acme v Beta/ }));
    fireEvent.click(screen.getByRole('button', { name: 'Confirm' }));

    await waitFor(() => expect(review.writeContext.webApi.updateRecord).toHaveBeenCalled());
    const call = (review.writeContext.webApi.updateRecord as jest.Mock).mock.calls[0];
    expect(call[0]).toBe('sprk_communication');
    expect(call[1]).toBe(HOST_ID); // hostRecordId === communicationId — the single write path
    await waitFor(() => expect(onConfirmed).toHaveBeenCalled());
  });

  it('fires the host onCreateNewRecord intent from the picker (no forked picker)', async () => {
    const onCreateNewRecord = jest.fn();
    renderCell(<RelatedToCell review={makeReview({ onCreateNewRecord })} />);

    fireEvent.click(screen.getByTestId('related-to-open-picker'));
    fireEvent.click(await screen.findByTestId('create-new-record'));

    expect(onCreateNewRecord).toHaveBeenCalledTimes(1);
  });

  it('shows the linked-record chip when the association is already confirmed', () => {
    renderCell(
      <RelatedToCell review={makeReview({ associationStatus: STATUS_RESOLVED, regardingRecordName: 'Acme v Beta' })} />
    );
    expect(screen.getByText('Acme v Beta')).toBeInTheDocument();
    expect(screen.queryByText('Requires review')).not.toBeInTheDocument();
  });

  it('renders under the dark Fluent theme (ADR-021) with no console errors', async () => {
    const errorSpy = jest.spyOn(console, 'error').mockImplementation(() => {});
    renderCell(<RelatedToCell review={makeReview()} />, webDarkTheme);
    fireEvent.click(screen.getByTestId('related-to-open-picker'));
    expect(await screen.findByTestId('email-connections-review')).toBeInTheDocument();
    await waitFor(() => expect(errorSpy).not.toHaveBeenCalled());
    errorSpy.mockRestore();
  });
});
