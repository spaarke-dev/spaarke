# 061 UAT Round 3 — Core-only auto-association (owner rule, 2026-07-31)

## Owner feedback (verbatim intent)
> "the association picked up two 'matters'; also auto filed to the contact BUT why? … update the
> association rule so that we are only auto associating to our core records (matters, projects) and not
> auto associating to contacts, organizations, invoices, etc. these can be suggestions and can be
> associated by the user but never auto associated."

Owner-selected core auto-file set (AskUserQuestion, 2026-07-31): **matter + project + service request**
(work-assignment intentionally NOT core → suggest-only).

## Diagnosis (from live provenance, comm `cfd3f282-938c-f111-8076-000d3a98755b`)
- Two matters conflicted at 0.999 / 0.99 → **Ambiguous**, matters correctly withheld ("Needs your decision"). ✅
- `sprk_regardingperson = Ralph Schroeder` was **`written: true`** — reinforced from ParticipantCorrelation
  (sender, 0.70) + ThreadContinuity (0.65) + ContactNameMatch (0.68) → 0.966. Because a contact field is
  non-conflicting, `AddWrites` persisted it → r5 rendered written = **"Filed automatically"**. That was the bug.
- Root cause: the write-set gate keyed off "substantive vs fallback" (fallbacks = contact/org/account were
  still WRITTEN, just not auto-file-eligible). Invoices/SRs/etc. were fully substantive → written.

## Fix — write-set + auto-file gate by CORE ENTITY TYPE
Single chokepoint: `AssociationStatusMapper`. Superseded the "fallback field" concept with a
config-driven **core-writable-entities** gate:
- **`AddWrites`** skips any field whose winning target entity ∉ core set → non-core becomes candidate-only
  (surfaced in provenance `written:false`, never persisted). Parallel to the existing `IsSurfaceOnly(rung)` skip.
- **Auto-file eligibility** keys off the top *core* deterministic winner (`topDetCore`, replacing
  `topDetSubstantive`) → invoice/SR-off-list/contact/org can never drive `Resolved`.
- Core set is **config-driven** (`Communication:AutoFile:CoreWritableEntities`, default
  `["sprk_matter","sprk_project","sprk_servicerequest"]`) via `AutoFileGate` (ADR-018) — retunable without a
  redeploy; per-tenant override supported.

The resolver (`IncomingAssociationResolver`) needed **no change** — it writes only `decision.RegardingWrites`,
so removing non-core from the mapper's write-set is the sole source of truth. Outbound `SendAsync`
(caller-supplied `associations`) is a different path and is unaffected — a user can still explicitly attach any
record type when composing.

## What the owner will now see
- Two matters → "Needs your decision" (unchanged, correct). ✅
- Ralph Schroeder (contact) → **"Suggested · confirm"** (was "Filed automatically"). ✅
- An attached invoice's invoice, a sender's org/account → Suggested candidates, never auto-filed. ✅
- A matter / project / service request at rung 0/1 ≥ 0.85 → still auto-files (Resolved). ✅

## Files
- `src/server/api/Sprk.Bff.Api/Configuration/AutoFileOptions.cs` — `CoreWritableEntities` (+ tenant override).
- `src/server/api/Sprk.Bff.Api/Services/Communication/Engine/AutoFileGate.cs` — resolve core set into `AutoFileSettings`.
- `src/server/api/Sprk.Bff.Api/Services/Communication/Engine/AssociationStatusMapper.cs` — `IsCoreWritable` gate; `topDetCore`; `AddWrites` core skip.
- Tests: `AssociationStatusMapperTests` (+4 new incl. exact UAT-round-3 regression; flipped the old "contact still written" tests) · `IncomingAssociationResolverTests` (flipped 5 non-core-write assertions to candidate-only + provenance-surfaced).

## Verification
- BFF build clean (0 err). Mapper + resolver tests: **51/51 pass**. Publish ~47 MB compressed (Δ≈0 vs baseline); no new package / CVE.
- 5 pre-existing sender-identity baseline failures unchanged (branch debt, `notes/wave2-review-findings.md` — not r1's).

## Open follow-up (not built)
- The same "surface-don't-write" principle for RecordNameMatch/ContactNameMatch was already in place (those
  never auto-file); round-3 extends non-write to ALL non-core entity types regardless of rung. Golden
  end-to-end regression using the UAT emails should be added at 090.
