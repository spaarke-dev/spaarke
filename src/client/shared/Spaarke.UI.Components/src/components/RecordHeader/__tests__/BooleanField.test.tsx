/**
 * BooleanField — unit tests (FR-08, FR-10, FR-11, task 012).
 *
 * NEW file per task 012's parallel-safety constraint — do NOT add assertions
 * to `fields.test.tsx` (owned by task 015) or touch `fields/index.ts` /
 * `RecordHeader/index.ts`. Mirrors the dedicated-file convention already
 * established by `TextField.emdash.test.tsx` for draft/commit coverage.
 */
import * as React from 'react';
import { screen, act, fireEvent } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import { BooleanField, EMPTY_VALUE_PLACEHOLDER } from '../fields/BooleanField';

describe('BooleanField', () => {
  // ─────────────────────────────────────────────────────────────────────
  // Read-mode display (FR-08, FR-11)
  // ─────────────────────────────────────────────────────────────────────

  it('renders "Yes" when value is true', () => {
    renderWithProviders(<BooleanField span={1} label="High Priority" value={true} />);
    expect(screen.getByText('High Priority')).toBeInTheDocument();
    const valueEl = screen.getByTestId('record-header-boolean-field-value');
    expect(valueEl).toHaveTextContent('Yes');
  });

  it('renders "No" when value is false — false is a REAL value, never the em-dash', () => {
    renderWithProviders(<BooleanField span={1} label="High Priority" value={false} />);
    const valueEl = screen.getByTestId('record-header-boolean-field-value');
    expect(valueEl).toHaveTextContent('No');
    expect(valueEl.textContent).not.toBe(EMPTY_VALUE_PLACEHOLDER);
  });

  it('renders the overridden trueLabel when value is true and trueLabel is provided', () => {
    renderWithProviders(<BooleanField span={1} label="Monitor" value={true} trueLabel="On" falseLabel="Off" />);
    const valueEl = screen.getByTestId('record-header-boolean-field-value');
    expect(valueEl).toHaveTextContent('On');
  });

  it('renders the overridden falseLabel when value is false and falseLabel is provided', () => {
    renderWithProviders(<BooleanField span={1} label="Monitor" value={false} trueLabel="On" falseLabel="Off" />);
    const valueEl = screen.getByTestId('record-header-boolean-field-value');
    expect(valueEl).toHaveTextContent('Off');
  });

  it('renders the em-dash when value is null', () => {
    renderWithProviders(<BooleanField span={1} label="High Priority" value={null} />);
    const valueEl = screen.getByTestId('record-header-boolean-field-value');
    expect(valueEl).toHaveTextContent(EMPTY_VALUE_PLACEHOLDER);
  });

  it('renders the em-dash when value is undefined', () => {
    renderWithProviders(<BooleanField span={1} label="High Priority" value={undefined} />);
    const valueEl = screen.getByTestId('record-header-boolean-field-value');
    expect(valueEl).toHaveTextContent(EMPTY_VALUE_PLACEHOLDER);
  });

  it('renders the em-dash when value is an empty string', () => {
    renderWithProviders(<BooleanField span={1} label="High Priority" value="" />);
    const valueEl = screen.getByTestId('record-header-boolean-field-value');
    expect(valueEl).toHaveTextContent(EMPTY_VALUE_PLACEHOLDER);
  });

  // ─────────────────────────────────────────────────────────────────────
  // Grid span (FR-03/FR-10 contract)
  // ─────────────────────────────────────────────────────────────────────

  it('applies gridColumn: span N inline style per span prop', () => {
    const { rerender } = renderWithProviders(<BooleanField span={1} label="High Priority" value={true} />);
    const cell1 = screen.getByTestId('record-header-boolean-field');
    expect(cell1.style.gridColumn).toBe('span 1');
    expect(cell1.getAttribute('data-span')).toBe('1');

    rerender(<BooleanField span={2} label="High Priority" value={true} />);
    const cell2 = screen.getByTestId('record-header-boolean-field');
    expect(cell2.style.gridColumn).toBe('span 2');
    expect(cell2.getAttribute('data-span')).toBe('2');

    rerender(<BooleanField span={3} label="High Priority" value={true} />);
    const cell3 = screen.getByTestId('record-header-boolean-field');
    expect(cell3.style.gridColumn).toBe('span 3');
    expect(cell3.getAttribute('data-span')).toBe('3');
  });

  // ─────────────────────────────────────────────────────────────────────
  // Required marker — D-10: BooleanField renders NOTHING (TextField-only)
  // ─────────────────────────────────────────────────────────────────────

  it('renders no required marker when required=true (D-10 — marker is TextField-only)', () => {
    renderWithProviders(<BooleanField span={1} label="High Priority" value={true} required />);
    const valueEl = screen.getByTestId('record-header-boolean-field-value');
    // Exactly the resolved label — no appended "*" or extra marker text/node.
    expect(valueEl).toHaveTextContent('Yes');
    expect(valueEl.textContent).toBe('Yes');
    expect(screen.queryByText('*')).not.toBeInTheDocument();
  });

  // ─────────────────────────────────────────────────────────────────────
  // Editability gate (FR-10 contract)
  // ─────────────────────────────────────────────────────────────────────

  it('is read-only when no onSave is provided', () => {
    renderWithProviders(<BooleanField span={1} label="High Priority" value={true} />);
    const root = screen.getByTestId('record-header-boolean-field');
    expect(root.getAttribute('data-editable')).toBe('false');
    const valueEl = screen.getByTestId('record-header-boolean-field-value');
    expect(valueEl).not.toHaveAttribute('role');
    expect(screen.queryByRole('switch')).not.toBeInTheDocument();
  });

  it('is read-only when onSave is provided but disabled=true', () => {
    const onSave = jest.fn().mockResolvedValue(undefined);
    renderWithProviders(<BooleanField span={1} label="High Priority" value={true} onSave={onSave} disabled />);
    const root = screen.getByTestId('record-header-boolean-field');
    expect(root.getAttribute('data-editable')).toBe('false');
  });

  it('with onSave alone, clicking the value enters edit mode showing a Fluent Switch checked per the current value', async () => {
    const onSave = jest.fn().mockResolvedValue(undefined);
    renderWithProviders(<BooleanField span={1} label="High Priority" value={true} onSave={onSave} />);

    const root = screen.getByTestId('record-header-boolean-field');
    expect(root.getAttribute('data-editable')).toBe('true');

    const valueEl = screen.getByTestId('record-header-boolean-field-value');
    await act(async () => {
      await userEvent.click(valueEl);
    });

    const switchEl = screen.getByTestId('record-header-boolean-field-switch') as HTMLInputElement;
    expect(switchEl).toBeInTheDocument();
    expect(switchEl.checked).toBe(true);
    expect(screen.getByTestId('record-header-boolean-field').getAttribute('data-editing')).toBe('true');
  });

  it('entering edit with a false value shows the Switch unchecked', async () => {
    const onSave = jest.fn().mockResolvedValue(undefined);
    renderWithProviders(<BooleanField span={1} label="High Priority" value={false} onSave={onSave} />);

    const valueEl = screen.getByTestId('record-header-boolean-field-value');
    await act(async () => {
      await userEvent.click(valueEl);
    });

    const switchEl = screen.getByTestId('record-header-boolean-field-switch') as HTMLInputElement;
    expect(switchEl.checked).toBe(false);
  });

  // ─────────────────────────────────────────────────────────────────────
  // Draft/commit/cancel semantics (FR-10, copied verbatim from TextField)
  // ─────────────────────────────────────────────────────────────────────

  describe('draft/commit/cancel semantics', () => {
    it('toggling the Switch changes ONLY the draft — onSave is not called until commit', async () => {
      const onSave = jest.fn().mockResolvedValue(undefined);
      renderWithProviders(<BooleanField span={1} label="High Priority" value={false} onSave={onSave} />);

      const valueEl = screen.getByTestId('record-header-boolean-field-value');
      await act(async () => {
        await userEvent.click(valueEl);
      });

      const switchEl = screen.getByTestId('record-header-boolean-field-switch') as HTMLInputElement;
      await act(async () => {
        await userEvent.click(switchEl);
      });

      expect(switchEl.checked).toBe(true);
      expect(onSave).not.toHaveBeenCalled();
    });

    it('Enter commits the draft via onSave', async () => {
      const onSave = jest.fn().mockResolvedValue(undefined);
      renderWithProviders(<BooleanField span={1} label="High Priority" value={false} onSave={onSave} />);

      const valueEl = screen.getByTestId('record-header-boolean-field-value');
      await act(async () => {
        await userEvent.click(valueEl);
      });

      const switchEl = screen.getByTestId('record-header-boolean-field-switch') as HTMLInputElement;
      await act(async () => {
        await userEvent.click(switchEl);
      });
      await act(async () => {
        fireEvent.keyDown(switchEl, { key: 'Enter' });
      });

      expect(onSave).toHaveBeenCalledTimes(1);
      expect(onSave).toHaveBeenCalledWith(true);
    });

    it('Escape cancels with zero onSave calls and exits edit mode', async () => {
      const onSave = jest.fn().mockResolvedValue(undefined);
      renderWithProviders(<BooleanField span={1} label="High Priority" value={false} onSave={onSave} />);

      const valueEl = screen.getByTestId('record-header-boolean-field-value');
      await act(async () => {
        await userEvent.click(valueEl);
      });

      const switchEl = screen.getByTestId('record-header-boolean-field-switch') as HTMLInputElement;
      await act(async () => {
        await userEvent.click(switchEl);
      });
      expect(switchEl.checked).toBe(true);

      await act(async () => {
        fireEvent.keyDown(switchEl, { key: 'Escape' });
      });

      expect(onSave).not.toHaveBeenCalled();
      const root = screen.getByTestId('record-header-boolean-field');
      expect(root.getAttribute('data-editing')).toBe('false');
      // Reverted display — back to "No" (the original committed value).
      expect(screen.getByTestId('record-header-boolean-field-value')).toHaveTextContent('No');
    });

    it('blur commits the draft', async () => {
      const onSave = jest.fn().mockResolvedValue(undefined);
      renderWithProviders(<BooleanField span={1} label="High Priority" value={false} onSave={onSave} />);

      const valueEl = screen.getByTestId('record-header-boolean-field-value');
      await act(async () => {
        await userEvent.click(valueEl);
      });

      const switchEl = screen.getByTestId('record-header-boolean-field-switch') as HTMLInputElement;
      await act(async () => {
        await userEvent.click(switchEl);
      });
      await act(async () => {
        fireEvent.blur(switchEl);
      });

      expect(onSave).toHaveBeenCalledTimes(1);
      expect(onSave).toHaveBeenCalledWith(true);
    });

    it('committing an unchanged draft is a no-op — onSave is called zero times', async () => {
      const onSave = jest.fn().mockResolvedValue(undefined);
      renderWithProviders(<BooleanField span={1} label="High Priority" value={false} onSave={onSave} />);

      const valueEl = screen.getByTestId('record-header-boolean-field-value');
      await act(async () => {
        await userEvent.click(valueEl);
      });

      const switchEl = screen.getByTestId('record-header-boolean-field-switch') as HTMLInputElement;
      await act(async () => {
        fireEvent.blur(switchEl);
      });

      expect(onSave).not.toHaveBeenCalled();
    });

    it('shows a disabled Switch + tiny Spinner while saving', async () => {
      let resolveSave: () => void = () => {};
      const onSave = jest.fn(
        () =>
          new Promise<void>(resolve => {
            resolveSave = resolve;
          })
      );
      renderWithProviders(<BooleanField span={1} label="High Priority" value={false} onSave={onSave} />);

      const valueEl = screen.getByTestId('record-header-boolean-field-value');
      await act(async () => {
        await userEvent.click(valueEl);
      });

      const switchEl = screen.getByTestId('record-header-boolean-field-switch') as HTMLInputElement;
      await act(async () => {
        await userEvent.click(switchEl);
      });
      await act(async () => {
        fireEvent.blur(switchEl);
      });

      // Save is in-flight — Switch disabled, Spinner visible.
      expect(screen.getByTestId('record-header-boolean-field-switch')).toBeDisabled();
      expect(screen.getByTestId('record-header-boolean-field-spinner')).toBeInTheDocument();

      await act(async () => {
        resolveSave();
        await Promise.resolve();
      });

      expect(screen.queryByTestId('record-header-boolean-field-spinner')).not.toBeInTheDocument();
    });
  });

  // ─────────────────────────────────────────────────────────────────────
  // Negative: rejected save reverts draft and STAYS in edit mode
  // ─────────────────────────────────────────────────────────────────────

  it('a rejected onSave reverts the draft and stays in edit mode', async () => {
    const onSave = jest.fn().mockRejectedValue(new Error('save failed'));
    renderWithProviders(<BooleanField span={1} label="High Priority" value={false} onSave={onSave} />);

    const valueEl = screen.getByTestId('record-header-boolean-field-value');
    await act(async () => {
      await userEvent.click(valueEl);
    });

    const switchEl = screen.getByTestId('record-header-boolean-field-switch') as HTMLInputElement;
    await act(async () => {
      await userEvent.click(switchEl);
    });
    expect(switchEl.checked).toBe(true);

    await act(async () => {
      fireEvent.blur(switchEl);
    });

    expect(onSave).toHaveBeenCalledTimes(1);

    // Still in edit mode.
    const root = screen.getByTestId('record-header-boolean-field');
    expect(root.getAttribute('data-editing')).toBe('true');

    // Draft reverted to the original (false / unchecked).
    const switchAfter = screen.getByTestId('record-header-boolean-field-switch') as HTMLInputElement;
    expect(switchAfter.checked).toBe(false);
  });
});
