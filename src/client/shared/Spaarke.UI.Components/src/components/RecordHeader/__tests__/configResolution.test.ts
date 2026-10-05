/**
 * configResolution.test — exhaustive unit tests for `resolveHeaderConfig`, the
 * pure two-tier RecordHeader config resolver.
 *
 * MAINTAIN-class per ADR-038 §7 — pure domain logic (a KEEP category): no
 * React, no I/O, no mocks, plain object literals in and a plain object out.
 * Mirrors the test approach of `components/DataGrid/__tests__/configResolution.*`.
 *
 * Covers record-header-and-notepad-r2 task 031 acceptance criteria
 * (spec FR-02, FR-03, FR-04, FR-05, NFR-10):
 *   - tier 1: the design.md §5.2 Matter example resolves fields exactly as configured
 *   - FR-05: span is ALWAYS clamped to `min(span, columns)`, in BOTH tiers
 *   - renderer-derived span defaulting (textarea ⇒ columns, everything else ⇒ 1)
 *   - FR-03/NFR-10: every malformed input ⇒ exactly ONE console.warn + tier 2, never throws
 *   - FR-04: tier-2 derivation — primary name first at span 2, ≤4 more in form order,
 *     the IDENTICAL skipSet as `synthesizeColumnsFromMetadata`
 *   - the full renderer-derivation table + config override precedence
 *   - columns normalization (2/3 honored, everything else ⇒ 3)
 *   - FR-02: no optionals survive the merge
 *   - summaryField pass-through (no RECORDSUMMARY_FIELD default here — task 034 owns it)
 *   - module purity (no react / @fluentui / DataGrid / services / hooks imports)
 *
 * @see ../configResolution — the resolver under test
 * @see ../../DataGrid/configResolution — the canonical structural reference it mirrors
 */

import * as fs from 'fs';
import * as path from 'path';
import { extractConfiguredAttributeNames, resolveHeaderConfig } from '../configResolution';
import type {
  HeaderAttributeMetadata,
  HeaderFormMetadata,
  ResolvedHeaderConfig,
  ResolvedHeaderField,
} from '../configResolution';
import type { RecordHeaderFieldRenderer } from '../../../types/RecordHeaderConfiguration';

// ─────────────────────────────────────────────────────────────────────────────
// Fixtures
// ─────────────────────────────────────────────────────────────────────────────

/**
 * The design.md §5.2 Matter example, verbatim — the same payload task 030's
 * guard test uses, so the two suites exercise one shape rather than two.
 */
const MATTER_MANIFEST_JSON = JSON.stringify({
  _version: '1.0',
  title: 'Matter',
  columns: 3,
  summaryField: 'sprk_mattersummary',
  fields: [
    { name: 'sprk_matternumber', span: 1, required: true },
    { name: 'sprk_mattername', span: 2 },
    { name: 'sprk_mattertype', span: 1 },
    { name: 'sprk_practicearea', span: 1 },
    { name: 'sprk_matterdescription', span: 3, maxLines: 10 },
  ],
});

/**
 * Metadata matching the Matter example. Deliberately mixes the label sources so
 * the merge precedence is exercised in one pass:
 *   - `sprk_matternumber`  → form-control `label` only
 *   - `sprk_mattertype`    → metadata `displayName` only
 *   - `sprk_practicearea`  → BOTH (form-control label must win)
 *   - `sprk_mattersubject` → NEITHER (humanized logical name must be used)
 */
const matterMetadata: HeaderFormMetadata = {
  entityLogicalName: 'sprk_matter',
  entityDisplayName: 'Legal Matter',
  primaryIdAttribute: 'sprk_matterid',
  primaryNameAttribute: 'sprk_mattername',
  attributes: {
    sprk_matterid: { attributeType: 'Uniqueidentifier' },
    sprk_matternumber: { attributeType: 'String', label: 'Matter Number', requiredLevel: 'ApplicationRequired' },
    sprk_mattername: { attributeType: 'String', label: 'Matter Name', requiredLevel: 'SystemRequired' },
    sprk_mattertype: { attributeType: 'Lookup', displayName: 'Matter Type' },
    sprk_practicearea: { attributeType: 'Lookup', label: 'Practice Area', displayName: 'IGNORED Display Name' },
    sprk_matterdescription: { attributeType: 'Memo', label: 'Matter Description' },
    sprk_mattersubject: { attributeType: 'String' },
  },
};

/**
 * Project metadata — the tier-2 workhorse.
 *
 * Insertion order interleaves system columns with business columns so the skip
 * list, the form-order walk and the 5-field cap are all exercised together.
 *
 * ⚠️ Primary name is `sprk_projectnumber`, NOT `sprk_projectname` — both columns
 * exist on the live entity (verified 2026-08-24). A resolver that guessed the
 * primary name from a naming convention would pick the wrong one here.
 */
const projectMetadata: HeaderFormMetadata = {
  entityLogicalName: 'sprk_project',
  entityDisplayName: 'Project',
  primaryIdAttribute: 'sprk_projectid',
  primaryNameAttribute: 'sprk_projectnumber',
  attributes: {
    sprk_projectid: { attributeType: 'Uniqueidentifier' },
    createdon: { attributeType: 'DateTime', format: 'DateAndTime' },
    sprk_projectnumber: { attributeType: 'String', label: 'Project Number' },
    sprk_projectname: { attributeType: 'String', label: 'Project Name' },
    modifiedon: { attributeType: 'DateTime', format: 'DateAndTime' },
    sprk_projecttype: { attributeType: 'Lookup', label: 'Project Type' },
    createdby: { attributeType: 'Lookup' },
    sprk_startdate: { attributeType: 'DateTime', format: 'DateOnly', label: 'Start Date' },
    modifiedby: { attributeType: 'Lookup' },
    ownerid: { attributeType: 'Owner' },
    sprk_budget: { attributeType: 'Money', label: 'Budget' },
    statecode: { attributeType: 'State' },
    statuscode: { attributeType: 'Status' },
    versionnumber: { attributeType: 'BigInt' },
    sprk_description: { attributeType: 'Memo', label: 'Description' },
    sprk_neverreached: { attributeType: 'String', label: 'Never Reached' },
  },
};

/** The eight audit/state columns the skip list must exclude, per FR-04. */
const AUDIT_SKIP_LIST: string[] = [
  'createdon',
  'modifiedon',
  'createdby',
  'modifiedby',
  'ownerid',
  'statecode',
  'statuscode',
  'versionnumber',
];

/** Build a minimal single-attribute metadata for focused renderer-derivation tests. */
function singleAttributeMetadata(
  attributeType: string | undefined,
  format?: string,
  extra?: Partial<HeaderAttributeMetadata>
): HeaderFormMetadata {
  return {
    entityLogicalName: 'sprk_widget',
    entityDisplayName: 'Widget',
    primaryIdAttribute: 'sprk_widgetid',
    primaryNameAttribute: 'sprk_widgetname',
    attributes: {
      sprk_field: { attributeType, format, ...(extra ?? {}) },
    },
  };
}

/** Wrap a `fields` array into a valid v1.0 manifest string. */
function manifest(fields: ReadonlyArray<Record<string, unknown>>, top?: Record<string, unknown>): string {
  return JSON.stringify({ _version: '1.0', ...(top ?? {}), fields });
}

/** Every field key that must be concrete (non-undefined) after the merge (FR-02). */
const REQUIRED_FIELD_KEYS: ReadonlyArray<keyof ResolvedHeaderField> = [
  'name',
  'label',
  'span',
  'renderer',
  'readOnly',
  'required',
];

/** Assert FR-02 ("no optionals after merge") + FR-05 (clamp) over a whole result. */
function expectFullyResolved(result: ResolvedHeaderConfig): void {
  expect(typeof result.title).toBe('string');
  expect(result.title.length).toBeGreaterThan(0);
  expect([2, 3]).toContain(result.columns);
  expect(Array.isArray(result.fields)).toBe(true);
  for (const field of result.fields) {
    for (const key of REQUIRED_FIELD_KEYS) {
      expect(field[key]).toBeDefined();
    }
    expect(typeof field.name).toBe('string');
    expect(field.name.length).toBeGreaterThan(0);
    expect(typeof field.label).toBe('string');
    expect(field.label.length).toBeGreaterThan(0);
    expect(typeof field.span).toBe('number');
    expect(typeof field.renderer).toBe('string');
    expect(typeof field.readOnly).toBe('boolean');
    expect(typeof field.required).toBe('boolean');
    // FR-05 — the clamp invariant, asserted on EVERY result this suite produces.
    expect(field.span).toBeGreaterThanOrEqual(1);
    expect(field.span).toBeLessThanOrEqual(result.columns);
  }
}

// ─────────────────────────────────────────────────────────────────────────────

describe('resolveHeaderConfig', () => {
  let warnSpy: jest.SpyInstance;

  beforeEach(() => {
    warnSpy = jest.spyOn(console, 'warn').mockImplementation(() => undefined);
  });

  afterEach(() => {
    warnSpy.mockRestore();
  });

  // ───────────────────────────────────────────────────────────────────────────
  // Tier 1 — the design.md §5.2 Matter example
  // ───────────────────────────────────────────────────────────────────────────

  describe('tier 1 — valid layoutJson (design.md §5.2 Matter example)', () => {
    let result: ResolvedHeaderConfig;

    beforeEach(() => {
      result = resolveHeaderConfig(MATTER_MANIFEST_JSON, matterMetadata);
    });

    it('emits no console.warn for a fully valid configuration', () => {
      expect(warnSpy).not.toHaveBeenCalled();
    });

    it('resolves the fields exactly as configured, in configured order', () => {
      expect(result.fields.map(f => f.name)).toEqual([
        'sprk_matternumber',
        'sprk_mattername',
        'sprk_mattertype',
        'sprk_practicearea',
        'sprk_matterdescription',
      ]);
    });

    it('honors the configured title, columns and summaryField', () => {
      expect(result.title).toBe('Matter');
      expect(result.columns).toBe(3);
      expect(result.summaryField).toBe('sprk_mattersummary');
    });

    it('merges labels per precedence: config ?? form-control label ?? displayName ?? humanized', () => {
      const labels = Object.fromEntries(result.fields.map(f => [f.name, f.label]));
      // form-control `label` only
      expect(labels['sprk_matternumber']).toBe('Matter Number');
      // metadata `displayName` only
      expect(labels['sprk_mattertype']).toBe('Matter Type');
      // BOTH present — the form-control label wins
      expect(labels['sprk_practicearea']).toBe('Practice Area');
    });

    it('prefers a config label over every metadata source', () => {
      const withLabel = resolveHeaderConfig(
        manifest([{ name: 'sprk_practicearea', label: 'Config Wins' }]),
        matterMetadata
      );
      expect(withLabel.fields[0].label).toBe('Config Wins');
    });

    it('falls back to humanizeLogicalName when neither label source exists', () => {
      const humanized = resolveHeaderConfig(manifest([{ name: 'sprk_mattersubject' }]), matterMetadata);
      expect(humanized.fields[0].label).toBe('Mattersubject');
    });

    it('derives renderers from attribute type for every configured field', () => {
      const renderers = Object.fromEntries(result.fields.map(f => [f.name, f.renderer]));
      expect(renderers).toEqual({
        sprk_matternumber: 'text',
        sprk_mattername: 'text',
        sprk_mattertype: 'lookup',
        sprk_practicearea: 'lookup',
        sprk_matterdescription: 'textarea',
      });
    });

    it('merges required per precedence: explicit config value, else the metadata requirement level', () => {
      const required = Object.fromEntries(result.fields.map(f => [f.name, f.required]));
      expect(required['sprk_matternumber']).toBe(true); // config `required: true`
      expect(required['sprk_mattername']).toBe(true); // derived from SystemRequired
      expect(required['sprk_mattertype']).toBe(false); // no config, no requiredLevel
      expect(required['sprk_matterdescription']).toBe(false);
    });

    it('resolves every configured span, all within the column count', () => {
      expect(result.fields.map(f => f.span)).toEqual([1, 2, 1, 1, 3]);
    });

    it('passes maxLines through only for the field that declared it', () => {
      expect(result.fields[4].name).toBe('sprk_matterdescription');
      expect(result.fields[4].maxLines).toBe(10);
      // Omitted entirely — not present-and-undefined.
      expect(result.fields[0]).not.toHaveProperty('maxLines');
      expect(Object.keys(result.fields[0])).toEqual(['name', 'label', 'span', 'renderer', 'readOnly', 'required']);
    });

    it('produces a fully-resolved result (FR-02) with every span clamped (FR-05)', () => {
      expectFullyResolved(result);
    });
  });

  describe('tier 1 — the reference ~600B and ~900B v1.0 payloads', () => {
    // Reused verbatim from task 030's guard suite so both suites exercise the
    // same realistic maker payloads. A realistic maximum layout is only ~900 B.
    const workAssignmentLayout =
      '{"_version":"1.0","title":"Work Assignment","columns":3,"summaryField":"sprk_recordsummary",' +
      '"fields":[{"name":"sprk_workassignmentnumber","span":1,"required":true,"label":"Assignment Number"},' +
      '{"name":"sprk_workassignmentname","span":2,"label":"Assignment Name"},' +
      '{"name":"sprk_assignmenttype","span":1,"label":"Assignment Type"},' +
      '{"name":"sprk_responseduedate","span":1,"label":"Response Due Date"},' +
      '{"name":"sprk_highpriority","span":1,"label":"High Priority"},' +
      '{"name":"sprk_assignmentstatus","span":1,"label":"Status"},' +
      '{"name":"sprk_assigneddescription","span":3,"maxLines":10,"label":"Assignment Description"}]}';

    const workAssignmentMetadata: HeaderFormMetadata = {
      entityLogicalName: 'sprk_workassignment',
      entityDisplayName: 'Work Assignment',
      primaryIdAttribute: 'sprk_workassignmentid',
      primaryNameAttribute: 'sprk_workassignmentname',
      attributes: {
        sprk_workassignmentnumber: { attributeType: 'String' },
        sprk_workassignmentname: { attributeType: 'String' },
        sprk_assignmenttype: { attributeType: 'Picklist' },
        sprk_responseduedate: { attributeType: 'DateTime', format: 'DateOnly' },
        sprk_highpriority: { attributeType: 'Boolean' },
        sprk_assignmentstatus: { attributeType: 'Status' },
        sprk_assigneddescription: { attributeType: 'Memo' },
      },
    };

    it('resolves the ~600B Work Assignment layout with no warning and the full renderer spread', () => {
      const result = resolveHeaderConfig(workAssignmentLayout, workAssignmentMetadata);
      expect(warnSpy).not.toHaveBeenCalled();
      expect(result.fields.map(f => f.renderer)).toEqual([
        'text',
        'text',
        'optionset',
        'date',
        'boolean',
        'optionset',
        'textarea',
      ]);
      expect(result.fields.map(f => f.span)).toEqual([1, 2, 1, 1, 1, 1, 3]);
      expectFullyResolved(result);
    });

    it('resolves the ~900B 11-field stress layout without warning or truncation', () => {
      const stressLayout =
        '{"_version":"1.0","title":"Work Assignment Stress Layout","columns":3,"summaryField":"sprk_recordsummary",' +
        '"fields":[{"name":"sprk_workassignmentnumber","span":1,"required":true,"label":"Assignment Number"},' +
        '{"name":"sprk_workassignmentname","span":2,"label":"Assignment Name"},' +
        '{"name":"sprk_assignmenttype","span":1,"label":"Assignment Type"},' +
        '{"name":"sprk_responseduedate","span":1,"label":"Response Due Date"},' +
        '{"name":"sprk_highpriority","span":1,"label":"High Priority Flag"},' +
        '{"name":"sprk_assignmentstatus","span":1,"label":"Assignment Status"},' +
        '{"name":"sprk_monitor","span":1,"label":"Monitor This Record"},' +
        '{"name":"sprk_estimatedhours","span":1,"label":"Estimated Hours"},' +
        '{"name":"sprk_actualhours","span":1,"label":"Actual Hours Logged"},' +
        '{"name":"sprk_regardingmatter","span":2,"label":"Regarding Matter"},' +
        '{"name":"sprk_assigneddescription","span":3,"maxLines":10,"label":"Assignment Description Long Form"}]}';
      const result = resolveHeaderConfig(stressLayout, workAssignmentMetadata);
      expect(warnSpy).not.toHaveBeenCalled();
      // Tier 1 has NO field cap — the 5-field cap belongs to tier-2 derivation only.
      expect(result.fields).toHaveLength(11);
      expectFullyResolved(result);
    });
  });

  // ───────────────────────────────────────────────────────────────────────────
  // FR-05 — the span clamp
  // ───────────────────────────────────────────────────────────────────────────

  describe('FR-05 — span clamp: span = min(span, columns)', () => {
    it('clamps a span 3 field to 2 in a columns 2 layout', () => {
      const result = resolveHeaderConfig(
        manifest([{ name: 'sprk_matternumber', span: 3 }], { columns: 2 }),
        matterMetadata
      );
      expect(result.columns).toBe(2);
      expect(result.fields[0].span).toBe(2);
    });

    it('defaults an unspecified textarea span to the column count (3 in a columns 3 layout)', () => {
      const result = resolveHeaderConfig(
        manifest([{ name: 'sprk_matterdescription' }], { columns: 3 }),
        matterMetadata
      );
      expect(result.fields[0].renderer).toBe('textarea');
      expect(result.fields[0].span).toBe(3);
    });

    it('defaults an unspecified textarea span to 2 in a columns 2 layout (default is itself clamped)', () => {
      const result = resolveHeaderConfig(
        manifest([{ name: 'sprk_matterdescription' }], { columns: 2 }),
        matterMetadata
      );
      expect(result.fields[0].span).toBe(2);
    });

    it('defaults an unspecified non-textarea span to 1', () => {
      const result = resolveHeaderConfig(manifest([{ name: 'sprk_matternumber' }]), matterMetadata);
      expect(result.fields[0].span).toBe(1);
    });

    it.each<[string, unknown, 2 | 3, number]>([
      ['span 3 in columns 3', 3, 3, 3],
      ['span 2 in columns 3', 2, 3, 2],
      ['span 99 in columns 3', 99, 3, 3],
      ['span 99 in columns 2', 99, 2, 2],
      ['span 0 in columns 3 (invalid ⇒ renderer default 1)', 0, 3, 1],
      ['span -5 in columns 3 (invalid ⇒ renderer default 1)', -5, 3, 1],
      ['span 2.7 in columns 3 (floors to 2)', 2.7, 3, 2],
      ["span '3' as a string (invalid ⇒ renderer default 1)", '3', 3, 1],
      ['span null (invalid ⇒ renderer default 1)', null, 3, 1],
      ['span NaN (invalid ⇒ renderer default 1)', NaN, 3, 1],
    ])('%s', (_label, span, columns, expected) => {
      const result = resolveHeaderConfig(manifest([{ name: 'sprk_matternumber', span }], { columns }), matterMetadata);
      expect(result.fields[0].span).toBe(expected);
    });

    it('clamps EVERY field in a mixed layout, not just the first', () => {
      const result = resolveHeaderConfig(
        manifest(
          [
            { name: 'sprk_matternumber', span: 3 },
            { name: 'sprk_mattername', span: 3 },
            { name: 'sprk_matterdescription' },
          ],
          { columns: 2 }
        ),
        matterMetadata
      );
      expect(result.fields.map(f => f.span)).toEqual([2, 2, 2]);
    });

    it('clamps tier-2 derived fields too (primary name span 2 in a columns 2 layout)', () => {
      // A guard-passing config with an empty `fields` array: `columns` is still
      // honored while the field list falls through to tier-2 derivation.
      const result = resolveHeaderConfig('{"_version":"1.0","columns":2,"fields":[]}', projectMetadata);
      expect(result.columns).toBe(2);
      for (const field of result.fields) {
        expect(field.span).toBeLessThanOrEqual(2);
      }
      expect(result.fields[0].span).toBe(2);
    });
  });

  // ───────────────────────────────────────────────────────────────────────────
  // FR-03 / NFR-10 — graceful degradation
  // ───────────────────────────────────────────────────────────────────────────

  describe('FR-03 / NFR-10 — malformed input ⇒ exactly one console.warn + tier-2 defaults, never throws', () => {
    const negativeCases: ReadonlyArray<[string, string | null | undefined]> = [
      ['undefined', undefined],
      ['null', null],
      ['an empty string', ''],
      ['whitespace only', '   '],
      ['unparseable JSON ({{{)', '{{{'],
      ['a truncated object', '{"_version":"1.0","fields":['],
      ["valid JSON with _version '2.0'", '{"_version":"2.0","fields":[{"name":"sprk_projectname"}]}'],
      ['valid JSON with fields missing', '{"_version":"1.0","title":"No Fields Key"}'],
      ['valid JSON with fields as an object', '{"_version":"1.0","fields":{}}'],
      ['JSON null', 'null'],
      ['a JSON array', '[{"name":"sprk_projectname"}]'],
      ['a JSON scalar', '42'],
    ];

    it.each(negativeCases)('%s produces exactly one console.warn', (_label, input) => {
      resolveHeaderConfig(input, projectMetadata);
      expect(warnSpy).toHaveBeenCalledTimes(1);
    });

    it.each(negativeCases)('%s falls through to a non-empty tier-2 result', (_label, input) => {
      const result = resolveHeaderConfig(input, projectMetadata);
      expect(result.fields.length).toBeGreaterThan(0);
      expect(result.fields[0].name).toBe('sprk_projectnumber');
      expectFullyResolved(result);
    });

    it.each(negativeCases)('%s never throws', (_label, input) => {
      expect(() => resolveHeaderConfig(input, projectMetadata)).not.toThrow();
    });

    it('never throws for an exotic input matrix (including metadata the type system says cannot happen)', () => {
      const exoticInputs: unknown[] = [
        undefined,
        null,
        '',
        '{{{',
        '{"_version":"1.0","fields":[{}]}',
        '{"_version":"1.0","fields":[null,42,"x",[],{}]}',
        '{"_version":"1.0","columns":"3","fields":[{"name":123}]}',
        '{"_version":"1.0","title":null,"summaryField":99,"fields":[{"name":"sprk_projectname","span":{},"renderer":7,"readOnly":"yes","required":"no","maxLines":"ten"}]}',
        JSON.stringify({ _version: '1.0', fields: [{ name: 'sprk_projectname' }] }),
      ];
      const exoticMetadata: unknown[] = [
        projectMetadata,
        undefined,
        null,
        {},
        { attributes: null },
        { attributes: [] },
        {
          entityLogicalName: 'sprk_x',
          primaryIdAttribute: 'sprk_xid',
          primaryNameAttribute: 'sprk_xname',
          attributes: {},
        },
      ];

      for (const input of exoticInputs) {
        for (const metadata of exoticMetadata) {
          expect(() =>
            resolveHeaderConfig(input as string | null | undefined, metadata as HeaderFormMetadata)
          ).not.toThrow();
        }
      }
    });

    it('renders a usable header even when metadata is entirely empty (never blank-by-exception)', () => {
      const empty = {
        entityLogicalName: 'sprk_thing',
        primaryIdAttribute: '',
        primaryNameAttribute: '',
        attributes: {},
      };
      const result = resolveHeaderConfig(undefined, empty as HeaderFormMetadata);
      expect(result.title).toBe('Thing');
      expect(result.columns).toBe(3);
      expect(result.fields).toEqual([]);
    });

    it('the warning names the resolver and the entity, so it is greppable in a browser console', () => {
      resolveHeaderConfig('{{{', projectMetadata);
      const message = String(warnSpy.mock.calls[0][0]);
      expect(message).toContain('[RecordHeader] resolveHeaderConfig:');
      expect(message).toContain('sprk_project');
    });
  });

  describe('shallow-guard fallout — fields the 030 guard deliberately lets through', () => {
    it('falls through to tier 2 (one warn) for fields: [{}] — the guard returns true for it', () => {
      const result = resolveHeaderConfig('{"_version":"1.0","fields":[{}]}', projectMetadata);
      expect(warnSpy).toHaveBeenCalledTimes(1);
      expect(result.fields[0].name).toBe('sprk_projectnumber');
      expectFullyResolved(result);
    });

    it('falls through to tier 2 (one warn) for an empty fields array', () => {
      const result = resolveHeaderConfig('{"_version":"1.0","fields":[]}', projectMetadata);
      expect(warnSpy).toHaveBeenCalledTimes(1);
      expect(result.fields.length).toBeGreaterThan(0);
    });

    it('drops only the unusable entries (one warn) when SOME entries are valid', () => {
      const result = resolveHeaderConfig(
        '{"_version":"1.0","fields":[{},{"name":"sprk_projectname"},null,{"name":""},{"name":"sprk_budget"}]}',
        projectMetadata
      );
      expect(warnSpy).toHaveBeenCalledTimes(1);
      expect(result.fields.map(f => f.name)).toEqual(['sprk_projectname', 'sprk_budget']);
      expectFullyResolved(result);
    });

    it.each(['toString', 'constructor', '__proto__', 'hasOwnProperty'])(
      'treats the prototype-member name %s as absent metadata, not as an attribute',
      protoName => {
        const result = resolveHeaderConfig(manifest([{ name: protoName }]), projectMetadata);
        expect(result.fields).toHaveLength(1);
        // Falls through the whole merge chain to the humanized-name fallback,
        // rather than reading an inherited Object.prototype member as metadata.
        expect(result.fields[0]).toMatchObject({ name: protoName, renderer: 'text', span: 1, required: false });
        expectFullyResolved(result);
      }
    );

    it('ignores a prototype-member primaryNameAttribute during tier-2 derivation', () => {
      const poisoned: HeaderFormMetadata = { ...projectMetadata, primaryNameAttribute: 'toString' };
      const result = resolveHeaderConfig(undefined, poisoned);
      expect(result.fields.map(f => f.name)).not.toContain('toString');
    });

    it('resolves a config field naming an attribute absent from metadata, without throwing', () => {
      const result = resolveHeaderConfig(manifest([{ name: 'sprk_doesnotexist' }]), projectMetadata);
      expect(result.fields).toHaveLength(1);
      expect(result.fields[0]).toMatchObject({
        name: 'sprk_doesnotexist',
        label: 'Doesnotexist',
        renderer: 'text',
        span: 1,
        readOnly: false,
        required: false,
      });
    });
  });

  // ───────────────────────────────────────────────────────────────────────────
  // FR-04 — tier-2 derived defaults
  // ───────────────────────────────────────────────────────────────────────────

  describe('FR-04 — tier-2 derivation', () => {
    let result: ResolvedHeaderConfig;

    beforeEach(() => {
      result = resolveHeaderConfig(undefined, projectMetadata);
    });

    it('puts the primary name field first, at span 2', () => {
      expect(result.fields[0].name).toBe('sprk_projectnumber');
      expect(result.fields[0].span).toBe(2);
    });

    it('emits at most 5 fields — the primary name plus up to four more', () => {
      expect(result.fields).toHaveLength(5);
    });

    it('takes the further fields in metadata insertion order (= form order)', () => {
      expect(result.fields.map(f => f.name)).toEqual([
        'sprk_projectnumber',
        'sprk_projectname',
        'sprk_projecttype',
        'sprk_startdate',
        'sprk_budget',
      ]);
    });

    it('excludes the primary id attribute', () => {
      expect(result.fields.map(f => f.name)).not.toContain('sprk_projectid');
    });

    it.each(AUDIT_SKIP_LIST)('excludes the system attribute %s', attributeName => {
      expect(result.fields.map(f => f.name)).not.toContain(attributeName);
    });

    it('stops at the cap rather than continuing past it', () => {
      expect(result.fields.map(f => f.name)).not.toContain('sprk_description');
      expect(result.fields.map(f => f.name)).not.toContain('sprk_neverreached');
    });

    it('derives each field label and renderer through the same merge chain as tier 1', () => {
      expect(result.fields.map(f => [f.label, f.renderer])).toEqual([
        ['Project Number', 'text'],
        ['Project Name', 'text'],
        ['Project Type', 'lookup'],
        ['Start Date', 'date'],
        ['Budget', 'currency'],
      ]);
    });

    it('gives every non-primary derived field span 1 (no renderer default fires here)', () => {
      expect(result.fields.slice(1).map(f => f.span)).toEqual([1, 1, 1, 1]);
    });

    it('clamps a derived textarea to the column count', () => {
      const memoFirst: HeaderFormMetadata = {
        entityLogicalName: 'sprk_note',
        entityDisplayName: 'Note',
        primaryIdAttribute: 'sprk_noteid',
        primaryNameAttribute: 'sprk_notename',
        attributes: {
          sprk_notename: { attributeType: 'String' },
          sprk_body: { attributeType: 'Memo' },
        },
      };
      const threeCol = resolveHeaderConfig(undefined, memoFirst);
      expect(threeCol.fields[1]).toMatchObject({ name: 'sprk_body', renderer: 'textarea', span: 3 });

      const twoCol = resolveHeaderConfig('{"_version":"1.0","columns":2,"fields":[]}', memoFirst);
      expect(twoCol.fields[1]).toMatchObject({ name: 'sprk_body', renderer: 'textarea', span: 2 });
    });

    it('honors a guard-passing config scalars while deriving its fields (title/columns/summaryField survive)', () => {
      // This describe's beforeEach already resolved once (and warned once).
      warnSpy.mockClear();
      const result = resolveHeaderConfig(
        '{"_version":"1.0","title":"Kept","columns":2,"summaryField":"sprk_aisummary","fields":[]}',
        projectMetadata
      );
      expect(warnSpy).toHaveBeenCalledTimes(1);
      expect(result.title).toBe('Kept');
      expect(result.columns).toBe(2);
      expect(result.summaryField).toBe('sprk_aisummary');
      expect(result.fields[0].name).toBe('sprk_projectnumber');
    });

    it('omits the primary name row when that attribute is not in the metadata map', () => {
      const noPrimaryName: HeaderFormMetadata = {
        ...projectMetadata,
        primaryNameAttribute: 'sprk_notpresent',
      };
      const derived = resolveHeaderConfig(undefined, noPrimaryName);
      expect(derived.fields.map(f => f.name)).not.toContain('sprk_notpresent');
      // Still capped at 5 total, still skipping the same set.
      expect(derived.fields).toHaveLength(5);
      expect(derived.fields.map(f => f.name)).toEqual([
        'sprk_projectnumber',
        'sprk_projectname',
        'sprk_projecttype',
        'sprk_startdate',
        'sprk_budget',
      ]);
    });

    it('renders usefully on a form with NO layoutJson at all (the FR-04 acceptance statement)', () => {
      expect(result.fields.length).toBeGreaterThan(0);
      expect(result.title).toBe('Project');
      expectFullyResolved(result);
    });
  });

  // ───────────────────────────────────────────────────────────────────────────
  // Renderer derivation table
  // ───────────────────────────────────────────────────────────────────────────

  describe('renderer derivation from attribute type', () => {
    it.each<[string | undefined, string | undefined, RecordHeaderFieldRenderer]>([
      ['Money', undefined, 'currency'],
      ['DateTime', 'DateOnly', 'date'],
      ['DateTime', 'DateAndTime', 'datetime'],
      ['DateTime', undefined, 'datetime'],
      ['Picklist', undefined, 'optionset'],
      ['Status', undefined, 'optionset'],
      ['State', undefined, 'optionset'],
      ['Boolean', undefined, 'boolean'],
      ['Lookup', undefined, 'lookup'],
      ['Memo', undefined, 'textarea'],
      ['Integer', undefined, 'number'],
      ['Decimal', undefined, 'number'],
      ['Double', undefined, 'number'],
      ['BigInt', undefined, 'number'],
      ['String', undefined, 'text'],
      ['String', 'Email', 'text'],
      ['Uniqueidentifier', undefined, 'text'],
      ['SomeFutureType', undefined, 'text'],
      [undefined, undefined, 'text'],
    ])('%s (format %s) ⇒ %s', (attributeType, format, expected) => {
      const result = resolveHeaderConfig(
        manifest([{ name: 'sprk_field' }]),
        singleAttributeMetadata(attributeType, format)
      );
      expect(result.fields[0].renderer).toBe(expected);
    });

    it('a config renderer override beats derivation', () => {
      const result = resolveHeaderConfig(
        manifest([{ name: 'sprk_field', renderer: 'textarea' }]),
        singleAttributeMetadata('Money')
      );
      expect(result.fields[0].renderer).toBe('textarea');
      // …and the override drives span defaulting too.
      expect(result.fields[0].span).toBe(3);
    });

    it.each<RecordHeaderFieldRenderer>([
      'text',
      'textarea',
      'lookup',
      'optionset',
      'date',
      'datetime',
      'number',
      'currency',
      'boolean',
    ])('accepts %s as a config renderer override', renderer => {
      const result = resolveHeaderConfig(
        manifest([{ name: 'sprk_field', renderer }]),
        singleAttributeMetadata('String')
      );
      expect(result.fields[0].renderer).toBe(renderer);
    });

    it.each<[string, unknown]>([
      ['an unknown renderer string', 'rainbow'],
      ['a number', 7],
      ['null', null],
      ['an empty string', ''],
    ])('ignores %s as a renderer override and falls back to derivation', (_label, renderer) => {
      const result = resolveHeaderConfig(
        manifest([{ name: 'sprk_field', renderer }]),
        singleAttributeMetadata('Money')
      );
      expect(result.fields[0].renderer).toBe('currency');
    });
  });

  // ───────────────────────────────────────────────────────────────────────────
  // columns normalization
  // ───────────────────────────────────────────────────────────────────────────

  describe('columns normalization', () => {
    it.each<[string, unknown, 2 | 3]>([
      ['2 is honored', 2, 2],
      ['3 is honored', 3, 3],
      ['absent normalizes to 3', undefined, 3],
      ['0 normalizes to 3', 0, 3],
      ['1 normalizes to 3', 1, 3],
      ['5 normalizes to 3', 5, 3],
      ['-2 normalizes to 3', -2, 3],
      ["the string '2' normalizes to 3", '2', 3],
      ['null normalizes to 3', null, 3],
      ['NaN normalizes to 3', NaN, 3],
      ['2.0 (=== 2) is honored', 2.0, 2],
    ])('%s', (_label, columns, expected) => {
      const top = columns === undefined ? {} : { columns };
      const result = resolveHeaderConfig(manifest([{ name: 'sprk_matternumber' }], top), matterMetadata);
      expect(result.columns).toBe(expected);
    });

    it('still applies the rest of the config when columns is invalid', () => {
      const result = resolveHeaderConfig(
        manifest([{ name: 'sprk_matternumber', label: 'Kept', span: 2 }], {
          columns: 5,
          title: 'Kept Title',
          summaryField: 'sprk_kept',
        }),
        matterMetadata
      );
      expect(result.columns).toBe(3);
      expect(result.title).toBe('Kept Title');
      expect(result.summaryField).toBe('sprk_kept');
      expect(result.fields[0]).toMatchObject({ name: 'sprk_matternumber', label: 'Kept', span: 2 });
    });

    it('does not warn merely because columns needed normalizing', () => {
      resolveHeaderConfig(manifest([{ name: 'sprk_matternumber' }], { columns: 5 }), matterMetadata);
      expect(warnSpy).not.toHaveBeenCalled();
    });
  });

  // ───────────────────────────────────────────────────────────────────────────
  // title resolution
  // ───────────────────────────────────────────────────────────────────────────

  describe('title resolution — config ?? entity display name ?? humanized logical name', () => {
    it('uses the config title when present', () => {
      const result = resolveHeaderConfig(
        manifest([{ name: 'sprk_projectname' }], { title: 'Custom' }),
        projectMetadata
      );
      expect(result.title).toBe('Custom');
    });

    it('falls back to the entity display name when the config omits a title', () => {
      const result = resolveHeaderConfig(manifest([{ name: 'sprk_projectname' }]), projectMetadata);
      expect(result.title).toBe('Project');
    });

    it('falls back to the humanized logical name when there is no display name either', () => {
      const noDisplayName: HeaderFormMetadata = { ...projectMetadata, entityDisplayName: undefined };
      const result = resolveHeaderConfig(manifest([{ name: 'sprk_projectname' }]), noDisplayName);
      expect(result.title).toBe('Project');
    });

    it('humanizes a multi-word logical name as the last resort', () => {
      const metadata: HeaderFormMetadata = {
        entityLogicalName: 'sprk_work_assignment',
        primaryIdAttribute: 'sprk_work_assignmentid',
        primaryNameAttribute: 'sprk_name',
        attributes: { sprk_name: { attributeType: 'String' } },
      };
      const result = resolveHeaderConfig(undefined, metadata);
      expect(result.title).toBe('Work Assignment');
    });

    it.each<[string, unknown]>([
      ['an empty-string title', ''],
      ['a whitespace title', '   '],
      ['a null title', null],
      ['a numeric title', 42],
    ])('ignores %s and falls back to the entity display name', (_label, title) => {
      const result = resolveHeaderConfig(manifest([{ name: 'sprk_projectname' }], { title }), projectMetadata);
      expect(result.title).toBe('Project');
    });
  });

  // ───────────────────────────────────────────────────────────────────────────
  // summaryField pass-through
  // ───────────────────────────────────────────────────────────────────────────

  describe('summaryField — pass-through only (task 034 owns the default + existence gate)', () => {
    it('passes a configured summaryField through verbatim', () => {
      const result = resolveHeaderConfig(
        manifest([{ name: 'sprk_projectname' }], { summaryField: 'sprk_aisummary' }),
        projectMetadata
      );
      expect(result.summaryField).toBe('sprk_aisummary');
    });

    it('passes it through even when the named attribute is absent from metadata', () => {
      const result = resolveHeaderConfig(
        manifest([{ name: 'sprk_projectname' }], { summaryField: 'sprk_nosuchcolumn' }),
        projectMetadata
      );
      expect(result.summaryField).toBe('sprk_nosuchcolumn');
    });

    it('resolves to undefined when the config omits it', () => {
      const result = resolveHeaderConfig(manifest([{ name: 'sprk_projectname' }]), projectMetadata);
      expect(result.summaryField).toBeUndefined();
    });

    it('resolves to undefined in the tier-2 path', () => {
      expect(resolveHeaderConfig(undefined, projectMetadata).summaryField).toBeUndefined();
    });

    it('resolves to undefined for a non-string config value', () => {
      const result = resolveHeaderConfig(
        manifest([{ name: 'sprk_projectname' }], { summaryField: 99 }),
        projectMetadata
      );
      expect(result.summaryField).toBeUndefined();
    });

    it('applies no RECORDSUMMARY_FIELD default — the key is present but undefined', () => {
      const result = resolveHeaderConfig(undefined, projectMetadata);
      expect(Object.prototype.hasOwnProperty.call(result, 'summaryField')).toBe(true);
      expect(result.summaryField).toBeUndefined();
    });
  });

  // ───────────────────────────────────────────────────────────────────────────
  // readOnly / required / maxLines merge details
  // ───────────────────────────────────────────────────────────────────────────

  describe('readOnly, required and maxLines merge details', () => {
    it.each<[string, unknown, boolean]>([
      ['config readOnly true ⇒ true', true, true],
      ['config readOnly false ⇒ false', false, false],
      ['config readOnly absent ⇒ false', undefined, false],
      ["config readOnly as the string 'true' ⇒ false (not a boolean)", 'true', false],
      ['config readOnly as null ⇒ false', null, false],
    ])('%s', (_label, readOnly, expected) => {
      const field = readOnly === undefined ? { name: 'sprk_field' } : { name: 'sprk_field', readOnly };
      const result = resolveHeaderConfig(manifest([field]), singleAttributeMetadata('String'));
      expect(result.fields[0].readOnly).toBe(expected);
    });

    it.each<[string, string | undefined, boolean]>([
      ["form-control level 'required' ⇒ true", 'required', true],
      ["metadata level 'SystemRequired' ⇒ true", 'SystemRequired', true],
      ["metadata level 'ApplicationRequired' ⇒ true", 'ApplicationRequired', true],
      ["form-control level 'recommended' ⇒ false (Dataverse renders + not *)", 'recommended', false],
      ["metadata level 'Recommended' ⇒ false", 'Recommended', false],
      ["form-control level 'none' ⇒ false", 'none', false],
      ["metadata level 'None' ⇒ false", 'None', false],
      ['no level at all ⇒ false', undefined, false],
      ['an unrecognised future level ⇒ false', 'SomethingNew', false],
    ])('derives required from %s', (_label, requiredLevel, expected) => {
      const result = resolveHeaderConfig(
        manifest([{ name: 'sprk_field' }]),
        singleAttributeMetadata('String', undefined, { requiredLevel })
      );
      expect(result.fields[0].required).toBe(expected);
    });

    it('a config required value overrides the derived requirement level, in both directions', () => {
      const forcedOff = resolveHeaderConfig(
        manifest([{ name: 'sprk_field', required: false }]),
        singleAttributeMetadata('String', undefined, { requiredLevel: 'SystemRequired' })
      );
      expect(forcedOff.fields[0].required).toBe(false);

      const forcedOn = resolveHeaderConfig(
        manifest([{ name: 'sprk_field', required: true }]),
        singleAttributeMetadata('String', undefined, { requiredLevel: 'None' })
      );
      expect(forcedOn.fields[0].required).toBe(true);
    });

    it.each<[string, unknown, number | undefined]>([
      ['config maxLines 10 ⇒ 10', 10, 10],
      ['config maxLines 4.9 ⇒ 4 (floors)', 4.9, 4],
      ['config maxLines 0 ⇒ omitted', 0, undefined],
      ['config maxLines -1 ⇒ omitted', -1, undefined],
      ["config maxLines '10' as a string ⇒ omitted", '10', undefined],
      ['config maxLines absent ⇒ omitted', undefined, undefined],
    ])('%s', (_label, maxLines, expected) => {
      const field = maxLines === undefined ? { name: 'sprk_field' } : { name: 'sprk_field', maxLines };
      const result = resolveHeaderConfig(manifest([field]), singleAttributeMetadata('Memo'));
      expect(result.fields[0].maxLines).toBe(expected);
    });
  });

  // ───────────────────────────────────────────────────────────────────────────
  // FR-02 — purity + fully-resolved output
  // ───────────────────────────────────────────────────────────────────────────

  describe('FR-02 — purity and fully-resolved output', () => {
    it('is deterministic — the same inputs produce a deep-equal result', () => {
      const a = resolveHeaderConfig(MATTER_MANIFEST_JSON, matterMetadata);
      const b = resolveHeaderConfig(MATTER_MANIFEST_JSON, matterMetadata);
      expect(a).toEqual(b);
    });

    it('does not mutate the metadata object it is given', () => {
      const snapshot = JSON.parse(JSON.stringify(projectMetadata));
      resolveHeaderConfig(undefined, projectMetadata);
      resolveHeaderConfig(MATTER_MANIFEST_JSON, projectMetadata);
      expect(JSON.parse(JSON.stringify(projectMetadata))).toEqual(snapshot);
    });

    it('leaves no undefined-valued key on any resolved field across the whole matrix', () => {
      const inputs: ReadonlyArray<string | undefined> = [
        MATTER_MANIFEST_JSON,
        undefined,
        '{{{',
        '{"_version":"1.0","fields":[{}]}',
        manifest([{ name: 'sprk_field' }]),
      ];
      for (const input of inputs) {
        for (const metadata of [matterMetadata, projectMetadata, singleAttributeMetadata('Memo')]) {
          const result = resolveHeaderConfig(input, metadata);
          for (const field of result.fields) {
            for (const [key, value] of Object.entries(field)) {
              // `maxLines` is the ONE sanctioned optional — it is omitted, never undefined-valued.
              expect([key, value]).not.toEqual([key, undefined]);
            }
          }
          expectFullyResolved(result);
        }
      }
    });

    it('never emits more than one console.warn for a single call', () => {
      const inputs: ReadonlyArray<string | undefined> = [
        undefined,
        '{{{',
        '{"_version":"2.0","fields":[]}',
        '{"_version":"1.0","fields":[]}',
        '{"_version":"1.0","fields":[{},{"name":"sprk_projectname"}]}',
      ];
      for (const input of inputs) {
        warnSpy.mockClear();
        resolveHeaderConfig(input, projectMetadata);
        expect(warnSpy.mock.calls.length).toBeLessThanOrEqual(1);
      }
    });
  });

  // ───────────────────────────────────────────────────────────────────────────
  // Module purity (static source scan) — mirrors task 030's approach
  // ───────────────────────────────────────────────────────────────────────────

  describe('module purity — static source scan', () => {
    const sourcePath = path.join(__dirname, '..', 'configResolution.ts');
    const source = fs.readFileSync(sourcePath, 'utf8');

    /**
     * Source with all comments removed. The scans below MUST run against this,
     * not the raw text: this module's JSDoc legitimately mentions `Xrm`,
     * `console.warn`, `React` and `toolbarLaunchDefaults` while *documenting
     * that it does not use them*. Task 030's guard suite hit the same
     * false-positive and solved it by scanning import lines only.
     */
    const codeOnly = source
      .replace(/\/\*[\s\S]*?\*\//g, '')
      .split('\n')
      .map(line => line.replace(/\/\/.*$/, ''))
      .join('\n');

    /** Every module specifier the file actually imports from. */
    const importSpecifiers = (codeOnly.match(/from\s+['"]([^'"]+)['"]/g) ?? []).map(m =>
      m.replace(/^from\s+['"]/, '').replace(/['"]$/, '')
    );

    it('imports ONLY the RecordHeaderConfiguration schema module', () => {
      expect(importSpecifiers.length).toBeGreaterThan(0);
      expect(Array.from(new Set(importSpecifiers))).toEqual(['../../types/RecordHeaderConfiguration']);
    });

    it.each([
      ['react', 'react'],
      ['@fluentui', '@fluentui'],
      ['DataGrid internals', 'DataGrid'],
      ['services', '/services'],
      ['hooks', '/hooks'],
      ['toolbarLaunchDefaults', 'toolbarLaunchDefaults'],
      ['@spaarke/auth', '@spaarke/auth'],
      ['IDataverseClient', 'IDataverseClient'],
    ])('does not import %s', (_label, fragment) => {
      for (const specifier of importSpecifiers) {
        expect(specifier).not.toContain(fragment);
      }
    });

    it('references no Xrm, fetch, React or hook surface in executable code', () => {
      expect(codeOnly).not.toMatch(/\bXrm\b/);
      expect(codeOnly).not.toMatch(/\bfetch\s*\(/);
      expect(codeOnly).not.toMatch(/\bReact\b/);
      expect(codeOnly).not.toMatch(/\buse[A-Z]\w*\s*\(/);
      expect(codeOnly).not.toMatch(/\btoolbarLaunchDefaults\b/);
      expect(codeOnly).not.toMatch(/\bRECORDSUMMARY_FIELD\b/);
    });

    it('uses console.warn as its only console surface (FR-03)', () => {
      const consoleCalls = codeOnly.match(/console\.\w+/g) ?? [];
      expect(consoleCalls).toEqual(['console.warn']);
    });
  });

  // ───────────────────────────────────────────────────────────────────────────
  // extractConfiguredAttributeNames — the pre-metadata name hint
  //
  // Exists so the metadata fetch can NAME the attributes it needs before the
  // layout is resolved. Getting this wrong reintroduces the v1.1.0 UAT defect:
  // an attribute the fetch never asked for has no type, and an untyped lookup
  // is `$select`ed by its bare name, which 400s the whole read.
  // ───────────────────────────────────────────────────────────────────────────
  describe('extractConfiguredAttributeNames', () => {
    it('returns every field name plus summaryField, de-duplicated, in order', () => {
      expect(
        extractConfiguredAttributeNames(
          '{"_version":"1.0","title":"Project","columns":3,"summaryField":"sprk_recordsummary","fields":[' +
            '{"name":"sprk_projectname","span":2},' +
            '{"name":"sprk_projecttype_ref"},' +
            '{"name":"sprk_openeddate"},' +
            '{"name":"sprk_projectname"}]}'
        )
      ).toEqual(['sprk_projectname', 'sprk_projecttype_ref', 'sprk_openeddate', 'sprk_recordsummary']);
    });

    it('gathers names from a config the resolver would REJECT (hint, not validator)', () => {
      // Wrong _version -> resolveHeaderConfig falls through to tier 2, but the
      // names are still worth requesting.
      expect(extractConfiguredAttributeNames('{"_version":"9.9","fields":[{"name":"sprk_openeddate"}]}')).toEqual([
        'sprk_openeddate',
      ]);
    });

    it('never throws and returns [] for unusable input', () => {
      for (const input of [
        null,
        undefined,
        '',
        '   ',
        'not json',
        '[]',
        '"a string"',
        '42',
        '{}',
        '{"fields":"nope"}',
        '{"fields":[null,42,"x",{},{"name":""},{"name":"  "},{"name":123}]}',
      ] as Array<string | null | undefined>) {
        expect(extractConfiguredAttributeNames(input)).toEqual([]);
      }
    });

    it('trims names and skips a non-string summaryField', () => {
      expect(extractConfiguredAttributeNames('{"summaryField":99,"fields":[{"name":"  sprk_openeddate  "}]}')).toEqual([
        'sprk_openeddate',
      ]);
    });
  });
});
