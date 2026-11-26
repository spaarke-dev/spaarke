/**
 * reconciliationResolversWrittenRegarding.test.ts — spaarkeai-word-add-in-r1 task 142 (ISS-043 reopened, #3194).
 *
 * Task 140 taught `derivePrimaryReview` to show a written regarding on an unconfirmed row as 'needs-confirmation', but
 * the reconciliation page's row mapper (`buildResolveReview`) never passed the written regarding to the review, so the
 * page kept showing "No confident match". These tests drive the mapper's output through the same reducer the review
 * component calls. Ids here are synthetic.
 */
import { buildResolveReview, resolveRegarding } from '../reconciliationResolvers';
import { derivePrimaryReview, ASSOCIATION_STATUS_RESOLVED_VALUE } from '../provenance';
import type { EmailConnectionsReviewProps } from '../../../components/EmailAssociationsAndTracking/EmailAssociationsAndTracking.types';

const SUGGESTED = 100000003;
const COMM_ID = 'cccccccc-0000-4000-8000-000000000001';
const MATTER_ID = 'aaaaaaaa-0000-4000-8000-000000000001';
const REQUEST_ID = 'aaaaaaaa-0000-4000-8000-000000000003';
const FORMATTED = '@OData.Community.Display.V1.FormattedValue';

/** A Suggested provenance trail with no candidates (the dev rows written before #3214's cap fix). */
const EMPTY_TRAIL = JSON.stringify({
  version: 1,
  direction: 'Incoming',
  decision: { status: 'Suggested', autoFiled: false, topConfidence: 0.9895 },
  rungsFired: ['ExplicitReference', 'RecordNameMatch'],
  candidates: [],
});

/** The lightweight grid row (what the view selects): status, provenance and the ADR-024 denorm pair only. */
const gridRow = (): Record<string, unknown> => ({
  sprk_communicationid: COMM_ID,
  sprk_associationstatus: SUGGESTED,
  sprk_associationprovenance: EMPTY_TRAIL,
  sprk_regardingrecordid: `{${MATTER_ID.toUpperCase()}}`,
  sprk_regardingrecordname: 'Acme v. Example',
  sprk_regardingrecordnumber: 'MAT-000123',
  sprk_regardingrecordtypename: 'Matter',
});

/** The same row after the workspace's on-open enrichment merged the full record (typed lookups + annotations). */
const enrichedRow = (): Record<string, unknown> => ({
  ...gridRow(),
  _sprk_regardingmatter_value: MATTER_ID,
  [`_sprk_regardingmatter_value${FORMATTED}`]: 'Acme v. Example',
});

const review = (row: Record<string, unknown>): EmailConnectionsReviewProps =>
  buildResolveReview({} as never, () => undefined)(row);

/** The reducer call `EmailConnectionsReview` makes with these props. */
const reduce = (p: EmailConnectionsReviewProps) =>
  derivePrimaryReview(p.associationProvenanceJson, p.associationStatus, p.filedAssociations, {
    recordName: p.regardingRecordName,
    recordNumber: p.regardingRecordNumber,
    recordTypeLabel: p.regardingRecordType,
    recordId: p.regardingRecordId,
  });

describe('buildResolveReview — written regarding (task 142)', () => {
  it('buildResolveReview_EnrichedSuggestedRowWithMatterWritten_ReviewNeedsConfirmationOnThatMatter', () => {
    const props = review(enrichedRow());

    expect(props.regardingRecordId).toBe(MATTER_ID);
    expect(props.filedAssociations).toEqual([
      { entityType: 'sprk_matter', recordId: MATTER_ID, recordName: 'Acme v. Example' },
    ]);

    const model = reduce(props);
    expect(model.state).toBe('needs-confirmation');
    expect(model.primary).toMatchObject({ entity: 'sprk_matter', targetId: MATTER_ID, targetName: 'Acme v. Example' });
    expect(model.candidates[0]?.targetId).toBe(MATTER_ID);
  });

  it('buildResolveReview_PairNamesSecondFiledSlot_PrimaryIsThePairRecordNotTheFirstSlot', () => {
    const row = {
      ...enrichedRow(),
      sprk_regardingrecordid: REQUEST_ID,
      _sprk_regardingservicerequest_value: REQUEST_ID,
    };

    const model = reduce(review(row));

    expect(model.state).toBe('needs-confirmation');
    expect(model.primary?.targetId).toBe(REQUEST_ID);
  });

  it('buildResolveReview_PairNamesNoFiledSlot_StaysRequiresReviewNotTheFirstSlot', () => {
    const model = reduce(review({ ...enrichedRow(), sprk_regardingrecordid: REQUEST_ID }));

    expect(model.state).toBe('requires-review');
    expect(model.primary?.targetId).not.toBe(MATTER_ID);
  });

  it('buildResolveReview_ResolvedRow_ConfirmedPrimaryIsTheTypedLookup', () => {
    const model = reduce(review({ ...enrichedRow(), sprk_associationstatus: ASSOCIATION_STATUS_RESOLVED_VALUE }));

    expect(model.state).toBe('confirmed');
    expect(model.primary).toMatchObject({ entity: 'sprk_matter', targetId: MATTER_ID });
  });

  it('buildResolveReview_BracedUppercaseLookup_NormalisedId', () => {
    const row = { ...gridRow(), _sprk_regardingmatter_value: `{${MATTER_ID.toUpperCase()}}` };

    expect(review(row).filedAssociations?.[0]?.recordId).toBe(MATTER_ID);
  });

  it('buildResolveReview_LookupWithoutFormattedValue_NameFallsBackToTheId', () => {
    const row = { ...gridRow(), _sprk_regardingmatter_value: MATTER_ID };

    expect(review(row).filedAssociations).toEqual([
      { entityType: 'sprk_matter', recordId: MATTER_ID, recordName: MATTER_ID },
    ]);
  });

  it('buildResolveReview_GridRowBeforeEnrichment_NoFiledLookupsAndStaysRequiresReview', () => {
    const props = review(gridRow());

    expect(props.filedAssociations).toEqual([]);
    expect(props.regardingRecordId).toBe(MATTER_ID);
    // Denorm-only: no typed lookup id to confirm, so the reducer keeps its red state until the record is enriched.
    expect(reduce(props).state).toBe('requires-review');
  });

  it('buildResolveReview_NoRegardingWritten_NullIdAndEmptyFiled', () => {
    const { sprk_regardingrecordid: _id, ...row } = gridRow();

    const props = review(row);

    expect(props.regardingRecordId).toBeNull();
    expect(props.filedAssociations).toEqual([]);
  });
});

describe('resolveRegarding — unchanged after sharing the reader', () => {
  it('resolveRegarding_ResolvedRowWithPairOnSecondSlot_ReturnsThePairRecord', () => {
    const row = {
      ...enrichedRow(),
      sprk_associationstatus: ASSOCIATION_STATUS_RESOLVED_VALUE,
      sprk_regardingrecordid: REQUEST_ID,
      _sprk_regardingservicerequest_value: REQUEST_ID,
    };

    expect(resolveRegarding(row)).toEqual({ entityType: 'sprk_servicerequest', recordId: REQUEST_ID });
  });

  it('resolveRegarding_ResolvedRowPairNamesNoFiledSlot_ReturnsFirstFiled', () => {
    const row = {
      ...enrichedRow(),
      sprk_associationstatus: ASSOCIATION_STATUS_RESOLVED_VALUE,
      sprk_regardingrecordid: REQUEST_ID,
    };

    expect(resolveRegarding(row)).toEqual({ entityType: 'sprk_matter', recordId: MATTER_ID });
  });

  it('resolveRegarding_SuggestedRow_ReturnsNull', () => {
    expect(resolveRegarding(enrichedRow())).toBeNull();
  });
});
