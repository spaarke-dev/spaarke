/**
 * EventDueDateCard — urgency colour at the canonical 3/7/10-day tier
 * boundaries (C-17 owner decision 2026-10-03; task 081 / F6).
 *
 * Mapping under test (three colours for five tiers):
 *   overdue, 0-3 days → red · 4-7 days → yellow · 8-10 days and 11+ → green.
 * Day 3 is red (most-urgent tier, matching `todoScoring.computeDueLabel`);
 * before task 081 it was yellow, and yellow ended at day 5.
 */
import * as React from 'react';
import { render, screen } from '@testing-library/react';
import { FluentProvider, webLightTheme, tokens } from '@fluentui/react-components';
import { EventDueDateCard } from '../EventDueDateCard';

function dateColumnBackground(daysUntilDue: number, isOverdue = false): string {
  render(
    <FluentProvider theme={webLightTheme}>
      <EventDueDateCard
        eventId="e1"
        eventName="Filing"
        eventTypeName="Deadline"
        dueDate={new Date(2026, 9, 5)}
        daysUntilDue={daysUntilDue}
        isOverdue={isOverdue}
      />
    </FluentProvider>
  );
  const label = screen.getByText('05-OCT-2026');
  return (label.parentElement as HTMLElement).style.backgroundColor;
}

describe('EventDueDateCard — 3/7/10 tier colours', () => {
  it.each([
    [3, tokens.colorPaletteRedBackground2],
    [4, tokens.colorPaletteYellowBackground2],
    [7, tokens.colorPaletteYellowBackground2],
    [8, tokens.colorPaletteGreenBackground2],
    [10, tokens.colorPaletteGreenBackground2],
    [11, tokens.colorPaletteGreenBackground2],
  ])('day %i → %s', (days, expected) => {
    expect(dateColumnBackground(days)).toBe(expected);
  });

  it('today (day 0) is red', () => {
    expect(dateColumnBackground(0)).toBe(tokens.colorPaletteRedBackground2);
  });

  it('overdue is red', () => {
    expect(dateColumnBackground(-2, true)).toBe(tokens.colorPaletteRedBackground2);
  });
});
