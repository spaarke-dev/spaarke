/**
 * WorkspaceShell/MetricCard + MetricCardRow — unit tests (task 052, D-24).
 *
 * The count-filter extension is opt-in: `selected` (aria-pressed), `note`, `progress`, `disableWhenEmpty` and the
 * `wide` layout. The first describe block pins the EXISTING behaviour with the new props absent (it passes on the code
 * before the extension too); the rest fail on it.
 */

import '@testing-library/jest-dom';
import * as React from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
import { FluentProvider, webDarkTheme, webLightTheme } from '@fluentui/react-components';
import { AlertRegular } from '@fluentui/react-icons';

import { MetricCard, MetricCardRow } from '..';
import type { MetricCardConfig } from '..';

const themed = (ui: React.ReactElement, dark = false) =>
  render(<FluentProvider theme={dark ? webDarkTheme : webLightTheme}>{ui}</FluentProvider>);

const base = { label: 'Matters', icon: AlertRegular, ariaLabel: 'View matters' };

describe('MetricCard — existing behaviour, new props absent', () => {
  it('is a button with the label and the value, and no aria-pressed or aria-disabled', () => {
    themed(<MetricCard {...base} value={7} />);
    const card = screen.getByRole('button', { name: 'View matters' });
    expect(card).toHaveTextContent('7');
    expect(card).toHaveTextContent('Matters');
    expect(card).not.toHaveAttribute('aria-pressed');
    expect(card).not.toHaveAttribute('aria-disabled');
    expect(card).toHaveAttribute('tabindex', '0');
  });

  it('click, Enter and Space call onClick', () => {
    const onClick = jest.fn();
    themed(<MetricCard {...base} value={7} onClick={onClick} />);
    const card = screen.getByRole('button', { name: 'View matters' });
    fireEvent.click(card);
    fireEvent.keyDown(card, { key: 'Enter' });
    fireEvent.keyDown(card, { key: ' ' });
    expect(onClick).toHaveBeenCalledTimes(3);
  });

  it('a card showing 0 is STILL clickable (disabling at 0 is opt-in)', () => {
    const onClick = jest.fn();
    themed(<MetricCard {...base} value={0} onClick={onClick} />);
    const card = screen.getByRole('button', { name: 'View matters' });
    fireEvent.click(card);
    expect(onClick).toHaveBeenCalledTimes(1);
    expect(card).not.toHaveAttribute('aria-disabled');
  });

  it('shows an em dash for an undefined value, a spinner while loading, and the overdue badge', () => {
    const { rerender } = themed(<MetricCard {...base} />);
    expect(screen.getByRole('button')).toHaveTextContent('—');
    rerender(
      <FluentProvider theme={webLightTheme}>
        <MetricCard {...base} isLoading value={3} badgeVariant="overdue" badgeCount={2} />
      </FluentProvider>
    );
    expect(screen.getByRole('button')).not.toHaveTextContent('3');
    expect(screen.getByRole('button')).not.toHaveTextContent('Overdue');
    rerender(
      <FluentProvider theme={webLightTheme}>
        <MetricCard {...base} value={3} badgeVariant="overdue" badgeCount={2} />
      </FluentProvider>
    );
    expect(screen.getByRole('button')).toHaveTextContent('2 Overdue');
  });

  it('renders no note and no progress line', () => {
    themed(<MetricCard {...base} value={7} />);
    expect(screen.queryByText(/done today/)).toBeNull();
    expect(screen.queryByRole('progressbar')).toBeNull();
  });
});

describe('MetricCard — count-filter extension (D-24)', () => {
  it('selected exposes aria-pressed true/false; absent exposes none', () => {
    const { rerender } = themed(<MetricCard {...base} value={4} selected />);
    expect(screen.getByRole('button')).toHaveAttribute('aria-pressed', 'true');
    rerender(
      <FluentProvider theme={webLightTheme}>
        <MetricCard {...base} value={4} selected={false} />
      </FluentProvider>
    );
    expect(screen.getByRole('button')).toHaveAttribute('aria-pressed', 'false');
  });

  it('a selected card looks different from an unselected one (selected class applied)', () => {
    const { rerender } = themed(<MetricCard {...base} value={4} selected={false} />);
    const off = screen.getByRole('button').className;
    rerender(
      <FluentProvider theme={webLightTheme}>
        <MetricCard {...base} value={4} selected />
      </FluentProvider>
    );
    expect(screen.getByRole('button').className).not.toBe(off);
  });

  it('shows the note line', () => {
    themed(<MetricCard {...base} value={4} note="oldest 4 days" />);
    expect(screen.getByText('oldest 4 days')).toBeInTheDocument();
  });

  it('shows "n of m done today" and a decorative bar, and clamps done to [0, total]', () => {
    const { rerender } = themed(<MetricCard {...base} value={4} progress={{ done: 3, total: 7 }} />);
    expect(screen.getByText('3 of 7 done today')).toBeInTheDocument();
    rerender(
      <FluentProvider theme={webLightTheme}>
        <MetricCard {...base} value={4} progress={{ done: 9, total: 7 }} />
      </FluentProvider>
    );
    expect(screen.getByText('7 of 7 done today')).toBeInTheDocument();
  });

  it('hides progress when total is 0', () => {
    themed(<MetricCard {...base} value={0} progress={{ done: 0, total: 0 }} />);
    expect(screen.queryByText(/done today/)).toBeNull();
  });

  it('disableWhenEmpty disables a card at count 0: aria-disabled, out of the tab order, no click, no keyboard activation', () => {
    const onClick = jest.fn();
    themed(<MetricCard {...base} value={0} disableWhenEmpty onClick={onClick} />);
    const card = screen.getByRole('button', { name: 'View matters' });
    expect(card).toHaveAttribute('aria-disabled', 'true');
    expect(card).toHaveAttribute('tabindex', '-1');
    fireEvent.click(card);
    fireEvent.keyDown(card, { key: 'Enter' });
    fireEvent.keyDown(card, { key: ' ' });
    expect(onClick).not.toHaveBeenCalled();
  });

  it('disableWhenEmpty leaves a non-zero card, an unloaded card and a loading card enabled', () => {
    const onClick = jest.fn();
    const { rerender } = themed(<MetricCard {...base} value={2} disableWhenEmpty onClick={onClick} />);
    fireEvent.click(screen.getByRole('button'));
    expect(onClick).toHaveBeenCalledTimes(1);
    for (const props of [{ value: undefined }, { value: 0, isLoading: true }]) {
      rerender(
        <FluentProvider theme={webLightTheme}>
          <MetricCard {...base} {...props} disableWhenEmpty onClick={onClick} />
        </FluentProvider>
      );
      expect(screen.getByRole('button')).not.toHaveAttribute('aria-disabled');
    }
  });

  it('works without an icon (a count filter has none)', () => {
    themed(<MetricCard label="Coming due" ariaLabel="Coming due" value={1} />);
    expect(screen.getByRole('button')).toHaveTextContent('Coming due');
  });

  it('renders in dark mode with every state (no colour literal: the card sets only theme tokens)', () => {
    themed(
      <MetricCard
        {...base}
        value={0}
        disableWhenEmpty
        selected
        note="n"
        progress={{ done: 1, total: 2 }}
        layout="wide"
      />,
      true
    );
    expect(screen.getByRole('button')).toBeInTheDocument();
  });
});

describe('MetricCardRow', () => {
  const cards: MetricCardConfig[] = [
    { id: 'a', label: 'A', ariaLabel: 'A', value: 3, selected: true, note: 'oldest 2 days' },
    { id: 'b', label: 'B', ariaLabel: 'B', value: 0, disableWhenEmpty: true },
  ];

  it('passes selected, note, progress and disableWhenEmpty through to its cards', () => {
    themed(<MetricCardRow cards={[{ ...cards[0], progress: { done: 1, total: 4 } }, cards[1]]} layout="wide" />);
    expect(screen.getByRole('button', { name: 'A' })).toHaveAttribute('aria-pressed', 'true');
    expect(screen.getByText('oldest 2 days')).toBeInTheDocument();
    expect(screen.getByText('1 of 4 done today')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'B' })).toHaveAttribute('aria-disabled', 'true');
  });

  it('keeps its default group label, and takes a per-lane one', () => {
    const { rerender } = themed(<MetricCardRow cards={cards} />);
    expect(screen.getByRole('group', { name: 'Summary metrics' })).toBeInTheDocument();
    rerender(
      <FluentProvider theme={webLightTheme}>
        <MetricCardRow cards={cards} ariaLabel="Decide filters" layout="wide" />
      </FluentProvider>
    );
    expect(screen.getByRole('group', { name: 'Decide filters' })).toBeInTheDocument();
  });

  it('applies a different grid for the wide layout than for the square one', () => {
    const { rerender } = themed(<MetricCardRow cards={cards} />);
    const square = screen.getByRole('group').className;
    rerender(
      <FluentProvider theme={webLightTheme}>
        <MetricCardRow cards={cards} layout="wide" />
      </FluentProvider>
    );
    expect(screen.getByRole('group').className).not.toBe(square);
  });

  it('an existing config (no new fields) renders exactly as before: plain buttons, no aria-pressed', () => {
    themed(<MetricCardRow cards={[{ id: 'x', label: 'X', icon: AlertRegular, ariaLabel: 'X', value: 0 }]} />);
    const card = screen.getByRole('button', { name: 'X' });
    expect(card).not.toHaveAttribute('aria-pressed');
    expect(card).not.toHaveAttribute('aria-disabled');
  });
});
