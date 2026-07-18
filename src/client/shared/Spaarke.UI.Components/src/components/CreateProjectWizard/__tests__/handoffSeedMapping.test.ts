/**
 * handoffSeedMapping.test.ts — CreateProjectWizard hand-off pre-seed mapper
 * (spaarkeai-assistant-enhancements-r1 UAT #1 — create-project parity).
 * Mirrors CreateMatterWizard/__tests__/handoffSeedMapping.test.ts.
 */

import { mapProjectHandoffSeed } from '../handoffSeedMapping';
import type { HandoffSeed } from '../../../services/surfaceHandoff';

function seed(partial: Partial<HandoffSeed>): HandoffSeed {
  return {
    draftValues: partial.draftValues ?? {},
    resolvedLookups: partial.resolvedLookups ?? {},
    fileIds: partial.fileIds ?? [],
  };
}

describe('mapProjectHandoffSeed', () => {
  it('returns undefined for a null / empty seed', () => {
    expect(mapProjectHandoffSeed(null)).toBeUndefined();
    expect(mapProjectHandoffSeed(undefined)).toBeUndefined();
    expect(mapProjectHandoffSeed(seed({}))).toBeUndefined();
  });

  it('maps snake_case drafted name + description onto projectName / description', () => {
    const result = mapProjectHandoffSeed(
      seed({ draftValues: { project_name: 'Acme Migration', project_description: 'ERP rollout.' } })
    );
    expect(result).toEqual({ projectName: 'Acme Migration', description: 'ERP rollout.' });
  });

  it('tolerates camelCase and Dataverse logical-name key spellings', () => {
    expect(mapProjectHandoffSeed(seed({ draftValues: { projectName: 'X', description: 'D' } }))).toEqual({
      projectName: 'X',
      description: 'D',
    });
    expect(mapProjectHandoffSeed(seed({ draftValues: { sprk_name: 'Y', sprk_projectdescription: 'Z' } }))).toEqual({
      projectName: 'Y',
      description: 'Z',
    });
  });

  it('ignores unknown / blank draft keys (never leaks into a form field)', () => {
    const result = mapProjectHandoffSeed(
      seed({ draftValues: { project_name: 'Named', cited_refs: ['doc-1'], random: 42, project_description: '  ' } })
    );
    expect(result).toEqual({ projectName: 'Named' });
  });

  it('pre-selects a HIGH-confidence resolved project-type / practice-area dropdown', () => {
    const result = mapProjectHandoffSeed(
      seed({
        draftValues: { project_name: 'Named' },
        resolvedLookups: {
          sprk_projecttype_ref: {
            confidence: 'high',
            recordId: 'pt-1',
            candidates: [{ recordId: 'pt-1', label: 'Transactional' }],
          },
          sprk_practicearea_ref: {
            confidence: 'high',
            recordId: 'pa-1',
            candidates: [{ recordId: 'pa-1', label: 'Corporate' }],
          },
        },
      })
    );
    expect(result).toMatchObject({
      projectName: 'Named',
      projectTypeId: 'pt-1',
      projectTypeName: 'Transactional',
      practiceAreaId: 'pa-1',
      practiceAreaName: 'Corporate',
    });
  });

  it('does NOT pre-select a low/none-confidence resolved lookup (picker stays default)', () => {
    const result = mapProjectHandoffSeed(
      seed({
        draftValues: { project_name: 'Named' },
        resolvedLookups: {
          sprk_projecttype_ref: {
            confidence: 'low',
            recordId: 'pt-1',
            candidates: [{ recordId: 'pt-1', label: 'Transactional' }],
          },
        },
      })
    );
    expect(result).toEqual({ projectName: 'Named' });
  });

  it('sets id with an empty name when a high-confidence lookup has no matching candidate label', () => {
    const result = mapProjectHandoffSeed(
      seed({
        draftValues: {},
        resolvedLookups: { projectType: { confidence: 'high', recordId: 'pt-9' } },
      })
    );
    expect(result).toEqual({ projectTypeId: 'pt-9', projectTypeName: '' });
  });
});
