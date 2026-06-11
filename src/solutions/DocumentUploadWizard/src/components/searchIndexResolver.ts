/**
 * searchIndexResolver.ts
 *
 * FR-WIZ-06 — `resolveSearchIndexNameForRecord` 3-step chain.
 *
 * Extracted to a standalone module (separate from `AssociateToStep.tsx`) for
 * three reasons:
 *   1. Pure-logic helper — no JSX, no Fluent UI imports → trivially unit-testable
 *      with ts-jest / vitest without DOM or @testing-library overhead.
 *   2. Pattern symmetry with `resolveContainerIdForRecord` is preserved — both
 *      live alongside `AssociateToStep.tsx`; `AssociateToStep.tsx` re-exports
 *      `resolveSearchIndexNameForRecord` so the public surface remains the
 *      same and downstream callers (task 027) can import from either location.
 *   3. Task 027's `DocumentRecordService.buildRecordPayload` consumes the
 *      resolver result; isolating the resolver behind a narrow interface
 *      (`IXrmWebApiLike`) means task 027 can also test the payload-assembly
 *      path without standing up the full wizard.
 *
 * Differences vs `resolveContainerIdForRecord` (lives in `AssociateToStep.tsx`):
 *   - **Container** resolver falls back to the CURRENT USER's BU and THROWS
 *     when no container is found (container is required for upload).
 *   - **SearchIndexName** resolver falls back to the PARENT RECORD's owning
 *     BU and NEVER throws — empty string is a legitimate result that defers
 *     to the server-side BFF tenant default chain (FR-BFF-04).
 *
 * @see projects/spaarke-multi-container-multi-index-r1/spec.md FR-WIZ-06
 * @see ADR-012 - Resolver logic lives in the wizard / shared lib (not directly
 *                in the code-page entry)
 * @see ADR-022 - Pure-TS helper, React-version-portable (no React imports)
 * @see ADR-028 - Uses Xrm.WebApi for Dataverse reads; no BFF calls
 */

/* eslint-disable @typescript-eslint/no-explicit-any */

/**
 * Narrow structural type covering only the `Xrm.WebApi.retrieveRecord` shape
 * used by the resolver. Lets callers pass either the full Xrm handle or a
 * test double without coupling tests to the rest of the Xrm surface.
 */
export interface IXrmWebApiLike {
    WebApi: {
        retrieveRecord: (
            entity: string,
            id: string,
            options: string
        ) => Promise<Record<string, unknown>>;
    };
}
/* eslint-enable @typescript-eslint/no-explicit-any */

/**
 * Treats null, undefined, and whitespace-only strings as "empty" for the
 * search-index-name cascade. Mirrors the INV-5 semantics used by
 * EntityCreationService cascade helpers (task 020).
 *
 * Exported for unit-test reuse; not part of the wizard's public API.
 */
export function isNonEmptyIndexName(value: unknown): value is string {
    return typeof value === "string" && value.trim().length > 0;
}

/**
 * Resolves the effective `sprk_searchindexname` for a parent record via the
 * FR-WIZ-06 3-step chain:
 *
 *   1. Parent record's own `sprk_searchindexname` (if non-empty)
 *   2. Parent record's owning Business Unit's `sprk_searchindexname` (if non-empty)
 *   3. Empty string — caller MUST omit the field from the create payload so
 *      the BFF tenant-default chain handles the fallback server-side.
 *
 * Pattern symmetry with `resolveContainerIdForRecord` (in `AssociateToStep.tsx`)
 * is intentional; the two resolvers stay separate functions to preserve their
 * different fallback semantics (see module docblock).
 *
 * The function never throws — read failures degrade to the next chain step.
 * Step 3 ("") is a legitimate result; callers (task 027's
 * `DocumentRecordService.buildRecordPayload`) MUST omit the field from the
 * Dataverse create payload when the result is empty so the server-side BFF
 * tenant-default chain (FR-BFF-04) takes over.
 *
 * @param xrm                Xrm.WebApi-bearing handle (or a structural double for tests).
 * @param entityLogicalName  Parent entity's logical name (e.g., "sprk_matter").
 * @param recordId           Parent record GUID (no braces, lowercase preferred).
 * @returns The effective index name, or `""` (empty string) if neither the
 *          parent record nor its owning BU has a value set.
 */
export async function resolveSearchIndexNameForRecord(
    xrm: IXrmWebApiLike,
    entityLogicalName: string,
    recordId: string
): Promise<string> {
    // ── Step 1: Parent record's own sprk_searchindexname ──────────────────
    // Also select _owningbusinessunit_value so we can fall through to step 2
    // without a second roundtrip when step 1 returns empty.
    let parentOwningBuId: string | undefined;
    try {
        const record = await xrm.WebApi.retrieveRecord(
            entityLogicalName,
            recordId,
            "?$select=sprk_searchindexname,_owningbusinessunit_value"
        );
        if (isNonEmptyIndexName(record["sprk_searchindexname"])) {
            return record["sprk_searchindexname"] as string;
        }
        const buRef = record["_owningbusinessunit_value"];
        if (typeof buRef === "string" && buRef.length > 0) {
            parentOwningBuId = buRef.replace(/[{}]/g, "");
        }
    } catch {
        // Parent record read failed (or field unavailable on this entity) —
        // degrade to step 3 (empty). Step 2 requires the parent's owning BU
        // id which we can't obtain without a successful parent read.
    }

    // ── Step 2: Parent record's owning BU's sprk_searchindexname ──────────
    if (parentOwningBuId) {
        try {
            const bu = await xrm.WebApi.retrieveRecord(
                "businessunit",
                parentOwningBuId,
                "?$select=sprk_searchindexname"
            );
            if (isNonEmptyIndexName(bu["sprk_searchindexname"])) {
                return bu["sprk_searchindexname"] as string;
            }
        } catch {
            // BU read failed — degrade to step 3 (empty). Never throw — empty
            // is a legitimate result per FR-WIZ-06 (server tenant default
            // chain applies, FR-BFF-04).
        }
    }

    // ── Step 3: Empty — server tenant default takes over (FR-BFF-04) ──────
    return "";
}
