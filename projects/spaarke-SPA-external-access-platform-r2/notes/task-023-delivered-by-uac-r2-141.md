# Task 023 (lazy contact attribution) — delivered by unified-access-control-r2 task 141

> Recorded 2026-10-01 by `unified-access-control-r2` task 141 (GitHub #1064), branch `task/uac-r2-141`.

## What was delivered, and where

SPA-r2 task 023 was never built (no `ContactAttributionService` exists). UAC-r2 task 141 built the mechanism as part
of the workforce identity-binding rewrite (defect C7):

- A workforce caller binds to a contact by **Entra oid** in `contact.sprk_externalobjectid` — the field this
  project's spec assumption (spec.md:224) widened to "CIAM or workforce oid". One field for both planes, plus a
  plane marker `contact.sprk_identityplane` (External / Workforce).
- **Exactly one contact per oid** — decided by the UAC-r2 owner (round 4 item 4, 2026-10-01, "B2"): field-level
  security stays on `sprk_externalobjectid`, and the alternate key `sprk_ExternalObjectIdUniqueKey` lives on an
  unsecured mirror `contact.sprk_externalobjectidkey` that the BFF writes with the same oid in the same request as
  every bind and create (Dataverse refuses a key on a field-secured column). Creation is create-only through that
  key, so two racing first sign-ins leave one contact. Nothing resolves by the mirror. Until the schema gate runs,
  contact CREATION denies (`contact_create_unavailable`); two contacts on one oid always deny.
- **Delivery status**: the code is delivered on the UAC-r2 branch; it is LIVE only after UAC-r2 141's manual gates
  (schema, `acct` claim, tenant setting, deploy — owner-approved, run by the UAC-r2 main session). Until then a
  SPA-r2 consumer must treat the requester contact as possibly null (see below).
- After the first bind, resolution is by oid only; email is never consulted again.
- The writer is `ContactIdentityBinder` (`src/server/api/Sprk.Bff.Api/Infrastructure/ExternalAccess/`); the
  consumer contract is `projects/unified-access-control-r2/notes/141-link-contract.md`.

## FR-11 wording superseded (owner decision)

FR-11 said a contact is created on the FIRST ATTRIBUTED ACTION and "no Contact is created merely by having
access". The owner decided otherwise on 2026-09-30 (UAC-r2 `session27-owner-decisions-and-research.md`, round 2
item 4 and Q3): **a contact keyed by the oid is created at the FIRST SIGN-IN of a member of a configured customer
workforce tenant** (`tid` in `WorkforceIdentity:CustomerTenantIds`, `acct` = 0), when no active contact carries
their email. The FR-11 "no Contact merely by access" wording no longer holds.

What still holds from FR-11's intent: **creating the contact grants nothing.** Access comes only from Dataverse
(systemusers) or explicit grants (contacts). A guest, a caller from another tenant, a token without `acct`, and an
app-only token never get a bind or a creation.

The cross-plane collision this task's escalation named was answered by the owner: **refuse and flag** — never merge,
never re-bind.

## What stays with SPA-r2

The **P3 intake-submit requester wiring** is SPA-r2's, as the first consumer of this mechanism:

- Take the requester contact from the resolved workforce principal (`WorkforcePrincipalResolution.ContactId`, which
  is set for both a licensed user with a link and a Type-2 member bound/created at sign-in).
- A null contact is a normal outcome (e.g. a flagged collision). Do not resolve a contact by email, and do not
  write `sprk_externalobjectid` / `sprk_primarycontact` — both are field-secured to the BFF writer.
