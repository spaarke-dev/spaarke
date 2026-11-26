import '@testing-library/jest-dom';
import { fireEvent, render, screen, within } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { RuleGroups } from '../components/RuleGroups';
import type { ReviewLine } from '../services/invoiceReviewApi';
import { finding, flagged, line } from './testKit';

// Owner review 2026-11-25 (INV-456.1 on dev): By rule read "No rule findings on these lines." while line 2 was To decide for a UTBMS code
// mismatch. The view now lists the checks that are not billing rules under their own heading.

function renderGroups(lines: ReviewLine[], onSelectLine = jest.fn()) {
  render(
    <FluentProvider theme={webLightTheme}>
      <RuleGroups lines={lines} canWrite={false} busy={false} onSelectLine={onSelectLine} onApply={jest.fn()} />
    </FluentProvider>
  );
  return onSelectLine;
}

const mismatched = (n: number): ReviewLine => {
  const base = flagged(n, { findings: [], findingCount: 0 });
  return { ...base, codes: { ...base.codes, task: { asBilled: 'L410', code: 'L190', firmCode: 'L410', mismatch: true } } };
};

describe('By rule view: other checks', () => {
  it('shows a code mismatch with no policy finding under "Other checks", and its line link selects the line', () => {
    const onSelect = renderGroups([line(1), mismatched(2)]);

    expect(screen.queryByText(/No rule findings/)).not.toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Other checks (not billing rules)' })).toBeInTheDocument();
    const group = screen.getByTestId('check-group-code-mismatch');
    expect(group).toHaveAccessibleName("UTBMS code differs from the firm's, 1 line");
    fireEvent.click(within(group).getByRole('button', { name: 'Open line 2' })); // a Fluent Link with no href renders as a button
    expect(onSelect).toHaveBeenCalledWith(line(2).id);
  });

  it('lists rule groups first and the other checks after them', () => {
    renderGroups([flagged(1, { findings: [finding()] }), mismatched(2)]);

    const sections = screen.getAllByRole('region');
    expect(sections.map(s => s.getAttribute('data-testid'))).toEqual([expect.stringMatching(/^rule-group-/), 'check-group-code-mismatch']);
  });

  it('shows the empty state only when there is neither a rule finding nor another check', () => {
    renderGroups([line(1), line(2)]);

    expect(screen.getByText('No rule findings or other checks on these lines.')).toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: /Other checks/ })).not.toBeInTheDocument();
  });
});
