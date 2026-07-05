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
  SUPPORTED_TODO_PARENTS,
  buildMemoFilterForParent,
  buildTodoFilterForParent,
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
      // Verified via Dataverse MCP task 020 (2026-07-02): actual name is `sprk_smarttodo`
      // (no `_page` suffix — that was assumed by design.md but never applied at SmartTodo R3 ship).
      expect(SMARTTODO_WEBRESOURCE_NAME).toBe('sprk_smarttodo');
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
        ['sprk_budget', 'sprk_event', 'sprk_invoice', 'sprk_matter', 'sprk_project', 'sprk_workassignment'].sort()
      );
    });

    it('maps each parent to its entity-specific regarding lookup', () => {
      expect(SUPPORTED_MEMO_PARENTS.sprk_matter).toBe('sprk_regardingmatter');
      expect(SUPPORTED_MEMO_PARENTS.sprk_project).toBe('sprk_regardingproject');
      expect(SUPPORTED_MEMO_PARENTS.sprk_event).toBe('sprk_regardingevent');
      expect(SUPPORTED_MEMO_PARENTS.sprk_invoice).toBe('sprk_regardinginvoice');
      expect(SUPPORTED_MEMO_PARENTS.sprk_budget).toBe('sprk_regardingbudget');
      expect(SUPPORTED_MEMO_PARENTS.sprk_workassignment).toBe('sprk_regardingworkassignment');
    });
  });

  describe('buildMemoFilterForParent', () => {
    it('builds OData filter using entity-specific lookup for a supported parent', () => {
      const filter = buildMemoFilterForParent('sprk_matter', '00000000-0000-0000-0000-000000000001');
      expect(filter).toBe('_sprk_regardingmatter_value eq 00000000-0000-0000-0000-000000000001');
    });

    it('builds filter for each of the six supported parents', () => {
      const guid = '11111111-1111-1111-1111-111111111111';
      expect(buildMemoFilterForParent('sprk_project', guid)).toBe(`_sprk_regardingproject_value eq ${guid}`);
      expect(buildMemoFilterForParent('sprk_event', guid)).toBe(`_sprk_regardingevent_value eq ${guid}`);
      expect(buildMemoFilterForParent('sprk_invoice', guid)).toBe(`_sprk_regardinginvoice_value eq ${guid}`);
      expect(buildMemoFilterForParent('sprk_budget', guid)).toBe(`_sprk_regardingbudget_value eq ${guid}`);
      expect(buildMemoFilterForParent('sprk_workassignment', guid)).toBe(
        `_sprk_regardingworkassignment_value eq ${guid}`
      );
    });

    it('returns null for an unsupported parent entity (e.g. sprk_document)', () => {
      const filter = buildMemoFilterForParent('sprk_document', '00000000-0000-0000-0000-000000000001');
      expect(filter).toBeNull();
    });

    it('returns null for an unknown entity name', () => {
      expect(buildMemoFilterForParent('foo_bar', 'guid1')).toBeNull();
    });

    it('is case-sensitive on entity name (Dataverse logical names are lowercase)', () => {
      expect(buildMemoFilterForParent('SPRK_MATTER', 'guid1')).toBeNull();
    });
  });

  describe('SUPPORTED_TODO_PARENTS (v1.0.2 fix — sprk_todo has 11 parent lookups, not polymorphic)', () => {
    it('covers exactly the eleven parent entities supported by sprk_todo schema', () => {
      expect(Object.keys(SUPPORTED_TODO_PARENTS).sort()).toEqual(
        [
          'contact',
          'sprk_analysis',
          'sprk_budget',
          'sprk_communication',
          'sprk_document',
          'sprk_event',
          'sprk_invoice',
          'sprk_matter',
          'sprk_organization',
          'sprk_project',
          'sprk_workassignment',
        ].sort()
      );
    });

    it('maps sprk_todo parents to entity-specific regarding lookups (ADR-024 dual-field)', () => {
      // Verified via Dataverse MCP describe (2026-07-03) after live QA reported
      // "Could not find a property named '_regardingobjectid_value'" from the
      // v1.0.0 build. sprk_todo does NOT have a polymorphic regarding column.
      expect(SUPPORTED_TODO_PARENTS.sprk_matter).toBe('sprk_regardingmatter');
      expect(SUPPORTED_TODO_PARENTS.sprk_project).toBe('sprk_regardingproject');
      expect(SUPPORTED_TODO_PARENTS.sprk_event).toBe('sprk_regardingevent');
      expect(SUPPORTED_TODO_PARENTS.sprk_invoice).toBe('sprk_regardinginvoice');
      expect(SUPPORTED_TODO_PARENTS.sprk_budget).toBe('sprk_regardingbudget');
      expect(SUPPORTED_TODO_PARENTS.sprk_workassignment).toBe('sprk_regardingworkassignment');
      expect(SUPPORTED_TODO_PARENTS.sprk_analysis).toBe('sprk_regardinganalysis');
      expect(SUPPORTED_TODO_PARENTS.sprk_communication).toBe('sprk_regardingcommunication');
      expect(SUPPORTED_TODO_PARENTS.contact).toBe('sprk_regardingcontact');
      expect(SUPPORTED_TODO_PARENTS.sprk_document).toBe('sprk_regardingdocument');
      expect(SUPPORTED_TODO_PARENTS.sprk_organization).toBe('sprk_regardingorganization');
    });

    it('is a strict superset of SUPPORTED_MEMO_PARENTS', () => {
      // sprk_todo supports every parent sprk_memo supports, plus five more.
      for (const parent of Object.keys(SUPPORTED_MEMO_PARENTS)) {
        expect(SUPPORTED_TODO_PARENTS[parent]).toBeDefined();
      }
    });
  });

  describe('buildTodoFilterForParent', () => {
    it('builds OData filter using entity-specific lookup for a supported parent', () => {
      const filter = buildTodoFilterForParent('sprk_matter', '00000000-0000-0000-0000-000000000001');
      expect(filter).toBe('_sprk_regardingmatter_value eq 00000000-0000-0000-0000-000000000001');
    });

    it('returns null for an unsupported parent (e.g. sprk_playbook — not in sprk_todo schema)', () => {
      expect(buildTodoFilterForParent('sprk_playbook', 'guid1')).toBeNull();
    });

    it('does NOT emit the legacy polymorphic filter that produced the v1.0.0 bug', () => {
      const filter = buildTodoFilterForParent('sprk_matter', 'guid1');
      // Regression guard — v1.0.0 emitted `_regardingobjectid_value eq guid1`
      // which Dataverse rejected with 400.
      expect(filter).not.toMatch(/_regardingobjectid_value/);
    });
  });
});
