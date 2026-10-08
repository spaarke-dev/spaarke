/** @jest-environment ../Spaarke.UI.Components/jest.newYorkEnvironment.js */
/**
 * EntityInfoWidget — timezone regression guard (F-9, e2e-completion-audit
 * 2026-07-10).
 *
 * The prod fix (task 021) pins the key-date formatter to `timeZone:'UTC'`
 * (EntityInfoWidget.tsx `formatDate`) so a date-ONLY ISO string like
 * "2026-09-30" (parsed as UTC midnight per the ECMAScript spec) never shifts a
 * calendar day back when formatted in a viewer timezone BEHIND UTC. The
 * covering assertion in EntityInfoWidget.test.tsx ("Sep 30, 2026") is NOT
 * hermetic: on a UTC CI runner it passes even if the fix is reverted, because
 * local == UTC there.
 *
 * This file makes the guard revert-proof by running in a UTC-behind zone
 * (America/New_York, UTC-4/-5) through the @jest-environment on line 1, which
 * sets TZ in the worker's real process and restores the previous zone on
 * teardown (task 098; a `process.env.TZ` assignment inside a jest test file
 * only changes Jest's per-file copy and never took effect). With the fix
 * present the formatter's explicit `timeZone:'UTC'` still yields
 * "Sep 30, 2026"; if the fix is reverted the formatter falls back to the
 * New York zone and yields "Sep 29, 2026", failing this test.
 */

// America/New_York is set by the @jest-environment above. Assigning process.env.TZ in a jest test file only
// changes Jest's per-file copy of process.env, so it never reached Node (task 098).
import '@testing-library/jest-dom';
import React from 'react';
import { render, screen } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { PaneEventBus } from '../../../events/PaneEventBus';
import { PaneEventBusProvider } from '../../../events/PaneEventBusContext';
import EntityInfoWidget from '../EntityInfoWidget';
import type { EntityInfoData } from '../EntityInfoWidget';
import type { ContextWidgetProps } from '../../../types/widget-types';

function renderWidget(data: EntityInfoData): void {
  const bus = new PaneEventBus();
  const props: ContextWidgetProps<EntityInfoData> = {
    data,
    widgetType: 'entity-info',
    isLoading: false,
  };
  render(
    <PaneEventBusProvider bus={bus}>
      <FluentProvider theme={webLightTheme}>
        <EntityInfoWidget {...props} />
      </FluentProvider>
    </PaneEventBusProvider>
  );
}

describe('EntityInfoWidget — key-date UTC pin is hermetic under a UTC-behind timezone (F-9)', () => {
  it('the zone override is in effect', () => {
    expect(Intl.DateTimeFormat().resolvedOptions().timeZone).toBe('America/New_York');
    expect(new Date('2026-09-30').getDate()).toBe(29);
  });

  it('confirms the harness timezone is genuinely behind UTC (guard is meaningful)', () => {
    // Sanity: without an explicit timeZone the same date shifts back a day here.
    // If this ever printed "Sep 30, 2026", local == UTC and the guard below would be toothless.
    const localShifted = new Intl.DateTimeFormat('en-US', {
      year: 'numeric',
      month: 'short',
      day: 'numeric',
    }).format(new Date('2026-09-30'));
    expect(localShifted).toBe('Sep 29, 2026');
  });

  it('renders the filing-deadline key date as the SOURCE calendar day (not shifted back)', () => {
    renderWidget({
      entityType: 'Matter',
      entityId: 'matter-001',
      displayName: 'Acme Corp v. Widget Co.',
      keyDates: [{ label: 'Filing Deadline', date: '2026-09-30' }],
    });

    // With the timeZone:'UTC' fix → "Sep 30, 2026". Reverting the fix makes the
    // formatter use the pinned America/New_York zone → "Sep 29, 2026" → FAILS.
    expect(screen.getByText('Sep 30, 2026')).toBeInTheDocument();
    expect(screen.queryByText('Sep 29, 2026')).not.toBeInTheDocument();
  });
});
