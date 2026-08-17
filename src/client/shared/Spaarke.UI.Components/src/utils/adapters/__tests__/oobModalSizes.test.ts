/**
 * oobModalSizes — the OOB size-scale constants module (spec FR-11 / FR-18,
 * spaarke-modal-system P7 task 090).
 *
 * Locks the three named sizes' exact values so no future edit can silently
 * drift the scale, and asserts the module stays React-free (NFR-04) so PCF
 * (`@types/react` 18) and Code Page (React 19) consumers can both import it
 * safely.
 *
 * @see utils/adapters/oobModalSizes.ts
 */

import * as fs from 'fs';
import * as path from 'path';
import { OOB_MODAL_SIZES, getOobModalSize, type OobModalSizeName } from '../oobModalSizes';

describe('oobModalSizes', () => {
  it('exports the record size at exactly 85% x 85%', () => {
    expect(OOB_MODAL_SIZES.record).toEqual({
      width: { value: 85, unit: '%' },
      height: { value: 85, unit: '%' },
    });
  });

  it('exports the createForm size at exactly 70% x 80%', () => {
    expect(OOB_MODAL_SIZES.createForm).toEqual({
      width: { value: 70, unit: '%' },
      height: { value: 80, unit: '%' },
    });
  });

  it('exports the wizard size at exactly 60% x 70%', () => {
    expect(OOB_MODAL_SIZES.wizard).toEqual({
      width: { value: 60, unit: '%' },
      height: { value: 70, unit: '%' },
    });
  });

  it('exports the fullCover size at exactly 100% x 100% (task 032 / FR-13 escalation)', () => {
    expect(OOB_MODAL_SIZES.fullCover).toEqual({
      width: { value: 100, unit: '%' },
      height: { value: 100, unit: '%' },
    });
  });

  it('exposes exactly the four sanctioned size names (record/createForm/wizard/fullCover)', () => {
    expect(Object.keys(OOB_MODAL_SIZES).sort()).toEqual(['createForm', 'fullCover', 'record', 'wizard']);
  });

  it('getOobModalSize(name) returns the same entry as the OOB_MODAL_SIZES map', () => {
    (Object.keys(OOB_MODAL_SIZES) as OobModalSizeName[]).forEach(name => {
      expect(getOobModalSize(name)).toBe(OOB_MODAL_SIZES[name]);
    });
  });

  it('freezes the top-level map and every named size (no accidental mutation)', () => {
    expect(Object.isFrozen(OOB_MODAL_SIZES)).toBe(true);
    expect(Object.isFrozen(OOB_MODAL_SIZES.record)).toBe(true);
    expect(Object.isFrozen(OOB_MODAL_SIZES.createForm)).toBe(true);
    expect(Object.isFrozen(OOB_MODAL_SIZES.wizard)).toBe(true);
    expect(Object.isFrozen(OOB_MODAL_SIZES.fullCover)).toBe(true);
  });

  it('imports no React module — the module stays React-free per NFR-04', () => {
    // Static source check (not just a runtime behavior check): confirms the
    // author never adds a `react` import (even a type-only one), which is the
    // actual NFR-04 invariant — PCF controls run React 16/17 and Code Pages
    // run React 19, so this shared constants module must depend on neither.
    const source = fs.readFileSync(path.join(__dirname, '../oobModalSizes.ts'), 'utf-8');
    expect(source).not.toMatch(/from\s+['"]react(-dom)?['"]/);
    expect(source).not.toMatch(/require\(\s*['"]react(-dom)?['"]\s*\)/);
    expect(source).not.toMatch(/import\s+React/);
  });
});
