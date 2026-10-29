/**
 * A stored association trace holds ids and reason codes only; a candidate's name comes from a read made AS THE
 * VIEWER, and a candidate the viewer cannot read (or that is not resolved yet) has no card.
 *
 *  - The reducers (`deriveConnections`, `flattenPrimaryCandidates`, `derivePrimaryReview`, `groupCandidatesByName`)
 *    show only viewer-readable candidates, named by the viewer read; old v1 rows' `name="…"`/`number="…"` text is
 *    never shown.
 *  - `derivePrimaryReview`'s state (🔴/🟡/🟢) does not change when candidates are hidden.
 *  - Reason codes and the v2 stored form match the server; a client re-save writes v2.
 *  - `createViewerRecordNameResolver` reads one query per entity per chunk, as the viewer, and caches.
 */
import {
  candidateDisplayName,
  candidateMatchReason,
  candidateRecordNumber,
  contributorPhrase,
  deriveConnections,
  derivePrimaryReview,
  flattenPrimaryCandidates,
  groupCandidatesByName,
  parseNameMatch,
  parseProvenance,
  storedReasonCode,
  toStoredProvenance,
  ASSOCIATION_STATUS_RESOLVED_VALUE,
  type ProvenanceCandidate,
  type ViewerTargetName,
} from '../provenance';
import { persistOverrideReason, type IResolverWriteContext } from '../ConnectionsWriteHandler';
import { createViewerRecordNameResolver } from '../viewerRecordNames';

const READABLE = '11111111-1111-1111-1111-111111111111';
const UNREADABLE = '22222222-2222-2222-2222-222222222222';
const PENDING = '33333333-3333-3333-3333-333333333333';
const STATUS_PENDING_REVIEW = 100000001;

/** Stands in for a host's viewer read: one readable record, one the viewer cannot read, one not resolved yet. */
const viewer: ViewerTargetName = (_entity, id) =>
  id === READABLE ? 'Viewer Read Name' : id === UNREADABLE ? null : undefined;

function v1Candidate(
  id: string,
  confidence: number,
  opts: { written?: boolean; field?: string } = {}
): ProvenanceCandidate {
  return {
    field: opts.field ?? 'sprk_regardingmatter',
    targetEntity: 'sprk_matter',
    targetId: id,
    targetName: `Stored Target Name ${id.slice(0, 4)}`,
    reinforcedConfidence: confidence,
    deterministicConfidence: confidence,
    written: opts.written ?? false,
    conflict: false,
    contributors: [
      {
        rung: 'RecordNameMatch',
        confidence,
        provenance: `record-name-match:sprk_matter:where=subject:matched=number:name="Stored Name ${id.slice(0, 4)}":number="STORED-NUM-${id.slice(0, 4)}":reason="reference number in subject"`,
      },
    ],
  };
}

function doc(candidates: ProvenanceCandidate[], autoFiled = false): string {
  return JSON.stringify({
    version: 1,
    direction: 'Incoming',
    decision: {
      status: autoFiled ? 'Resolved' : 'Suggested',
      autoFiled,
      killSwitchEnabled: true,
      autoFileThreshold: 0.85,
      topDeterministicConfidence: 0.9,
      topConfidence: 0.9,
      aiInvolved: false,
      reason: 'Reinforced confidence in band.',
    },
    rungsFired: ['RecordNameMatch'],
    candidates,
    signals: [],
  });
}

const STORED_TEXT = /Stored Name|STORED-NUM|Stored Target Name|reference number in subject/;

describe('candidate names come only from the viewer read', () => {
  const candidate = v1Candidate(READABLE, 0.9);

  it('a v1 row with name= and number= shows neither', () => {
    expect(candidateDisplayName(candidate)).toBeUndefined();
    expect(candidateRecordNumber(candidate)).toBeUndefined();
    expect(candidateDisplayName(candidate, () => null)).toBeUndefined();
  });

  it('the viewer-read name is the display name', () => {
    expect(candidateDisplayName(candidate, viewer)).toBe('Viewer Read Name');
  });

  it('parseNameMatch keeps where/matched for v1 strings and v2 codes, and returns no record text', () => {
    expect(parseNameMatch(candidate.contributors[0].provenance)).toEqual({ where: 'subject', matched: 'number' });
    expect(parseNameMatch('record-name-match:where=body:matched=name')).toEqual({ where: 'body', matched: 'name' });
    expect(candidateMatchReason(candidate)).toBe('Matched reference in subject line');
  });

  it('contributorPhrase never returns raw provenance text', () => {
    expect(
      contributorPhrase({ rung: 'ContactNameMatch', confidence: 0.7, provenance: 'name="Stored Name"' })
    ).not.toMatch(/Stored Name/);
  });
});

describe('only candidates the viewer can read become cards', () => {
  const candidates = [v1Candidate(READABLE, 0.9), v1Candidate(UNREADABLE, 0.95), v1Candidate(PENDING, 0.8)];

  it('deriveConnections: the readable candidate is the slot, unreadable and unresolved ones are absent', () => {
    const slots = deriveConnections(parseProvenance(doc(candidates))!, false, viewer);
    expect(slots).toHaveLength(1);
    expect(slots[0].targetId).toBe(READABLE);
    expect(slots[0].targetName).toBe('Viewer Read Name');
    expect(slots[0].otherCandidates).toBeUndefined();
    expect(JSON.stringify(slots)).not.toMatch(STORED_TEXT);
  });

  it('deriveConnections: no viewer read at all → no slots', () => {
    expect(deriveConnections(parseProvenance(doc(candidates))!, false)).toEqual([]);
  });

  it('flattenPrimaryCandidates: only the readable candidate, named by the viewer read', () => {
    const flat = flattenPrimaryCandidates(parseProvenance(doc(candidates))!, viewer);
    expect(flat.map(c => [c.targetId, c.targetName])).toEqual([[READABLE, 'Viewer Read Name']]);
    expect(JSON.stringify(flat)).not.toMatch(STORED_TEXT);
  });

  it('groupCandidatesByName: unreadable candidates are not grouped; no viewer → no groups', () => {
    const groups = groupCandidatesByName(candidates, viewer);
    expect(groups.map(g => g.targetName)).toEqual(['Viewer Read Name']);
    expect(groups[0].candidates.map(c => c.targetId)).toEqual([READABLE]);
    expect(groupCandidatesByName(candidates)).toEqual([]);
  });

  it('a WRITTEN target the viewer cannot read is hidden too', () => {
    const written = [v1Candidate(UNREADABLE, 1, { written: true })];
    expect(deriveConnections(parseProvenance(doc(written, true))!, true, viewer)).toEqual([]);
  });
});

describe('derivePrimaryReview: hiding candidates never changes the review state', () => {
  const everyoneReads: ViewerTargetName = () => 'Readable';
  const nobodyReads: ViewerTargetName = () => null;

  it.each([
    ['requires-review', doc([v1Candidate(UNREADABLE, 0.9), v1Candidate(PENDING, 0.8)]), STATUS_PENDING_REVIEW],
    ['needs-confirmation', doc([v1Candidate(UNREADABLE, 1)], true), STATUS_PENDING_REVIEW],
    ['confirmed', doc([v1Candidate(UNREADABLE, 1, { written: true })], true), ASSOCIATION_STATUS_RESOLVED_VALUE],
  ])('%s holds whether or not the viewer can read the candidates', (state, json, status) => {
    const filed =
      state === 'confirmed'
        ? [{ entityType: 'sprk_matter', recordId: UNREADABLE, recordName: 'Filed Lookup Name' }]
        : [];
    expect(derivePrimaryReview(json, status, filed, undefined, everyoneReads).state).toBe(state);
    expect(derivePrimaryReview(json, status, filed, undefined, nobodyReads).state).toBe(state);
    expect(derivePrimaryReview(json, status, filed).state).toBe(state);
  });

  it('needs-confirmation with an unreadable auto-matched top: the state holds, but no primary card', () => {
    const model = derivePrimaryReview(
      doc([v1Candidate(UNREADABLE, 1)], true),
      STATUS_PENDING_REVIEW,
      [],
      undefined,
      viewer
    );
    expect(model.state).toBe('needs-confirmation');
    expect(model.primary).toBeUndefined();
    expect(model.candidates).toEqual([]);
  });

  it('needs-confirmation with a readable auto-matched top shows it as the primary', () => {
    const model = derivePrimaryReview(
      doc([v1Candidate(READABLE, 1)], true),
      STATUS_PENDING_REVIEW,
      [],
      undefined,
      viewer
    );
    expect(model.primary?.targetName).toBe('Viewer Read Name');
  });
});

describe('v2 stored form', () => {
  it.each([
    [
      'RecordNameMatch',
      'record-name-match:sprk_matter:where=attachment:matched=number:name="x":number="y":reason="z"',
      'record-name-match:where=attachment:matched=number',
    ],
    ['RecordNameMatch', 'record-name-match:sprk_matter:where=elsewhere:matched=title:name="x"', 'record-name-match'],
    ['SemanticMatch', 'semantic:sprk_project:score=0.62:where=subject in the title', 'semantic-match'],
    ['ParticipantCorrelation', 'participant:to:someone@contoso.example→member:sprk_matter', 'participant-correlation'],
    ['Not A Rung', 'anything', 'unknown'],
  ])('storedReasonCode(%s) matches the server', (rung, provenance, expected) => {
    expect(storedReasonCode(rung, provenance)).toBe(expected);
  });

  it('toStoredProvenance converts a v1 document to v2 and keeps the decision', () => {
    const v1 = JSON.parse(doc([v1Candidate(READABLE, 0.9)])) as Record<string, unknown>;
    const v2 = toStoredProvenance(v1);
    expect(v2.version).toBe(2);
    expect(JSON.stringify(v2)).not.toMatch(STORED_TEXT);
    expect(v2.decision).toEqual(v1.decision);
    const [c] = v2.candidates as Array<{ targetId: string; contributors: Array<{ provenance: string }> }>;
    expect(c.targetId).toBe(READABLE);
    expect(c.contributors[0].provenance).toBe('record-name-match:where=subject:matched=number');
  });

  it('a client re-save (persistOverrideReason) writes the v2 form', async () => {
    const updateRecord = jest.fn().mockResolvedValue(undefined);
    const ctx = {
      webApi: { updateRecord, retrieveMultipleRecords: jest.fn() },
      hostEntity: 'sprk_communication',
      hostRecordId: READABLE,
    } as unknown as IResolverWriteContext;

    const result = await persistOverrideReason(
      ctx,
      doc([v1Candidate(UNREADABLE, 0.9)]),
      'sprk_regardingmatter',
      'reviewer reason',
      '2026-10-28T00:00:00.000Z'
    );

    expect(result.success).toBe(true);
    const saved = JSON.parse(updateRecord.mock.calls[0][2].sprk_associationprovenance as string);
    expect(saved.version).toBe(2);
    expect(saved.decision.overrideReason).toBe('reviewer reason');
    expect(JSON.stringify(saved)).not.toMatch(STORED_TEXT);
  });
});

describe('createViewerRecordNameResolver', () => {
  const ids = (n: number, prefix: string): string[] =>
    Array.from({ length: n }, (_, i) => `${prefix}${String(i).padStart(7, '0')}-0000-0000-0000-000000000000`);

  function api(rowsFor: (entity: string, query: string) => Record<string, unknown>[] | Error) {
    return {
      retrieveMultipleRecords: jest.fn(async (entity: string, query: string) => {
        const rows = rowsFor(entity, query);
        if (rows instanceof Error) throw rows;
        return { entities: rows };
      }),
    };
  }

  it('reads each entity once per chunk, as the viewer: returned rows are names, missing rows are null', async () => {
    const webApi = api((entity, query) =>
      entity === 'sprk_matter' && query.includes(READABLE)
        ? [{ sprk_matterid: READABLE, sprk_mattername: 'Matter A' }]
        : []
    );
    const resolver = createViewerRecordNameResolver(webApi, { nameFieldOf: async () => 'sprk_mattername' });

    expect(resolver.nameOf('sprk_matter', READABLE)).toBeUndefined();
    await resolver.resolve([
      { entity: 'sprk_matter', id: READABLE },
      { entity: 'sprk_matter', id: `{${UNREADABLE.toUpperCase()}}` },
    ]);

    expect(webApi.retrieveMultipleRecords).toHaveBeenCalledTimes(1);
    const [entity, query] = webApi.retrieveMultipleRecords.mock.calls[0];
    expect(entity).toBe('sprk_matter');
    expect(query).toBe(
      `?$select=sprk_matterid,sprk_mattername&$filter=sprk_matterid eq ${READABLE} or sprk_matterid eq ${UNREADABLE}`
    );
    expect(resolver.nameOf('sprk_matter', READABLE)).toBe('Matter A');
    expect(resolver.nameOf('sprk_matter', UNREADABLE)).toBeNull();
  });

  it('caches: a second resolve of known ids makes no read', async () => {
    const webApi = api(() => []);
    const resolver = createViewerRecordNameResolver(webApi, { nameFieldOf: async () => 'fullname' });
    await resolver.resolve([{ entity: 'contact', id: READABLE }]);
    await resolver.resolve([{ entity: 'contact', id: READABLE }]);
    expect(webApi.retrieveMultipleRecords).toHaveBeenCalledTimes(1);
  });

  it('chunks the ids (one query per chunk) and keeps entities separate', async () => {
    const webApi = api(() => []);
    const resolver = createViewerRecordNameResolver(webApi, { chunkSize: 25, nameFieldOf: async () => 'sprk_name' });
    await resolver.resolve([
      ...ids(30, 'a').map(id => ({ entity: 'sprk_project', id })),
      { entity: 'sprk_invoice', id: READABLE },
    ]);
    const entities = webApi.retrieveMultipleRecords.mock.calls.map(c => c[0]);
    expect(entities.filter(e => e === 'sprk_project')).toHaveLength(2);
    expect(entities.filter(e => e === 'sprk_invoice')).toHaveLength(1);
  });

  it('a failed read resolves to null (fail closed), and listeners are told', async () => {
    const webApi = api(() => new Error('403'));
    const resolver = createViewerRecordNameResolver(webApi, { nameFieldOf: async () => 'sprk_mattername' });
    const listener = jest.fn();
    resolver.subscribe(listener);
    await resolver.resolve([{ entity: 'sprk_matter', id: READABLE }]);
    expect(resolver.nameOf('sprk_matter', READABLE)).toBeNull();
    expect(listener).toHaveBeenCalledTimes(1);
  });

  it('an entity with no name field, or a malformed id, resolves to null without a read', async () => {
    const webApi = api(() => []);
    const resolver = createViewerRecordNameResolver(webApi, { nameFieldOf: async () => null });
    await resolver.resolve([
      { entity: 'sprk_unknownthing', id: READABLE },
      { entity: 'sprk_matter', id: "x' or 1 eq 1" },
      { entity: undefined, id: null },
      { entity: 'sprk_matter', id: undefined },
    ]);
    expect(resolver.nameOf('sprk_unknownthing', READABLE)).toBeNull();
    expect(resolver.nameOf('sprk_matter', "x' or 1 eq 1")).toBeNull();
    expect(webApi.retrieveMultipleRecords.mock.calls.filter(c => c[0] !== 'sprk_recordtype_ref')).toHaveLength(0);
  });
});
