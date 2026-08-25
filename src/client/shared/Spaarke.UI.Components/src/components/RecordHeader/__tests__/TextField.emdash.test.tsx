/**
 * TextField — em-dash empty-value alignment tests (FR-11, task 014).
 *
 * TextField previously rendered `''` as an empty styled box while its
 * sibling renderers (OptionSetField, TextareaField) already rendered `''`
 * as the em-dash placeholder. This file verifies the FR-11 alignment: an
 * empty string now renders the same em-dash as `null` / `undefined`, the
 * edit-mode draft round-trip is unaffected (draft still opens as `''`, not
 * `'—'`), and the number `0` / string `'0'` still render as real values.
 *
 * Per task 014's parallel-safety constraint this is a NEW file — do not add
 * assertions to `fields.test.tsx` (owned by task 015) or touch
 * `fields/index.ts` / `RecordHeader/index.ts`.
 */
import * as React from 'react';
import { screen, act, fireEvent } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import { TextField, EMPTY_VALUE_PLACEHOLDER } from '../fields/TextField';

describe('TextField — em-dash empty-value parity (FR-11)', () => {
  // ─────────────────────────────────────────────────────────────────────
  // Display-value cases
  // ─────────────────────────────────────────────────────────────────────

  it('renders an em-dash when value is an empty string', () => {
    renderWithProviders(<TextField span={1} label="Matter Name" value="" />);
    const valueEl = screen.getByTestId('record-header-text-field-value');
    expect(valueEl).toHaveTextContent(EMPTY_VALUE_PLACEHOLDER);
  });

  it('regression: still renders an em-dash when value is null', () => {
    renderWithProviders(<TextField span={1} label="Matter Name" value={null} />);
    const valueEl = screen.getByTestId('record-header-text-field-value');
    expect(valueEl).toHaveTextContent(EMPTY_VALUE_PLACEHOLDER);
  });

  it('regression: still renders an em-dash when value is undefined', () => {
    renderWithProviders(<TextField span={1} label="Matter Name" value={undefined} />);
    const valueEl = screen.getByTestId('record-header-text-field-value');
    expect(valueEl).toHaveTextContent(EMPTY_VALUE_PLACEHOLDER);
  });

  it('regression: the number 0 still renders as "0", not an em-dash', () => {
    renderWithProviders(<TextField span={1} label="Count" value={0} />);
    const valueEl = screen.getByTestId('record-header-text-field-value');
    expect(valueEl).toHaveTextContent('0');
    expect(valueEl.textContent).not.toBe(EMPTY_VALUE_PLACEHOLDER);
  });

  it('regression: the string "0" still renders as "0", not an em-dash', () => {
    renderWithProviders(<TextField span={1} label="Count" value="0" />);
    const valueEl = screen.getByTestId('record-header-text-field-value');
    expect(valueEl).toHaveTextContent('0');
    expect(valueEl.textContent).not.toBe(EMPTY_VALUE_PLACEHOLDER);
  });

  it('regression: a non-empty string still renders itself', () => {
    renderWithProviders(<TextField span={1} label="Matter Name" value="Acme Corp" />);
    const valueEl = screen.getByTestId('record-header-text-field-value');
    expect(valueEl).toHaveTextContent('Acme Corp');
  });

  it('a whitespace-only string renders as-is (out of scope for the strict "" check)', () => {
    renderWithProviders(<TextField span={1} label="Matter Name" value=" " />);
    const valueEl = screen.getByTestId('record-header-text-field-value');
    // Assert on raw textContent — a matcher like toHaveTextContent normalizes
    // whitespace, which would defeat the point of this assertion.
    expect(valueEl.textContent).toBe(' ');
  });

  // ─────────────────────────────────────────────────────────────────────
  // Edit round-trip with an empty-string value (contract untouched)
  // ─────────────────────────────────────────────────────────────────────

  describe('edit round-trip with an empty-string value', () => {
    it('opens the draft as "" (input empty, NOT the em-dash placeholder)', async () => {
      const onSave = jest.fn().mockResolvedValue(undefined);
      renderWithProviders(<TextField span={1} label="Matter Name" value="" onSave={onSave} />);

      const valueEl = screen.getByTestId('record-header-text-field-value');
      await act(async () => {
        await userEvent.click(valueEl);
      });

      const input = screen.getByTestId('record-header-text-field-input') as HTMLInputElement;
      expect(input.value).toBe('');
    });

    it('committing an unchanged empty draft is a no-op — onSave is called zero times', async () => {
      const onSave = jest.fn().mockResolvedValue(undefined);
      renderWithProviders(<TextField span={1} label="Matter Name" value="" onSave={onSave} />);

      const valueEl = screen.getByTestId('record-header-text-field-value');
      await act(async () => {
        await userEvent.click(valueEl);
      });

      const input = screen.getByTestId('record-header-text-field-input');
      await act(async () => {
        fireEvent.blur(input);
      });

      expect(onSave).not.toHaveBeenCalled();
    });

    it('typing "abc" and committing calls onSave once with "abc"', async () => {
      const onSave = jest.fn().mockResolvedValue(undefined);
      renderWithProviders(<TextField span={1} label="Matter Name" value="" onSave={onSave} />);

      const valueEl = screen.getByTestId('record-header-text-field-value');
      await act(async () => {
        await userEvent.click(valueEl);
      });

      const input = screen.getByTestId('record-header-text-field-input');
      await act(async () => {
        await userEvent.type(input, 'abc');
      });
      await act(async () => {
        fireEvent.blur(input);
      });

      expect(onSave).toHaveBeenCalledTimes(1);
      expect(onSave).toHaveBeenCalledWith('abc');
    });

    it('typing then clearing back to "" within the same session commits as a no-op (net-unchanged draft, "exactly as today")', async () => {
      const onSave = jest.fn().mockResolvedValue(undefined);
      renderWithProviders(<TextField span={1} label="Matter Name" value="" onSave={onSave} />);

      const valueEl = screen.getByTestId('record-header-text-field-value');
      await act(async () => {
        await userEvent.click(valueEl);
      });

      const input = screen.getByTestId('record-header-text-field-input');
      await act(async () => {
        await userEvent.type(input, 'abc');
      });
      await act(async () => {
        await userEvent.clear(input);
      });
      await act(async () => {
        fireEvent.blur(input);
      });

      // Net draft change is zero (back to the original '' ) — the existing
      // draft === original short-circuit in commit() means no save call,
      // same as before this task's display-only change.
      expect(onSave).not.toHaveBeenCalled();
    });

    it('a value committed to non-empty, then cleared back to "" in a later session, saves "" (real change is a real save)', async () => {
      const onSave = jest.fn().mockResolvedValue(undefined);
      const { rerender } = renderWithProviders(<TextField span={1} label="Matter Name" value="" onSave={onSave} />);

      // Session 1: empty -> "abc".
      let valueEl = screen.getByTestId('record-header-text-field-value');
      await act(async () => {
        await userEvent.click(valueEl);
      });
      let input = screen.getByTestId('record-header-text-field-input');
      await act(async () => {
        await userEvent.type(input, 'abc');
      });
      await act(async () => {
        fireEvent.blur(input);
      });
      expect(onSave).toHaveBeenNthCalledWith(1, 'abc');

      // Consumer syncs the committed value back into the controlled prop
      // (mirrors how MatterHeaderView re-renders after a successful save).
      rerender(<TextField span={1} label="Matter Name" value="abc" onSave={onSave} />);

      // Session 2: "abc" -> "" — a real change against the new original.
      valueEl = screen.getByTestId('record-header-text-field-value');
      await act(async () => {
        await userEvent.click(valueEl);
      });
      input = screen.getByTestId('record-header-text-field-input');
      await act(async () => {
        await userEvent.clear(input);
      });
      await act(async () => {
        fireEvent.blur(input);
      });

      expect(onSave).toHaveBeenCalledTimes(2);
      expect(onSave).toHaveBeenNthCalledWith(2, '');
    });
  });

  // ─────────────────────────────────────────────────────────────────────
  // Negative: rejected save reverts draft and stays in edit mode
  // ─────────────────────────────────────────────────────────────────────

  it('a rejected onSave reverts the draft and stays in edit mode (contract untouched by this change)', async () => {
    const onSave = jest.fn().mockRejectedValue(new Error('save failed'));
    renderWithProviders(<TextField span={1} label="Matter Name" value="" onSave={onSave} />);

    const valueEl = screen.getByTestId('record-header-text-field-value');
    await act(async () => {
      await userEvent.click(valueEl);
    });

    const input = screen.getByTestId('record-header-text-field-input');
    await act(async () => {
      await userEvent.type(input, 'abc');
    });
    await act(async () => {
      fireEvent.blur(input);
    });

    expect(onSave).toHaveBeenCalledTimes(1);

    // Still in edit mode.
    const root = screen.getByTestId('record-header-text-field');
    expect(root.getAttribute('data-editing')).toBe('true');

    // Draft reverted to the original empty value.
    const inputAfter = screen.getByTestId('record-header-text-field-input') as HTMLInputElement;
    expect(inputAfter.value).toBe('');
  });
});
