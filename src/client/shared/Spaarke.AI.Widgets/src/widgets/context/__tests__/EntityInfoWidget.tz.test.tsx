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
 * This file makes the guard revert-proof by PINNING the process timezone to a
 * UTC-behind zone (America/New_York, UTC-4/-5) BEFORE any module that touches
 * `Intl`/`Date` loads — `process.env.TZ` set at top-of-module is read by
 * V8/ICU when each `Intl.DateTimeFormat` is constructed (verified: resolved
 * timezone reflects the set value). With the fix present the formatter's
 * explicit `timeZone:'UTC'` still yields "Sep 30, 2026"; if the fix is reverted
 * the formatter falls back to the pinned local zone and yields "Sep 29, 2026",
 * failing this test. (Confirmed locally by toggling the line.)
 *
 * TZ is restored in afterAll so the pin cannot leak into other test files
 * sharing this worker.
 */

const ORIGINAL_TZ = process.env.TZ;
// Must be set before the imports below (Intl reads TZ at formatter construction).
process.env.TZ = 'America/New_York';

import '@testing-library/jest-dom';
import React from 'react';
import { render, screen } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { PaneEventBus } from '../../../events/PaneEventBus';
import { PaneEventBusProvider } from '../../../events/PaneEventBusContext';
import EntityInfoWidget from '../EntityInfoWidget';
import type { EntityInfoData } from '../EntityInfoWidget';
import type { ContextWidgetProps } from '../../../types/widget-types';

afterAll(() => {
  if (ORIGINAL_TZ === undefined) {
    delete process.env.TZ;
  } else {
    process.env.TZ = ORIGINAL_TZ;
  }
});

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
  it('confirms the harness timezone is genuinely behind UTC (guard is meaningful)', () => {
    // Sanity: without an explicit timeZone the same date shifts back a day here.
    // If this ever prints America/New_York → local == UTC, the guard below is toothless.
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
