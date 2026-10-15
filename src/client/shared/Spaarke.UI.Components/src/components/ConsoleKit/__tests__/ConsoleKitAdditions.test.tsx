/**
 * ConsoleKit additions — unit tests (ebilling-r1 task 109). Covers the stated contracts:
 *  - StatusBar / RecordRow `statusOverride`: given label + tone shown; absent = the Console mapping, unchanged
 *  - OutcomeCards: radio group with a name; one chosen; body only under the chosen card; disabled option; keyboard
 *  - CountFilters: "label: count" names, aria-pressed, one selected, counts unchanged by selection, onChange
 *  - Disclosure: aria-expanded / aria-controls, expands and collapses, body not mounted while closed
 *  - ADR-021: the new components render under the dark theme; no react-19-only hooks in the new sources
 */

import '@testing-library/jest-dom';
import * as fs from 'fs';
import * as path from 'path';
import * as React from 'react';
import { fireEvent, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FluentProvider, webDarkTheme, webLightTheme } from '@fluentui/react-components';

import { CONSOLE_STATE_BADGES, CountFilters, Disclosure, OutcomeCards, RecordRow, StatusBar } from '..';
import type { CountFilterItem, OutcomeCardOption } from '..';

function renderThemed(ui: React.ReactElement, dark = false) {
  return render(<FluentProvider theme={dark ? webDarkTheme : webLightTheme}>{ui}</FluentProvider>);
}

describe('statusOverride on StatusBar', () => {
  it('shows the given label and tone, and the bar intent follows the tone', () => {
    renderThemed(
      <StatusBar
        state="Decided"
        statusOverride={{ label: 'Returned to firm', tone: 'warning', description: 'Sent back for correction.' }}
        recordId="DR-0001"
      />
    );
    const badge = screen.getByTestId('status-badge');
    expect(badge).toHaveTextContent('Returned to firm');
    expect(badge).toHaveAttribute('data-tone', 'warning');
    expect(badge).toHaveAttribute('aria-label', 'Returned to firm. Sent back for correction.');
    expect(screen.getByTestId('status-bar')).toHaveAttribute('data-intent', 'warning');
    expect(screen.queryByText('Decided')).not.toBeInTheDocument();
  });

  it('names the badge by its label alone when the override has no description', () => {
    renderThemed(<StatusBar state="Decided" statusOverride={{ label: 'Rejected', tone: 'critical' }} />);
    expect(screen.getByTestId('status-badge')).toHaveAttribute('aria-label', 'Rejected');
    expect(screen.getByTestId('status-bar')).toHaveAttribute('data-intent', 'error');
  });

  it('uses the override tone for the intent even where the state has its own intent exception (Dismissed)', () => {
    renderThemed(<StatusBar state="Dismissed" statusOverride={{ label: 'Approved', tone: 'success' }} />);
    expect(screen.getByTestId('status-bar')).toHaveAttribute('data-intent', 'success');
  });

  it('without an override renders exactly the Console mapping', () => {
    renderThemed(<StatusBar state="Dismissed" />);
    const badge = screen.getByTestId('status-badge');
    expect(badge).toHaveTextContent(CONSOLE_STATE_BADGES.Dismissed.label);
    expect(badge).toHaveAttribute('data-tone', CONSOLE_STATE_BADGES.Dismissed.tone);
    expect(badge).toHaveAttribute(
      'aria-label',
      `${CONSOLE_STATE_BADGES.Dismissed.label}. ${CONSOLE_STATE_BADGES.Dismissed.description}`
    );
    expect(screen.getByTestId('status-bar')).toHaveAttribute('data-intent', 'info');
  });

  it('renders identically with statusOverride undefined and absent', () => {
    // Generated Fluent ids differ per render; everything else must be byte-for-byte equal.
    const markup = () => screen.getByTestId('status-bar').outerHTML.replace(/fui-_r_[0-9a-z]+_/g, 'fui-id');
    const a = renderThemed(<StatusBar state="Denied" recordId="DR-1" />);
    const without = markup();
    a.unmount();
    renderThemed(<StatusBar state="Denied" recordId="DR-1" statusOverride={undefined} />);
    expect(markup()).toBe(without);
  });

  it('leaves the Console table with its nine entries', () => {
    expect(Object.keys(CONSOLE_STATE_BADGES)).toHaveLength(9);
  });
});

describe('statusOverride on RecordRow', () => {
  it('shows the given label and tone', () => {
    renderThemed(
      <RecordRow
        recordId="DR-0002"
        title="Line 4 disallowed"
        state="Decided"
        statusOverride={{ label: 'Disallowed', tone: 'critical' }}
      />
    );
    const badge = screen.getByTestId('status-badge');
    expect(badge).toHaveTextContent('Disallowed');
    expect(badge).toHaveAttribute('data-tone', 'critical');
  });

  it('without an override shows the Console label and tone', () => {
    renderThemed(<RecordRow recordId="DR-0002" title="x" state="Authorized" />);
    const badge = screen.getByTestId('status-badge');
    expect(badge).toHaveTextContent(CONSOLE_STATE_BADGES.Authorized.label);
    expect(badge).toHaveAttribute('data-tone', CONSOLE_STATE_BADGES.Authorized.tone);
  });
});

type Choice = 'approve' | 'reduce' | 'disallow';

function Cards(props: { initial?: Choice; onChange?: (v: Choice) => void; disableReduce?: boolean }) {
  const [value, setValue] = React.useState<Choice | undefined>(props.initial);
  const options: OutcomeCardOption<Choice>[] = [
    { value: 'approve', label: 'Accept', caption: 'Suggested', body: <span>Accept body</span> },
    { value: 'reduce', label: 'Reduce', disabled: props.disableReduce, body: <span>Reduce body</span> },
    { value: 'disallow', label: 'Disallow', body: <span>Disallow body</span> },
  ];
  return (
    <OutcomeCards
      options={options}
      value={value}
      ariaLabel="Decision"
      onChange={v => {
        setValue(v);
        props.onChange?.(v);
      }}
    />
  );
}

describe('OutcomeCards', () => {
  it('is a named radio group with one radio per option, in the given order', () => {
    renderThemed(<Cards />);
    const group = screen.getByRole('radiogroup', { name: 'Decision' });
    const radios = within(group).getAllByRole('radio');
    expect(radios.map(r => (r as HTMLInputElement).value)).toEqual(['approve', 'reduce', 'disallow']);
    expect(screen.getByText('Suggested')).toBeInTheDocument();
  });

  it('shows no body and no selection until something is chosen', () => {
    renderThemed(<Cards />);
    for (const r of screen.getAllByRole('radio')) expect(r).not.toBeChecked();
    expect(screen.queryByText(/body$/)).not.toBeInTheDocument();
  });

  it('on click: selects that card, reports the value once, and shows only the chosen card body', () => {
    const onChange = jest.fn();
    renderThemed(<Cards onChange={onChange} />);
    fireEvent.click(screen.getByRole('radio', { name: /Reduce/ }));
    expect(onChange).toHaveBeenCalledTimes(1);
    expect(onChange).toHaveBeenCalledWith('reduce');
    expect(screen.getByRole('radio', { name: /Reduce/ })).toBeChecked();
    expect(screen.getByText('Reduce body')).toBeInTheDocument();
    expect(screen.queryByText('Accept body')).not.toBeInTheDocument();
    expect(screen.queryByText('Disallow body')).not.toBeInTheDocument();
    expect(screen.getByTestId('outcome-card-reduce')).toHaveAttribute('data-selected', 'true');
    expect(screen.getByTestId('outcome-card-approve')).toHaveAttribute('data-selected', 'false');

    fireEvent.click(screen.getByRole('radio', { name: /Disallow/ }));
    expect(screen.getByText('Disallow body')).toBeInTheDocument();
    expect(screen.queryByText('Reduce body')).not.toBeInTheDocument();
  });

  it('shows the body of an initially chosen value without any interaction', () => {
    renderThemed(<Cards initial="approve" />);
    expect(screen.getByRole('radio', { name: /Accept/ })).toBeChecked();
    expect(screen.getByText('Accept body')).toBeInTheDocument();
  });

  it('cannot choose a disabled option, and reports nothing', async () => {
    const onChange = jest.fn();
    renderThemed(<Cards disableReduce onChange={onChange} />);
    const reduce = screen.getByRole('radio', { name: /Reduce/ });
    expect(reduce).toBeDisabled();
    await userEvent.click(reduce);
    expect(onChange).not.toHaveBeenCalled();
    expect(reduce).not.toBeChecked();
    expect(screen.queryByText('Reduce body')).not.toBeInTheDocument();
  });

  it('keyboard: Tab reaches the group, Space chooses, ArrowDown moves the choice to the next card', async () => {
    const onChange = jest.fn();
    renderThemed(<Cards onChange={onChange} />);
    await userEvent.tab();
    expect(screen.getByRole('radio', { name: /Accept/ })).toHaveFocus();
    await userEvent.keyboard(' ');
    expect(onChange).toHaveBeenLastCalledWith('approve');
    await userEvent.keyboard('{ArrowDown}');
    expect(onChange).toHaveBeenLastCalledWith('reduce');
    expect(screen.getByRole('radio', { name: /Reduce/ })).toBeChecked();
    expect(screen.getByText('Reduce body')).toBeInTheDocument();
  });

  it('keyboard: arrow navigation skips a disabled option', async () => {
    const onChange = jest.fn();
    renderThemed(<Cards disableReduce onChange={onChange} />);
    await userEvent.tab();
    await userEvent.keyboard(' ');
    await userEvent.keyboard('{ArrowDown}');
    expect(onChange).toHaveBeenLastCalledWith('disallow');
  });

  it('records nothing by itself: it only reports the chosen value', () => {
    const onChange = jest.fn();
    renderThemed(<Cards onChange={onChange} />);
    expect(screen.queryByRole('button')).not.toBeInTheDocument();
    expect(onChange).not.toHaveBeenCalled();
  });
});

type Bucket = 'all' | 'flagged' | 'clean';
const BUCKETS: CountFilterItem<Bucket>[] = [
  { key: 'all', label: 'All', count: 24, sub: '3 of 24 decided' },
  { key: 'flagged', label: 'To decide', count: 5 },
  { key: 'clean', label: 'Clean', count: 0 },
];

function Filters(props: { onChange?: (k: Bucket) => void }) {
  const [value, setValue] = React.useState<Bucket>('all');
  return (
    <CountFilters
      items={BUCKETS}
      value={value}
      ariaLabel="Filter lines"
      onChange={k => {
        setValue(k);
        props.onChange?.(k);
      }}
    />
  );
}

describe('CountFilters', () => {
  it('is a named group of toggle buttons named "label: count", with the sub-line when given', () => {
    renderThemed(<Filters />);
    const group = screen.getByRole('group', { name: 'Filter lines' });
    const buttons = within(group).getAllByRole('button');
    expect(buttons.map(b => b.getAttribute('aria-label'))).toEqual([
      'All: 24, 3 of 24 decided',
      'To decide: 5',
      'Clean: 0',
    ]);
  });

  it('exactly one card is pressed, and it is the value', () => {
    renderThemed(<Filters />);
    const pressed = screen.getAllByRole('button').filter(b => b.getAttribute('aria-pressed') === 'true');
    expect(pressed).toHaveLength(1);
    expect(pressed[0]).toHaveAttribute('aria-label', 'All: 24, 3 of 24 decided');
  });

  it('selecting a card moves the single selection, calls onChange, and leaves every count unchanged', () => {
    const onChange = jest.fn();
    renderThemed(<Filters onChange={onChange} />);
    fireEvent.click(screen.getByTestId('count-filter-flagged'));
    expect(onChange).toHaveBeenCalledWith('flagged');
    const pressed = screen.getAllByRole('button').filter(b => b.getAttribute('aria-pressed') === 'true');
    expect(pressed).toHaveLength(1);
    expect(pressed[0]).toBe(screen.getByTestId('count-filter-flagged'));
    expect(screen.getByTestId('count-filter-all')).toHaveTextContent('24');
    expect(screen.getByTestId('count-filter-flagged')).toHaveTextContent('5');
    expect(screen.getByTestId('count-filter-clean')).toHaveTextContent('0');
  });

  it('renders a real zero count as 0', () => {
    renderThemed(<Filters />);
    expect(screen.getByTestId('count-filter-clean')).toHaveTextContent('0');
  });

  it('is operable by keyboard (Tab then Space on a card)', async () => {
    const onChange = jest.fn();
    renderThemed(<Filters onChange={onChange} />);
    await userEvent.tab();
    await userEvent.tab();
    expect(screen.getByTestId('count-filter-flagged')).toHaveFocus();
    await userEvent.keyboard(' ');
    expect(onChange).toHaveBeenCalledWith('flagged');
  });
});

describe('Disclosure', () => {
  it('is collapsed by default: aria-expanded false and the body is not mounted', () => {
    renderThemed(
      <Disclosure label="Why this was flagged">
        <span>Detail</span>
      </Disclosure>
    );
    const button = screen.getByRole('button', { name: 'Why this was flagged' });
    expect(button).toHaveAttribute('aria-expanded', 'false');
    expect(screen.queryByText('Detail')).not.toBeInTheDocument();
  });

  it('expands and collapses on click, and aria-controls names the body', () => {
    renderThemed(
      <Disclosure label="Why this was flagged">
        <span>Detail</span>
      </Disclosure>
    );
    const button = screen.getByRole('button', { name: 'Why this was flagged' });
    fireEvent.click(button);
    expect(button).toHaveAttribute('aria-expanded', 'true');
    const body = screen.getByTestId('disclosure-body');
    expect(body).toHaveTextContent('Detail');
    expect(body.id).toBeTruthy();
    expect(button).toHaveAttribute('aria-controls', body.id);
    fireEvent.click(button);
    expect(button).toHaveAttribute('aria-expanded', 'false');
    expect(screen.queryByText('Detail')).not.toBeInTheDocument();
  });

  it('can start open', () => {
    renderThemed(
      <Disclosure label="More" defaultOpen>
        <span>Detail</span>
      </Disclosure>
    );
    expect(screen.getByRole('button', { name: 'More' })).toHaveAttribute('aria-expanded', 'true');
    expect(screen.getByText('Detail')).toBeInTheDocument();
  });

  it('gives two disclosures different ids', () => {
    renderThemed(
      <>
        <Disclosure label="A" defaultOpen>
          <span>a</span>
        </Disclosure>
        <Disclosure label="B" defaultOpen>
          <span>b</span>
        </Disclosure>
      </>
    );
    const [a, b] = screen.getAllByTestId('disclosure-body');
    expect(a.id).not.toBe(b.id);
  });

  it('keyboard: Enter toggles it', async () => {
    renderThemed(
      <Disclosure label="Why">
        <span>Detail</span>
      </Disclosure>
    );
    await userEvent.tab();
    await userEvent.keyboard('{Enter}');
    expect(screen.getByRole('button', { name: 'Why' })).toHaveAttribute('aria-expanded', 'true');
  });
});

describe('ADR-021 and React-16 safety of the additions', () => {
  it('renders every addition under the dark theme', () => {
    renderThemed(
      <>
        <StatusBar state="Decided" statusOverride={{ label: 'Approved', tone: 'success' }} />
        <Cards initial="approve" />
        <Filters />
        <Disclosure label="Why" defaultOpen>
          <span>x</span>
        </Disclosure>
      </>,
      true
    );
    expect(screen.getByTestId('status-bar')).toBeInTheDocument();
    expect(screen.getByTestId('outcome-cards')).toBeInTheDocument();
    expect(screen.getByTestId('count-filters')).toBeInTheDocument();
    expect(screen.getByTestId('disclosure')).toBeInTheDocument();
  });

  it('the new sources use no useId / useSyncExternalStore / createRoot', () => {
    for (const f of ['OutcomeCards.tsx', 'CountFilters.tsx', 'Disclosure.tsx']) {
      const code = fs
        .readFileSync(path.join(__dirname, '..', f), 'utf8')
        .split('\n')
        .filter(l => !l.trim().startsWith('*') && !l.trim().startsWith('//'))
        .join('\n');
      expect({ f, hit: /useId|useSyncExternalStore|createRoot/.test(code) }).toEqual({ f, hit: false });
    }
  });
});
