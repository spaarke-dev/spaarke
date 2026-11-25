/**
 * analysisService.warnings.test.ts
 *
 * field-mapping-server-write-path-r1 task 028 (task 043 review): `POST /api/ai/analysis/create` may answer its 201 with
 * a `warnings` array (e.g. no fields were copied from the document). The service hands it to the caller; a body with no
 * `warnings` member carries nothing. `createAndAssociate` keeps returning the id alone for its other caller.
 */
import { createAndAssociate, createAndAssociateWithWarnings } from '../analysisService';
import type { IAnalysisConfig } from '../types';

const config: IAnalysisConfig = {
  documentId: 'doc-1',
  documentName: 'MSA.docx',
  playbookId: 'pb-1',
  actionId: 'act-1',
  skillIds: [],
  knowledgeIds: [],
  toolIds: [],
};

function fetchReturning(body: unknown): jest.Mock {
  return jest.fn().mockResolvedValue({
    ok: true,
    status: 201,
    json: async () => body,
    text: async () => '',
  });
}

describe('analysisService - the create response warnings', () => {
  it('returns the warnings of a 201 body that carries them, and posts to /api/ai/analysis/create', async () => {
    const authFetch = fetchReturning({
      analysisId: 'analysis-1',
      warnings: ['No fields were copied from the document.'],
    });

    const result = await createAndAssociateWithWarnings(authFetch, 'https://bff.example.com/', config);

    expect(result).toEqual({ analysisId: 'analysis-1', warnings: ['No fields were copied from the document.'] });
    expect(authFetch).toHaveBeenCalledTimes(1);
    expect(authFetch.mock.calls[0][0]).toBe('https://bff.example.com/api/ai/analysis/create');
  });

  it('returns no warnings for a body with no warnings member', async () => {
    const result = await createAndAssociateWithWarnings(
      fetchReturning({ analysisId: 'analysis-1' }),
      'https://bff.example.com',
      config
    );

    expect(result).toEqual({ analysisId: 'analysis-1', warnings: [] });
  });

  it('keeps only non-empty strings from a warnings member', async () => {
    const result = await createAndAssociateWithWarnings(
      fetchReturning({ analysisId: 'analysis-1', warnings: ['kept', '', '   ', 7, null, { a: 1 }] }),
      'https://bff.example.com',
      config
    );

    expect(result.warnings).toEqual(['kept']);
  });

  it('ignores a warnings member that is not an array', async () => {
    const result = await createAndAssociateWithWarnings(
      fetchReturning({ analysisId: 'analysis-1', warnings: 'no fields were copied' }),
      'https://bff.example.com',
      config
    );

    expect(result.warnings).toEqual([]);
  });

  it('createAndAssociate still resolves to the analysis id alone', async () => {
    const id = await createAndAssociate(
      fetchReturning({ analysisId: 'analysis-1', warnings: ['x'] }),
      'https://bff.example.com',
      config
    );

    expect(id).toBe('analysis-1');
  });
});
