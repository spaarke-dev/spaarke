/**
 * analysisRegardingResolver.test.ts — unit tests for task 012 (consume `sprk_worktype` + the
 * regarding field-set via the RegardingResolver / ADR-024 dual-field pattern).
 *
 * Covers the three acceptance-criteria cases (one/zero/multiple populated regarding fields) plus
 * the `sprk_documentid` exclusion and `sprk_worktype` pass-through.
 */

import { resolveAnalysisRegarding } from '../analysisRegardingResolver';
import { SprkAnalysisWorkType, type ISprkAnalysisRecord } from '../../types/sprkAnalysis';

function baseRecord(overrides: Partial<ISprkAnalysisRecord> = {}): ISprkAnalysisRecord {
  return {
    sprk_analysisid: '11111111-1111-1111-1111-111111111111',
    sprk_name: 'Test Analysis',
    ...overrides,
  };
}

describe('resolveAnalysisRegarding', () => {
  it('resolves the single populated regarding lookup (Matter) and reads sprk_worktype', () => {
    const record = baseRecord({
      _sprk_regardingmatter_value: '{22222222-2222-2222-2222-222222222222}',
      sprk_regardingrecordname: 'Smith v. Jones',
      sprk_regardingrecordnumber: 'MAT-2026-0042',
      sprk_worktype: SprkAnalysisWorkType.AgreementAnalysis,
    });

    const result = resolveAnalysisRegarding(record);

    expect(result.status).toBe('resolved');
    if (result.status !== 'resolved') throw new Error('expected resolved');
    expect(result.entityType).toBe('sprk_matter');
    // Braces stripped + lowercased by cleanGuid.
    expect(result.recordId).toBe('22222222-2222-2222-2222-222222222222');
    expect(result.recordName).toBe('Smith v. Jones');
    expect(result.recordNumber).toBe('MAT-2026-0042');
    expect(result.workType).toBe(SprkAnalysisWorkType.AgreementAnalysis);
    expect(result.workTypeId).toBe('agreement-analysis');
  });

  it('resolves Project when sprk_regardingproject is the populated lookup', () => {
    const record = baseRecord({
      _sprk_regardingproject_value: '33333333-3333-3333-3333-333333333333',
    });

    const result = resolveAnalysisRegarding(record);

    expect(result.status).toBe('resolved');
    if (result.status !== 'resolved') throw new Error('expected resolved');
    expect(result.entityType).toBe('sprk_project');
    expect(result.recordId).toBe('33333333-3333-3333-3333-333333333333');
  });

  it('resolves Document when sprk_regardingdocument is the populated lookup', () => {
    const record = baseRecord({
      _sprk_regardingdocument_value: '44444444-4444-4444-4444-444444444444',
    });

    const result = resolveAnalysisRegarding(record);

    expect(result.status).toBe('resolved');
    if (result.status !== 'resolved') throw new Error('expected resolved');
    expect(result.entityType).toBe('sprk_document');
  });

  it('rejects/flags as unresolvable when ZERO regarding fields are populated', () => {
    const record = baseRecord({ sprk_worktype: SprkAnalysisWorkType.LegalResearch });

    const result = resolveAnalysisRegarding(record);

    expect(result.status).toBe('unresolved');
    if (result.status !== 'unresolved') throw new Error('expected unresolved');
    expect(result.reason).toMatch(/no populated regarding field/i);
    // sprk_worktype is still read even when regarding is unresolved.
    expect(result.workType).toBe(SprkAnalysisWorkType.LegalResearch);
    expect(result.workTypeId).toBe('legal-research');
  });

  it('rejects/flags as invalid when MORE THAN ONE regarding field is populated (does not silently pick one)', () => {
    const record = baseRecord({
      _sprk_regardingmatter_value: '22222222-2222-2222-2222-222222222222',
      _sprk_regardingproject_value: '33333333-3333-3333-3333-333333333333',
    });

    const result = resolveAnalysisRegarding(record);

    expect(result.status).toBe('invalid');
    if (result.status !== 'invalid') throw new Error('expected invalid');
    expect(result.populatedEntityTypes).toEqual(expect.arrayContaining(['sprk_matter', 'sprk_project']));
    expect(result.populatedEntityTypes).toHaveLength(2);
    expect(result.reason).toMatch(/multiple regarding fields populated/i);
  });

  it('flags as invalid when all three regarding fields are populated', () => {
    const record = baseRecord({
      _sprk_regardingmatter_value: '22222222-2222-2222-2222-222222222222',
      _sprk_regardingproject_value: '33333333-3333-3333-3333-333333333333',
      _sprk_regardingdocument_value: '44444444-4444-4444-4444-444444444444',
    });

    const result = resolveAnalysisRegarding(record);

    expect(result.status).toBe('invalid');
    if (result.status !== 'invalid') throw new Error('expected invalid');
    expect(result.populatedEntityTypes).toHaveLength(3);
  });

  it('does NOT count sprk_documentid (SPE subject-pointer) toward the single-valued invariant', () => {
    // sprk_documentid populated but no regarding field populated → still unresolved, not resolved.
    const record = baseRecord({
      _sprk_documentid_value: '55555555-5555-5555-5555-555555555555',
    });

    const result = resolveAnalysisRegarding(record);

    expect(result.status).toBe('unresolved');
  });

  it('does NOT let sprk_documentid substitute for a missing regarding field when exactly one true regarding lookup is populated', () => {
    // sprk_documentid AND sprk_regardingmatter both populated — resolves via the regarding field,
    // sprk_documentid plays no role in the count (still exactly-one true regarding field).
    const record = baseRecord({
      _sprk_documentid_value: '55555555-5555-5555-5555-555555555555',
      _sprk_regardingmatter_value: '22222222-2222-2222-2222-222222222222',
    });

    const result = resolveAnalysisRegarding(record);

    expect(result.status).toBe('resolved');
    if (result.status !== 'resolved') throw new Error('expected resolved');
    expect(result.entityType).toBe('sprk_matter');
  });

  it('returns null workType/workTypeId when sprk_worktype is unset', () => {
    const record = baseRecord({
      _sprk_regardingmatter_value: '22222222-2222-2222-2222-222222222222',
    });

    const result = resolveAnalysisRegarding(record);

    expect(result.workType).toBeNull();
    expect(result.workTypeId).toBeNull();
  });

  it('treats a whitespace-only lookup value as not populated', () => {
    const record = baseRecord({
      _sprk_regardingmatter_value: '   ',
    });

    const result = resolveAnalysisRegarding(record);

    expect(result.status).toBe('unresolved');
  });
});
