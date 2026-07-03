/**
 * RecordHeader field renderers — unit tests (FR-04).
 *
 * One `describe` block per sibling field renderer so parallel Group-B tasks
 * (005 TextField / 006 LookupField / 007 OptionSetField / 008 TextareaField)
 * can each append their block without merge conflict.
 */

import * as React from 'react';
import { screen, act } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import { LookupField, type ILookupFieldValue } from '../fields/LookupField';
import { OptionSetField } from '../fields/OptionSetField';
import { TextField } from '../fields/TextField';
import { TextareaField } from '../fields/TextareaField';

/** Hyphen glyph used for null / undefined values (FR-04). */
const EMPTY_VALUE_GLYPH = '—';

// ═══════════════════════════════════════════════════════════════════════════
// OptionSetField (task 007, FR-04 OptionSetField clause)
// ═══════════════════════════════════════════════════════════════════════════

describe('OptionSetField', () => {
  it('renders the label and the resolved option value', () => {
    renderWithProviders(<OptionSetField span={1} label="Status" value="Open" />);
    expect(screen.getByText('Status')).toBeInTheDocument();
    expect(screen.getByText('Open')).toBeInTheDocument();
  });

  it('renders a hyphen when value is null', () => {
    renderWithProviders(<OptionSetField span={1} label="Status" value={null} />);
    expect(screen.getByText('Status')).toBeInTheDocument();
    expect(screen.getByText(EMPTY_VALUE_GLYPH)).toBeInTheDocument();
  });

  it('renders a hyphen when value is undefined', () => {
    renderWithProviders(<OptionSetField span={1} label="Status" value={undefined} />);
    expect(screen.getByText('Status')).toBeInTheDocument();
    expect(screen.getByText(EMPTY_VALUE_GLYPH)).toBeInTheDocument();
  });

  it('renders a hyphen when value is an empty string', () => {
    renderWithProviders(<OptionSetField span={1} label="Status" value="" />);
    expect(screen.getByText(EMPTY_VALUE_GLYPH)).toBeInTheDocument();
  });

  it('applies gridColumn: span N inline style per span prop', () => {
    const { rerender } = renderWithProviders(
      <OptionSetField span={1} label="Status" value="Open" />,
    );
    const cell1 = screen.getByText('Open').parentElement!;
    expect(cell1.style.gridColumn).toBe('span 1');

    rerender(<OptionSetField span={2} label="Status" value="Open" />);
    const cell2 = screen.getByText('Open').parentElement!;
    expect(cell2.style.gridColumn).toBe('span 2');

    rerender(<OptionSetField span={3} label="Status" value="Open" />);
    const cell3 = screen.getByText('Open').parentElement!;
    expect(cell3.style.gridColumn).toBe('span 3');
  });

  it('carries data-field-type="optionset" and data-span attributes on the cell root', () => {
    renderWithProviders(<OptionSetField span={2} label="Priority" value="High" />);
    const cell = screen.getByText('High').parentElement!;
    expect(cell.getAttribute('data-field-type')).toBe('optionset');
    expect(cell.getAttribute('data-span')).toBe('2');
  });
});

// ═══════════════════════════════════════════════════════════════════════════
// LookupField (task 006, FR-04 LookupField clause)
// ═══════════════════════════════════════════════════════════════════════════

describe('LookupField', () => {
  // Preserve/restore the global Xrm across tests so the "no Xrm" case is not
  // polluted by a leftover mock from an earlier test.
  const originalXrm = (window as unknown as { Xrm?: unknown }).Xrm;

  afterEach(() => {
    if (originalXrm === undefined) {
      delete (window as unknown as { Xrm?: unknown }).Xrm;
    } else {
      (window as unknown as { Xrm?: unknown }).Xrm = originalXrm;
    }
  });

  const sampleValue: ILookupFieldValue = {
    id: '11111111-1111-1111-1111-111111111111',
    name: 'Acme Corp',
    entityType: 'sprk_matter',
  };

  // ────────────────────────────────────────────────────────────────────────
  // Render — label + Link with display name
  // ────────────────────────────────────────────────────────────────────────

  it('renders the label and value name as a clickable Link', () => {
    renderWithProviders(<LookupField label="Matter Type" value={sampleValue} span={1} />);

    // Label present.
    expect(screen.getByText('Matter Type')).toBeInTheDocument();

    // Value is rendered inside a Fluent v9 Link (role="link").
    const link = screen.getByRole('link', { name: 'Acme Corp' });
    expect(link).toBeInTheDocument();
    expect(link.textContent).toBe('Acme Corp');
  });

  // ────────────────────────────────────────────────────────────────────────
  // Click → Xrm.Navigation.navigateTo with correct pageInput
  // ────────────────────────────────────────────────────────────────────────

  it('invokes Xrm.Navigation.navigateTo with pageType, entityName, entityId on click', async () => {
    const navigateTo = jest.fn().mockResolvedValue(undefined);
    // Minimal Xrm shim — only what getXrm() + LookupField need.
    (window as unknown as { Xrm?: unknown }).Xrm = {
      WebApi: {},
      Navigation: { navigateTo },
    };

    renderWithProviders(<LookupField label="Matter Type" value={sampleValue} span={1} />);

    const link = screen.getByRole('link', { name: 'Acme Corp' });
    await act(async () => {
      await userEvent.click(link);
    });

    expect(navigateTo).toHaveBeenCalledTimes(1);
    expect(navigateTo).toHaveBeenCalledWith({
      pageType: 'entityrecord',
      entityName: 'sprk_matter',
      entityId: '11111111-1111-1111-1111-111111111111',
    });
  });

  // ────────────────────────────────────────────────────────────────────────
  // Null value → hyphen empty state
  // ────────────────────────────────────────────────────────────────────────

  it('renders "—" when value is null', () => {
    renderWithProviders(<LookupField label="Matter Type" value={null} span={1} />);

    // Link not rendered in the empty state.
    expect(screen.queryByRole('link')).not.toBeInTheDocument();

    // Hyphen placeholder present.
    expect(screen.getByText(EMPTY_VALUE_GLYPH)).toBeInTheDocument();
  });

  // ────────────────────────────────────────────────────────────────────────
  // Undefined value → hyphen empty state (mirrors null path)
  // ────────────────────────────────────────────────────────────────────────

  it('renders "—" when value is undefined', () => {
    renderWithProviders(<LookupField label="Matter Type" value={undefined} span={1} />);

    expect(screen.queryByRole('link')).not.toBeInTheDocument();
    expect(screen.getByText(EMPTY_VALUE_GLYPH)).toBeInTheDocument();
  });

  // ────────────────────────────────────────────────────────────────────────
  // Xrm undefined → click does not throw (test-env graceful fallback)
  // ────────────────────────────────────────────────────────────────────────

  it('does not throw when Xrm is unavailable and the Link is clicked', async () => {
    // Ensure Xrm is not present on window or parent for this test.
    delete (window as unknown as { Xrm?: unknown }).Xrm;

    renderWithProviders(<LookupField label="Matter Type" value={sampleValue} span={1} />);

    const link = screen.getByRole('link', { name: 'Acme Corp' });
    // If the click handler threw synchronously, userEvent.click would reject
    // and fail the test — so this awaited call IS the assertion.
    await act(async () => {
      await expect(userEvent.click(link)).resolves.not.toThrow();
    });
  });

  // ────────────────────────────────────────────────────────────────────────
  // span prop → inline gridColumn applied to the field cell
  // ────────────────────────────────────────────────────────────────────────

  it('applies gridColumn: span N inline for FieldGrid integration', () => {
    const { container } = renderWithProviders(
      <LookupField label="Matter Type" value={sampleValue} span={2} />,
    );
    // Locate the LookupField root by its data-field-type marker (container is
    // the FluentProvider wrapper — reach through it to the actual field cell).
    const cell = container.querySelector('[data-field-type="lookup"]') as HTMLElement;
    expect(cell).toBeTruthy();
    expect(cell.style.gridColumn).toBe('span 2');
  });
});

// ═══════════════════════════════════════════════════════════════════════════
// TextField (task 005, FR-04 TextField clause)
// ═══════════════════════════════════════════════════════════════════════════

describe('TextField', () => {
  it('renders the label and the value', () => {
    renderWithProviders(<TextField span={1} label="Matter Number" value="M-001" />);
    expect(screen.getByText('Matter Number')).toBeInTheDocument();
    expect(screen.getByText('M-001')).toBeInTheDocument();
  });

  it('renders a numeric value coerced to string', () => {
    renderWithProviders(<TextField span={1} label="Count" value={42} />);
    expect(screen.getByText('42')).toBeInTheDocument();
  });

  it('renders a hyphen when value is null', () => {
    renderWithProviders(<TextField span={1} label="Matter Name" value={null} />);
    // Label is present.
    expect(screen.getByText('Matter Name')).toBeInTheDocument();
    // Em-dash placeholder rendered in the value slot.
    const valueEl = screen.getByTestId('record-header-text-field-value');
    expect(valueEl).toHaveTextContent(EMPTY_VALUE_GLYPH);
  });

  it('renders a hyphen when value is undefined', () => {
    renderWithProviders(<TextField span={1} label="Matter Name" value={undefined} />);
    expect(screen.getByText('Matter Name')).toBeInTheDocument();
    const valueEl = screen.getByTestId('record-header-text-field-value');
    expect(valueEl).toHaveTextContent(EMPTY_VALUE_GLYPH);
  });

  it('sets ellipsis / overflow / whiteSpace styles on the value slot', () => {
    renderWithProviders(
      <TextField
        span={1}
        label="Description"
        value="A very long value that should be clipped with ellipsis on overflow inside a narrow container"
      />,
    );
    const valueEl = screen.getByTestId('record-header-text-field-value');
    const cs = window.getComputedStyle(valueEl);
    expect(cs.overflow).toBe('hidden');
    expect(cs.textOverflow).toBe('ellipsis');
    expect(cs.whiteSpace).toBe('nowrap');
  });

  it('renders the required marker when required=true', () => {
    renderWithProviders(
      <TextField span={1} label="Matter Number" value="M-001" required />,
    );
    const marker = screen.getByTestId('record-header-text-field-required-marker');
    expect(marker).toBeInTheDocument();
    expect(marker).toHaveTextContent('*');
  });

  it('does NOT render the required marker when required is omitted', () => {
    renderWithProviders(<TextField span={1} label="Matter Name" value="Acme" />);
    expect(
      screen.queryByTestId('record-header-text-field-required-marker'),
    ).not.toBeInTheDocument();
  });

  it('does NOT render the required marker when required=false', () => {
    renderWithProviders(
      <TextField span={1} label="Matter Name" value="Acme" required={false} />,
    );
    expect(
      screen.queryByTestId('record-header-text-field-required-marker'),
    ).not.toBeInTheDocument();
  });

  it('applies gridColumn: span N inline style per span prop', () => {
    const { rerender } = renderWithProviders(
      <TextField span={1} label="X" value="a" />,
    );
    const cell1 = screen.getByTestId('record-header-text-field');
    expect(cell1.style.gridColumn).toBe('span 1');
    expect(cell1.getAttribute('data-span')).toBe('1');

    rerender(<TextField span={2} label="X" value="a" />);
    const cell2 = screen.getByTestId('record-header-text-field');
    expect(cell2.style.gridColumn).toBe('span 2');
    expect(cell2.getAttribute('data-span')).toBe('2');

    rerender(<TextField span={3} label="X" value="a" />);
    const cell3 = screen.getByTestId('record-header-text-field');
    expect(cell3.style.gridColumn).toBe('span 3');
    expect(cell3.getAttribute('data-span')).toBe('3');
  });
});

// ═══════════════════════════════════════════════════════════════════════════
// TextareaField (task 008, FR-04 TextareaField clause)
// ═══════════════════════════════════════════════════════════════════════════

/**
 * Force overflow-measurement dimensions on the HTMLElement prototype.
 *
 * TextareaField measures overflow by comparing `scrollHeight` vs
 * `clientHeight` on the clamped element. In jsdom both values default to 0
 * (no layout engine), so we stub them at the prototype level for a test and
 * restore them afterwards.
 */
const stubOverflow = (opts: {
  scrollHeight: number;
  clientHeight: number;
}): (() => void) => {
  const origScroll = Object.getOwnPropertyDescriptor(HTMLElement.prototype, 'scrollHeight');
  const origClient = Object.getOwnPropertyDescriptor(HTMLElement.prototype, 'clientHeight');
  Object.defineProperty(HTMLElement.prototype, 'scrollHeight', {
    configurable: true,
    get() {
      return opts.scrollHeight;
    },
  });
  Object.defineProperty(HTMLElement.prototype, 'clientHeight', {
    configurable: true,
    get() {
      return opts.clientHeight;
    },
  });
  return () => {
    if (origScroll) Object.defineProperty(HTMLElement.prototype, 'scrollHeight', origScroll);
    else delete (HTMLElement.prototype as unknown as Record<string, unknown>).scrollHeight;
    if (origClient) Object.defineProperty(HTMLElement.prototype, 'clientHeight', origClient);
    else delete (HTMLElement.prototype as unknown as Record<string, unknown>).clientHeight;
  };
};

describe('TextareaField', () => {
  it('renders label + short value without a "Show more" affordance', () => {
    // scrollHeight <= clientHeight → no overflow
    const restore = stubOverflow({ scrollHeight: 40, clientHeight: 60 });
    try {
      renderWithProviders(
        <TextareaField span={3} label="Description" value="A short single-line note." />,
      );
      expect(screen.getByText('Description')).toBeInTheDocument();
      expect(screen.getByText('A short single-line note.')).toBeInTheDocument();
      expect(screen.queryByTestId('sprk-textarea-show-more')).not.toBeInTheDocument();
    } finally {
      restore();
    }
  });

  it('renders the "Show more" link when the clamped value overflows', () => {
    // scrollHeight > clientHeight → overflow
    const restore = stubOverflow({ scrollHeight: 500, clientHeight: 60 });
    try {
      const longText = Array.from({ length: 40 }, (_v, i) => `line ${i}`).join('\n');
      renderWithProviders(<TextareaField span={3} label="Description" value={longText} />);
      const link = screen.getByTestId('sprk-textarea-show-more');
      expect(link).toBeInTheDocument();
      expect(link).toHaveTextContent('Show more');
    } finally {
      restore();
    }
  });

  it('opens the Popover with the full value when "Show more" is clicked', async () => {
    const restore = stubOverflow({ scrollHeight: 500, clientHeight: 60 });
    try {
      const longText =
        'First paragraph of long description that would overflow the clamped view. ' +
        'Second paragraph continues here with more details. Third paragraph.';
      renderWithProviders(<TextareaField span={3} label="Description" value={longText} />);

      // Popover surface not present in DOM until the trigger is clicked
      expect(screen.queryByTestId('sprk-textarea-popover')).not.toBeInTheDocument();

      const user = userEvent.setup();
      await act(async () => {
        await user.click(screen.getByTestId('sprk-textarea-show-more'));
      });

      const surface = await screen.findByTestId('sprk-textarea-popover');
      expect(surface).toBeInTheDocument();
      // Popover body contains the FULL text (not clamped)
      expect(surface).toHaveTextContent(longText);
    } finally {
      restore();
    }
  });

  it('renders a hyphen when value is null (no clamp, no link)', () => {
    renderWithProviders(<TextareaField span={3} label="Description" value={null} />);
    expect(screen.getByText('Description')).toBeInTheDocument();
    expect(screen.getByText(EMPTY_VALUE_GLYPH)).toBeInTheDocument();
    expect(screen.queryByTestId('sprk-textarea-show-more')).not.toBeInTheDocument();
    expect(screen.queryByTestId('sprk-textarea-clamped')).not.toBeInTheDocument();
  });

  it('renders a hyphen when value is undefined (no clamp, no link)', () => {
    renderWithProviders(<TextareaField span={3} label="Description" value={undefined} />);
    expect(screen.getByText('Description')).toBeInTheDocument();
    expect(screen.getByText(EMPTY_VALUE_GLYPH)).toBeInTheDocument();
    expect(screen.queryByTestId('sprk-textarea-show-more')).not.toBeInTheDocument();
  });

  it('renders a hyphen when value is an empty string', () => {
    renderWithProviders(<TextareaField span={3} label="Description" value="" />);
    expect(screen.getByText(EMPTY_VALUE_GLYPH)).toBeInTheDocument();
    expect(screen.queryByTestId('sprk-textarea-show-more')).not.toBeInTheDocument();
  });

  it('applies gridColumn: span N inline style per span prop', () => {
    const { rerender } = renderWithProviders(
      <TextareaField span={1} label="Description" value="short" />,
    );
    const cell1 = screen
      .getByText('short')
      .closest('[data-field-type="textarea"]') as HTMLElement;
    expect(cell1.style.gridColumn).toBe('span 1');

    rerender(<TextareaField span={2} label="Description" value="short" />);
    const cell2 = screen
      .getByText('short')
      .closest('[data-field-type="textarea"]') as HTMLElement;
    expect(cell2.style.gridColumn).toBe('span 2');

    rerender(<TextareaField span={3} label="Description" value="short" />);
    const cell3 = screen
      .getByText('short')
      .closest('[data-field-type="textarea"]') as HTMLElement;
    expect(cell3.style.gridColumn).toBe('span 3');
  });

  it('respects the maxLines prop via the CSS custom property', () => {
    // Default maxLines = 3
    const { rerender } = renderWithProviders(
      <TextareaField span={3} label="Description" value="one" />,
    );
    const wrapperDefault = screen
      .getByText('one')
      .closest('[data-field-type="textarea"]') as HTMLElement;
    expect(wrapperDefault.style.getPropertyValue('--sprk-textarea-max-lines')).toBe('3');

    // Override maxLines = 5
    rerender(<TextareaField span={3} label="Description" value="one" maxLines={5} />);
    const wrapper5 = screen
      .getByText('one')
      .closest('[data-field-type="textarea"]') as HTMLElement;
    expect(wrapper5.style.getPropertyValue('--sprk-textarea-max-lines')).toBe('5');

    // Override maxLines = 1
    rerender(<TextareaField span={3} label="Description" value="one" maxLines={1} />);
    const wrapper1 = screen
      .getByText('one')
      .closest('[data-field-type="textarea"]') as HTMLElement;
    expect(wrapper1.style.getPropertyValue('--sprk-textarea-max-lines')).toBe('1');
  });

  it('carries data-field-type="textarea" and data-span attributes on the cell root', () => {
    renderWithProviders(<TextareaField span={2} label="Description" value="body" />);
    const cell = screen
      .getByText('body')
      .closest('[data-field-type="textarea"]') as HTMLElement;
    expect(cell).not.toBeNull();
    expect(cell.getAttribute('data-field-type')).toBe('textarea');
    expect(cell.getAttribute('data-span')).toBe('2');
  });
});
