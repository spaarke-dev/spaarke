/**
 * DateRangeFilter — shared by the SemanticSearch code page and PCF (#3093).
 *
 * Covers the behaviours the two former copies disagreed on:
 *   - the From/To inputs show with no range set, so a custom range can be typed directly (the code
 *     page hid them until a preset was picked);
 *   - clearing emits `null` (the code page emitted `{ from: null, to: null }`, the PCF `null`);
 *   - the preset dropdown shows "Off" / the preset / "Custom" (the PCF had a menu with no state);
 *   - a reversed range is refused with a message.
 * The LOCAL-date preset fix is guarded in DateRangeFilter.presets.tz.test.ts (New York).
 */

import * as React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { DateRangeFilter, detectActivePreset } from '../DateRangeFilter';
import type { DateRangeFilterValue } from '../DateRangeFilter';

const march10 = () => new Date(2026, 2, 10, 9, 0, 0);

const renderFilter = (value: DateRangeFilterValue | null, onChange = jest.fn(), disabled = false) => {
  const utils = render(
    <FluentProvider theme={webLightTheme}>
      <DateRangeFilter value={value} onChange={onChange} disabled={disabled} />
    </FluentProvider>
  );
  return { ...utils, onChange };
};

describe('detectActivePreset', () => {
  it('detects the active preset, "custom" otherwise, "off" when empty', () => {
    const now = march10();
    expect(detectActivePreset(null, now)).toBe('off');
    expect(detectActivePreset({ from: null, to: null }, now)).toBe('off');
    expect(detectActivePreset({ from: '2026-01-01', to: '2026-03-10' }, now)).toBe('thisYear');
    expect(detectActivePreset({ from: '2026-02-01', to: '2026-02-15' }, now)).toBe('custom');
  });
});

describe('DateRangeFilter', () => {
  afterEach(() => {
    jest.useRealTimers();
  });

  it('choosing "This year" emits that range', () => {
    jest.useFakeTimers({
      now: march10(),
      doNotFake: [
        'setTimeout',
        'clearTimeout',
        'setInterval',
        'clearInterval',
        'setImmediate',
        'clearImmediate',
        'queueMicrotask',
        'nextTick',
        'requestAnimationFrame',
        'cancelAnimationFrame',
        'requestIdleCallback',
        'cancelIdleCallback',
        'performance',
        'hrtime',
      ],
    });
    const { onChange } = renderFilter(null);

    fireEvent.click(screen.getByRole('combobox', { name: 'Date range preset' }));
    fireEvent.click(screen.getByRole('option', { name: 'This year' }));

    expect(onChange).toHaveBeenLastCalledWith({ from: '2026-01-01', to: '2026-03-10' });
  });

  it('shows the From/To inputs with no range set, so a custom range can be typed', () => {
    const { onChange } = renderFilter(null);

    fireEvent.change(screen.getByLabelText('From date'), { target: { value: '2026-02-01' } });

    expect(onChange).toHaveBeenLastCalledWith({ from: '2026-02-01', to: null });
  });

  it('labels a range that matches no preset "Custom"', () => {
    renderFilter({ from: '2001-02-01', to: '2001-02-15' });
    expect(screen.getByRole('combobox', { name: 'Date range preset' })).toHaveTextContent('Custom');
  });

  it('"Off" clears the filter with null', () => {
    const { onChange } = renderFilter({ from: '2001-02-01', to: '2001-02-15' });

    fireEvent.click(screen.getByRole('combobox', { name: 'Date range preset' }));
    fireEvent.click(screen.getByRole('option', { name: 'Off' }));

    expect(onChange).toHaveBeenLastCalledWith(null);
  });

  it('the Clear button clears the filter with null, and is absent with no range', () => {
    const { onChange } = renderFilter({ from: '2001-02-01', to: null });
    fireEvent.click(screen.getByRole('button', { name: 'Clear' }));
    expect(onChange).toHaveBeenLastCalledWith(null);
    expect(screen.queryByRole('button', { name: 'Clear' })).toBeNull();
  });

  it('emptying both inputs emits null', () => {
    const { onChange } = renderFilter({ from: '2001-02-01', to: null });
    fireEvent.change(screen.getByLabelText('From date'), { target: { value: '' } });
    expect(onChange).toHaveBeenLastCalledWith(null);
  });

  it('refuses a reversed range with a message', () => {
    const { onChange } = renderFilter({ from: '2001-02-15', to: null });

    fireEvent.change(screen.getByLabelText('To date'), { target: { value: '2001-02-01' } });

    expect(screen.getByRole('alert')).toHaveTextContent('From date must be before To date');
    expect(onChange).not.toHaveBeenCalled();
  });

  it('disables its inputs when disabled', () => {
    renderFilter({ from: '2001-02-01', to: null }, jest.fn(), true);
    expect(screen.getByLabelText('From date')).toBeDisabled();
    expect(screen.getByLabelText('To date')).toBeDisabled();
  });
});
