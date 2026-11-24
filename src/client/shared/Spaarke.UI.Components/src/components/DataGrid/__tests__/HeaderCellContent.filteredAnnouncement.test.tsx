/**
 * HeaderCellContent.filteredAnnouncement.test — a filtered column header is announced, not only drawn
 * (spaarke-SPA-external-access-platform-r4 task 032, owner 2026-11-23). The visual `Filter16Filled` glyph is
 * `aria-hidden`, so the trigger's accessible name carries the state: `<label> column options, filtered`.
 */
import * as React from 'react';
import { render, screen } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { HeaderCellContent } from '../HeaderCellContent';
import type { ChipDescriptor, ChipState } from '../filterChips/types';

const DESCRIPTOR: ChipDescriptor = { kind: 'text', attribute: 'sprk_name', label: 'Name' };

function renderHeader(state: ChipState) {
  return render(
    <FluentProvider theme={webLightTheme}>
      <HeaderCellContent label="Name" descriptor={DESCRIPTOR} state={state} onStateChange={() => undefined} />
    </FluentProvider>
  );
}

/** The trigger holds the dropdown chevron, plus the aria-hidden filter glyph when the column is filtered. */
const iconCount = (trigger: HTMLElement): number => trigger.querySelectorAll('svg').length;

describe('HeaderCellContent filtered announcement', () => {
  it('a filtered column shows the filter icon and its name says "filtered"', () => {
    renderHeader({ sprk_name: { kind: 'text', value: 'Engagement', op: 'contains' } });
    const trigger = screen.getByRole('button', { name: 'Name column options, filtered' });
    expect(iconCount(trigger)).toBe(2);
    expect(trigger.getAttribute('aria-label')).toBe('Name column options, filtered');
  });

  it('an unfiltered column has no filter icon and no "filtered" in its name (must not fire)', () => {
    renderHeader({});
    const trigger = screen.getByRole('button', { name: 'Name column options' });
    expect(iconCount(trigger)).toBe(1);
    expect(trigger.getAttribute('aria-label')).not.toContain('filtered');
  });

  it('a filter on another column does not mark this one (must not fire)', () => {
    renderHeader({ sprk_other: { kind: 'text', value: 'x', op: 'contains' } });
    const trigger = screen.getByRole('button', { name: 'Name column options' });
    expect(iconCount(trigger)).toBe(1);
  });
});
