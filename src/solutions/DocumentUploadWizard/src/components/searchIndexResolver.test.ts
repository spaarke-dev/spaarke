/**
 * searchIndexResolver.test.ts
 *
 * Unit tests for `resolveSearchIndexNameForRecord` (FR-WIZ-06).
 *
 * Covers the 3-step resolution chain:
 *   1. Parent record's `sprk_searchindexname` is returned when non-empty.
 *   2. Parent record empty → parent's owning BU's `sprk_searchindexname` is returned.
 *   3. Both empty → empty string returned (caller omits the field; BFF tenant default applies).
 *
 * Additional coverage:
 *   - INV-5-style empty semantics (whitespace-only / null treated as empty).
 *   - Parent record read failure degrades to step 3 (never throws).
 *   - BU read failure degrades to step 3 (never throws).
 *   - Brace-stripping of `_owningbusinessunit_value` (Dataverse OData quirk).
 *
 * Runner: jest (when DocumentUploadWizard adds jest devDeps). Until then, the
 * file type-checks via `tsc --noEmit` from the wizard's tsconfig.json and is
 * runnable wherever a jest-compatible runner is available.
 */

import {
    resolveSearchIndexNameForRecord,
    isNonEmptyIndexName,
    type IXrmWebApiLike,
} from "./searchIndexResolver";

// ---------------------------------------------------------------------------
// Test helpers
// ---------------------------------------------------------------------------

/**
 * Builds a structural `IXrmWebApiLike` whose `retrieveRecord` dispatches by
 * entity name. Each handler receives `(id, options)` and returns the mocked
 * record. A missing handler throws (mirrors a real WebApi 404).
 */
type EntityHandler = (
    id: string,
    options: string
) => Promise<Record<string, unknown>> | Record<string, unknown>;

function makeXrm(handlers: Record<string, EntityHandler>): IXrmWebApiLike {
    return {
        WebApi: {
            retrieveRecord: async (entity, id, options) => {
                const handler = handlers[entity];
                if (!handler) {
                    throw new Error(`No handler for entity '${entity}'`);
                }
                return handler(id, options);
            },
        },
    };
}

const PARENT_ENTITY = "sprk_matter";
const PARENT_ID = "11111111-1111-1111-1111-111111111111";
const BU_ID = "22222222-2222-2222-2222-222222222222";

// ---------------------------------------------------------------------------
// isNonEmptyIndexName (helper unit tests)
// ---------------------------------------------------------------------------

describe("isNonEmptyIndexName", () => {
    it("returns true for non-empty strings", () => {
        expect(isNonEmptyIndexName("spaarke-knowledge-index-v2")).toBe(true);
    });

    it("returns false for empty / whitespace / null / undefined / non-string", () => {
        expect(isNonEmptyIndexName("")).toBe(false);
        expect(isNonEmptyIndexName("   ")).toBe(false);
        expect(isNonEmptyIndexName(null)).toBe(false);
        expect(isNonEmptyIndexName(undefined)).toBe(false);
        expect(isNonEmptyIndexName(0)).toBe(false);
        expect(isNonEmptyIndexName(false)).toBe(false);
    });
});

// ---------------------------------------------------------------------------
// resolveSearchIndexNameForRecord — 3-step chain
// ---------------------------------------------------------------------------

describe("resolveSearchIndexNameForRecord — FR-WIZ-06 chain", () => {
    // Step 1: parent record has a non-empty value
    it("Step 1: returns parent record's sprk_searchindexname when non-empty", async () => {
        const xrm = makeXrm({
            [PARENT_ENTITY]: () => ({
                sprk_searchindexname: "spaarke-file-index",
                _owningbusinessunit_value: BU_ID,
            }),
            // BU handler intentionally NOT registered — if step 1 short-circuits,
            // we never call it. A test failure here would surface as a wrong
            // value, not an exception (since BU is unreachable).
        });

        const result = await resolveSearchIndexNameForRecord(xrm, PARENT_ENTITY, PARENT_ID);

        expect(result).toBe("spaarke-file-index");
    });

    // Step 2: parent empty, BU has value
    it("Step 2: returns parent's owning BU's sprk_searchindexname when parent value is empty", async () => {
        const xrm = makeXrm({
            [PARENT_ENTITY]: () => ({
                sprk_searchindexname: null, // empty per INV-5 semantics
                _owningbusinessunit_value: BU_ID,
            }),
            businessunit: (id) => {
                expect(id).toBe(BU_ID); // sanity: we looked up the parent's owning BU
                return {
                    sprk_searchindexname: "spaarke-knowledge-index-v2",
                };
            },
        });

        const result = await resolveSearchIndexNameForRecord(xrm, PARENT_ENTITY, PARENT_ID);

        expect(result).toBe("spaarke-knowledge-index-v2");
    });

    // Step 3: both empty
    it("Step 3: returns empty string when both parent and BU values are empty", async () => {
        const xrm = makeXrm({
            [PARENT_ENTITY]: () => ({
                sprk_searchindexname: "",
                _owningbusinessunit_value: BU_ID,
            }),
            businessunit: () => ({
                sprk_searchindexname: null, // BU also unset (Spaarke Dev 1 / Test 1 scenario)
            }),
        });

        const result = await resolveSearchIndexNameForRecord(xrm, PARENT_ENTITY, PARENT_ID);

        expect(result).toBe("");
    });

    // INV-5-style empty semantics: whitespace at step 1 falls through
    it("treats whitespace-only parent value as empty (cascades to BU)", async () => {
        const xrm = makeXrm({
            [PARENT_ENTITY]: () => ({
                sprk_searchindexname: "   ",
                _owningbusinessunit_value: BU_ID,
            }),
            businessunit: () => ({
                sprk_searchindexname: "spaarke-knowledge-index-v2",
            }),
        });

        const result = await resolveSearchIndexNameForRecord(xrm, PARENT_ENTITY, PARENT_ID);

        expect(result).toBe("spaarke-knowledge-index-v2");
    });

    // Brace-stripping on _owningbusinessunit_value (Dataverse OData quirk)
    it("strips braces from _owningbusinessunit_value before BU lookup", async () => {
        const bracedBuRef = `{${BU_ID}}`;
        const xrm = makeXrm({
            [PARENT_ENTITY]: () => ({
                sprk_searchindexname: null,
                _owningbusinessunit_value: bracedBuRef,
            }),
            businessunit: (id) => {
                // The resolver must strip braces before passing to retrieveRecord.
                expect(id).toBe(BU_ID);
                return { sprk_searchindexname: "spaarke-file-index" };
            },
        });

        const result = await resolveSearchIndexNameForRecord(xrm, PARENT_ENTITY, PARENT_ID);

        expect(result).toBe("spaarke-file-index");
    });

    // Graceful degradation: parent read throws → step 3 (empty)
    it("returns empty string (does not throw) when parent record read fails", async () => {
        const xrm = makeXrm({
            [PARENT_ENTITY]: () => {
                throw new Error("Record not found (simulated)");
            },
        });

        const result = await resolveSearchIndexNameForRecord(xrm, PARENT_ENTITY, PARENT_ID);

        // Without a successful parent read we cannot reach step 2 — degrade to empty.
        expect(result).toBe("");
    });

    // Graceful degradation: BU read throws → step 3 (empty)
    it("returns empty string (does not throw) when BU read fails after empty parent value", async () => {
        const xrm = makeXrm({
            [PARENT_ENTITY]: () => ({
                sprk_searchindexname: null,
                _owningbusinessunit_value: BU_ID,
            }),
            businessunit: () => {
                throw new Error("BU read failed (simulated)");
            },
        });

        const result = await resolveSearchIndexNameForRecord(xrm, PARENT_ENTITY, PARENT_ID);

        expect(result).toBe("");
    });

    // Missing _owningbusinessunit_value on parent → cannot reach step 2 → step 3
    it("returns empty string when parent has empty value and no owning BU reference", async () => {
        const xrm = makeXrm({
            [PARENT_ENTITY]: () => ({
                sprk_searchindexname: "",
                _owningbusinessunit_value: null,
            }),
        });

        const result = await resolveSearchIndexNameForRecord(xrm, PARENT_ENTITY, PARENT_ID);

        expect(result).toBe("");
    });

    // OData $select assembly — sanity check we ask for both fields in one roundtrip
    it("requests both sprk_searchindexname and _owningbusinessunit_value in a single roundtrip", async () => {
        let capturedOptions = "";
        const xrm = makeXrm({
            [PARENT_ENTITY]: (_id, options) => {
                capturedOptions = options;
                return { sprk_searchindexname: "spaarke-file-index" };
            },
        });

        await resolveSearchIndexNameForRecord(xrm, PARENT_ENTITY, PARENT_ID);

        expect(capturedOptions).toContain("sprk_searchindexname");
        expect(capturedOptions).toContain("_owningbusinessunit_value");
    });
});
