/**
 * RecordHeaderConfiguration.test — unit tests for the v1.0 schema's shallow,
 * non-throwing runtime guard `isValidRecordHeaderConfiguration`.
 *
 * MAINTAIN-class per ADR-038 §7 — a pure-function KEEP-category test for a
 * TypeScript shared-library type guard (no React, no I/O, no mocks needed).
 *
 * Covers record-header-and-notepad-r2 task 030 acceptance criteria (spec FR-01, FR-03):
 *   - minimal + full valid configurations return true (design.md §5.2 Matter example)
 *   - the full battery of negative discriminator failures returns false
 *   - the guard is intentionally SHALLOW — field-entry shape errors are NOT rejected
 *   - the guard never throws, and never calls console (callers own the console.warn)
 *   - the module imports nothing from react, @fluentui, or any Xrm/service surface
 *
 * @see ../RecordHeaderConfiguration.ts — the schema + guard under test
 * @see ../DataGridConfiguration.ts `isValidDataGridConfiguration` — the guard this mirrors
 */

import * as fs from 'fs';
import * as path from 'path';
import { isValidRecordHeaderConfiguration, RecordHeaderConfiguration } from '../RecordHeaderConfiguration';

describe('isValidRecordHeaderConfiguration', () => {
  describe('valid inputs', () => {
    it('returns true for the minimal valid configuration ({"_version":"1.0","fields":[]})', () => {
      const parsed = JSON.parse('{"_version":"1.0","fields":[]}');
      expect(isValidRecordHeaderConfiguration(parsed)).toBe(true);
    });

    it('returns true for the design.md §5.2 Matter example, which also type-checks as RecordHeaderConfiguration', () => {
      const matterExample: RecordHeaderConfiguration = {
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
      };
      // Round-trip through JSON to exercise the same path a maker's manifest paste takes.
      const parsed = JSON.parse(JSON.stringify(matterExample));
      expect(isValidRecordHeaderConfiguration(parsed)).toBe(true);
    });

    it('returns true for a realistic ~600B Work Assignment layout (7 fields, no renderer overrides)', () => {
      const workAssignmentLayout =
        '{"_version":"1.0","title":"Work Assignment","columns":3,"summaryField":"sprk_recordsummary",' +
        '"fields":[{"name":"sprk_workassignmentnumber","span":1,"required":true,"label":"Assignment Number"},' +
        '{"name":"sprk_workassignmentname","span":2,"label":"Assignment Name"},' +
        '{"name":"sprk_assignmenttype","span":1,"label":"Assignment Type"},' +
        '{"name":"sprk_responseduedate","span":1,"label":"Response Due Date"},' +
        '{"name":"sprk_highpriority","span":1,"label":"High Priority"},' +
        '{"name":"sprk_assignmentstatus","span":1,"label":"Status"},' +
        '{"name":"sprk_assigneddescription","span":3,"maxLines":10,"label":"Assignment Description"}]}';
      expect(isValidRecordHeaderConfiguration(JSON.parse(workAssignmentLayout))).toBe(true);
    });

    it('returns true for a ~900B stress layout (11 fields)', () => {
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
      expect(isValidRecordHeaderConfiguration(JSON.parse(stressLayout))).toBe(true);
    });
  });

  describe('negative — top-level shape / type failures', () => {
    it.each<[string, unknown]>([
      ['null', null],
      ['undefined', undefined],
      ['a number', 42],
      ['a string', 'a string'],
      ['a top-level array', ['_version', '1.0']],
      ['an object with no _version', { fields: [] }],
    ])('returns false for %s', (_label, value) => {
      expect(isValidRecordHeaderConfiguration(value)).toBe(false);
    });
  });

  describe('negative — _version discriminator failures', () => {
    it.each<[string, unknown]>([
      ["_version '2.0'", { _version: '2.0', fields: [] }],
      ['_version as the number 1.0', { _version: 1.0, fields: [] }],
      ["_version as an empty string ''", { _version: '', fields: [] }],
    ])('returns false for %s', (_label, value) => {
      expect(isValidRecordHeaderConfiguration(value)).toBe(false);
    });
  });

  describe('negative — fields discriminator failures', () => {
    it.each<[string, unknown]>([
      ['fields missing entirely', { _version: '1.0' }],
      ['fields as an object', { _version: '1.0', fields: {} }],
      ['fields as a string', { _version: '1.0', fields: 'not-an-array' }],
    ])('returns false for %s', (_label, value) => {
      expect(isValidRecordHeaderConfiguration(value)).toBe(false);
    });
  });

  describe('shallow by design — deep field-entry errors are NOT rejected', () => {
    it('returns TRUE for _version "1.0" + fields [{}] (a field entry missing name) — deep errors degrade downstream', () => {
      const value = { _version: '1.0', fields: [{}] };
      expect(isValidRecordHeaderConfiguration(value)).toBe(true);
    });
  });

  describe('never throws, never logs', () => {
    // The full test-matrix — every case above plus a few extra exotic inputs — run
    // through the guard once more here to assert the no-throw / no-console contract
    // holds across the board, not just for the specific cases checked for a return value.
    const testMatrix: unknown[] = [
      null,
      undefined,
      42,
      NaN,
      'a string',
      '',
      ['_version', '1.0'],
      {},
      { fields: [] },
      { _version: '1.0' },
      { _version: '1.0', fields: null },
      { _version: '1.0', fields: [{}] },
      { _version: '2.0', fields: [] },
      Symbol('x'),
      () => undefined,
      new Date(),
      new Map(),
    ];

    it.each(testMatrix.map((value, index) => [index, value] as const))(
      'does not throw for test-matrix input #%i',
      (_index, value) => {
        expect(() => isValidRecordHeaderConfiguration(value)).not.toThrow();
      }
    );

    it('never calls console.warn / console.error / console.log for any input in the matrix', () => {
      const warnSpy = jest.spyOn(console, 'warn').mockImplementation(() => undefined);
      const errorSpy = jest.spyOn(console, 'error').mockImplementation(() => undefined);
      const logSpy = jest.spyOn(console, 'log').mockImplementation(() => undefined);

      for (const value of testMatrix) {
        isValidRecordHeaderConfiguration(value);
      }

      expect(warnSpy).not.toHaveBeenCalled();
      expect(errorSpy).not.toHaveBeenCalled();
      expect(logSpy).not.toHaveBeenCalled();

      warnSpy.mockRestore();
      errorSpy.mockRestore();
      logSpy.mockRestore();
    });
  });

  describe('module purity', () => {
    it('imports nothing from react, @fluentui, or any Xrm/service module', () => {
      const sourcePath = path.join(__dirname, '..', 'RecordHeaderConfiguration.ts');
      const source = fs.readFileSync(sourcePath, 'utf8');
      const importOrReexportLines = source
        .split('\n')
        .filter(line => /^\s*import\b/.test(line) || /^\s*export\s+\*?\s*\{?[^}]*\}?\s*from\b/.test(line));

      // No import/re-export statements at all — the file is types + one pure guard.
      // (The guard's own no-console contract is verified behaviorally above via
      // console spies — a static text scan would false-positive on this file's
      // JSDoc prose, which references `console.warn` when documenting caller behavior.)
      expect(importOrReexportLines).toHaveLength(0);
      expect(source).not.toMatch(/from\s+['"]react/);
      expect(source).not.toMatch(/from\s+['"]@fluentui/);
      expect(source).not.toMatch(/\bXrm\./);
      expect(source).not.toMatch(/from\s+['"]\.\.\/services/);
    });
  });
});
