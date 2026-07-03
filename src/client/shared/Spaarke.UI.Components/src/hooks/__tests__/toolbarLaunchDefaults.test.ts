/**
 * toolbarLaunchDefaults Unit Tests
 *
 * Verifies constants match spec (FR-08/09/10/14) exact values and that
 * buildMemoFilterForParent respects the ADR-024 dual-field pattern.
 */

import {
  LAYOUT_1_MODAL,
  NOTEPAD_MODAL,
  NOTEPAD_WEBRESOURCE_NAME,
  SMARTTODO_WEBRESOURCE_NAME,
  RECORDSUMMARY_FIELD,
  SUPPORTED_MEMO_PARENTS,
  buildMemoFilterForParent,
} from '../toolbarLaunchDefaults';

describe('toolbarLaunchDefaults', () => {
  describe('LAYOUT_1_MODAL', () => {
    it('matches R2 canonical modal standard (85% x 85%, target=2, position=1)', () => {
      expect(LAYOUT_1_MODAL).toEqual({
        target: 2,
        position: 1,
        width: { value: 85, unit: '%' },
        height: { value: 85, unit: '%' },
      });
    });
  });

  describe('NOTEPAD_MODAL', () => {
    it('matches Notepad specialized-editor sizing (70% x 80%, target=2, position=1)', () => {
      expect(NOTEPAD_MODAL).toEqual({
        target: 2,
        position: 1,
        width: { value: 70, unit: '%' },
        height: { value: 80, unit: '%' },
      });
    });
  });

  describe('webresource names', () => {
    it('exposes the Notepad code page name', () => {
      expect(NOTEPAD_WEBRESOURCE_NAME).toBe('sprk_notepad_page');
    });

    it('exposes the SmartTodo code page name', () => {
      expect(SMARTTODO_WEBRESOURCE_NAME).toBe('sprk_smarttodo_page');
    });
  });

  describe('RECORDSUMMARY_FIELD', () => {
    it('names the multiline-text field, not an entity', () => {
      expect(RECORDSUMMARY_FIELD).toBe('sprk_recordsummary');
    });
  });

  describe('SUPPORTED_MEMO_PARENTS', () => {
    it('covers exactly the six parent entities supported by sprk_memo schema', () => {
      expect(Object.keys(SUPPORTED_MEMO_PARENTS).sort()).toEqual(
        [
          'sprk_budget',
          'sprk_event',
          'sprk_invoice',
          'sprk_matter',
          'sprk_project',
          'sprk_workassignment',
        ].sort()
      );
    });

    it('maps each parent to its entity-specific regarding lookup', () => {
      expect(SUPPORTED_MEMO_PARENTS.sprk_matter).toBe('sprk_regardingmatter');
      expect(SUPPORTED_MEMO_PARENTS.sprk_project).toBe('sprk_regardingproject');
      expect(SUPPORTED_MEMO_PARENTS.sprk_event).toBe('sprk_regardingevent');
      expect(SUPPORTED_MEMO_PARENTS.sprk_invoice).toBe('sprk_regardinginvoice');
      expect(SUPPORTED_MEMO_PARENTS.sprk_budget).toBe('sprk_regardingbudget');
      expect(SUPPORTED_MEMO_PARENTS.sprk_workassignment).toBe(
        'sprk_regardingworkassignment'
      );
    });
  });

  describe('buildMemoFilterForParent', () => {
    it('builds OData filter using entity-specific lookup for a supported parent', () => {
      const filter = buildMemoFilterForParent(
        'sprk_matter',
        '00000000-0000-0000-0000-000000000001'
      );
      expect(filter).toBe(
        '_sprk_regardingmatter_value eq 00000000-0000-0000-0000-000000000001'
      );
    });

    it('builds filter for each of the six supported parents', () => {
      const guid = '11111111-1111-1111-1111-111111111111';
      expect(buildMemoFilterForParent('sprk_project', guid)).toBe(
        `_sprk_regardingproject_value eq ${guid}`
      );
      expect(buildMemoFilterForParent('sprk_event', guid)).toBe(
        `_sprk_regardingevent_value eq ${guid}`
      );
      expect(buildMemoFilterForParent('sprk_invoice', guid)).toBe(
        `_sprk_regardinginvoice_value eq ${guid}`
      );
      expect(buildMemoFilterForParent('sprk_budget', guid)).toBe(
        `_sprk_regardingbudget_value eq ${guid}`
      );
      expect(buildMemoFilterForParent('sprk_workassignment', guid)).toBe(
        `_sprk_regardingworkassignment_value eq ${guid}`
      );
    });

    it('returns null for an unsupported parent entity (e.g. sprk_document)', () => {
      const filter = buildMemoFilterForParent(
        'sprk_document',
        '00000000-0000-0000-0000-000000000001'
      );
      expect(filter).toBeNull();
    });

    it('returns null for an unknown entity name', () => {
      expect(buildMemoFilterForParent('foo_bar', 'guid1')).toBeNull();
    });

    it('is case-sensitive on entity name (Dataverse logical names are lowercase)', () => {
      expect(
        buildMemoFilterForParent('SPRK_MATTER', 'guid1')
      ).toBeNull();
    });
  });
});
