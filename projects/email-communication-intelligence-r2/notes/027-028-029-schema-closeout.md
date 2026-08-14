# Pillar C dedup schema — closeout (tasks 027 / 028 / 029)

> 2026-08-06. Gated schema batch. Operator created the columns manually (dev uses UNMANAGED
> solutions — see memory `dev-uses-unmanaged-solutions`); Claude verified live + rewired 029.

## Outcome

All Pillar-C dedup schema is in place and **verified live in spaarkedev1** against the merged code's
attribute-name contract:

| Task | Field | Live result | Code contract |
|---|---|---|---|
| 028 | `sprk_communication.sprk_deliveredmailboxes` | Memo ✓ | `DeliveryContextMerge.DeliveredMailboxesAttribute` |
| 028 | `sprk_communication.sprk_savedbyusers` | Memo ✓ | `DeliveryContextMerge.SavedByUsersAttribute` |
| 027 | `sprk_document.sprk_canonicaldocument` | Lookup → `sprk_document` (self) ✓ | `ContentDedupDetector` / `ComposeService.CanonicalDocumentAttribute` |
| 029 | `sprk_document.sprk_relatedcommunication` | Lookup → `sprk_communication` ✓ | `CrossPathLink.LinkedCommunicationAttribute` (rewired) |

## 029 — resolved by REUSE, not a new column (operator §11 challenge)

The operator flagged that `sprk_document` already has **`sprk_relatedcommunication`** (Lookup →
`sprk_communication`, the sibling of `sprk_relatedmatter`/`sprk_relatedproject` = "the confirmed
related record this document points at"). Verified: it exists, targets `sprk_communication`, and has
**zero code consumers** (grep) — so reusing it for FR-C4 is clean and is the exact intended semantic.
Creating a parallel `sprk_linkedcommunication` would have been the §11 violation. The 029 POML's
`<existing>` justification ("No lookup on sprk_document targets sprk_communication") was simply wrong.

**Action:** rewired `CrossPathLink.LinkedCommunicationAttribute` value `sprk_linkedcommunication` →
`sprk_relatedcommunication` (const NAME kept — feature-scoped to the FR-C4 link; STORAGE is the
existing field). Updated caller comments (`OfficeDocumentPersistence`, `IncomingCommunicationProcessor`)
and test comments/strings. No new column created.

Minor note for the operator: FR-C4 now treats `sprk_relatedcommunication` as system-owned (it
overwrites the lookup on link if it points elsewhere). It mirrors the auto-populated
`sprk_relatedmatter`/`sprk_relatedproject`, so this is consistent — but if manual maker use of that
field was intended, flag it.

## Verification

- BFF build: 0 errors.
- `CrossPathLink` + `OfficeDocumentPersistenceDedup` + `CommunicationIntegration` tests: **36 passed / 0 failed** (3 pre-existing skips).
- Live schema: all 4 attributes confirmed present with correct type + lookup target.

## Removed artifact

`scripts/Deploy-EmailIntelligenceR2-DedupSchema.ps1` (staged earlier this session) was DELETED — it
assumed managed-solution-in-dev (wrong; dev is unmanaged) and the operator creates schema manually.
The durable record is this note + the POMLs. Regenerate correctly if a fresh-environment artifact is
ever needed.
