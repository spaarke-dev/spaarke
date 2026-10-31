# Round 122: a linked `.eml` archive's access matches its communication

Owner (round 122, 2026-10-22): "the access should match the 'parent'". The case: an email-derived document saved as own-Restricted because its email could not be identified at save (#2177, task 130). When it is later linked to exactly one communication, its access follows that communication.

## 1. Facts (origin/master, 2026-10-22)

- **The one save-time writer.** `OfficeDocumentPersistence.ApplyEmailCommunicationOutcome` (`Services/Office/OfficeDocumentPersistence.cs:126-145`) handles every email-derived create: the `.eml`, each attachment child, a single attachment, and the worker fallback.
  - With no communication, it writes `sprk_accesspermission = Restricted` and `sprk_accessinheritance = AccessInheritance.OwnNarrowing(Restricted)` (empty `Parents`, floor Standard, own Restricted).
  - `DocumentCreateStagesCommunicationFieldsGuardTests` fails if any other writer assigns `AccessInheritanceRecord`, so a marker added there has one writer by construction (word-add-in, through R3).
- **Today a save-time Restricted and a hand-set one cannot be told apart.** After the first reconcile (`ParentLineage.cs:719-735`) both are "own = Restricted with real parents".
- **The stored value** is `max(own, inherited)` (`ParentLineage.DecideDocument`, `:687`).
  - Inherited is the most restrictive of the filing tops (`InheritedAccessPermission.ResolveAsync`).
  - `sprk_relatedcommunication` is already a filing parent (`DocumentLinkFields`, `SecureChildLineage.cs:109-117`), and the walk continues to the communication's own tops.
  - So once a document is linked, its inherited floor already equals the communication's. Only the own Restricted keeps it Restricted.
- **The link writers** (`CrossPathLink.FindAndLinkArchiveDocumentsAsync`; `EmailArchiveCommunicationLinkSweepJob.LinkAsync`) write `sprk_relatedcommunication` only. The reconciler re-derives the stored value within its cycle (at most 5 minutes).
- **D7** (`ExternalDocumentNarrowing`) already decides the internal-only leg at read time through the first link on the document, or on its parent-document chain within 3 hops. Only `sprk_accesspermission` is open.
- **Attachment children** are linked to the `.eml` through `sprk_parentdocument`, not to the communication. `CrossPathLink` links only the `.eml`, and the sweep only `.eml` rows without a parent.
- **Dev:** 0 unlinked own-Restricted email archives (190 census). Re-count at build time.

## 2. Design (option a, extended to children)

1. **Marker.** Add a field `OwnProvisional` (bool, default false) to the `AccessInheritance` record. Bump the record version; `Parse` reads older versions as `false`. Only `ApplyEmailCommunicationOutcome` sets it, through a named factory `AccessInheritance.OwnNarrowingUntilEmailIdentified(Restricted)`. No schema change: the record is JSON in the existing column.
2. **Release rule, one place.** In `AccessInheritance.OwnPermissionOf` (the one copy of the round 87 rule), a provisional own narrowing counts as **no own value** when the document's communication walk finds a link. That is the same walk D7 uses: the document's own `sprk_relatedcommunication`, or the first one up `sprk_parentdocument` within 3 hops. A cycle or an unreadable step keeps the narrowing (fail closed).
   - **Result:** stored = the inherited floor = the communication's tops, which matches the communication. Children follow once their `.eml` is linked.
3. **Hand edits keep round 87.** A user edit of `sprk_accesspermission` is classified "outside the writers" and recorded as a new own value without the marker, so a hand-set Restricted stays. `OwnProvisional` must also clear on any own-value change other than the release.
4. **The reconciler persists it.** When the release applies, `DecideDocument` writes the lowered value and a record with `OwnProvisional` false and own empty. Release is one-way.
5. **Link writers unchanged.** They keep writing only the link; the reconciler lowers the value within its cycle. Until then the document stays Restricted, which fails closed.
6. **Legacy rows:** none on dev. Other environments get #2177 and this change together, so no backfill is needed. Verify on dev before the build.

## 3. Tests (must-fire and must-not-fire)

- A provisional `.eml` that gets linked follows the communication's tops: Restricted → the inherited value.
- An attachment child of a linked provisional `.eml` is released through `sprk_parentdocument` (1, 2 and 3 hops).
- The walk at 4 hops, a cycle, or an unreadable step keeps the Restricted.
- A **hand-set** Restricted (no marker) on a linked archive stays Restricted (must-not-fire).
- A provisional row with no link stays Restricted.
- A provisional row edited by hand to Restricted again keeps it after linking.
- Records of older versions parse as not provisional.
- The guard: `ApplyEmailCommunicationOutcome` stays the one writer of the factory.
- D7 is unchanged: a released row under an internal-only communication is still hidden from partners.

## 4. Ownership

- **UAC builds:** the record field and factory, `OwnPermissionOf`, the `DecideDocument` persistence, and the tests.
- **word-add-in reviews** the one-line change in `ApplyEmailCommunicationOutcome` and their I-19 guards before merge, as they asked. They may also build that line.
- **CLAUDE.md §11:**
  - Existing: the `AccessInheritance` record and the round 87 rule.
  - Extension: a field on that record, so no new column or service.
  - Cost of doing nothing: owner round 122 is unmet, and a linked email stays Restricted forever.

## 5. Built (PR #2234, merged 230302c307, 2026-10-22)

- **Reviews:** word-add-in approved its scope, and its F1 is fixed. Two UAC adversarial passes ACCEPT. Fix round 1 covered word-add-in F1, a failed walk that was never retried, the field-security check now enforced in code, the copy constructor dropping the marker, and the docs. Fix round 2 covered "not secured" as a refusal that is not carried, the marker kept only for the same own value, and the doc notes.
- **Checks:** BFF and sln builds 0/0. ArchTests 1352/1352. Both integration suites have 0 failures. Publish +10,412 B. All mutants killed.
- **Known limits:** recorded in I-19 and the PR body (K2, K4, K5, K6, the single-attachment case, and `chain_own_narrowing`). A BFF restart is needed after reverting the field-security script.
- **Deploy:** batched with the #2237 fix through R3, with the owner's go. Dev has the precondition (`Set-DocumentNarrowingFieldSecurity.ps1` applied) and 0 affected rows.
- **Server gate after deploy:** the `secure-child-reconciliation` run report shows `releaseRefusedLinkColumnsNotSecured = 0` and no new exception class.

## 6. Deployed and gated (2026-10-22)

The batch went to dev in one R3 deploy, approved by the owner in R3's session: master **1283c41817**, replacing a7b956fb55 (snapshot `deploy/rollback/wwwroot-20261022T124709Z`). ProductVersion read back at 12:50:18Z; healthz and healthz/dataverse returned 200.

**What it contains:**
- #2234 (round 122)
- #2239 (#2237)
- 018a
- #2244 (#2240)
- #2249 (#2247)
- #2248 (#2246)

**Server gate, 12:50:18Z → 13:20Z** (App Insights `spe-insights-dev-67e2xz`, role `spaarke-bff-dev`; script in the session scratch area, read-only): **PASS**.

| Check | Result |
|---|---|
| `[CHILD-ACCESS-PERMISSION]` runs | 17; `releaseRefusedLinkColumnsNotSecured` max 0, `failed` max 0, `failure` always `(null)`, 0 faults |
| `[DOC-PROFILE]` warnings | 0 |
| `*_server_only` refusals (`document_bff_column`, `owner`, `creator`, and the rest) | 0 |
| `attachment_item_*` refusals | 0 |
| New exception types | none. The only exceptions are 6 Graph `ODataError` "Invalid hostname for this tenancy" (#1830 / ISS-006), at the pre-deploy rate of 288 in 24 h |
| R3's 30-minute reading (12:49:00Z → 13:20:13Z) | clean: no new class, the same counts, the 10 secure-child-reconciliation runs kept all Succeeded |

**Not exercised in the window:** partner document reads, wizard uploads and email archives. Owner browser checks stay on the checklist.

**#2248's clients, redeployed to dev with the owner's go (2026-10-22), built from 1283c41817:**
- **CommunicationActions PCF:** solution 1.3.20 → **1.3.21**, through `Import-SolutionScoped.ps1`, publishing only its two bundle resources. Master's sources still say 1.3.20, so the next deploy of this control starts at **1.3.22**. The bundle also carries master's "Internal Only" tag work (task 068, 85d1d26f66), which dev did not have.
- **`sprk_emailpage`:** deployed.
- **`sprk_spaarkeai` (Console):** deployed. LegalWorkspace has no web resource of its own: its sections, including EmailWorkspace, ship inside `sprk_spaarkeai`.
- **Read-back:** for all three, the SHA-256 of the Dataverse record and of the served copy equals the build, and both forms of the notice are present.
- **Ancestry:** the bundles carry no build stamp. Every live bundle's identifiers were a strict subset of the build's, so nothing newer was overwritten.
