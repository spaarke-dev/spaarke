/**
 * FR-11 (spaarkeai-compose-r4 task 003) — round-trip + validation tests for the CLIENT half of the
 * shared operation schema (compose-operations.ts). The acceptance criterion is that a serialized op
 * log round-trips client → server → client WITHOUT LOSS and that both ends validate against the SAME
 * schema. The SERVER half is covered by
 *   tests/unit/Sprk.Bff.Api.Tests/Services/Compose/ComposeOperationSchemaTests.cs
 * The two together assert the cross-language contract: the wire strings this test emits are the exact
 * discriminators the server keys on (`ComposeOperation` [JsonDerivedType] tags), and vice-versa.
 *
 * Each test names a concrete behavior that breaks if deleted:
 *   - a full ten-op log survives JSON.stringify → JSON.parse byte-identical (lossless round-trip)
 *   - the emitted JSON carries the exact FR-11 `type` discriminators + `schemaVersion` the server reads
 *   - `isComposeOperationLog` accepts a well-formed log and rejects a closed-set violation / bad anchor
 *     (the client half of "both ends validate against the same schema", esp. for AI-returned op JSON)
 */

import {
  COMPOSE_OPERATION_SCHEMA_VERSION,
  COMPOSE_OPERATION_TYPES,
  isComposeOperation,
  isComposeOperationLog,
} from './compose-operations';
import type { ComposeOperation, ComposeOperationLog } from './compose-operations';
// Re-export surface check (project §11: the operation types are ALSO re-exported from the
// contracts module — importing from there must resolve the same symbols, not a fork).
import { COMPOSE_OPERATION_SCHEMA_VERSION as VERSION_VIA_CONTRACTS } from './compose-contracts';

/** A canonical op log exercising ALL TEN op types + the structural-op second-paragraph references. */
function buildCanonicalLog(): ComposeOperationLog {
  const operations: ComposeOperation[] = [
    {
      type: 'insertText',
      paraId: '0A1B2C3D',
      at: { runIndex: 0, offset: 5 },
      text: 'hello',
      marks: ['Bold', 'Italic'],
    },
    {
      type: 'deleteRange',
      paraId: '0A1B2C3D',
      range: { start: { runIndex: 0, offset: 2 }, end: { runIndex: 1, offset: 4 } },
    },
    {
      type: 'replaceRange',
      paraId: '1122AABB',
      range: { start: { runIndex: 0, offset: 0 }, end: { runIndex: 0, offset: 3 } },
      text: 'world',
      marks: ['Underline'],
    },
    {
      type: 'setMark',
      paraId: '1122AABB',
      range: { start: { runIndex: 0, offset: 1 }, end: { runIndex: 0, offset: 6 } },
      mark: 'Bold',
    },
    {
      type: 'clearMark',
      paraId: '1122AABB',
      range: { start: { runIndex: 0, offset: 1 }, end: { runIndex: 0, offset: 6 } },
      mark: 'Italic',
    },
    { type: 'splitParagraph', paraId: 'DEADBEEF', at: { runIndex: 1, offset: 2 }, newParaId: 'CAFEF00D' },
    { type: 'mergeParagraph', paraId: 'CAFEF00D', targetParaId: 'DEADBEEF' },
    { type: 'insertParagraph', paraId: 'DEADBEEF', newParaId: '0BADF00D', position: 'After' },
    { type: 'deleteParagraph', paraId: '0BADF00D' },
    { type: 'setBlockAttr', paraId: 'DEADBEEF', attr: 'Alignment', value: 'Center' },
  ];
  return { schemaVersion: COMPOSE_OPERATION_SCHEMA_VERSION, operations };
}

describe('compose-operations schema (FR-11)', () => {
  it('COMPOSE_OPERATION_TYPES is the exact closed set of ten op discriminators', () => {
    expect([...COMPOSE_OPERATION_TYPES]).toEqual([
      'insertText',
      'deleteRange',
      'replaceRange',
      'setMark',
      'clearMark',
      'splitParagraph',
      'mergeParagraph',
      'insertParagraph',
      'deleteParagraph',
      'setBlockAttr',
    ]);
    expect(COMPOSE_OPERATION_TYPES).toHaveLength(10);
  });

  it('a full ten-op log round-trips JSON.stringify → JSON.parse without loss', () => {
    const log = buildCanonicalLog();

    const json1 = JSON.stringify(log);
    const roundTripped = JSON.parse(json1) as ComposeOperationLog;
    const json2 = JSON.stringify(roundTripped);

    expect(json2).toBe(json1);
    expect(roundTripped).toEqual(log);
    expect(roundTripped.operations).toHaveLength(10);
    expect(roundTripped.schemaVersion).toBe('compose-ops-v1');
  });

  it('emits the exact FR-11 `type` discriminators + schemaVersion the server keys on', () => {
    const json = JSON.stringify(buildCanonicalLog());

    for (const t of COMPOSE_OPERATION_TYPES) {
      expect(json).toContain(`"type":"${t}"`);
    }
    expect(json).toContain('"schemaVersion":"compose-ops-v1"');
  });

  it('the operation schema version is re-exported unchanged via the contracts module (no fork)', () => {
    expect(VERSION_VIA_CONTRACTS).toBe(COMPOSE_OPERATION_SCHEMA_VERSION);
    expect(VERSION_VIA_CONTRACTS).toBe('compose-ops-v1');
  });

  describe('isComposeOperation / isComposeOperationLog validation', () => {
    it('accepts every op in a well-formed canonical log', () => {
      const log = buildCanonicalLog();
      expect(isComposeOperationLog(log)).toBe(true);
      for (const op of log.operations) {
        expect(isComposeOperation(op)).toBe(true);
      }
    });

    it('rejects an op whose `type` is outside the closed set (e.g. AI-returned drift)', () => {
      const rogue = { type: 'insertTable', paraId: '0A1B2C3D' };
      expect(isComposeOperation(rogue)).toBe(false);
    });

    it('rejects an op missing its paraId anchor', () => {
      const noAnchor = { type: 'insertText', at: { runIndex: 0, offset: 0 }, text: 'x' };
      expect(isComposeOperation(noAnchor)).toBe(false);
    });

    it('rejects a log whose operations array contains an invalid op', () => {
      const badLog = {
        schemaVersion: 'compose-ops-v1',
        operations: [
          { type: 'insertText', paraId: 'ABC' },
          { type: 'nope', paraId: 'ABC' },
        ],
      };
      expect(isComposeOperationLog(badLog)).toBe(false);
    });

    it('rejects a non-object / null', () => {
      expect(isComposeOperation(null)).toBe(false);
      expect(isComposeOperation(42)).toBe(false);
      expect(isComposeOperationLog(undefined)).toBe(false);
    });
  });
});
