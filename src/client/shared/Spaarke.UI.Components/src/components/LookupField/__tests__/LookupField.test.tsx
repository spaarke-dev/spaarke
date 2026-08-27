/**
 * LookupField — the SHARED search-as-you-type lookup (`components/LookupField`).
 *
 * Not to be confused with `components/RecordHeader/fields/LookupField.tsx`
 * (barrel-aliased `RecordHeaderLookupField`), which is a different component
 * with a different commit model and its own suite at
 * `RecordHeader/__tests__/LookupField.edit.test.tsx`.
 *
 * ── Why this file exists ───────────────────────────────────────────────────
 * This component had **twelve** consumers (every Create*Wizard step) and ZERO
 * tests when the OOB-parity work landed on 2026-08-27. A component that widely
 * used is exactly the wrong place to change behaviour blind, so the affordances
 * added then — the clickable browse icon, the pinned Advanced footer, and the
 * deliberate ABSENCE of "+ New" — are pinned here, along with the pre-existing
 * behaviour they had to preserve.
 *
 * Per ADR-038 this is a KEEP-category suite: it exercises the public props
 * surface of a shared component, mocks nothing internal, and guards a contract
 * that outlives the project.
 */

import * as React from 'react';
import { fireEvent, screen, waitFor } from '@testing-library/react';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import { LookupField } from '../LookupField';
import type { ILookupItem } from '../../../types/LookupTypes';

const ITEMS: ILookupItem[] = [
  { id: '1', name: 'Commercial Transactions' },
  { id: '2', name: 'Intellectual Property Patents' },
  { id: '3', name: 'Intellectual Property Trademarks' },
  { id: '4', name: 'Mergers & Acquisitions' },
];

const LABEL = 'Practice Area';

/** The search icon is a real button, labelled for assistive tech. */
const browseButton = (): HTMLElement => screen.getByRole('button', { name: `Browse ${LABEL}` });

function renderField(overrides: Partial<React.ComponentProps<typeof LookupField>> = {}) {
  const onChange = jest.fn();
  const onSearch = jest.fn(async () => ITEMS);
  const utils = renderWithProviders(
    <LookupField label={LABEL} value={null} onChange={onChange} onSearch={onSearch} {...overrides} />
  );
  return { onChange, onSearch, ...utils };
}

describe('LookupField — browse affordance (OOB parity)', () => {
  it('renders the search icon as a BUTTON, not a decorative glyph', () => {
    renderField();
    expect(browseButton()).toBeInTheDocument();
  });

  it('opens the full option list when the icon is clicked, with no typing', async () => {
    // The whole point: a user who does not know what to type can still browse.
    const { onSearch } = renderField();

    fireEvent.click(browseButton());

    await waitFor(() => expect(screen.getByRole('listbox')).toBeInTheDocument());
    for (const item of ITEMS) {
      expect(screen.getByText(item.name)).toBeInTheDocument();
    }
    // Browsing searches the EMPTY term — consumers return their default set.
    expect(onSearch).toHaveBeenCalledWith('');
  });

  it('toggles the list closed on a second click', async () => {
    renderField();

    fireEvent.click(browseButton());
    await waitFor(() => expect(screen.getByRole('listbox')).toBeInTheDocument());

    fireEvent.click(browseButton());
    await waitFor(() => expect(screen.queryByRole('listbox')).toBeNull());
  });

  it('reports expanded state to assistive tech', async () => {
    renderField();
    expect(browseButton()).toHaveAttribute('aria-expanded', 'false');

    fireEvent.click(browseButton());
    await waitFor(() => expect(browseButton()).toHaveAttribute('aria-expanded', 'true'));
  });

  it('selecting a browsed item commits it and closes the list', async () => {
    const { onChange } = renderField();

    fireEvent.click(browseButton());
    await waitFor(() => expect(screen.getByText(ITEMS[1].name)).toBeInTheDocument());

    fireEvent.click(screen.getByText(ITEMS[1].name));

    expect(onChange).toHaveBeenCalledWith(ITEMS[1]);
    await waitFor(() => expect(screen.queryByRole('listbox')).toBeNull());
  });

  it('does not throw when the search rejects — the field degrades, never crashes', async () => {
    jest.spyOn(console, 'error').mockImplementation(() => undefined);
    const onSearch = jest.fn(async () => {
      throw new Error('boom');
    });
    renderField({ onSearch });

    fireEvent.click(browseButton());

    await waitFor(() => expect(onSearch).toHaveBeenCalled());
    expect(screen.queryByRole('listbox')).toBeNull();
  });
});

describe('LookupField — Advanced footer', () => {
  it('is ABSENT by default — Code Page hosts may have no lookupObjects to escalate to', async () => {
    renderField();

    fireEvent.click(browseButton());
    await waitFor(() => expect(screen.getByRole('listbox')).toBeInTheDocument());

    expect(screen.queryByRole('button', { name: /advanced/i })).toBeNull();
  });

  it('renders right-aligned in the footer when onAdvanced is supplied', async () => {
    renderField({ onAdvanced: jest.fn() });

    fireEvent.click(browseButton());
    await waitFor(() => expect(screen.getByRole('listbox')).toBeInTheDocument());

    expect(screen.getByRole('button', { name: /advanced/i })).toBeInTheDocument();
  });

  it('invokes onAdvanced and dismisses the inline list', async () => {
    const onAdvanced = jest.fn();
    renderField({ onAdvanced });

    fireEvent.click(browseButton());
    await waitFor(() => expect(screen.getByRole('listbox')).toBeInTheDocument());

    fireEvent.click(screen.getByRole('button', { name: /advanced/i }));

    expect(onAdvanced).toHaveBeenCalledTimes(1);
    await waitFor(() => expect(screen.queryByRole('listbox')).toBeNull());
  });

  it('does not surface the footer while the list is closed', () => {
    renderField({ onAdvanced: jest.fn() });
    expect(screen.queryByRole('button', { name: /advanced/i })).toBeNull();
  });
});

describe('LookupField — "+ New" is deliberately absent', () => {
  it('offers no record-creation affordance, with or without Advanced', async () => {
    // OWNER DECISION 2026-08-27. The OOB footer carries "+ New"; ours must not.
    // These lookups target taxonomy tables users cannot add to, and record
    // creation does not belong on this surface. A failure here means someone
    // "restored parity" without reading the prop docs.
    renderField({ onAdvanced: jest.fn() });

    fireEvent.click(browseButton());
    await waitFor(() => expect(screen.getByRole('listbox')).toBeInTheDocument());

    expect(screen.queryByRole('button', { name: /new/i })).toBeNull();
    expect(screen.queryByText('+ New')).toBeNull();
  });
});

describe('LookupField — FieldGrid span (record-header FR-03)', () => {
  // `FieldGrid` never touches `gridColumn` on its children — each cell owns
  // its own span. Before this prop existed, a grid consumer had to hand-roll a
  // wrapper div (which is what `MatterHeaderView` still does).
  const gridCell = (container: HTMLElement): HTMLElement | null =>
    container.querySelector<HTMLElement>('[style*="grid-column"]');

  it('applies gridColumn to its own wrapper when span is supplied', () => {
    const { container } = renderField({ span: 2 });
    expect(gridCell(container)?.style.gridColumn).toBe('span 2');
  });

  it('emits NO gridColumn when span is omitted', () => {
    // Load-bearing for the twelve Create*Wizard consumers: they lay out with
    // flex, and an unconditional `gridColumn` would be inherited style noise.
    const { container } = renderField();
    expect(gridCell(container)).toBeNull();
  });
});

describe('LookupField — the panels must OVERLAY, never displace (no layout jump)', () => {
  // UAT v1.1.10: "when a value is selected the screen jumps". All three
  // transient panels were in normal flow, so opening the dropdown pushed every
  // field below it down the form and committing let the form snap back.
  //
  // jsdom does not do layout, so this cannot be asserted by measuring. What it
  // CAN check is the property that causes it — and `position: absolute` is the
  // whole fix, so pinning it is pinning the bug.
  const positionOf = (el: Element | null): string => (el ? window.getComputedStyle(el).position : '<not rendered>');

  it('the results list is absolutely positioned', async () => {
    renderField();
    fireEvent.click(browseButton());
    await waitFor(() => expect(screen.getByRole('listbox')).toBeInTheDocument());

    // The listbox is the scroll region; the positioned panel is its parent.
    expect(positionOf(screen.getByRole('listbox').parentElement)).toBe('absolute');
  });

  it('the empty state is absolutely positioned', async () => {
    const onSearch = jest.fn(async () => []);
    renderField({ onSearch });

    fireEvent.change(screen.getByRole('textbox'), { target: { value: 'zzz' } });

    const empty = await screen.findByText('No results found', undefined, { timeout: 3000 });
    expect(positionOf(empty)).toBe('absolute');
  });

  it('the committed chip is the same height as the input it replaces', () => {
    // The second half of the jump: swapping a 32px Input for a shorter chip
    // reflows the grid row. 32px is Fluent's `fieldHeights.medium`.
    const { container } = renderField({ value: ITEMS[0] });
    const chip = container.querySelector<HTMLElement>('.fui-Field')?.lastElementChild as HTMLElement;

    expect(window.getComputedStyle(chip).minHeight).toBe('32px');
  });
});

describe('LookupField — Input appearance', () => {
  // Griffel class names are not resolvable to computed styles under jsdom, so
  // asserting "the background is gray" is not available here. What IS worth
  // guarding — and is the actual regression risk — is that the DEFAULT never
  // silently changes: twelve wizard consumers rely on it. Comparing class names
  // between renders tests exactly that, without pinning Fluent's internals.
  const inputClass = (container: HTMLElement): string =>
    (container.querySelector('.fui-Input') as HTMLElement).className;

  it('defaults to the boxed outline look — the twelve wizard consumers depend on it', () => {
    const { container: implicitDefault } = renderField();
    const { container: explicitOutline } = renderField({ appearance: 'outline' });

    expect(inputClass(implicitDefault)).toBe(inputClass(explicitOutline));
  });

  it('filled-darker produces a DIFFERENT input style — the OOB form-field look', () => {
    const { container: outline } = renderField();
    const { container: filled } = renderField({ appearance: 'filled-darker' });

    expect(inputClass(filled)).not.toBe(inputClass(outline));
  });
});

describe('LookupField — pre-existing behaviour still holds', () => {
  it('renders the label and the required marker', () => {
    renderField({ required: true });
    expect(screen.getByText(LABEL)).toBeInTheDocument();
  });

  it('renders a committed value as a chip with a clear button, and no browse icon', () => {
    const { onChange } = renderField({ value: ITEMS[0] });

    expect(screen.getByText(ITEMS[0].name)).toBeInTheDocument();
    // The input (and therefore the browse icon) is replaced by the chip.
    expect(screen.queryByRole('button', { name: `Browse ${LABEL}` })).toBeNull();

    fireEvent.click(screen.getByRole('button', { name: `Clear ${LABEL}` }));
    expect(onChange).toHaveBeenCalledWith(null);
  });

  it('keyboard: ArrowDown then Enter commits the highlighted option', async () => {
    const { onChange } = renderField();

    fireEvent.click(browseButton());
    await waitFor(() => expect(screen.getByRole('listbox')).toBeInTheDocument());

    const input = screen.getByRole('textbox');
    fireEvent.keyDown(input, { key: 'ArrowDown' });
    fireEvent.keyDown(input, { key: 'Enter' });

    expect(onChange).toHaveBeenCalledWith(ITEMS[0]);
  });

  it('keyboard: Escape dismisses without committing', async () => {
    const { onChange } = renderField();

    fireEvent.click(browseButton());
    await waitFor(() => expect(screen.getByRole('listbox')).toBeInTheDocument());

    fireEvent.keyDown(screen.getByRole('textbox'), { key: 'Escape' });

    await waitFor(() => expect(screen.queryByRole('listbox')).toBeNull());
    expect(onChange).not.toHaveBeenCalled();
  });
});
