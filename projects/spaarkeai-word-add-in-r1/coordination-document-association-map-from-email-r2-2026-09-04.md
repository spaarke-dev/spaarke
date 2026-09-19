# Coordination — Document association map ↔ `EntityAccessFilter` (finding + outcome from email-communication-intelligence-r2)

> 🔴 **CORRECTION APPENDED 2026-09-04 by `spaarkeai-word-add-in-r1`** — §3, §4.1 and §1 contain factual errors about `sprk_document`'s schema, verified against live Dataverse metadata via MCP. **§3's "mappable set" is wrong and must not be used as authoritative.** See [§0 Correction](#0-correction-appended-by-spaarkeai-word-add-in-r1-2026-09-04) immediately below before reading further. The §2 outcome (one consolidated map) and the §3 *lockstep invariant itself* remain sound — it is the schema facts underneath them that are wrong, and the invariant is currently **violated by `event`**.

---

## 0. Correction (appended by `spaarkeai-word-add-in-r1`, 2026-09-04)

Verified against live `sprk_document` metadata (Dataverse MCP, 2026-09-04) and corroborated by the maker-portal Columns view. `sprk_document` has **two** lookup families:

- **Direct — FOUR only**: `sprk_matter`, `sprk_project`, `sprk_invoice`, `sprk_workassignment`
- **Related — TWELVE**: `sprk_relatedagreement`, `sprk_relatedcommunication`, `sprk_relatedcontact`, `sprk_relatedevent`, `sprk_relatedinvoice`, `sprk_relatedmatter`, `sprk_relatedorganization`, `sprk_relatedproject`, `sprk_relatedservicerequest`, `sprk_relatedtodo`, `sprk_relatedvendororg`, `sprk_relatedworkassignment`

Against that, four claims in this document are wrong:

| § | Claim | Reality |
|---|---|---|
| §3 | "lookup columns exist for matter, project, invoice, workassignment, **event**" | ❌ **There is no `sprk_event`.** `SELECT sprk_event FROM sprk_document` returns `'sprk_Document' entity doesn't contain attribute with Name = 'sprk_event'`. Only `sprk_relatedevent` exists. |
| §3 | "`sprk_todo` … **unmappable** — a document cannot be associated to a to-do at all … **needs a schema change first**" | ❌ **`sprk_relatedtodo` exists.** True only of a column literally named `sprk_todo`. No schema change is required; the *direct* family simply has no `sprk_todo` member. |
| §4.1 | "`sprk_document` has **no account/contact lookup column**" | ⚠️ Half right. **`sprk_relatedcontact` exists.** `account` is correct — there is no account lookup at all (`sprk_relatedorganization`/`sprk_relatedvendororg` point at `sprk_organization`). |
| §1 | "maps an association token → the **`sprk_related{recordtype}`** lookup column to write" | ❌ The code writes the **direct** columns (`DataverseServiceClientImpl.cs:905-916` → `sprk_matter`, `sprk_project`, …). Nothing in the Office save path writes any `sprk_related*` column. This document describes one family and the code it documents writes the other. |

### The consequence: §3's own invariant is violated by `event`

§3 states the rule correctly — *"a type belongs in **both** maps or **neither**"* — and then names `event` as safe. But `event` is in **both** maps (`DocumentAssociationMap.TryApply` returns `true`; `EntityAccessFilter.EntitySetByType:126-127` authorizes it) while having **no direct column**. `DataverseServiceClientImpl.cs:916` writes `document["sprk_event"]` against an attribute that is not on the entity. Filing a document to an Event is therefore authorized and then cannot associate — precisely the failure mode the invariant exists to prevent.

The likely mechanism: the 2026-09-03 verification checked the `sprk_related*` family (where `sprk_relatedevent` **does** exist) while the code writes the direct family — the same family confusion visible in §1.

### Ownership

This is **`unified-access-control-r2`'s** to fix (its Q4 widening introduced the `event` entry, and `EntityAccessFilter` is its component), not `spaarkeai-word-add-in-r1`'s. r1 has corrected its own tasks 026 and 035 so they do not inherit the false premise, and reads `sprk_relatedevent` — never `sprk_event`.

Also affected: §4.2 repoints `RecordKeyedUploadAuthorizationTests`' deny-path example at `sprk_todo` as "genuinely unmappable". Given `sprk_relatedtodo` exists, that example rests on the same false premise and should be re-examined.

---

> **From**: `email-communication-intelligence-r2` · **To**: `unified-access-control-r2`
> **Date**: 2026-09-04 · **Commit**: `f85796f70` (`fix(documents): one association map — stop silently unassociating documents`, merged to master 2026-09-03)
> **Why you (UAC-r2) care**: the fix modified **`Api/Filters/EntityAccessFilter.cs`** (your access map, `EntitySetByType`) and surfaced a **lockstep invariant** between that access map and the document association map, plus two live authorization gaps on the record-keyed upload path. This complements the "association slots" + "upload-collision data-loss" facts already in your notes.

---

## 1. The finding — four drifted copies of the association switch → silent unassociation

When a `sprk_document` is saved/filed, code maps an **association token** (the record type it's filed to) → the `sprk_related{recordtype}` lookup column to write. There were **four independent copies** of that switch, and they had drifted on which spelling they accepted:

| Copy | Accepted | On mismatch |
|---|---|---|
| `UploadFinalizationWorker` | only **friendly** (`"matter"`) | warn-and-continue |
| `OfficeDocumentPersistence` | friendly **and** logical | warn-and-continue |
| `EmailAttachmentProcessor` | only **logical** (`"sprk_matter"`) | warn-and-continue |
| `RecordMatchEndpoints` | only logical | **fail-closed** (the only one) |

**The drift *was* the defect.** The same token resolved a lookup in one path and **silently vanished** in another purely by which spelling that copy listed — and three of the four logged a warning and carried on, so the document was created **with no association and no error**. The user believes it's filed; it isn't. Widening three of them separately (the original plan) would have added a *fifth* divergence.

---

## 2. The outcome — one map, both spellings, caller-owned miss behavior

- **`Spaarke.Dataverse.DocumentAssociationMap`** — a single map accepting **both** spellings for every supported type, returning a **bool** so each caller keeps its own correct miss behavior (workers log loudly; the request handler still `400`s). All four call sites now route through it.
- **Now wired end-to-end** (the columns already existed; only the code was missing — which is exactly why the gap was invisible): `UpdateDocumentRequest` gains `WorkAssignmentLookup`/`EventLookup`, `DataverseServiceClientImpl` writes them, the Office save endpoint + `AssociationType` enum accept them.
- **22 new tests** (`DocumentAssociationMapTests`) assert **both spellings per type** (the friendly/logical split is exactly what drifted, so it's not spot-checked), that unsupported types write nothing, empty GUID is refused, and applying one type leaves the others null. Perturbation-checked.
- **Verification**: `dotnet build Spaarke.sln` green; BFF unit suite **11,794 passed / 0 failed**; ArchTests **182/182**. No `.csproj`/package change (publish-size delta nil).

---

## 3. The invariant that is yours to keep — access map ↔ association map lockstep

**`EntityAccessFilter.EntitySetByType` (your access map) and `DocumentAssociationMap` must stay in lockstep.** A type authorized for the **record-keyed upload route** but with **no `sprk_document` association column** authorizes an upload that *can only ever land unassociated*. So a type belongs in **both** maps or **neither** — never just the access map.

This is why **`sprk_todo` is deliberately in NEITHER map.** Verified against live `sprk_document` metadata: lookup columns exist for **matter, project, invoice, workassignment, event** — but there is **no `sprk_todo` column**. Todo is not "not-yet-mapped", it is **unmappable** — a document cannot be associated to a to-do at all. Widening the access map for it would authorize a route whose upload could only land unassociated. **Needs a schema change first, not a code change.**

> This directly updates the "association slots" fact in your notes: the mappable set for `sprk_document` is **{matter, project, invoice, workassignment, event}** — and `sprk_todo` is a decoy that must stay out of `EntitySetByType`.

---

## 4. Live gaps found on the way — NOT introduced, and deliberately NOT silently changed (owner decisions)

These are authorization-surface gaps that intersect UAC-r2's record-keyed upload path — flagged for your tracking, left as owner decisions:

1. **`account` / `contact` are accepted but unmappable.** The Office save endpoint accepts `account` and `contact`, and `sprk_document` has **no account/contact lookup column** — so **every save filed to one is persisted unassociated today.** Left *accepted* (rather than rejected) because refusing them changes a user-visible flow → owner decision (add the columns, or reject the type). The drop is now **logged loudly** at all three persistence sites and documented at the endpoint. **Client mirror**: `DocumentUploadWizard`'s `ENTITY_CONFIGS` also claims `sprk_account`/`sprk_contact` lookups that don't exist — same gap, client side.
2. **Your own deny-path test moved.** `RecordKeyedUploadAuthorizationTests` used `sprk_workassignment` as its example of an **UNMAPPED** entity — but this change *mapped* it, so the deny-path test stopped testing the deny path. It was **repointed at `sprk_todo`** (a better example: genuinely unmappable, not merely not-yet-mapped). If UAC-r2 touches that authorization suite, keep the unmappable example on `sprk_todo`, not `sprk_workassignment`.

---

## 5. Reference to updated documentation

| Topic | Where |
|---|---|
| **Content identity & deduplication** (the sibling "how a saved document is wired" concern — item/content/message dedup, graduate-on-divergence) | **NEW** canonical doc: `docs/architecture/content-identity-and-deduplication-architecture.md` (on master 2026-09-04) |
| Office add-in save path (feeds this association surface) | `docs/architecture/office-outlook-teams-integration-architecture.md` (refreshed 2026-09-04) + `src/client/office-addins/CLAUDE.md` |
| The association map itself | **Code-canonical** — `Spaarke.Dataverse.DocumentAssociationMap` + `DocumentAssociationMapTests` + commit `f85796f70`. **Not yet in a docs/ architecture file.** |

**Suggestion for UAC-r2**: the **access-map ↔ association-map lockstep invariant** (§3) is an *access-control* rule and `EntityAccessFilter` is your component — it belongs in a UAC access-control doc, not in the document/dedup docs. Consider folding §3 (plus the `sprk_todo`/`account`/`contact` unmappable set) into your own access-model documentation so the "authorize a route only if the document can actually be associated" rule has a home. Happy to help draft that line if useful.

---

## 6. One-line summary

Filing a document to a record was governed by **four drifted copies** of the type→lookup switch, silently unassociating documents; it's now **one `DocumentAssociationMap`**. For UAC-r2 specifically: **`EntitySetByType` must never authorize a type the document can't be associated to** — `sprk_todo` (unmappable) stays out of both maps, and `account`/`contact` are an open owner decision (add columns or reject).
