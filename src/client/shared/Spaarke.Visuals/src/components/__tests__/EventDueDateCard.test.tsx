/**
 * EventDueDateCard — tier → colour (task 081 / H1, owner decision 2026-10-05).
 *
 * The card holds NO tier boundaries: the tier arrives as the `urgency` prop
 * (computed by the caller with the shared `dueUrgencyForDays`). The palette is
 * SmartTodo's due badge palette:
 *   overdue → red (danger) · 3d → dark orange (severe) · 7d → yellow (warning)
 *   · 10d and none → neutral (informative). Green is gone.
 */
import * as React from 'react';
import { render, screen } from '@testing-library/react';
import { FluentProvider, webLightTheme, tokens } from '@fluentui/react-components';
import { EventDueDateCard, type EventDueUrgency } from '../EventDueDateCard';

function renderCard(urgency: EventDueUrgency, daysUntilDue = 5): () => void {
  const { unmount } = render(
    <FluentProvider theme={webLightTheme}>
      <EventDueDateCard
        eventId="e1"
        eventName="Filing"
        eventTypeName="Deadline"
        dueDate={new Date(2026, 9, 5)}
        daysUntilDue={daysUntilDue}
        isOverdue={false}
        urgency={urgency}
      />
    </FluentProvider>
  );
  return unmount;
}

function dateColumnBackground(): string {
  const label = screen.getByText('05-OCT-2026');
  return (label.parentElement as HTMLElement).style.backgroundColor;
}

describe('EventDueDateCard — tier colours follow the urgency prop (SmartTodo palette)', () => {
  it.each([
    ['overdue', tokens.colorPaletteRedBackground2],
    ['3d', tokens.colorPaletteDarkOrangeBackground2],
    ['7d', tokens.colorPaletteYellowBackground2],
    ['10d', tokens.colorNeutralBackground3],
    ['none', tokens.colorNeutralBackground3],
  ] as const)('%s → date column %s', (urgency, expected) => {
    renderCard(urgency);
    expect(dateColumnBackground()).toBe(expected);
  });

  it('colour follows the urgency prop, not daysUntilDue (no boundary copy in this package)', () => {
    // daysUntilDue 1 was red under the old private tiers; the prop says 'none'.
    renderCard('none', 1);
    expect(dateColumnBackground()).toBe(tokens.colorNeutralBackground3);
  });

  it('no tier renders green any more', () => {
    for (const urgency of ['overdue', '3d', '7d', '10d', 'none'] as const) {
      const unmount = renderCard(urgency, 20);
      expect(dateColumnBackground()).not.toBe(tokens.colorPaletteGreenBackground2);
      unmount();
    }
  });
});
