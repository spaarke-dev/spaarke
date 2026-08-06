/**
 * agreementTypeLookupWrite.ts — classifier-path `sprk_agreementtype` lookup write (task 023,
 * spec FR-09 classifier door).
 *
 * When the classifier (021) resolves an agreement sub-domain for a casual chat review that later
 * gets PROMOTED (or forked — see `HistoryOverlay.tsx`'s "Promote to Analysis…" affordance) into a
 * persisted `sprk_analysis` record, the resolved type was NEVER an explicit wizard pick — nothing
 * has written it onto the Analysis. Without this write, persistence silently diverges from
 * routing: the Analysis's `sprk_agreementtype` lookup stays empty even though the review that
 * produced its content ran under a specific type's knowledge pack, so a later reopen (task 022's
 * "open-existing derivation" door) cannot restore the same orientation.
 *
 * Reuse, not reinvention (CLAUDE.md §11): the nav-prop discovery + `@odata.bind` write mechanics
 * are the SAME mechanism the hub's A1 wizard-finish write already uses (`git show 1e1a6579b`,
 * `CreateAnalysisWizardWidget.tsx`) and the SAME shape `documentAssociationWrite.ts`
 * (`components/compose/`) already established for `sprk_document`'s parent-association write —
 * `discoverNavProps` + a PascalCase nav-prop fallback, `cleanGuid` on every GUID that crosses the
 * `@odata.bind` boundary (ADR-044), never throws (mirrors both precedents' graceful-degrade
 * convention).
 *
 * NAMING FOOTGUN (empirically confirmed via Dataverse MCP `describe`, task 022's step 0 —
 * `notes/022-execution-notes.md` / `notes/NOTIFY-hub-r1-deep-threading-legs-2026-07-31.md`): the
 * LIVE `sprk_analysis` lookup column is `sprk_agreementtype` (`_sprk_agreementtype_value`), NOT
 * `sprk_agreementtypeid` (that's the REGISTRY row's own primary key). A1's wizard write
 * (`CreateAnalysisWizardWidget.tsx`) still searches `columnName === 'sprk_agreementtypeid'` — a
 * still-unfixed hub-side bug (flagged, not ours to patch — that file is hub-owned) that happens to
 * work in practice only because its OWN fallback nav-prop guess (`'sprk_AgreementType'`) is
 * already correct. This module searches the CORRECT column name directly (`sprk_agreementtype`)
 * so the `discoverNavProps` safety net is actually load-bearing here, not dead code papered over
 * by a lucky fallback.
 *
 * @see ./useAgreementReviewGate.ts — `getLastResolvedSubDomainKey()`, the classifier-resolution
 *      seam this module's caller (HistoryOverlay's promote flow) reads from.
 * @see ../compose/documentAssociationWrite.ts — the sibling write-helper this mirrors.
 * @see .claude/adr/ADR-044-dataverse-guid-canonicalization.md — `cleanGuid` at every boundary.
 */

import { cleanGuid, discoverNavProps } from "@spaarke/ui-components";
import type { IDataService } from "@spaarke/ui-components";

/** The registry column identity + write target (task 022's empirically-confirmed naming). */
const AGREEMENT_TYPE_COLUMN_NAME = "sprk_agreementtype";
const AGREEMENT_TYPE_NAV_PROP_FALLBACK = "sprk_AgreementType";
const AGREEMENT_TYPE_ENTITY_SET = "sprk_agreementtypes";
const AGREEMENT_TYPE_TABLE = "sprk_agreementtype";
const ANALYSIS_TABLE = "sprk_analysis";

export interface IAgreementTypeLookupWriteResult {
  /** True when the write succeeded. */
  success: boolean;
  /** Set when `success` is false — a non-fatal, loggable reason; never thrown to the caller. */
  warning?: string;
}

/**
 * Resolves the `sprk_agreementtype` registry row's GUID for a given `sprk_key` slug (e.g.
 * `"employment"`). Never throws — a read failure or an unmatched key both degrade to `null`.
 */
export async function resolveAgreementTypeRowId(
  dataService: IDataService,
  subDomainKey: string
): Promise<string | null> {
  if (!subDomainKey) return null;
  try {
    // Escape embedded single-quotes for the OData literal (defensive — registry keys are slugs,
    // but never trust a value crossing into a query string unescaped).
    const escapedKey = subDomainKey.replace(/'/g, "''");
    const result = await dataService.retrieveMultipleRecords(
      AGREEMENT_TYPE_TABLE,
      `?$select=sprk_agreementtypeid&$filter=sprk_key eq '${escapedKey}'&$top=1`
    );
    const row = (result.entities ?? [])[0] as Record<string, unknown> | undefined;
    const id = row?.sprk_agreementtypeid;
    return typeof id === "string" && id.length > 0 ? id : null;
  } catch {
    return null;
  }
}

/**
 * Writes the `sprk_agreementtype` lookup (`_sprk_agreementtype_value`) onto an `sprk_analysis`
 * record via the SAME `discoverNavProps` + PascalCase-fallback mechanism the hub's A1 write uses
 * (see module doc for the naming-footgun correction). Never throws — a discovery or write failure
 * degrades to `{success: false, warning}`, mirroring `documentAssociationWrite.ts`'s convention so
 * a failed lookup write never blocks the promote/fork flow that triggered it.
 */
export async function writeAgreementTypeLookup(
  dataService: IDataService,
  analysisId: string,
  agreementTypeId: string
): Promise<IAgreementTypeLookupWriteResult> {
  try {
    let navProps: Awaited<ReturnType<typeof discoverNavProps>> = [];
    try {
      navProps = await discoverNavProps(ANALYSIS_TABLE);
    } catch {
      // Discovery unavailable — fall through to the PascalCase fallback below.
    }
    const navProp =
      navProps.find((e) => e.columnName === AGREEMENT_TYPE_COLUMN_NAME)?.navPropName ??
      AGREEMENT_TYPE_NAV_PROP_FALLBACK;

    // ADR-044: both GUIDs cross an `@odata.bind` boundary — canonicalize via the shared `cleanGuid`,
    // never a hand-rolled brace-stripper.
    const cleanAnalysisId = cleanGuid(analysisId);
    const cleanAgreementTypeId = cleanGuid(agreementTypeId);
    if (!cleanAnalysisId || !cleanAgreementTypeId) {
      return { success: false, warning: "Missing analysisId or agreementTypeId." };
    }

    await dataService.updateRecord(ANALYSIS_TABLE, cleanAnalysisId, {
      [`${navProp}@odata.bind`]: `/${AGREEMENT_TYPE_ENTITY_SET}(${cleanAgreementTypeId})`,
    });
    return { success: true };
  } catch (err) {
    const message = err instanceof Error ? err.message : "Unknown error";
    console.error("[agreementTypeLookupWrite] Failed to write sprk_agreementtype lookup:", err);
    return {
      success: false,
      warning: `Could not set the agreement type on the Analysis (${message}).`,
    };
  }
}

/**
 * Convenience one-shot: resolves `subDomainKey` -> registry row GUID, then writes it onto
 * `analysisId`. This is the seam a caller (HistoryOverlay's promote flow today; task 033's
 * wizard/fork auto-run bridge in the future) invokes with only the two values it already has —
 * the classifier's resolved key + the newly-bound Analysis id. Never throws.
 */
export async function applyAgreementTypeToAnalysis(
  dataService: IDataService,
  analysisId: string,
  subDomainKey: string
): Promise<IAgreementTypeLookupWriteResult> {
  const agreementTypeId = await resolveAgreementTypeRowId(dataService, subDomainKey);
  if (!agreementTypeId) {
    return {
      success: false,
      warning: `No sprk_agreementtype row found for key "${subDomainKey}".`,
    };
  }
  return writeAgreementTypeLookup(dataService, analysisId, agreementTypeId);
}
