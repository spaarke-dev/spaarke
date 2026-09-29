/**
 * useRecordHeaderFields Unit Tests
 *
 * FR-13 / FR-14 / FR-19 (record-header-and-notepad-r2 task 022).
 *
 * The load-bearing assertions here are the NEGATIVE ones. R1 v1.0.7 fixed a
 * bug where saving via `Xrm.WebApi.updateRecord` + refetch toggled the shell's
 * loading skeleton and flashed the entire PCF on every edit. So these tests
 * assert not only that `setValue` is called, but that `updateRecord` is NEVER
 * called, that `retrieveRecord` is NOT re-issued after a save, and that
 * `loading` never flips on a save.
 *
 * Field names are deliberately NON-`sprk_` (`hdr_*`) — the hook is entity- and
 * field-agnostic (ADR-012) and the tests prove it by never handing it a Spaarke
 * schema name.
 */

import * as fs from 'fs';
import * as path from 'path';
import { act, renderHook, waitFor } from '@testing-library/react';
import type { ILookupItem } from '../../types/LookupTypes';
import { projectLookup, useRecordHeaderFields } from '../useRecordHeaderFields';

// ─────────────────────────────────────────────────────────────────────────────
// Xrm mocks
// ─────────────────────────────────────────────────────────────────────────────

type AttributeMock = {
  setValue: jest.Mock;
  getIsDirty: jest.Mock;
};

let mockRetrieveRecord: jest.Mock;
let mockUpdateRecord: jest.Mock;
let mockGetAttribute: jest.Mock;
let attributes: Record<string, AttributeMock>;

function makeAttribute(): AttributeMock {
  return { setValue: jest.fn(), getIsDirty: jest.fn(() => true) };
}

/**
 * Install `window.Xrm` with `WebApi` (read path) and, unless `withPage` is
 * false, `Page` (form buffer). `withPage: false` simulates a host where
 * `Xrm.Page` is unreachable on every frame the shared walker checks — in jsdom
 * `window.parent === window`, so `getXrmPage()`'s parent branch short-circuits
 * and it returns `null`.
 */
function installXrm(options?: { withPage?: boolean; attributeNames?: string[] }): void {
  const withPage = options?.withPage !== false;
  const attributeNames = options?.attributeNames ?? ['hdr_title', 'hdr_notes', 'hdr_owner'];

  mockRetrieveRecord = jest.fn();
  mockUpdateRecord = jest.fn();

  attributes = {};
  for (const name of attributeNames) {
    attributes[name] = makeAttribute();
  }
  mockGetAttribute = jest.fn((name: string) => attributes[name] ?? null);

  const xrm: Record<string, unknown> = {
    WebApi: { retrieveRecord: mockRetrieveRecord, updateRecord: mockUpdateRecord },
  };
  if (withPage) {
    xrm.Page = { getAttribute: mockGetAttribute };
  }

  (globalThis as unknown as { Xrm?: unknown }).Xrm = xrm;
  (window as unknown as { Xrm?: unknown }).Xrm = xrm;
}

function uninstallXrm(): void {
  delete (globalThis as unknown as { Xrm?: unknown }).Xrm;
  delete (window as unknown as { Xrm?: unknown }).Xrm;
}

// ─────────────────────────────────────────────────────────────────────────────
// Fixtures
// ─────────────────────────────────────────────────────────────────────────────

const FORMATTED = '@OData.Community.Display.V1.FormattedValue';

const RECORD_1: Record<string, unknown> = {
  hdr_title: 'Original Title',
  hdr_notes: 'Original notes body',
  // Dataverse returns a lookup ONLY under the decorated `_<field>_value` key —
  // there is no bare `hdr_owner` key. displayLookup must read the decorated one.
  _hdr_owner_value: 'owner-guid-1',
  [`_hdr_owner_value${FORMATTED}`]: 'Contoso Ltd',
};

const RECORD_2: Record<string, unknown> = {
  hdr_title: 'Second Record Title',
  hdr_notes: 'Second record notes',
  _hdr_owner_value: 'owner-guid-2',
  [`_hdr_owner_value${FORMATTED}`]: 'Fabrikam Inc',
};

const FIELDS = ['hdr_title', 'hdr_notes', '_hdr_owner_value'];

const PICKED: ILookupItem = { id: 'owner-guid-9', name: 'Northwind Traders' };

/** Render the hook against `recordId` and wait for the initial read to settle. */
async function renderLoaded(recordId = 'rec-1') {
  const view = renderHook(
    ({ id, fields }: { id: string; fields: string[] }) =>
      useRecordHeaderFields({ entity: 'hdr_record', recordId: id, fields }),
    { initialProps: { id: recordId, fields: FIELDS } }
  );
  await waitFor(() => expect(view.result.current.values).not.toBeNull());
  return view;
}

// ─────────────────────────────────────────────────────────────────────────────
// Tests
// ─────────────────────────────────────────────────────────────────────────────

describe('projectLookup (pure)', () => {
  it('projects { id, name } from _field_value + its FormattedValue annotation', () => {
    expect(projectLookup(RECORD_1, 'hdr_owner')).toEqual({ id: 'owner-guid-1', name: 'Contoso Ltd' });
  });

  it('falls back to an empty name when the FormattedValue annotation is absent', () => {
    expect(projectLookup({ _hdr_owner_value: 'owner-guid-1' }, 'hdr_owner')).toEqual({
      id: 'owner-guid-1',
      name: '',
    });
  });

  it('returns null for a missing, empty, or non-string id', () => {
    expect(projectLookup({}, 'hdr_owner')).toBeNull();
    expect(projectLookup({ _hdr_owner_value: '' }, 'hdr_owner')).toBeNull();
    expect(projectLookup({ _hdr_owner_value: null }, 'hdr_owner')).toBeNull();
    expect(projectLookup({ _hdr_owner_value: 12345 }, 'hdr_owner')).toBeNull();
  });

  it('returns null when values is null or undefined', () => {
    expect(projectLookup(null, 'hdr_owner')).toBeNull();
    expect(projectLookup(undefined, 'hdr_owner')).toBeNull();
  });
});

describe('useRecordHeaderFields', () => {
  afterEach(() => {
    uninstallXrm();
    jest.clearAllMocks();
  });

  // ───────────────────────────────────────────────────────────────────────────
  // FR-13 — text staging via the form buffer, no Dataverse write, no refetch
  // ───────────────────────────────────────────────────────────────────────────

  describe('saveText — form-buffer staging (FR-13)', () => {
    it('calls setValue, records the pending value, and reflects it in displayText', async () => {
      installXrm();
      mockRetrieveRecord.mockResolvedValue(RECORD_1);

      const { result } = await renderLoaded();
      expect(result.current.displayText('hdr_title')).toBe('Original Title');

      await act(async () => {
        await result.current.saveText('hdr_title', 'Staged Title');
      });

      expect(mockGetAttribute).toHaveBeenCalledWith('hdr_title');
      expect(attributes.hdr_title.setValue).toHaveBeenCalledTimes(1);
      expect(attributes.hdr_title.setValue).toHaveBeenCalledWith('Staged Title');
      expect(result.current.displayText('hdr_title')).toBe('Staged Title');
    });

    it('NEVER calls Xrm.WebApi.updateRecord and NEVER re-retrieves after staging', async () => {
      installXrm();
      mockRetrieveRecord.mockResolvedValue(RECORD_1);

      const { result } = await renderLoaded();
      expect(mockRetrieveRecord).toHaveBeenCalledTimes(1);

      await act(async () => {
        await result.current.saveText('hdr_title', 'Staged Title');
        await result.current.saveText('hdr_notes', 'Staged notes');
      });

      // The R1 v1.0.7 regression guard: no write, no round trip.
      expect(mockUpdateRecord).not.toHaveBeenCalled();
      expect(mockRetrieveRecord).toHaveBeenCalledTimes(1);
    });

    it('does not toggle loading on save (the anti-flash guarantee)', async () => {
      installXrm();
      mockRetrieveRecord.mockResolvedValue(RECORD_1);

      const { result } = await renderLoaded();
      await waitFor(() => expect(result.current.loading).toBe(false));

      const loadingSamples: boolean[] = [];
      await act(async () => {
        await result.current.saveText('hdr_title', 'Staged Title');
        loadingSamples.push(result.current.loading);
      });
      loadingSamples.push(result.current.loading);

      expect(loadingSamples.every(v => v === false)).toBe(true);
    });

    it('does not expose a refresh() escape hatch that could reintroduce the refetch', async () => {
      installXrm();
      mockRetrieveRecord.mockResolvedValue(RECORD_1);

      const { result } = await renderLoaded();

      expect(result.current).not.toHaveProperty('refresh');
    });

    it('leaves other fields resolving from the Dataverse payload', async () => {
      installXrm();
      mockRetrieveRecord.mockResolvedValue(RECORD_1);

      const { result } = await renderLoaded();
      await act(async () => {
        await result.current.saveText('hdr_title', 'Staged Title');
      });

      expect(result.current.displayText('hdr_notes')).toBe('Original notes body');
    });
  });

  // ───────────────────────────────────────────────────────────────────────────
  // FR-13 — lookup staging + the `in`-check cleared-value semantics
  // ───────────────────────────────────────────────────────────────────────────

  describe('saveLookup — form-buffer staging (FR-13)', () => {
    it('writes the [{ id, name, entityType }] Xrm lookup shape', async () => {
      installXrm();
      mockRetrieveRecord.mockResolvedValue(RECORD_1);

      const { result } = await renderLoaded();

      act(() => {
        result.current.saveLookup('hdr_owner', PICKED, 'hdr_account');
      });

      expect(attributes.hdr_owner.setValue).toHaveBeenCalledTimes(1);
      expect(attributes.hdr_owner.setValue).toHaveBeenCalledWith([
        { id: 'owner-guid-9', name: 'Northwind Traders', entityType: 'hdr_account' },
      ]);
      expect(result.current.displayLookup('hdr_owner')).toEqual(PICKED);
      expect(mockUpdateRecord).not.toHaveBeenCalled();
      expect(mockRetrieveRecord).toHaveBeenCalledTimes(1);
    });

    it('writes null on clear AND displayLookup returns empty even though values still holds the Dataverse value', async () => {
      installXrm();
      mockRetrieveRecord.mockResolvedValue(RECORD_1);

      const { result } = await renderLoaded();
      // Pre-condition: the loaded record DOES have a lookup value.
      expect(result.current.displayLookup('hdr_owner')).toEqual({ id: 'owner-guid-1', name: 'Contoso Ltd' });

      act(() => {
        result.current.saveLookup('hdr_owner', null, 'hdr_account');
      });

      expect(attributes.hdr_owner.setValue).toHaveBeenCalledWith(null);
      // The `'name' in pendingLookup` membership check — a `??` fallback would
      // wrongly resurrect 'Contoso Ltd' here.
      expect(result.current.displayLookup('hdr_owner')).toBeNull();
      expect(result.current.values?._hdr_owner_value).toBe('owner-guid-1');
    });

    it('projects from values for a lookup that has NOT been staged', async () => {
      installXrm();
      mockRetrieveRecord.mockResolvedValue(RECORD_1);

      const { result } = await renderLoaded();

      expect(result.current.displayLookup('hdr_owner')).toEqual({ id: 'owner-guid-1', name: 'Contoso Ltd' });
      expect(attributes.hdr_owner.setValue).not.toHaveBeenCalled();
    });
  });

  // ───────────────────────────────────────────────────────────────────────────
  // FR-14 — the unified throwing path (no silent no-op in EITHER path)
  // ───────────────────────────────────────────────────────────────────────────

  describe('FR-14 — missing attribute throws in BOTH paths', () => {
    it('saveText rejects with "Field \'<name>\' not on form"', async () => {
      installXrm({ attributeNames: ['hdr_title'] });
      mockRetrieveRecord.mockResolvedValue(RECORD_1);

      const { result } = await renderLoaded();

      await expect(result.current.saveText('hdr_absent', 'value')).rejects.toThrow("Field 'hdr_absent' not on form");
    });

    it('saveLookup THROWS (it used to console.warn and silently drop the edit)', async () => {
      installXrm({ attributeNames: ['hdr_title'] });
      mockRetrieveRecord.mockResolvedValue(RECORD_1);

      const { result } = await renderLoaded();

      expect(() => result.current.saveLookup('hdr_absent', PICKED, 'hdr_account')).toThrow(
        "Field 'hdr_absent' not on form"
      );
    });

    it('a throwing save leaves the pending buffer untouched (no half-staged state)', async () => {
      installXrm({ attributeNames: ['hdr_title'] });
      mockRetrieveRecord.mockResolvedValue(RECORD_1);

      const { result } = await renderLoaded();

      await expect(result.current.saveText('hdr_absent', 'value')).rejects.toThrow();
      expect(() => result.current.saveLookup('hdr_absent', PICKED, 'hdr_account')).toThrow();

      expect(result.current.displayText('hdr_absent')).toBeUndefined();
      expect(result.current.displayLookup('hdr_absent')).toBeNull();
    });
  });

  describe('FR-14 — Xrm.Page unavailable throws in BOTH paths', () => {
    it('saveText rejects with "Form buffer unavailable"', async () => {
      installXrm({ withPage: false });
      mockRetrieveRecord.mockResolvedValue(RECORD_1);

      const { result } = await renderLoaded();

      await expect(result.current.saveText('hdr_title', 'value')).rejects.toThrow('Form buffer unavailable');
    });

    it('saveLookup throws "Form buffer unavailable" (no silent no-op)', async () => {
      installXrm({ withPage: false });
      mockRetrieveRecord.mockResolvedValue(RECORD_1);

      const { result } = await renderLoaded();

      expect(() => result.current.saveLookup('hdr_owner', PICKED, 'hdr_account')).toThrow('Form buffer unavailable');
    });
  });

  // ───────────────────────────────────────────────────────────────────────────
  // FR-13 — buffer reset ONLY on recordId change
  // ───────────────────────────────────────────────────────────────────────────

  describe('pending buffers reset on recordId change and at no other time', () => {
    it('resets BOTH buffers when recordId changes', async () => {
      installXrm();
      mockRetrieveRecord.mockImplementation((_entity: string, id: string) =>
        Promise.resolve(id === 'rec-1' ? RECORD_1 : RECORD_2)
      );

      const { result, rerender } = await renderLoaded('rec-1');

      await act(async () => {
        await result.current.saveText('hdr_title', 'Staged Title');
      });
      act(() => {
        result.current.saveLookup('hdr_owner', null, 'hdr_account');
      });

      expect(result.current.displayText('hdr_title')).toBe('Staged Title');
      expect(result.current.displayLookup('hdr_owner')).toBeNull();

      rerender({ id: 'rec-2', fields: FIELDS });

      await waitFor(() => expect(result.current.values).toEqual(RECORD_2));
      // Both buffers cleared → display falls back to the NEW record's values.
      expect(result.current.displayText('hdr_title')).toBe('Second Record Title');
      expect(result.current.displayLookup('hdr_owner')).toEqual({ id: 'owner-guid-2', name: 'Fabrikam Inc' });
    });

    it('does NOT reset on an unrelated re-render with the same recordId', async () => {
      installXrm();
      mockRetrieveRecord.mockResolvedValue(RECORD_1);

      const { result, rerender } = await renderLoaded('rec-1');

      await act(async () => {
        await result.current.saveText('hdr_title', 'Staged Title');
      });
      act(() => {
        result.current.saveLookup('hdr_owner', null, 'hdr_account');
      });

      // New `fields` ARRAY REFERENCE, identical contents, same recordId.
      rerender({ id: 'rec-1', fields: [...FIELDS] });
      await act(async () => {
        await Promise.resolve();
      });

      expect(result.current.displayText('hdr_title')).toBe('Staged Title');
      expect(result.current.displayLookup('hdr_owner')).toBeNull();
      // And no extra read was issued either (stable dep key).
      expect(mockRetrieveRecord).toHaveBeenCalledTimes(1);
    });
  });

  // ───────────────────────────────────────────────────────────────────────────
  // Read-path pass-through
  // ───────────────────────────────────────────────────────────────────────────

  describe('read path', () => {
    it('issues exactly one retrieveRecord with the $select built from fields', async () => {
      installXrm();
      mockRetrieveRecord.mockResolvedValue(RECORD_1);

      await renderLoaded();

      expect(mockRetrieveRecord).toHaveBeenCalledTimes(1);
      expect(mockRetrieveRecord).toHaveBeenCalledWith(
        'hdr_record',
        'rec-1',
        '?$select=hdr_title,hdr_notes,_hdr_owner_value'
      );
    });

    it('surfaces the read error without disturbing the staging surface', async () => {
      installXrm();
      const failure = new Error('Dataverse retrieve failed');
      mockRetrieveRecord.mockRejectedValue(failure);

      const { result } = renderHook(() =>
        useRecordHeaderFields({ entity: 'hdr_record', recordId: 'rec-1', fields: FIELDS })
      );

      await waitFor(() => expect(result.current.error).toBe(failure));
      expect(result.current.values).toBeNull();
      expect(result.current.loading).toBe(false);

      // Staging still works — the form buffer is independent of the read.
      await act(async () => {
        await result.current.saveText('hdr_title', 'Staged Despite Read Failure');
      });
      expect(attributes.hdr_title.setValue).toHaveBeenCalledWith('Staged Despite Read Failure');
      expect(result.current.displayText('hdr_title')).toBe('Staged Despite Read Failure');
    });
  });

  // ───────────────────────────────────────────────────────────────────────────
  // Structural constraints (ADR-012 / FR-20) — guards a real regression path:
  // someone re-adding a local window-walker or an entity constant to the hook.
  // ───────────────────────────────────────────────────────────────────────────

  describe('module structure (ADR-012 / FR-20)', () => {
    const SOURCE = fs.readFileSync(path.join(__dirname, '..', 'useRecordHeaderFields.ts'), 'utf8');
    // Strip comments — doc examples legitimately mention schema names; the
    // constraint is about CODE containing them.
    const CODE = SOURCE.replace(/\/\*[\s\S]*?\*\//g, '').replace(/^[^\n]*?\/\/.*$/gm, '');

    it('imports getXrmPage from the shared utils/xrmContext', () => {
      expect(SOURCE).toMatch(/getXrmPage[\s\S]{0,120}from '\.\.\/utils\/xrmContext'/);
    });

    it('contains no local window-walker', () => {
      expect(CODE).not.toMatch(/window\s*\.\s*(parent|top)/);
    });

    it('contains no sprk_-prefixed field or entity constants', () => {
      expect(CODE).not.toMatch(/sprk_/);
    });

    it('contains no Dataverse write call', () => {
      expect(CODE).not.toMatch(/updateRecord|createRecord|deleteRecord/);
    });

    it('contains no PCF ComponentFramework types', () => {
      expect(CODE).not.toMatch(/ComponentFramework/);
    });
  });
});
