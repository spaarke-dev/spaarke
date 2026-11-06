# Secure record access — "working as intended?" verification (2026-10-20)

Commissioned by owner round 106 item 1 ("let's make sure that it is not a matter of code or other wiring that is just not
working correctly"). Read-only: code on `origin/master` @ `2b07480ab`, plus GET-only reads of dev (`spaarkedev1`) as the
default `az` admin login. Nothing was changed. No Key Vault was read.

## Verdict for the owner (plain language)

1. **For outside contacts, Secure works exactly as built and intended.** Only a grant made to the contact by name opens a Secure record. Organization-wide grants, standing grants, organization membership and assigned-field access give nothing. This holds in the code and on dev.
2. **PAT-908068 on dev matches the rule.** It has two by-name grants (CIAM Test User, Ralph Dewey), one internal share (you), no organization or standing grant, its own file container, and a document that follows the matter's owner and share.
3. **No owner decision ever said "Secure = internal only".** Round 2 (2026-09-30) said the opposite, and round 106 confirmed it. The build has not drifted.
4. **One real defect, on the INTERNAL side (F1).** In the external portal and Teams, an internal user named in an "Assigned …" field on a Secure record can open it with no share, even though the model-driven app refuses them. No dev record is exposed today. Filed as #2121.
5. **One gap in the other direction (K1).** An internal user who was shared a Secure record does not see it in the portal or Teams unless an "Assigned …" field also names them. Task 036 (on hold) is the fix for both.
6. **Things that are by design but may surprise you** (N1–N6 below). For example, a contact with Collaborate or Full Access can name a colleague from their own firm, and an assigned-field grant made before the record became Secure is kept.

## Defects

| Id | Class | What | Concrete scenario |
|---|---|---|---|
| **F1** | **F (fix now)** | **Systemuser-plane membership term is not Secure-suppressed (over-grant on the SPA/Teams plane).** | Secure matter M (owned by Secure Record Owners). An operator sets `sprk_assignedattorney1` = contact C. C is the `sprk_primarycontact` of internal user U. U holds no share. The Assigned-To rule only *suggests* a share on a Secure record (`AssignedAccessMaterializer.cs:148-150, 1071`), so none is written. In MDA, U is denied: the Secure BU blocks them. In the external SPA or Teams, U signs in with a workforce token and lands on the systemuser plane. Term 1 (ADR-034 membership, app-only, lookup columns) matches `sprk_assignedattorney1` through U's linked contact and gives Read, Write and Create. U can then list M's documents, stream them app-only, and create to-dos and events. The same applies to an external-flagged licensed user (outside counsel). |
| **K1** | K (limit, tracked by task 036, owner-held) | **The SPA/Teams plane never reads Dataverse shares (under-grant).** | You hold the share on PAT-908068. No registry lookup on it names your linked contact, and its owner is the memberless team. So PAT-908068 is not in your SPA/Teams accessible set, although MDA opens it. This was inferred from code (`MembershipResolverService` reads lookup columns and ownership only; `IImpersonatedRootSetSource` is registered but not consumed) and not exercised live. It leaks nothing: it fails closed. Fixed by 036's impersonated root sets. |
| **K2** | K (documented limit) | **Service-request requester rule ignores the request's regarding links.** | External contact X submits service request SR. Staff set `sprk_regardingmatter` = Secure matter M, and later upload a memo to the SR only, not to M. X can list and download that memo, and read the SR's message thread, with no grant on M. X cannot reach M, M's own documents, or any document also filed to M (`ExternalMultiRootRule.cs:37`; `SecureChildLineage.cs:54` deliberately leaves out `sprk_servicerequest`; `ExternalProjectDataEndpoints.cs:1021-1056` checks only the requester). Design §2 declares service requests "scoped by REQUESTER, not by grant". |

**F1 fix options** (for the owner):

- **(a) Narrow interim fix.** On the systemuser plane only, drop Term 1's registry-column contributions for ids whose *effective* flags are Secure. Use `IsSecure`, not `IsDirectOnly`, because Limited governs contacts only.
  - Ownership terms are unaffected. On a Secure record they confer nothing anyway: the team is memberless, and the BU has 0 users (live).
  - ~~This removes only the over-grant. K1 is unchanged.~~ **Corrected after review (PR #2122, two adversarial passes):** it also WIDENS K1. A user who holds a Dataverse share on a Secure record and reached it on the SPA/Teams plane only through an Assigned lookup (the normal ADR-034 A4 flow: lookup → suggested share → operator confirms) loses it there; the MDA still opens it. Dev example: project `65a3fab2…`.
- **(a′) Dataverse check for the lookup-only Secure ids (review pass B).** For the few lookup-only ids on Secure records, ask Dataverse app-only `RetrievePrincipalAccess` bound to the systemuser (the pattern `NoAccessEnforcementStore.GetPrincipalRightsAsync` already uses on master, task 143) and keep the ones it admits. Closes F1 without widening K1. It is the same app-only named-principal mechanism as 036 option C, so it waits for that decision.
- **(b) Land 036** with `ExternalAccess:ImpersonatedRootSets:Enabled` on. Dataverse's own answer would then replace the membership rules for systemusers, which fixes F1 and K1 together. It is held pending the OBO decision (rounds 103–106).

Recommendation: (a) now, because it is small and closes the disclosure path regardless of how 036 resolves. Then (b) as planned.

F1 was first flagged as "P3 OVER-GRANT (plausible; not tested live)" in the session-26 merge audit (`notes/raw/session26-uac-trace-digest.txt:402`). It was never carried into a binding note or an issue. **Live:** of the 4 Secure roots on dev, the only one with a populated registry contact column (project `65a3fab2…`, `sprk_assignedattorney1`/`sprk_assignedparalegal1`) names a contact whose linked user already holds a share. So no dev user is over-granted today.

## Notable, by design (not defects, but they bear on "Secure = internal only")

- **N1. Named external contacts are allowed on Secure.** This is owner round 2 item 3 (BINDING, 2026-09-30), quoted in full below, and was reconfirmed by round 106 item 1.
- **N2. A contact can name a colleague.** A contact whose own by-name grant is Collaborate or Full Access can grant a colleague of their own organization by name, capped at their own level. Organization-wide grants are refused (`ContactGrantEndpoints.cs:20-23`; owner session 27 C4/Q2).
  - This is an external-to-external by-name grant with no internal user involved.
  - On PAT-908068 it is possible but currently moot: Ralph Dewey has no organization, and CIAM Test User's organization has no other active member.
- **N3. Assigned-To grants made before Secure are kept.** An Assigned-To auto grant to a contact that existed before the record became Secure is kept (A3). After the record becomes Secure, newly assigned contacts and users are only suggested (`AssignedAccessMaterializer.cs:148-150, 1069-1101`).
- **N4. Organization-wide grants stay as rows.** Organization-wide grant rows on a record that becomes Secure stay active in Dataverse. They are suppressed at read time, shown "No effect" in Manage Access, and confer again if the record is made un-secure (read-time rule).
- **N5. External-flagged licensed users can be shared.** A Secure, non-Restricted record may be shared with an external-flagged licensed systemuser (owner round 78, BINDING).
- **N6. A linked contact's by-name grant counts on the SPA/Teams plane only.** A systemuser whose linked contact holds a by-name `sprk_externalrecordaccess` grant on a Secure record reaches it on the SPA/Teams plane through term 2. MDA does not admit them without a share. This is an explicit by-name decision by an operator, so it fits the letter of the rule, but the two planes disagree.

## Owner-decision search (did anyone say "exclude external contacts from Secure"?)

**Searched:**

- `notes/session27-owner-decisions-and-research.md` (all rounds);
- `spec.md`;
- `design.md`;
- every `notes/**/*.md`.

Patterns: internal-only / exclude external or contacts / no external / cannot be shared with external, near "secure".

**None found.** What exists says the opposite:

- Round 2 (2026-09-30), BINDING, item 3 (`session27-owner-decisions-and-research.md:21-22`):
  > **Restricted** is a grant-access level on ANY record: no contact-based (non-systemuser) access at all; internal users unaffected.
  > **Secure** is a separate flag: a secure record MAY have contact access as long as it is not Restricted. For contacts, only named, direct grants count, which is why Secure implies Limited for contacts.
- Round 78 (2026-10-07), BINDING (`:1155-1160`): the owner's draft refusal text "Restricted and secure records cannot be shared with external users" was corrected. The owner said: "we should allow external systemusers (e.g., outside counsel who have Spaarke licenses should have access)". The copy now mentions Restricted only.
- Round 106 (2026-10-20) item 1 (`:1689`): "Secure meaning: KEEP as built. Named external contacts are allowed on a Secure record…"
- Spec FR-22 (`spec.md:85`) suppresses derived-member and org-expansion terms "for every principal kind". It never removes the direct grant.

**Conclusion:** the "internal only" belief is not recorded anywhere as a decision, so the build did not drift. The one place the build falls short of its own spec is **F1**. FR-22 says "for every principal kind", but the systemuser plane's derived-member (Assigned-field) term is not suppressed.

## Part 1 — code audit (`origin/master` @ `2b07480ab`)

Paths are relative to `src/server/api/Sprk.Bff.Api/` unless they are written out in full.

### (a) External read path — the Secure veto

**Flags.** `ExternalParticipationService.GetRootRecordFlagsAsync` (`Infrastructure/ExternalAccess/ExternalParticipationService.cs:914`) reads `sprk_issecure, sprk_accesspermission, statecode`.

- An empty `sprk_issecure` fails closed (`:977-984`).
- An unreadable row becomes `RootRecordFlags.Unreadable`, which is Secure + Restricted + Limited + inactive (`ExternalCallerContext.cs:204`).
- The flags are then folded with every parent through `EffectiveRootFlags.Fold`. A work assignment or project filed under a Secure parent is effectively Secure (`EffectiveRootFlags.cs:58`; `AccessibleRecordSetService.cs:2267-2271`).

**The predicate.** `RootRecordFlags.IsDirectOnly => IsSecure || IsLimited` (`ExternalCallerContext.cs:193`) is the one pre-max suppression predicate. `DirectOnlyPredicate` (`AccessibleRecordSetService.cs:406-407`) builds it once per composition.

**Contact plane** (`ComposeContactPlaneAsync`, `AccessibleRecordSetService.cs:2066`). It is shared by CIAM (`includeDerivedMemberTerms: false`, `:1525`) and the workforce contact (`true`, `:2049`).

- **Term 1, grants** (`:2273-2279`). On a direct-only id, `GrantedRightsFor` uses `DirectAccessLevel`, not `AccessLevel` (`:433-455`).
  - `DirectAccessLevel` is set only from rows naming the contact (`ExternalParticipationService.cs:1519, 1545, 1554`).
  - Organization-grant rows (organization set, contact null; filter at `:92-95`) carry `DirectAccessLevel = null` (`:1580, 1587, 1594`), which maps to `AccessRights.None`.
  - So an organization-wide grant contributes nothing on Secure. It is suppressed, not subtracted, so the max cannot absorb it.
- **Term 2, standing-grant membership** (`:2281-2292`): `.Where(id => !isDirectOnly(id))` at `:2290`.
- **Term 3, organization expansion** (`:2294-2306`): `.Where(id => !isDirectOnly(id))` at `:2304`.
- On CIAM, Terms 2 and 3 never run at all (`:2155, 2205`).
- **Vetoes after the max.** The deny list runs, then Restricted/inactive (`ApplyVetoPipeline`, `:559-645`; called at `:2330`).
- **Last,** `RemoveEntriesWithoutRead` (`:726, 2335`) deletes the None key that an organization-only grant on a Secure root leaves. That removal closes the old "key present = accessible" hole (task 136 C2).

**Systemuser plane, Term 2** (contact grants of the user's linked contact) uses the same `GrantedRightsFor(…, isDirectOnly)` (`:1937-1946`). So a Type 1 user's contact organization grant is suppressed too. **Term 1 (membership, `:1927-1935`) is NOT suppressed: this is F1.**

**Only a by-name grant survives.** For an external contact on a Secure root, the only non-None contribution is `DirectAccessLevel` from a row with `_sprk_contact_value` = that contact, and only while it confers: `statecode 0`, `sprk_expiresdate >= today` (`:86, 133`), and its organization, if any, active (`:266-267`).

**Who consumes the result.** `CallerPrincipalResolver` builds the CIAM principal from `ComposeForCiamContactAsync` (`CallerPrincipalResolver.cs:516-522`) and the workforce principal from `ComposeAsync` (`:713-717`). Every `/api/v1/external/*` handler reads that principal.

**Sibling routes** (service requests, invoices, to-dos, events, messages, notifications, the module data seam, `/me`, `/contact-grants`, Teams), each traced:

- All authorize through the evaluator's Read-bearing sets or `RightsOn`, plus the live-parent and secure-root checks for children.
- The single exception is the service-request requester rule (**K2**).
- Teams uses the same `/api/v1/external` group.
- The Copilot agent surface (`/api/agent`) authorizes records through `AiAuthorizationFilter`, which calls RetrievePrincipalAccess, which is Dataverse's own answer. So the Secure BU applies there.

### (b) Grant paths

**One decision function.** `ExternalGrantLifecycle.DecideGrantPolicy` (`Infrastructure/ExternalAccess/ExternalGrantLifecycle.cs:747-760`) is shared by `/grant`, `/invite-and-grant`, `/invite`, `/contact-grants` and the Assigned-To rule (all through the grant core). In order:

1. Unreadable → refuse (503).
2. Restricted → refuse every contact or organization grantee.
3. **Organization grantee on direct-only (Secure/Limited) → refuse** with `sdap.access.grant.org_grant_direct_only_record` (`:640-641, 756-757`).
4. Otherwise allow.

It reads the **effective** flags (`:783`).

**Call sites:**

- `GrantExternalAccessEndpoint.CheckGrantAsync` runs it first (`Api/ExternalAccess/GrantExternalAccessEndpoint.cs:822-825`).
- `InviteAndGrantExternalUserEndpoint` runs it before onboarding (`InviteAndGrantExternalUserEndpoint.cs:124-127`), and the core runs it again.

**What may be granted on a Secure record:**

- A named contact, at or below the grantor's ceiling.
- A Dataverse share to a user (`/share-user`).

**What may not:**

- An organization-wide grant. It is refused at write time.
- A standing grant is not a per-record object at all: it is `sprk_standinggrant` on the contact or organization, so it cannot be "created on" a record.

**Org grant written directly in Dataverse anyway** (bypassing the BFF): it is suppressed at read (see (a)). The comment at `:744` states the evaluator stays authoritative.

**UI.** `AccessGrantModal.tsx:57-58` does not render "+ Organization" on Limited/Secure. The Secure banner (`:1019-1026`) reads: "Contacts get access only through grants made to them by name; organization-wide grants, standing grants and organization membership give no access to it." This matches the server.

**Assigned-To rule on Secure:**

- An organization target is skipped `OrganizationOnSecure` (`Services/ExternalAccess/AssignedAccessMaterializer.cs:1042-1046`).
- A contact or user target becomes `PendingConfirmation`, a suggestion (`:1069-1101`).

**Broker-only.** No SPE container permission is ever written for an external grantee (`GrantExternalAccessEndpoint.cs:26-28, 233-237`).

### (c) Children and documents

**List** (`Api/ExternalAccess/ExternalProjectDataEndpoints.cs:591-616`):

- It requires Read on the route root from the evaluator (`RightsForRoot`, `:852-858`).
- `GetDocumentsAsync` (`:608`) then filters each document on its own six root links and live parents, with effective secure flags.

**Download** (`:628-690`), in order:

1. Read on the parent (`:647-653`).
2. The document must be under that parent AND readable on every Secure root it is filed to (`IsDocumentReadableUnderAsync`, `:667, 1081-1093`, owner option B 2026-10-12). Otherwise a uniform 403, which does not reveal whether the document exists.
3. Only then are SPE pointers resolved, and the content is streamed **app-only** (`fileStore.DownloadFileAsync`, `:681`). Pointers never reach the client.

**Children inherit isolation.** `SecureChildLineage.Children` (`Services/Access/SecureChildLineage.cs:62-…`) and `SecureChildReconciler` re-own documents, events, communications, to-dos and grant rows to the Secure team. `SecureChildShareSynchronizer` mirrors the root's shares onto them. Confirmed live for PAT-908068's document (Part 2).

### (d) Making a record Secure (`ProvisionProjectEndpoint`, incl. `transition: "make-secure"`)

The sequence is in the header (`Api/ExternalAccess/ProvisionProjectEndpoint.cs:18-60`):

1. Set the flag and read it back.
2. Share the creator first.
3. Assign the record to the named, memberless Secure Record Owners team, after proving 0 members and 0 users in the BU.
4. Create the record's own container.
5. Re-own and re-share the existing children, **including its access-grant rows** (step 8; `SecureChildReconciler.cs:363, 1164`).

**Nothing deactivates any `sprk_externalrecordaccess` row.** There is no `DeactivateAsync` call in the endpoint. As a result:

- **By-name contact grants are kept** and keep conferring, since a by-name grant is the allowed path. Live: both PAT-908068 grants are owned by the Secure team.
- **Organization-wide grants are kept as rows but suppressed at read** (`DirectAccessLevel = null` → None → removed). They are marked "No effect" in Manage Access (N4).
- **Standing grants and organization expansion** are suppressed at read by `isDirectOnly` (Terms 2 and 3).
- **Assigned-To grants:** a pre-existing auto grant to a contact is kept (A3, N3). New ones are suggestions.
- **Files:** the record gets its own container, and existing files are relocated (`tests/integration/data-mutation/DocumentContainer/MakeSecureFileRelocationTests.cs`).

### (e) Every path by which an external contact could reach a Secure record without a by-name grant

| Path | Result | Evidence |
|---|---|---|
| Organization-wide grant | **Blocked** at write (`DecideGrantPolicy`) and at read (`DirectAccessLevel` null → None → removed) | `ExternalGrantLifecycle.cs:756-757`; `ExternalParticipationService.cs:1580-1594`; `AccessibleRecordSetService.cs:433-455, 2335` |
| Standing grant (contact or organization) | **Blocked**: Term 2 / Term 3 skip direct-only ids; CIAM never runs them | `AccessibleRecordSetService.cs:2290, 2304, 2155, 2205` |
| Assigned-access (Assigned-To rule) | **Blocked** for a new assignment: organization skipped, contact only suggested. A grant from before Secure is kept (by name, N3) | `AssignedAccessMaterializer.cs:1042-1046, 1071-1101` |
| Derived lookup (`sprk_assignedto` / `sprk_assignedattorney1` → contact) | **Blocked** for contacts: that is the standing-gated membership walk, suppressed by `isDirectOnly`. **Not blocked for systemusers → F1** | `AccessibleRecordSetService.cs:2290` vs `:1927-1935` |
| Organization membership / organization expansion | **Blocked** (Term 3, `isDirectOnly`) | `:2294-2306` |
| Service-request requester rule | **Partly open → K2.** The SR's own documents and thread only; never the Secure root or its documents | `ExternalProjectDataEndpoints.cs:1021-1056`; `ExternalMultiRootRule.cs:37` |
| Communication participants | **No such rule.** Messages are gated on the record, then `SecureRootsFilter` | `ExternalMessageEndpoints.cs` (gate `EvaluateRecordGateAsync`) |
| Teams | Same `/api/v1/external` group and the same principal. `/api/agent` uses Dataverse RPA | `CallerPrincipalResolver.cs:44`; `Api/Filters/AiAuthorizationFilter.cs:54` |
| Invoice parent / document filed to several roots | **Blocked**: requires Read on every Secure root it is filed to | `ExternalProjectDataEndpoints.cs:1081-1093` |
| Contact-to-colleague grant (`/contact-grants`) | By name, capped at the grantor's level, own organization only (N2) | `ContactGrantEndpoints.cs:20-23` |

## Part 2 — live check on dev (GET only)

Scripts:

- `C:/Users/RALPHS~1/AppData/Local/Temp/claude/c--code-files-spaarke-wt-unified-access-control-r2/993ea642-9455-4e0e-8f57-ed99084c5e37/scratchpad/secaudit/sa_secure_live.py`
- `.../sa_secure_census.py`
- `.../sa_secure_member.py`
- `.../sa_container_unique.py`

Each script runs `python -I` with the default `az` login. The output files (`sa_out.json`, `sa_census.json`, `sa_member.json`) sit next to the scripts. Ids and booleans only.

**PAT-908068** `5d46df87-e762-f111-ab0c-000d3a4d8152`:

| Check | Observed | Matches rule |
|---|---|---|
| `sprk_issecure` / Access Permission / state | `true` / not set (Standard) / Active | yes |
| Owner | team **Secure Record Owners** `6eabc7f9…` (not default, owner type, **0 members**), BU **Secure Record** `d9ec0b6f…` with **0 systemusers** | yes: no principal reads it by ownership or BU |
| Container | own container; recorded on exactly 1 matter, 0 projects, 0 work assignments, 0 BUs | yes |
| `sprk_externalrecordaccess` rows | 2. Both **by-name contact** rows (organization null), Full Access, Active, expire 2027-01-18, owned by the Secure team. Contacts `394fda9f…` = **CIAM Test User** (has a CIAM oid, standing grant false) and `e6ddaa78…` = **Ralph Dewey** (has an oid, standing grant false) | yes |
| Organization-wide grants on it | **0** | yes |
| Shares | 1. systemuser `1d02f31c…` = **Ralph Schroeder** (Read, Write, Append, AppendTo, Delete, Share), not external-flagged | yes |
| Organization and contact lookups on it | none populated (`sprk_assignedlawfirm1/2`, `sprk_assigned*` all empty) | nothing to derive from |
| Its document `87df221f…` | owner = Secure team, BU = Secure Record, same container, share = Ralph Schroeder with the same mask as the root | yes: the child mirrors the root |

**What admits whom:**

- CIAM Test User and Ralph Dewey are admitted by their own by-name rows only.
- No organization grant or standing grant contributes.
- CIAM Test User's one organization (`67577f8c…`) is active. It has no standing grant (`sprk_standinggrant` empty), 8 organization-wide grant rows elsewhere and **none on this matter**, and **1 active member (CIAM Test User only)**, so there is no non-granted colleague to test.
- Ralph Dewey has no organization membership.

**Hypothetical non-granted colleague** (answered from the evaluator's inputs, no grant created). Suppose such a colleague existed, and that organization held an organization-wide grant on PAT-908068, or a standing grant with the matter naming that organization in `sprk_assignedlawfirm1/2`:

- the organization row would enter the grant term with `DirectAccessLevel = null` → None → removed (`:2335`);
- the organization-expansion and standing terms would skip the id (`:2290, 2304`).

The colleague would get **no access**. Only a contact-named row would admit them.

**Dev-wide census:**

- 4 Secure roots: 3 projects, 1 matter, 0 work assignments.
- 105 grant rows. **0 organization-wide grants on any Secure root.** 2 active by-name rows on Secure roots (PAT-908068's).
- Standing-grant subjects: 2 contacts, 0 organizations. These are suppressed on Secure in any case.

**F1 exposure on dev:** none today. The one Secure root with a populated registry contact column (project `65a3fab2…`) names a contact linked to a user who already holds a share.

**Not checked live:**

- SPE container permissions. That needs a Graph token with FileStorageContainer scope; no Key Vault was used and nothing was elevated. The code writes no external container permission (broker-only, `GrantExternalAccessEndpoint.cs:233-237`).
- An actual SPA/Teams sign-in for K1/F1. That would need test-user credentials.
