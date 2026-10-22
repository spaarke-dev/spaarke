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
