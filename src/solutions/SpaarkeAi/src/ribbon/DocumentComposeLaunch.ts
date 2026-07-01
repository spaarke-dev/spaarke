/**
 * DocumentComposeLaunch.ts — `sprk_document` form "Open in Compose" command-bar handler.
 *
 * Project:  spaarkeai-compose-r1
 * Task:     046 — Frontend: wire modal launch (Path A entry UX)
 * Phase:    Phase 4 — Frontend Compose surface
 *
 * Behaviour:
 *   Reads the open `sprk_document` record's identity and SPE pointer from the
 *   Xrm form context (PrimaryControl), then delegates to launch-resolver's
 *   `openSpaarkeAiCompose` to open the SpaarkeAi modal directly into the
 *   Compose editor surface with the document pre-loaded.
 *
 *   This is the Path A entry per `projects/spaarkeai-compose-r1/design.md` §14
 *   row 3 ("Modal with full-screen toggle"). The full-screen toggle UX is
 *   provided by the Xrm dialog chrome itself (platform-provided Expand button
 *   on the modal header) — no new modal abstraction is created.
 *
 * Architecture (ADR-006 — ribbon scripts are invocation-only):
 *   This file contains ZERO URL construction or navigation logic. It:
 *     1. Extracts `sprk_documentid` from the form context (synchronous).
 *     2. Fetches the SPE drive-item id (+ optional drive id + display name)
 *        via `Xrm.WebApi.retrieveRecord` (async — fast for a single record).
 *     3. Delegates the actual modal open to `openSpaarkeAiCompose`.
 *
 *   All URL assembly, parameter-encoding, and `Xrm.Navigation.navigateTo`
 *   plumbing lives in `utils/launch-resolver.ts`.
 *
 * Ribbon XML registration (deployed via DocumentRibbons solution):
 *   Library:      $webresource:sprk_spaarkeai_documentcomposelaunch
 *   FunctionName: Sprk.SpaarkeAi.DocumentComposeLaunch.openInCompose
 *   CrmParameters: <CrmParameter Value="PrimaryControl" />
 *
 *   See `infrastructure/dataverse/ribbon/DocumentRibbons/opencompose-button.xml`
 *   for the full RibbonDiffXml + deployment instructions.
 *
 * SPE drive-item id field on `sprk_document`:
 *   The canonical field is `sprk_graphitemid` — the SPE drive-item id assigned
 *   by Graph when the file is uploaded. The drive id field is `sprk_driveid`
 *   (optional; the BFF Load endpoint can also resolve it from the container).
 *   These names are used in the WebApi retrieve call below; if the schema
 *   evolves they MUST be updated here.
 *
 * @see ADR-006 — Ribbon scripts must be invocation-only (no business logic)
 * @see ADR-028 — Spaarke Auth v2 (no manual token handling at the launch boundary)
 * @see launch-resolver.ts — `openSpaarkeAiCompose` does the actual modal open
 * @see projects/spaarkeai-compose-r1/spec.md FR-19 — Document → Compose modal entry
 * @see projects/spaarkeai-compose-r1/design.md §14 row 3 — locked decision
 */

/// <reference path="./xrm-globals.d.ts" />

import { openSpaarkeAiCompose } from "../utils/launch-resolver";

// ---------------------------------------------------------------------------
// `sprk_document` field constants
//
// Update these if the schema is ever renamed. The schema is established in
// existing Document plumbing (see `src/server/api/Sprk.Bff.Api/Api/Documents/`
// + ChatAttachment endpoints which already read these fields).
// ---------------------------------------------------------------------------
const FIELD_DOCUMENT_ID = "sprk_documentid";
const FIELD_GRAPH_ITEM_ID = "sprk_graphitemid";
const FIELD_DRIVE_ID = "sprk_driveid";
const FIELD_DISPLAY_NAME = "sprk_displayname";

/**
 * Opens the SpaarkeAi modal directly into the Compose editor surface, pre-seeded
 * with the open `sprk_document` record's identity and SPE drive-item id.
 *
 * Called by the ribbon command definition:
 *   FunctionName: Sprk.SpaarkeAi.DocumentComposeLaunch.openInCompose
 *   CrmParameters: <CrmParameter Value="PrimaryControl" />
 *
 * Failure modes:
 *   - Form is in "new" state (no GUID yet): logs a warn and exits. The ribbon
 *     enable rule should prevent this; the runtime guard is defensive.
 *   - WebApi retrieve fails (record deleted, no permission): logs the error
 *     and opens Compose in empty-state (Browse / Search picker). The user gets
 *     a Compose surface, just without the pre-loaded document — matching the
 *     R1 default-open behaviour locked in design.md §14 row 5.
 *
 * No exceptions propagate out — ribbon callbacks must never throw or the entire
 * command bar errors out.
 *
 * @param primaryControl - The Xrm FormContext passed by the ribbon framework
 *   via the PrimaryControl CrmParameter.
 */
export async function openInCompose(
  primaryControl: Xrm.FormContext,
): Promise<void> {
  let documentId: string;
  try {
    documentId = primaryControl.data.entity.getId();
  } catch (err) {
    console.warn(
      "[DocumentComposeLaunch] Could not read record id from primaryControl:",
      err,
    );
    // No record id — open Compose in empty-state.
    openSpaarkeAiCompose({});
    return;
  }

  // Strip braces (Xrm.WebApi expects no braces).
  const normalizedDocumentId = documentId.replace(/^\{|\}$/g, "");
  if (!normalizedDocumentId) {
    console.warn(
      "[DocumentComposeLaunch] Record id is empty (likely an unsaved form). Opening empty Compose.",
    );
    openSpaarkeAiCompose({});
    return;
  }

  // Fetch SPE pointer + display name from the open record.
  // $select keeps the round-trip small (one row, three fields).
  const select =
    `?$select=${FIELD_DOCUMENT_ID},${FIELD_GRAPH_ITEM_ID},${FIELD_DRIVE_ID},${FIELD_DISPLAY_NAME}`;

  try {
    const record = await Xrm.WebApi.retrieveRecord(
      "sprk_document",
      normalizedDocumentId,
      select,
    );

    const speDriveItemId = record[FIELD_GRAPH_ITEM_ID] as string | undefined;
    const speDriveId = record[FIELD_DRIVE_ID] as string | undefined;
    const speFileName = record[FIELD_DISPLAY_NAME] as string | undefined;

    if (!speDriveItemId) {
      // No SPE drive-item id — the document hasn't been promoted to SPE yet.
      // Open Compose in empty-state with the Dataverse record id so post-Save
      // promotion can still link back to the open record (ChatSession binding).
      console.warn(
        "[DocumentComposeLaunch] sprk_graphitemid is missing on record",
        normalizedDocumentId,
        "— opening Compose in empty state.",
      );
      openSpaarkeAiCompose({
        entityLogicalName: "sprk_document",
        entityId: normalizedDocumentId,
        sprkDocumentId: normalizedDocumentId,
      });
      return;
    }

    openSpaarkeAiCompose({
      entityLogicalName: "sprk_document",
      entityId: normalizedDocumentId,
      sprkDocumentId: normalizedDocumentId,
      speDriveItemId,
      speDriveId: speDriveId ?? undefined,
      speFileName: speFileName ?? undefined,
    });
  } catch (err) {
    // WebApi retrieve failed (record deleted, no permission, transient network).
    // Fail gracefully — open Compose in empty-state so the user still lands on a
    // working surface.
    console.error(
      "[DocumentComposeLaunch] retrieveRecord failed for sprk_document",
      normalizedDocumentId,
      err,
    );
    openSpaarkeAiCompose({
      entityLogicalName: "sprk_document",
      entityId: normalizedDocumentId,
      sprkDocumentId: normalizedDocumentId,
    });
  }
}
