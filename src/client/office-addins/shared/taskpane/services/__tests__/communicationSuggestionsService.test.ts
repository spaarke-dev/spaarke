import { fetchEnginePreSelection, fetchRelatedCandidates } from '../communicationSuggestionsService';
import { apiClient, ApiClientError } from '@shared/services';

// Mock the shared apiClient; keep a real ApiClientError class so the service's
// `instanceof` 404 branch works. The service imports the REAL shared
// `derivePrimaryReview` (jest maps it to the pure provenance source), so these
// tests exercise the actual candidate model — proving no fork (ADR-045).
jest.mock('@shared/services', () => {
  class ApiClientError extends Error {
    error: { type: string; title: string; status: number };
    constructor(error: { type: string; title: string; status: number }) {
      super('api error');
      this.name = 'ApiClientError';
      this.error = error;
    }
  }
  return {
    apiClient: { get: jest.fn() },
    ApiClientError,
  };
});

const mockGet = apiClient.get as jest.Mock;

function matterSuggestion(overrides?: Record<string, unknown>, names?: Record<string, string>) {
  return {
    communicationId: 'comm-1',
    subject: 'Re: Smith matter',
    ...(names ? { names } : {}),
    suggestions: {
      communicationId: 'comm-1',
      status: 'Suggested',
      autoFileEligible: false,
      candidates: [
        {
          field: 'sprk_regardingmatter',
          targetEntity: 'sprk_matter',
          targetId: '11111111-1111-1111-1111-111111111111',
          reinforcedConfidence: 0.92,
          deterministicConfidence: 0.92,
          written: false,
          conflict: false,
          contributors: [
            {
              rung: 'RecordNameMatch',
              confidence: 0.92,
              provenance:
                'record-name-match:sprk_matter:where=subject:matched=number:number="REAL-2026-123":reason="number in subject"',
            },
          ],
        },
      ],
      ...(overrides ?? {}),
    },
  };
}

describe('fetchEnginePreSelection', () => {
  it('returns null for an empty internetMessageId without a network call', async () => {
    const result = await fetchEnginePreSelection(undefined);
    expect(result).toBeNull();
    expect(mockGet).not.toHaveBeenCalled();
  });

  it('uses the server-resolved display name for the predicted record', async () => {
    mockGet.mockResolvedValueOnce(
      matterSuggestion(undefined, { '11111111-1111-1111-1111-111111111111': 'Smith v Jones' })
    );

    const result = await fetchEnginePreSelection('<abc@contoso.com>');

    expect(result).not.toBeNull();
    // The picker shows the resolved NAME (not the GUID) — folded into the shared model's targetName.
    expect(result!.predicted.name).toBe('Smith v Jones');
  });

  it('falls back to the id for the name when the server resolved none (matches the code page)', async () => {
    mockGet.mockResolvedValueOnce(matterSuggestion());

    const result = await fetchEnginePreSelection('<abc@contoso.com>');

    expect(result).not.toBeNull();
    expect(result!.predicted.name).toBe('11111111-1111-1111-1111-111111111111');
  });

  it('maps the engine-predicted matter to a picker EntitySearchResult', async () => {
    mockGet.mockResolvedValueOnce(matterSuggestion());

    const result = await fetchEnginePreSelection('<abc@contoso.com>');

    expect(result).not.toBeNull();
    expect(result!.predicted.entityType).toBe('Matter');
    expect(result!.predicted.logicalName).toBe('sprk_matter');
    expect(result!.predicted.id).toBe('11111111-1111-1111-1111-111111111111');
    // Record number (from the RecordNameMatch contributor) surfaces as displayInfo.
    expect(result!.predicted.displayInfo).toBe('REAL-2026-123');
    // Endpoint is the by-message-id/{id}/suggestions route with the id URL-encoded.
    expect(mockGet).toHaveBeenCalledWith(expect.stringContaining('/api/office/communications/by-message-id/'));
    expect(mockGet).toHaveBeenCalledWith(expect.stringContaining('/suggestions'));
  });

  it('returns null (no pre-selection) when the email is not captured (404)', async () => {
    mockGet.mockRejectedValueOnce(new ApiClientError({ type: 'about:blank', title: 'Not Found', status: 404 }));

    const result = await fetchEnginePreSelection('<not-captured@contoso.com>');

    expect(result).toBeNull();
  });

  it('returns null when the top candidate is a type the picker cannot represent', async () => {
    mockGet.mockResolvedValueOnce(
      matterSuggestion({
        candidates: [
          {
            field: 'sprk_regardingorganization',
            targetEntity: 'sprk_organization',
            targetId: '22222222-2222-2222-2222-222222222222',
            reinforcedConfidence: 0.95,
            deterministicConfidence: 0.95,
            written: false,
            conflict: false,
            contributors: [],
          },
        ],
      })
    );

    const result = await fetchEnginePreSelection('<org@contoso.com>');

    expect(result).toBeNull();
  });

  it('returns null when the engine has no candidate above the confidence floor', async () => {
    mockGet.mockResolvedValueOnce(
      matterSuggestion({
        candidates: [
          {
            field: 'sprk_regardingmatter',
            targetEntity: 'sprk_matter',
            targetId: '33333333-3333-3333-3333-333333333333',
            reinforcedConfidence: 0.4, // below PRIMARY_MATCH_MIN_CONFIDENCE (0.7)
            deterministicConfidence: 0.4,
            written: false,
            conflict: false,
            contributors: [],
          },
        ],
      })
    );

    const result = await fetchEnginePreSelection('<weak@contoso.com>');

    expect(result).toBeNull();
  });

  it('re-throws non-404 errors so the caller treats it as no pre-selection (best-effort)', async () => {
    mockGet.mockRejectedValueOnce(new ApiClientError({ type: 'about:blank', title: 'Server Error', status: 500 }));

    await expect(fetchEnginePreSelection('<err@contoso.com>')).rejects.toBeInstanceOf(ApiClientError);
  });

  it('returns null for an Account prediction: the save refuses "account", so the ribbon must not try (task 084 / #1075)', async () => {
    mockGet.mockResolvedValueOnce(
      matterSuggestion({
        candidates: [
          {
            field: 'sprk_regardingaccount',
            targetEntity: 'account',
            targetId: '44444444-4444-4444-4444-444444444444',
            reinforcedConfidence: 0.95,
            deterministicConfidence: 0.95,
            written: false,
            conflict: false,
            contributors: [],
          },
        ],
      })
    );

    const result = await fetchEnginePreSelection('<account@contoso.com>');

    expect(result).toBeNull();
  });
});

// ---------------------------------------------------------------------------------------------
// Task 084 (#1037, "pickable equals savable"): the suggestions route's `filingAccess` map (keyed by
// candidate targetId, like `names`) becomes `canFile` on the cards AND on the ribbon's prediction.
// ---------------------------------------------------------------------------------------------

const MATTER_ID = '11111111-1111-1111-1111-111111111111';
const PROJECT_ID = '55555555-5555-5555-5555-555555555555';

/** A matter (0.92) + a project (0.8) suggestion, optionally with a `filingAccess` map. */
function twoCandidateSuggestion(filingAccess?: Record<string, boolean>) {
  const base = matterSuggestion();
  const project = {
    field: 'sprk_regardingproject',
    targetEntity: 'sprk_project',
    targetId: PROJECT_ID,
    reinforcedConfidence: 0.8,
    deterministicConfidence: 0.8,
    written: false,
    conflict: false,
    contributors: [],
  };
  return {
    ...base,
    ...(filingAccess ? { filingAccess } : {}),
    suggestions: { ...base.suggestions, candidates: [...base.suggestions.candidates, project] },
  };
}

describe('filingAccess → canFile (task 084)', () => {
  it('fetchRelatedCandidates carries filingAccess onto each card as canFile', async () => {
    mockGet.mockResolvedValueOnce(twoCandidateSuggestion({ [MATTER_ID]: false, [PROJECT_ID]: true }));

    const cards = await fetchRelatedCandidates('<abc@contoso.com>');

    expect(cards.find(c => c.id === MATTER_ID)?.canFile).toBe(false);
    expect(cards.find(c => c.id === PROJECT_ID)?.canFile).toBe(true);
  });

  it('fetchEnginePreSelection carries filingAccess onto the predicted record (and the alternates)', async () => {
    mockGet.mockResolvedValueOnce(twoCandidateSuggestion({ [MATTER_ID]: false, [PROJECT_ID]: true }));

    const result = await fetchEnginePreSelection('<abc@contoso.com>');

    expect(result).not.toBeNull();
    expect(result!.predicted.id).toBe(MATTER_ID);
    expect(result!.predicted.canFile).toBe(false);
    expect(result!.alternates.find(a => a.id === PROJECT_ID)?.canFile).toBe(true);
  });

  it('an absent key leaves canFile ABSENT (selectable, as before) — never invented as true or false', async () => {
    mockGet.mockResolvedValueOnce(twoCandidateSuggestion({ [MATTER_ID]: false }));

    const cards = await fetchRelatedCandidates('<abc@contoso.com>');

    expect(cards.find(c => c.id === MATTER_ID)?.canFile).toBe(false);
    expect(cards.find(c => c.id === PROJECT_ID)).not.toHaveProperty('canFile');
  });

  it('no filingAccess map at all → no canFile anywhere (a pre-084 server response behaves as before)', async () => {
    mockGet.mockResolvedValueOnce(twoCandidateSuggestion());

    const cards = await fetchRelatedCandidates('<abc@contoso.com>');
    mockGet.mockResolvedValueOnce(twoCandidateSuggestion());
    const pre = await fetchEnginePreSelection('<abc@contoso.com>');

    expect(cards).toHaveLength(2);
    cards.forEach(c => expect(c).not.toHaveProperty('canFile'));
    expect(pre!.predicted).not.toHaveProperty('canFile');
  });

  it('filingAccess does not change the ranking (ADR-045: the shared model is untouched)', async () => {
    mockGet.mockResolvedValueOnce(twoCandidateSuggestion());
    const without = await fetchRelatedCandidates('<abc@contoso.com>');
    mockGet.mockResolvedValueOnce(twoCandidateSuggestion({ [MATTER_ID]: false, [PROJECT_ID]: true }));
    const withAccess = await fetchRelatedCandidates('<abc@contoso.com>');

    expect(withAccess.map(c => [c.id, c.confidence])).toEqual(without.map(c => [c.id, c.confidence]));
  });
});
