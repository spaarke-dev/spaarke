# Punch #7 — "Organization grant = everyone in the org" — design analysis + escalation

> Owner ask (2026-08-11): "adding Organization grants everyone associated to that organization access"
> (NOT the current firm-scope-metadata behavior). This note captures the investigation and the ONE
> decision the owner must make before any BFF/schema code is written. **No code written for #7 yet.**

## TL;DR — why this is blocked on an owner decision

Making "org grant → everyone in the org" work is **not just code** — it hits a **data-model gap**: there is
**no `contact → sprk_organization` relationship** in Spaarke today, and the grant table stamps a *different*
org entity than the one the system can actually enumerate members from. That mismatch must be resolved first.

## What exists today (verified)

- **Grant model is strictly per-contact.** One `sprk_externalrecordaccess` row = one Contact + one root
  (Project/Matter/WorkAssignment). `sprk_Contact` is mandatory; there is no contactless/org-level grant row.
  (`GrantExternalAccessEndpoint.cs:279`, `:69-70`.)
- **`sprk_Organization` on the grant is decorative.** Written when `OrganizationId` is supplied
  (`GrantExternalAccessEndpoint.cs:300-306`) but **never read** by the access check
  (`AccessibleRecordSetService.cs` never reads it). It is firm-scoping metadata / reporting only — exactly the
  "firm-scope metadata" described to the owner. Confirmed in `task-070-deviations.md:45-58`.
- **Three DIFFERENT "org" concepts, none of which is contact→sprk_organization:**
  - **(A) OOB `account` via `contact.parentcustomerid`** — the ONLY contact-anchored org link the code can
    query. "Members of account X" = `contacts?$filter=_parentcustomerid_value eq {accountId}`.
    (`ExternalDataService.cs:384,395-407`.)
  - **(B) Custom `sprk_organization`** (the law-firm/vendor entity) — what the grant's `sprk_Organization`
    lookup + the modal's "Add organization" picker point to. **It has NO reverse contact membership** — no
    `contact.sprk_organization` lookup exists in the schema. It is only ever a lookup *target*.
  - **(C) `sprk_organization → systemuser` membership lookup** (configurable, default OFF, ignores contactId)
    — used for the AI membership resolver, not usable for "contacts of an org".
- **The mismatch:** the grant/picker use **`sprk_organization`** (firm entity, no contact members); the only
  queryable "who belongs to this org" is **OOB `account`** via `parentcustomerid` — a *different entity*.

## The runtime-union template (standing-grant) — the pattern we'd mirror once the model is fixed

`AccessibleRecordSetService.ComposeForContactAsync` (`:244-301`): a contact's accessible records =
Term 1 explicit grants ∪ Term 2 standing-grant runtime membership. Standing grant = a flag on the contact →
live membership union, **zero per-record rows**, revocable by flipping the flag. An org grant should add a
**Term 3**: contact → its org → org-grants on records → union those record IDs. The insertion point is right
after Term 2 (`AccessibleRecordSetService.cs:286`); fold the org filter into the existing participation query
(`ExternalParticipationService.cs:397-398`, widen `$filter` + add `_sprk_organization_value` to `$select`,
bump `CacheVersion`) to keep it **one query / one cache entry** on the authz hot path.

## THE DECISION the owner must make: which org model?

**Option 1 — "Org" = OOB `account` (via `contact.parentcustomerid`).** Zero new relationship needed for
"members": members = contacts whose `parentcustomerid` = that account. BUT: (a) the grant's org lookup +
the modal picker currently target `sprk_organization`, so they'd change to `account`; (b) requires that every
external contact's `parentcustomerid` is actually set to their firm's account record.
- ✅ Uses an existing, queryable relationship; least schema work.
- ❌ Changes the org entity from the firm entity (`sprk_organization`) to OOB `account`; depends on
  `parentcustomerid` being populated + accounts representing firms.

**Option 2 — Keep `sprk_organization` (firm entity) + ADD a `contact → sprk_organization` lookup.** Add a new
lookup on `contact` (e.g. `sprk_organizationid`) so each external contact belongs to a firm; members =
`contacts?$filter=_sprk_organization_value eq {orgId}`.
- ✅ Keeps the firm entity that the grant table + picker already use; clean semantic ("contact belongs to firm").
- ❌ Schema change (new lookup) **+ a data step**: every existing external contact must be assigned its firm
  before org grants do anything. New contacts must get it set (field-mapping / intake).

**Option 3 — Fan-out instead of runtime union** (write a per-contact grant per current member on "Add org").
Reuses the per-contact machinery unchanged, but: new firm members joining later DON'T inherit; revoke = delete
N rows. Still needs a members-of-org query (so still needs Option 1 or 2's relationship). Not recommended vs.
runtime union.

## Recommendation

- **Model:** **Option 2** if "firm" is the durable concept (it matches the existing `sprk_organization` firm
  entity the picker already uses) AND the owner is willing to populate a contact→firm lookup (a one-time data
  step + intake wiring). **Option 1** if external contacts already carry `parentcustomerid` = their firm's
  account and the business treats "account" as the firm.
- **Mechanism:** **runtime union** (Term 3), mirroring standing-grant — new members auto-inherit, single-row
  revoke, no stale rows. Fold the org filter into the existing participation query; bump cache version;
  org-revoke either fans out per-member cache invalidation or relies on the 60s TTL.
- **Store:** an org-level grant row on `sprk_externalrecordaccess` with `sprk_Organization` set and a new
  "grantee type = organization" marker so `sprk_Contact` can be empty for these rows (small schema change:
  relax contact-required + a discriminator), OR a dedicated small org-grant table. Decide alongside the model.

## FINALIZED DESIGN (2026-08-11 — owner chose the junction model; building now)

**Membership model (owner-built):** `sprk_contactorganization` junction (N:1 → contact, N:1 → sprk_organization),
subgrids on both forms, `statecode` = active/former gate. Query fields: `_sprk_contact_value`,
`_sprk_organization_value`, `statecode`.

**Org-grant STORAGE — reuse `sprk_externalrecordaccess` (no new table):** an org grant = a row with
`sprk_Organization` set + `sprk_Contact` **empty** + one root lookup + `sprk_accesslevel`. Type is INFERRED
from empty contact (a per-contact grant ALWAYS has a contact, so "contact empty" uniquely = org grant — **no
discriminator column needed**). Reuse means Current-Access read + revoke-by-id already work.

**⚠️ OWNER SCHEMA PREREQUISITE (the one thing needed before org grants can be written):** on
`sprk_externalrecordaccess`, set the **`sprk_Contact` lookup to Optional** (RequiredLevel = None). Today it's
`Required=Yes` in the schema doc, and the BFF also hard-rejects empty ContactId in code (which I'll relax).
Both must change; the Dataverse field-required-level is the owner's one action (it can't be empty otherwise).

**BFF changes (this project, hot-path §10):**
1. WRITE — `GrantExternalAccessEndpoint`: relax the `ContactId == Guid.Empty` guard to allow an org grant when
   `OrganizationId` is present + contact empty; `BuildGrantPayload` omits the `sprk_Contact@odata.bind` for
   org rows (keeps `sprk_Organization` bind + root + level).
2. READ (Term 3) — `AccessibleRecordSetService.ComposeForContactAsync` after Term 2 (`:286`): resolve the
   contact's ACTIVE orgs from `sprk_contactorganization` (new reader, `_sprk_contact_value eq {c} and statecode
   eq 0`), query org-grant rows (`_sprk_organization_value in {orgs} and _sprk_contact_value eq null and
   statecode eq 0`), union the granted record ids. Add an `OrganizationGrants` flag to `AccessibleRecordSetSources`.
   Bump `ExternalParticipationService.CacheVersion` (2 → 3). Fail-closed like Terms 1/2. Add the same term to
   `ComposeForSystemUserAsync` for a systemuser who is a member of a granted org (parity).
3. REVOKE — works by `AccessRecordId` unchanged (deactivates the org row); relax the revoke DTO's
   ContactId-required guard for org rows. Cache: rely on the 60s participation TTL for member fan-out in MVP
   (note the org-scoped-invalidation option as a follow-up).

**PCF modal (`AccessGrantModal` + trio):** "Add organization" + level + Add → POST `/grant` with ContactId
empty + OrganizationId set. `fetchExistingGrants` also selects `_sprk_organization_value` (+ FormattedValue);
rows with empty contact render in Current Access as "Everyone at {org}" with a Revoke button.

**Tests:** BFF unit tests for org-grant write (contact empty allowed only with org), Term-3 union (member of
granted org gets the record; NON-member does NOT; former member `statecode=1` does NOT — the negative/authz
cases), and org revoke. adr-check + code-review + publish-size before deploy.

## Scope / governance

This is **BFF access-control (hot path, §10)** + a **Dataverse schema change** + a **PCF modal change** — a
proper task (R2 P2 access-control), NOT a UAT redeploy. It's also **security-sensitive** (widens who can see a
record), so per root CLAUDE.md §6 it needs explicit owner sign-off on the model before building. Recommend
authoring it as a numbered task via `/task-create` once the owner picks Option 1 vs 2.
