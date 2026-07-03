/**
 * configResolution.pageSize.test — unit tests for the framework `pageSize` default.
 *
 * Covers spaarke-dataset-grid-framework-r2 task 003 (FR-07) acceptance criteria:
 *   - When `config.behavior.pageSize` is undefined, the resolved effective
 *     pageSize is `25` (not the pre-FR-07 default of `100`).
 *   - When `config.behavior.pageSize` is set explicitly, the explicit value
 *     is preserved (additive-safe: existing records with explicit pageSize
 *     are unaffected).
 *   - `configRecord === null` (missing/unparseable record) still resolves to
 *     the new framework default of `25`.
 *
 * MAINTAIN-class per ADR-038 § 7 (framework-contract test on a pure function).
 * Does NOT mock HTTP, DI, or any React surface — the pure resolver is exercised
 * directly.
 *
 * @see configResolution.ts `resolveConfig`, `FRAMEWORK_DEFAULT_BEHAVIOR`
 * @see DataGridConfiguration.ts `BehaviorConfig.pageSize`
 * @see spec.md § FR-07
 */

import { resolveConfig } from '../configResolution';
import type { DataGridConfiguration } from '../../../types/DataGridConfiguration';
import type { EntityMetadata } from '../../../services/IDataverseClient';

/** Minimal entity metadata fixture — resolver requires this argument non-null. */
const minimalMetadata: EntityMetadata = {
  primaryIdAttribute: 'sprk_documentid',
  primaryNameAttribute: 'sprk_name',
  attributes: {
    sprk_name: {
      attributeType: 'String',
      displayName: 'Name',
      isPrimaryName: true,
    },
  },
};

/** Config record with `behavior` omitted entirely — the "record with omitted pageSize" case. */
const configWithoutBehavior: DataGridConfiguration = {
  _version: '1.0',
  source: { type: 'savedquery-set', entityLogicalName: 'sprk_document' },
};

/** Config record with `behavior` present but `pageSize` undefined. */
const configWithBehaviorNoPageSize: DataGridConfiguration = {
  _version: '1.0',
  source: { type: 'savedquery-set', entityLogicalName: 'sprk_document' },
  behavior: {
    selectionMode: 'single',
    enableSorting: false,
  },
};

describe('resolveConfig pageSize default — FR-07 (framework default 25)', () => {
  describe('behavior.pageSize omitted (framework default applied)', () => {
    it('resolves to 25 when the config record omits `behavior` entirely', () => {
      const resolved = resolveConfig(undefined, configWithoutBehavior, minimalMetadata, undefined);
      expect(resolved.behavior.pageSize).toBe(25);
    });

    it('resolves to 25 when `behavior` is present but `pageSize` is undefined', () => {
      const resolved = resolveConfig(undefined, configWithBehaviorNoPageSize, minimalMetadata, undefined);
      expect(resolved.behavior.pageSize).toBe(25);
    });

    it('resolves to 25 when `configRecord === null` (missing/unparseable record)', () => {
      // FR-DG-04: a configId pointing to a non-existent record MUST still
      // render; resolver falls through to framework defaults gracefully.
      const resolved = resolveConfig(undefined, null, minimalMetadata, undefined);
      expect(resolved.behavior.pageSize).toBe(25);
    });
  });

  describe('behavior.pageSize set explicitly (additive-safe)', () => {
    it('preserves an explicit `pageSize: 100` (drill-through / full-page consumers)', () => {
      const config: DataGridConfiguration = {
        _version: '1.0',
        source: { type: 'savedquery-set', entityLogicalName: 'sprk_document' },
        behavior: { pageSize: 100 },
      };
      const resolved = resolveConfig(undefined, config, minimalMetadata, undefined);
      expect(resolved.behavior.pageSize).toBe(100);
    });

    it('preserves an explicit `pageSize: 50` (mid-range override)', () => {
      const config: DataGridConfiguration = {
        _version: '1.0',
        source: { type: 'savedquery-set', entityLogicalName: 'sprk_document' },
        behavior: { pageSize: 50 },
      };
      const resolved = resolveConfig(undefined, config, minimalMetadata, undefined);
      expect(resolved.behavior.pageSize).toBe(50);
    });

    it('preserves an explicit `pageSize: 25` (workspace-widget majority — no-op after FR-07)', () => {
      // Owner note: the 6 workspace-widget config records currently set
      // `pageSize: 25` explicitly. This test confirms FR-07 is invisible
      // to them (their explicit value is honored, not overridden).
      const config: DataGridConfiguration = {
        _version: '1.0',
        source: { type: 'savedquery-set', entityLogicalName: 'sprk_document' },
        behavior: { pageSize: 25 },
      };
      const resolved = resolveConfig(undefined, config, minimalMetadata, undefined);
      expect(resolved.behavior.pageSize).toBe(25);
    });
  });

  describe('coexistence with other behavior fields', () => {
    it('applies framework `pageSize: 25` even when the record sets other behavior fields', () => {
      // Config sets `selectionMode` + `enableSorting` but omits `pageSize`.
      // The `...FRAMEWORK_DEFAULT_BEHAVIOR` spread + `...configRecord.behavior`
      // spread MUST preserve the new default for `pageSize` (not undefined).
      const config: DataGridConfiguration = {
        _version: '1.0',
        source: { type: 'savedquery-set', entityLogicalName: 'sprk_document' },
        behavior: {
          selectionMode: 'none',
          enableSorting: false,
        },
      };
      const resolved = resolveConfig(undefined, config, minimalMetadata, undefined);
      expect(resolved.behavior.pageSize).toBe(25);
      expect(resolved.behavior.selectionMode).toBe('none');
      expect(resolved.behavior.enableSorting).toBe(false);
    });
  });
});
