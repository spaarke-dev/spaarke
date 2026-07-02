/**
 * DataGrid.recordOpen.test — unit tests for `buildRecordOpenNavArgs`.
 *
 * Covers the ai-spaarke-ai-workspace-UI-r2 task 002 acceptance criteria:
 *   - FR-01: `rowOpen.formId` is deserializable and honored.
 *   - FR-02: When `formId` is set, it's forwarded as `pageInput.formId`.
 *   - FR-03/FR-20: navOptions is always the Layout 1 standard
 *     (target 2, position 1, width 85%, height 85%) — one size for every
 *     entity, no per-record variation.
 *
 * @see DataGrid.tsx `buildRecordOpenNavArgs`
 */

import { buildRecordOpenNavArgs } from '../DataGrid';

describe('buildRecordOpenNavArgs — R2 Layout 1 unification', () => {
  describe('Layout 1 standard nav options (FR-20)', () => {
    it('emits exactly target=2, position=1, 85% width, 85% height', () => {
      const { navOptions } = buildRecordOpenNavArgs('sprk_matter', 'record-1', undefined);

      expect(navOptions.target).toBe(2);
      expect(navOptions.position).toBe(1);
      expect(navOptions.width).toEqual({ value: 85, unit: '%' });
      expect(navOptions.height).toEqual({ value: 85, unit: '%' });
    });

    it('emits identical nav options regardless of entity (one size for every entity)', () => {
      const documents = buildRecordOpenNavArgs('sprk_document', 'r1', undefined).navOptions;
      const matters = buildRecordOpenNavArgs('sprk_matter', 'r2', undefined).navOptions;
      const invoices = buildRecordOpenNavArgs('sprk_invoice', 'r3', undefined).navOptions;

      expect(matters).toEqual(documents);
      expect(invoices).toEqual(documents);
    });
  });

  describe('pageInput construction', () => {
    it('sets pageType=entityrecord and forwards entityName + entityId', () => {
      const { pageInput } = buildRecordOpenNavArgs('sprk_project', 'abc-123', undefined);

      expect(pageInput.pageType).toBe('entityrecord');
      expect(pageInput.entityName).toBe('sprk_project');
      expect(pageInput.entityId).toBe('abc-123');
    });

    it('strips curly braces from recordId (Dataverse GUID formats)', () => {
      const { pageInput } = buildRecordOpenNavArgs('sprk_matter', '{abc-123-def}', undefined);

      expect(pageInput.entityId).toBe('abc-123-def');
    });
  });

  describe('formId forwarding (FR-01, FR-02)', () => {
    it('omits formId from pageInput when rowOpen is undefined', () => {
      const { pageInput } = buildRecordOpenNavArgs('sprk_matter', 'r1', undefined);

      expect(pageInput.formId).toBeUndefined();
      expect('formId' in pageInput).toBe(false);
    });

    it('omits formId from pageInput when rowOpen.formId is absent', () => {
      const { pageInput } = buildRecordOpenNavArgs('sprk_matter', 'r1', {});

      expect(pageInput.formId).toBeUndefined();
      expect('formId' in pageInput).toBe(false);
    });

    it('forwards rowOpen.formId as pageInput.formId when set', () => {
      const formGuid = '11111111-2222-3333-4444-555555555555';
      const { pageInput } = buildRecordOpenNavArgs('sprk_matter', 'r1', { formId: formGuid });

      expect(pageInput.formId).toBe(formGuid);
    });

    it('honors formId across every entity uniformly (framework contract, not per-entity)', () => {
      const formGuid = 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee';
      const documents = buildRecordOpenNavArgs('sprk_document', 'r1', { formId: formGuid });
      const invoices = buildRecordOpenNavArgs('sprk_invoice', 'r2', { formId: formGuid });

      expect(documents.pageInput.formId).toBe(formGuid);
      expect(invoices.pageInput.formId).toBe(formGuid);
    });
  });

  describe('null-safety', () => {
    it('tolerates null rowOpen (deserialization edge case)', () => {
      const { pageInput, navOptions } = buildRecordOpenNavArgs('sprk_matter', 'r1', null);

      expect(pageInput.entityName).toBe('sprk_matter');
      expect(pageInput.formId).toBeUndefined();
      expect(navOptions.target).toBe(2);
    });
  });
});
