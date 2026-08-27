/**
 * LookupField — editable-mode unit tests (FR-15, FR-15a, task 023).
 *
 * Standalone file — mirrors `OptionSetField.edit.test.tsx`'s precedent: does
 * NOT append to `fields.test.tsx` (which owns the pre-existing read-only
 * `LookupField` describe block) and does NOT modify it. This file covers
 * ONLY the new editable surface: the OOB `Xrm.Utility.lookupObjects` picker.
 *
 * Per ADR-038 this is a KEEP-category suite — public props surface only, no
 * renderer internals mocked beyond the host `Xrm` global every consumer of
 * this component must stub the same way.
 */

import * as React from 'react';
import { act, screen, fireEvent } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import { LookupField, type ILookupFieldValue } from '../fields/LookupField';

const MATTER_TYPE_TARGET = 'sprk_mattertype_ref';

const sampleValue: ILookupFieldValue = {
  id: '11111111-1111-1111-1111-111111111111',
  name: 'Litigation',
  entityType: MATTER_TYPE_TARGET,
};

describe('LookupField — editable mode (FR-15, FR-15a)', () => {
  // Preserve/restore the global Xrm across tests (same pattern as fields.test.tsx).
  const originalXrm = (window as unknown as { Xrm?: unknown }).Xrm;

  afterEach(() => {
    if (originalXrm === undefined) {
      delete (window as unknown as { Xrm?: unknown }).Xrm;
    } else {
      (window as unknown as { Xrm?: unknown }).Xrm = originalXrm;
    }
  });

  /**
   * Installs a minimal Xrm shim with a mocked `Utility.lookupObjects`.
   *
   * ══════════════════════════════════════════════════════════════════════════
   * The mock is `this`-SENSITIVE ON PURPOSE. Do not simplify it back to a
   * plain `jest.fn(impl)`.
   * ══════════════════════════════════════════════════════════════════════════
   * The real `Xrm.Utility.lookupObjects` reads `this._clientApiExecutor`
   * internally, so calling it through a detached local alias
   * (`const lookupObjects = xrm.Utility.lookupObjects; lookupObjects(...)`)
   * throws `TypeError: Cannot read properties of undefined (reading
   * '_clientApiExecutor')`.
   *
   * The component shipped exactly that bug from task 023 through v1.1.6. This
   * suite passed the whole time, because a plain `jest.fn()` neither needs nor
   * checks its receiver — the mock was strictly more permissive than the thing
   * it stood in for, so the one property that mattered went untested. The
   * production `catch {}` then swallowed the TypeError on every click, and the
   * cell merely looked read-only.
   *
   * Replicating the receiver requirement makes this suite able to fail for the
   * reason production failed.
   */
  function stubLookupObjects(
    impl: (options: unknown) => Promise<Array<{ id: string; name: string; entityType: string }>>
  ): jest.Mock {
    const utility: Record<string, unknown> = { _clientApiExecutor: {} };
    const lookupObjects = jest.fn(function (this: unknown, options: unknown) {
      // A detached call has `this === undefined` under strict mode (and
      // `globalThis` otherwise — neither carries `_clientApiExecutor`).
      if ((this as Record<string, unknown> | undefined)?._clientApiExecutor === undefined) {
        throw new TypeError("Cannot read properties of undefined (reading '_clientApiExecutor')");
      }
      return impl(options);
    });
    utility.lookupObjects = lookupObjects;
    (window as unknown as { Xrm?: unknown }).Xrm = { WebApi: {}, Utility: utility };
    return lookupObjects;
  }

  // ─────────────────────────────────────────────────────────────────────
  // Editability gating
  // ─────────────────────────────────────────────────────────────────────

  describe('editability gating', () => {
    it('is read-only with no onSave/targets — value renders as a navigation Link, not a button', () => {
      renderWithProviders(<LookupField label="Matter Type" value={sampleValue} span={1} />);
      const valueEl = screen.getByTestId('record-header-lookup-field-value');
      // Read-only populated value is a Fluent `Link` (native `<a>` semantics —
      // implicitly focusable, no explicit `role="button"`), never an
      // action-trigger `role="button"` span. Mirrors fields.test.tsx's
      // `getByRole('link', …)` assertion for the same element.
      expect(valueEl.getAttribute('role')).not.toBe('button');
      expect(screen.getByTestId('record-header-lookup-field').getAttribute('data-editable')).toBe('false');
    });

    it('is read-only when onSave + targets are supplied but disabled=true', () => {
      const onSave = jest.fn();
      renderWithProviders(
        <LookupField label="Matter Type" value={sampleValue} span={1} targets={[MATTER_TYPE_TARGET]} onSave={onSave} disabled />
      );
      expect(screen.getByTestId('record-header-lookup-field').getAttribute('data-editable')).toBe('false');
    });

    it('is read-only when onSave is supplied without a non-empty targets array', () => {
      const onSave = jest.fn();
      renderWithProviders(<LookupField label="Matter Type" value={sampleValue} span={1} onSave={onSave} targets={[]} />);
      expect(screen.getByTestId('record-header-lookup-field').getAttribute('data-editable')).toBe('false');
    });

    it('is editable when onSave + a non-empty targets array are both supplied', () => {
      const onSave = jest.fn();
      renderWithProviders(
        <LookupField label="Matter Type" value={sampleValue} span={1} targets={[MATTER_TYPE_TARGET]} onSave={onSave} />
      );
      expect(screen.getByTestId('record-header-lookup-field').getAttribute('data-editable')).toBe('true');
      const valueEl = screen.getByTestId('record-header-lookup-field-value');
      expect(valueEl.getAttribute('role')).toBe('button');
      expect(valueEl.getAttribute('tabIndex')).toBe('0');
    });

    it('is editable (and clickable) for an EMPTY value when onSave + targets are supplied', () => {
      const onSave = jest.fn();
      renderWithProviders(
        <LookupField label="Matter Type" value={null} span={1} targets={[MATTER_TYPE_TARGET]} onSave={onSave} />
      );
      const valueEl = screen.getByTestId('record-header-lookup-field-value');
      expect(valueEl.textContent).toBe('—');
      expect(valueEl.getAttribute('role')).toBe('button');
      expect(valueEl.getAttribute('tabIndex')).toBe('0');
    });
  });

  // ─────────────────────────────────────────────────────────────────────
  // Opening the picker — call shape (FR-15a)
  // ─────────────────────────────────────────────────────────────────────

  describe('opening the picker', () => {
    it('clicking a populated editable value invokes lookupObjects with entityTypes:[targets[0]] and allowMultiSelect:false', async () => {
      const lookupObjects = stubLookupObjects(async () => []);
      const onSave = jest.fn();
      renderWithProviders(
        <LookupField label="Matter Type" value={sampleValue} span={1} targets={[MATTER_TYPE_TARGET]} onSave={onSave} />
      );

      await act(async () => {
        await userEvent.click(screen.getByTestId('record-header-lookup-field-value'));
      });

      expect(lookupObjects).toHaveBeenCalledTimes(1);
      expect(lookupObjects).toHaveBeenCalledWith({
        entityTypes: [MATTER_TYPE_TARGET],
        defaultEntityType: MATTER_TYPE_TARGET,
        allowMultiSelect: false,
      });
    });

    it('clicking an EMPTY editable value also invokes lookupObjects (first-time population)', async () => {
      const lookupObjects = stubLookupObjects(async () => []);
      const onSave = jest.fn();
      renderWithProviders(
        <LookupField label="Matter Type" value={null} span={1} targets={[MATTER_TYPE_TARGET]} onSave={onSave} />
      );

      await act(async () => {
        await userEvent.click(screen.getByTestId('record-header-lookup-field-value'));
      });

      expect(lookupObjects).toHaveBeenCalledTimes(1);
    });

    it('uses only targets[0] when multiple targets are supplied', async () => {
      const lookupObjects = stubLookupObjects(async () => []);
      const onSave = jest.fn();
      renderWithProviders(
        <LookupField
          label="Regarding"
          value={sampleValue}
          span={1}
          targets={['sprk_matter', 'sprk_project']}
          onSave={onSave}
        />
      );

      await act(async () => {
        await userEvent.click(screen.getByTestId('record-header-lookup-field-value'));
      });

      expect(lookupObjects).toHaveBeenCalledWith(
        expect.objectContaining({ entityTypes: ['sprk_matter'], defaultEntityType: 'sprk_matter' })
      );
    });

    it('Enter key on the editable value opens the picker', async () => {
      const lookupObjects = stubLookupObjects(async () => []);
      const onSave = jest.fn();
      renderWithProviders(
        <LookupField label="Matter Type" value={sampleValue} span={1} targets={[MATTER_TYPE_TARGET]} onSave={onSave} />
      );

      await act(async () => {
        fireEvent.keyDown(screen.getByTestId('record-header-lookup-field-value'), { key: 'Enter' });
      });

      expect(lookupObjects).toHaveBeenCalledTimes(1);
    });
  });

  // ─────────────────────────────────────────────────────────────────────
  // Selection round-trip (FR-15a payload contract)
  // ─────────────────────────────────────────────────────────────────────

  describe('selection round-trip', () => {
    it('a resolved selection calls onSave exactly once with the exact form-buffer payload shape', async () => {
      stubLookupObjects(async () => [{ id: '22222222-2222-2222-2222-222222222222', name: 'Corporate', entityType: MATTER_TYPE_TARGET }]);
      const onSave = jest.fn();
      renderWithProviders(
        <LookupField label="Matter Type" value={sampleValue} span={1} targets={[MATTER_TYPE_TARGET]} onSave={onSave} />
      );

      await act(async () => {
        await userEvent.click(screen.getByTestId('record-header-lookup-field-value'));
      });

      expect(onSave).toHaveBeenCalledTimes(1);
      expect(onSave).toHaveBeenCalledWith({
        id: '22222222-2222-2222-2222-222222222222',
        name: 'Corporate',
        entityType: MATTER_TYPE_TARGET,
      });
    });

    it('normalizes the returned id — strips braces and lowercases', async () => {
      stubLookupObjects(async () => [
        { id: '{3FA85F64-5717-4562-B3FC-2C963F66AFA6}', name: 'Corporate', entityType: MATTER_TYPE_TARGET },
      ]);
      const onSave = jest.fn();
      renderWithProviders(
        <LookupField label="Matter Type" value={sampleValue} span={1} targets={[MATTER_TYPE_TARGET]} onSave={onSave} />
      );

      await act(async () => {
        await userEvent.click(screen.getByTestId('record-header-lookup-field-value'));
      });

      expect(onSave).toHaveBeenCalledWith(
        expect.objectContaining({ id: '3fa85f64-5717-4562-b3fc-2c963f66afa6' })
      );
    });
  });

  // ─────────────────────────────────────────────────────────────────────
  // Cancel / empty result — negative contract (FR-15a)
  // ─────────────────────────────────────────────────────────────────────

  describe('cancel stages nothing', () => {
    it('an empty-array result (Cancel) calls onSave zero times', async () => {
      stubLookupObjects(async () => []);
      const onSave = jest.fn();
      renderWithProviders(
        <LookupField label="Matter Type" value={sampleValue} span={1} targets={[MATTER_TYPE_TARGET]} onSave={onSave} />
      );

      await act(async () => {
        await userEvent.click(screen.getByTestId('record-header-lookup-field-value'));
      });

      expect(onSave).not.toHaveBeenCalled();
      // The displayed value is untouched — still the original sample value.
      expect(screen.getByTestId('record-header-lookup-field-value').textContent).toBe('Litigation');
    });

    it('an undefined result calls onSave zero times (defensive)', async () => {
      stubLookupObjects(async () => undefined as unknown as []);
      const onSave = jest.fn();
      renderWithProviders(
        <LookupField label="Matter Type" value={sampleValue} span={1} targets={[MATTER_TYPE_TARGET]} onSave={onSave} />
      );

      await act(async () => {
        await userEvent.click(screen.getByTestId('record-header-lookup-field-value'));
      });

      expect(onSave).not.toHaveBeenCalled();
    });
  });

  // ─────────────────────────────────────────────────────────────────────
  // Graceful degradation — no throw when the host can't open a picker
  // ─────────────────────────────────────────────────────────────────────

  describe('graceful degradation', () => {
    it('does not throw and does not call onSave when Xrm.Utility.lookupObjects is absent', async () => {
      (window as unknown as { Xrm?: unknown }).Xrm = { WebApi: {} }; // no Utility at all
      const onSave = jest.fn();
      renderWithProviders(
        <LookupField label="Matter Type" value={sampleValue} span={1} targets={[MATTER_TYPE_TARGET]} onSave={onSave} />
      );

      await act(async () => {
        await expect(userEvent.click(screen.getByTestId('record-header-lookup-field-value'))).resolves.not.toThrow();
      });

      expect(onSave).not.toHaveBeenCalled();
    });

    it('does not throw when Xrm is entirely unavailable', async () => {
      delete (window as unknown as { Xrm?: unknown }).Xrm;
      const onSave = jest.fn();
      renderWithProviders(
        <LookupField label="Matter Type" value={sampleValue} span={1} targets={[MATTER_TYPE_TARGET]} onSave={onSave} />
      );

      await act(async () => {
        await expect(userEvent.click(screen.getByTestId('record-header-lookup-field-value'))).resolves.not.toThrow();
      });

      expect(onSave).not.toHaveBeenCalled();
    });

    it('does not throw and stays in edit mode when onSave itself rejects', async () => {
      stubLookupObjects(async () => [{ id: 'x', name: 'Corp', entityType: MATTER_TYPE_TARGET }]);
      const onSave = jest.fn().mockRejectedValue(new Error('save failed'));
      renderWithProviders(
        <LookupField label="Matter Type" value={sampleValue} span={1} targets={[MATTER_TYPE_TARGET]} onSave={onSave} />
      );

      await act(async () => {
        await expect(userEvent.click(screen.getByTestId('record-header-lookup-field-value'))).resolves.not.toThrow();
      });

      expect(onSave).toHaveBeenCalledTimes(1);
    });
  });

  // ─────────────────────────────────────────────────────────────────────
  // Read-only path is untouched when onSave is supplied but targets is not
  // ─────────────────────────────────────────────────────────────────────

  describe('read-only fallback interaction (regression)', () => {
    /** Installs Xrm with BOTH navigateTo and lookupObjects mocked, so a test
     * can assert exactly one of the two paths fired. */
    function stubBothPaths(): { navigateTo: jest.Mock; lookupObjects: jest.Mock } {
      const navigateTo = jest.fn().mockResolvedValue(undefined);
      const lookupObjects = jest.fn();
      (window as unknown as { Xrm?: unknown }).Xrm = {
        WebApi: {},
        Navigation: { navigateTo },
        Utility: { lookupObjects },
      };
      return { navigateTo, lookupObjects };
    }

    it('navigates via Xrm.Navigation.navigateTo (not the picker) when targets is omitted', async () => {
      const { navigateTo, lookupObjects } = stubBothPaths();
      const onSave = jest.fn();
      // onSave supplied, but targets omitted — must stay read-only per contract.
      renderWithProviders(<LookupField label="Matter Type" value={sampleValue} span={1} onSave={onSave} />);

      await act(async () => {
        await userEvent.click(screen.getByTestId('record-header-lookup-field-value'));
      });

      expect(navigateTo).toHaveBeenCalledTimes(1);
      expect(lookupObjects).not.toHaveBeenCalled();
      expect(onSave).not.toHaveBeenCalled();
    });

    it('navigates via Xrm.Navigation.navigateTo (not the picker) when targets is an empty array', async () => {
      const { navigateTo, lookupObjects } = stubBothPaths();
      const onSave = jest.fn();
      renderWithProviders(
        <LookupField label="Matter Type" value={sampleValue} span={1} targets={[]} onSave={onSave} />
      );

      await act(async () => {
        await userEvent.click(screen.getByTestId('record-header-lookup-field-value'));
      });

      expect(navigateTo).toHaveBeenCalledTimes(1);
      expect(lookupObjects).not.toHaveBeenCalled();
      expect(onSave).not.toHaveBeenCalled();
    });

    it('navigates via Xrm.Navigation.navigateTo (not the picker) when disabled=true, even with onSave + targets supplied', async () => {
      const { navigateTo, lookupObjects } = stubBothPaths();
      const onSave = jest.fn();
      // This is acceptance criterion 4's explicit bundling: no onSave / disabled
      // true / empty targets are ALL "render the current display-only behavior".
      renderWithProviders(
        <LookupField
          label="Matter Type"
          value={sampleValue}
          span={1}
          targets={[MATTER_TYPE_TARGET]}
          onSave={onSave}
          disabled
        />
      );

      await act(async () => {
        await userEvent.click(screen.getByTestId('record-header-lookup-field-value'));
      });

      expect(navigateTo).toHaveBeenCalledTimes(1);
      expect(lookupObjects).not.toHaveBeenCalled();
      expect(onSave).not.toHaveBeenCalled();
    });
  });
});
