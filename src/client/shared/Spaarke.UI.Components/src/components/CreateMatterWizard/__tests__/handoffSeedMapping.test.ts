/**
 * handoffSeedMapping.test.ts — CreateMatterWizard hand-off pre-seed mapper
 * (spaarkeai-assistant-enhancements-r1 task 013 part 2).
 */

import { mapMatterHandoffSeed } from '../handoffSeedMapping';
import type { HandoffSeed } from '../../../services/surfaceHandoff';

function seed(partial: Partial<HandoffSeed>): HandoffSeed {
  return {
    draftValues: partial.draftValues ?? {},
    resolvedLookups: partial.resolvedLookups ?? {},
    fileIds: partial.fileIds ?? [],
  };
}

describe('mapMatterHandoffSeed', () => {
  it('returns undefined for a null / empty seed', () => {
    expect(mapMatterHandoffSeed(null)).toBeUndefined();
    expect(mapMatterHandoffSeed(undefined)).toBeUndefined();
    expect(mapMatterHandoffSeed(seed({}))).toBeUndefined();
  });

  it('maps snake_case drafted name + description onto matterName / summary', () => {
    const result = mapMatterHandoffSeed(
      seed({ draftValues: { matter_name: 'Acme v. Beta', matter_description: 'Contract dispute.' } })
    );
    expect(result).toEqual({ matterName: 'Acme v. Beta', summary: 'Contract dispute.' });
  });

  it('tolerates camelCase and Dataverse logical-name key spellings', () => {
    expect(mapMatterHandoffSeed(seed({ draftValues: { matterName: 'X', description: 'D' } }))).toEqual({
      matterName: 'X',
      summary: 'D',
    });
    expect(mapMatterHandoffSeed(seed({ draftValues: { sprk_name: 'Y', sprk_description: 'Z' } }))).toEqual({
      matterName: 'Y',
      summary: 'Z',
    });
  });

  it('ignores unknown / blank draft keys (never leaks into a form field)', () => {
    const result = mapMatterHandoffSeed(
      seed({ draftValues: { matter_name: 'Named', cited_refs: ['doc-1'], random: 42, matter_description: '  ' } })
    );
    expect(result).toEqual({ matterName: 'Named' });
  });

  it('pre-selects a HIGH-confidence resolved matter-type / practice-area dropdown', () => {
    const result = mapMatterHandoffSeed(
      seed({
        draftValues: { matter_name: 'Named' },
        resolvedLookups: {
          sprk_mattertype_ref: {
            confidence: 'high',
            recordId: 'mt-1',
            candidates: [{ recordId: 'mt-1', label: 'Litigation' }],
          },
          sprk_practicearea_ref: {
            confidence: 'high',
            recordId: 'pa-1',
            candidates: [{ recordId: 'pa-1', label: 'Commercial' }],
          },
        },
      })
    );
    expect(result).toMatchObject({
      matterName: 'Named',
      matterTypeId: 'mt-1',
      matterTypeName: 'Litigation',
      practiceAreaId: 'pa-1',
      practiceAreaName: 'Commercial',
    });
  });

  it('does NOT pre-select a low/none-confidence resolved lookup (picker stays default)', () => {
    const result = mapMatterHandoffSeed(
      seed({
        draftValues: { matter_name: 'Named' },
        resolvedLookups: {
          sprk_mattertype_ref: {
            confidence: 'low',
            recordId: 'mt-1',
            candidates: [{ recordId: 'mt-1', label: 'Litigation' }],
          },
        },
      })
    );
    expect(result).toEqual({ matterName: 'Named' });
  });

  it('sets id with an empty name when a high-confidence lookup has no matching candidate label', () => {
    const result = mapMatterHandoffSeed(
      seed({
        draftValues: {},
        resolvedLookups: { matterType: { confidence: 'high', recordId: 'mt-9' } },
      })
    );
    expect(result).toEqual({ matterTypeId: 'mt-9', matterTypeName: '' });
  });
});
