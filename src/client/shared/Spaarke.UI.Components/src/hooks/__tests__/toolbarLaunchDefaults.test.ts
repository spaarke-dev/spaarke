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
    it('matches Notepad compact-editor sizing (25% x 35%, target=2, position=1)', () => {
      // R1 v1.0.7 deliberately shrank this from 70% x 80% to 25% x 35% after live
      // QA ("compact editor" — see MatterHeaderView.tsx's v1.0.7 note). This test
      // kept asserting the pre-v1.0.7 numbers and had been red ever since, which
      // made it worse than no test: it described behaviour the product had already
      // rejected. Corrected 2026-08-25 (record-header-and-notepad-r2 task 024).
      expect(NOTEPAD_MODAL).toEqual({
        target: 2,
        position: 1,
        width: { value: 25, unit: '%' },
        height: { value: 35, unit: '%' },
      });
    });
  });

  describe('webresource names', () => {
    it('exposes the Notepad code page name', () => {
      // Re-verified live against spaarkedev1 on 2026-08-25 (task 024):
      //   webresourceset?$filter=startswith(name,'sprk_notepad') -> sprk_notepad ("Notepad HTML")
      // The `_page` suffix was assumed by design.md and never shipped — the exact
      // same trap as the SmartTodo name below, and the one pcf-build-scaffold.md
      // gotcha #9 warns about. Verify the name; do not infer it.
      expect(NOTEPAD_WEBRESOURCE_NAME).toBe('sprk_notepad');
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
    it('covers exactly the seven parent entities supported by sprk_memo schema', () => {
      expect(Object.keys(SUPPORTED_MEMO_PARENTS).sort()).toEqual(
        [
          'sprk_agreement',
          'sprk_budget',
          'sprk_event',
          'sprk_invoice',
          'sprk_matter',
          'sprk_project',
          'sprk_workassignment',
        ].sort()
      );
      expect(Object.keys(SUPPORTED_MEMO_PARENTS)).toHaveLength(7);
    });

    it('maps each parent to its entity-specific regarding lookup', () => {
      expect(SUPPORTED_MEMO_PARENTS.sprk_matter).toBe('sprk_regardingmatter');
      expect(SUPPORTED_MEMO_PARENTS.sprk_project).toBe('sprk_regardingproject');
      expect(SUPPORTED_MEMO_PARENTS.sprk_event).toBe('sprk_regardingevent');
      expect(SUPPORTED_MEMO_PARENTS.sprk_invoice).toBe('sprk_regardinginvoice');
      expect(SUPPORTED_MEMO_PARENTS.sprk_budget).toBe('sprk_regardingbudget');
      expect(SUPPORTED_MEMO_PARENTS.sprk_workassignment).toBe('sprk_regardingworkassignment');
      // FR-24 (R2 task 024): sprk_agreement added — owner live-verified
      // sprk_regardingagreement exists on sprk_memo (2026-08-25).
      expect(SUPPORTED_MEMO_PARENTS.sprk_agreement).toBe('sprk_regardingagreement');
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

    it('builds OData filter for sprk_agreement (FR-24 — added R2 task 024)', () => {
      const filter = buildMemoFilterForParent('sprk_agreement', '00000000-0000-0000-0000-000000000001');
      expect(filter).toBe('_sprk_regardingagreement_value eq 00000000-0000-0000-0000-000000000001');
    });
  });

  describe('SUPPORTED_TODO_PARENTS (v1.0.2 fix — sprk_todo has 12 parent lookups, not polymorphic)', () => {
    it('covers exactly the twelve parent entities supported by sprk_todo schema', () => {
      expect(Object.keys(SUPPORTED_TODO_PARENTS).sort()).toEqual(
        [
          'contact',
          'sprk_agreement',
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
      expect(Object.keys(SUPPORTED_TODO_PARENTS)).toHaveLength(12);
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
      // FR-24 (R2 task 024): sprk_agreement added — owner live-verified
      // sprk_regardingagreement exists on sprk_todo (2026-08-25).
      expect(SUPPORTED_TODO_PARENTS.sprk_agreement).toBe('sprk_regardingagreement');
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
