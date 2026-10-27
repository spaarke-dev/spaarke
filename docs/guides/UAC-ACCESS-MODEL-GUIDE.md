# Unified Access Control (UAC) — How Access Works Guide

> **Last Updated**: 2026-10-27 (verified against master `3ff2c04bf5`)
> **Purpose**: Explain, end to end, how Spaarke decides who can see and change a record — for every kind of user, every
> access setting, and every family of related records — so that people understand the behaviour and every new feature is
> built to the same model.
> **Audience**: Administrators and support (who set access and answer "why can / can't this person see this?"), and
> developers (who add tables, endpoints, wizards or jobs that touch access).

---

## How to read this guide

Each numbered section answers one question. Every section has the same shape: a plain answer first, then the detail,
then a short **Where in the code** line for developers. The code is the source of truth (CLAUDE.md §2); if this guide
and the code disagree, the code is right and this guide must be fixed.

### Terms used throughout

| Term | Meaning |
|---|---|
| **Core record** (root) | A record that holds its own access: **Matter**, **Project**, **Work Assignment** (and Service Request, which is a core record for filing but not a grant root). |
| **Child record** | A record filed under a core record: Document, Invoice, Event, To Do, Communication, Analysis, Memo, and the other family tables (§6). |
| **MDA user** | A person with a Power Apps licence using the model-driven app (Spaarke Matter Management). A Dataverse **systemuser**. |
| **Workforce SPA user** | An employee (workforce Entra identity) using the external SPA or Teams. Either a licensed **systemuser** or an unlicensed **contact-only** employee. |
| **Partner** | An external person signing in through Entra External ID (CIAM) to the SPA. Always a **contact**. |
| **Grant** | A row in `sprk_externalrecordaccess` that gives a contact (or a whole organization) a level on a core record. Written by **Manage Access**. |
| **Share** | A native Dataverse record share (`principalobjectaccess`) to a systemuser or team. Written by Manage Access "+ User", the Assigned-To rule, or the platform **Share** button. |
| **Access Permission** | The record's policy column `sprk_accesspermission`: **Standard**, **Limited** or **Restricted** (§3). |
| **Secure** | The record's flag `sprk_issecure`. A Secure record is owned by a memberless team in an empty business unit, so only explicit shares reach it (§8). |
| **Access level** | **View Only**, **Collaborate**, **Full Access** (§4). |
| **No Access list** | Entries in `sprk_noaccessentry` that deny a person or organization a record, whatever else they hold. |
| **BFF** | The Spaarke API (`Sprk.Bff.Api`). It is the only writer of access data and the only path to files for the SPA and partners. |

---

## Quick reference

| Item | Value |
|---|---|
| Access Permission column | `sprk_accesspermission` on matter, project, work assignment — Standard `100000000` (default), Limited `100000001`, Restricted `100000002` |
| Secure flag | `sprk_issecure` on matter, project, work assignment — field-secured; only the BFF app user (and System Administrators) can write it |
| Access level values | `sprk_accesslevel`: View Only `100000000`, Collaborate `100000001`, Full Access `100000002` |
| Grant table | `sprk_externalrecordaccess` (contact or organization grantee, level, expiry date) |
| Standing grant (contact / organization) | `sprk_standinggrant` + baseline level `sprk_accesspermissiongrant` |
| No Access list | `sprk_noaccessentry` (subject: contact, organization or systemuser; object: a record) |
| Secure business unit / owner team | `Secure Record` / `Secure Record Owners` (config `SecureRecord:BusinessUnitName`, `SecureRecord:OwnerTeamName`) |
| Who may manage access | Write **and** Share on the record, checked as the caller (`DelegationRuleFilter`) |
| Default grant expiry | Today + 90 days (Date Only, UTC) |
| Make Secure / Remove Secure | `POST /api/v1/external-access/provision-project` / `POST /api/v1/external-access/unsecure-project` |
| Grant / invite / share | `/grant`, `/invite-and-grant`, `/share-user`, `/unshare-user`, `/revoke` under `/api/v1/external-access`; contacts granting colleagues: `/api/v1/external/contact-grants` |

**Background jobs that keep access consistent** (they repair changes made outside the BFF):

| Job | Every | What it keeps true | Log tag |
|---|---|---|---|
| `SecureRootInheritanceJob` | 5 min | Filed work assignments / projects follow their parent's Secure flag and Access Permission floor | `[SECURE-INHERIT]`, `[FOLLOW-PARENT]` |
| `SecureChildReconciliationJob` | 2 min | Children of a Secure record are owned by the secure team; files sit in the right container; child Access Permission copies | — |
| `SecureChildShareReconciliationJob` | 2 min | Children of a Secure record carry the record's shares (including shares made with the platform Share button) | `[SECURE-CHILD-SHARES]` |
| `CoreAncestorStampReconciliationJob` | 5 min | Each child's "which core record am I under" stamp is current | `[CORE-ANCESTOR-RECON]` |
| `AssignedAccessReconciliationJob` | 5 min | Assigned-To people hold their automatic access; Restricted shares to external-flagged users are removed | `[ASSIGNED-ACCESS-RECON]` |
| `NoAccessShareReconciliationJob` | 5 min | No Access entries remove direct shares on Secure records | `[NO-ACCESS-RECON]` |
| `SpeContainerMembershipSyncJob` | 5 min | SharePoint Embedded container roles for internal users match their Dataverse access | — |
| `SecureRecordIsolationCensusJob` | 15 min | Read-only check that nothing (role depth, team membership) silently reaches Secure records | `[SECURE-CENSUS]` |
| `ExternalAccessReconciliationJob` | Daily 05:00 | Stamps today + 90 days on undated grants; deactivates grants of inactive organizations, ended memberships and deleted records | — |
| `GrantExpiryReminderJob` | Daily 06:00 | Reminds people about grants that are about to expire | — |
| `IdentityLinkReconciliationJob` | 5 min | Keeps systemuser ↔ contact links (used by licensed SPA users) current | `[ID-LINK-RECON]` |
| `MembershipReconciliationJob` | Nightly | Rebuilds Spaarke's people ↔ records index (`sprk_userentityassociation`) | — |

---

## 1. How is access to a record decided for each type of user?

**Short answer.** Two engines decide access, and which one applies depends on *where* the person is working:

- In the **model-driven app**, Dataverse itself decides, using the person's security roles, business unit, ownership,
  teams and shares. Spaarke influences the result only by writing ordinary Dataverse data (shares and ownership).
- In the **SPA, Teams and every BFF route** (including all file access), the BFF decides. It composes an *accessible
  record set* for the caller from several sources and then applies vetoes.

### 1(a) MDA user with a Power Apps licence

| Question | Answer |
|---|---|
| Who decides? | Dataverse, natively. |
| From what? | Security roles and their **depth** (User / Business Unit / Deep / Global), the record's owner (user or team) and owning business unit, the person's team memberships, and record **shares**. |
| What does UAC change? | It writes shares (Manage Access "+ User", the Assigned-To rule, secure-child mirroring) and moves ownership (Secure records go to the memberless Secure Record Owners team). It never adds a second check inside the MDA. |
| Files (SharePoint Embedded)? | Opened through BFF routes. The BFF asks Dataverse *as the user* (their own token, `RetrievePrincipalAccess`) whether they hold the right on the document's record, then streams the file with the app identity. Editing in Office runs as the user: internal users hold a writer role on their business unit's container, and a just-in-time writer role on a Secure record's own container while Dataverse says they have Write. |

The person's licence and roles do **not** by themselves give access to records in the SPA — see 1(b).

### 1(b) Workforce SPA user (internal employee using the SPA or Teams)

The BFF first resolves the signed-in employee to **exactly one** principal, or denies:

1. **Licensed employee → their systemuser.** Their record set is the **union** of:
   - their own access as a systemuser, and
   - the grants held by their **linked contact** (the contact named in the systemuser's `sprk_primarycontact`, else the
     contact bound to their identity), counted only while that contact is Active.

   "Their own access" comes from one of two sources, selected by the setting
   `ExternalAccess:ImpersonatedRootSets:Enabled` (default **off**):
   - **Off:** a membership walk over ownership, owning team, owning business unit and the registry's people lookups
     (Assigned To and similar). It confers Read, Write and Create. It does **not** see shares.
   - **On:** Dataverse answers for the user (an impersonated read), so roles, depth, teams and shares all count. It
     confers Read; every write is re-confirmed with Dataverse as the user.

   On a **Secure** record, a systemuser's access that comes only through a lookup or the business unit is dropped unless
   Dataverse confirms Read for that user, and is then kept at **Read only**. At most 50 such records are confirmed per
   request; the rest are dropped.
2. **Unlicensed employee → their contact** ("contact-only" workforce principal; the contact is bound to the employee's
   identity in `sprk_externalobjectid`, plane = Workforce). Their record set is the union of:
   - their own grants and their organizations' grants (as for a partner, below),
   - **standing-grant membership**: if the contact's `sprk_standinggrant` is set with a baseline level, they get that
     level on records where they are a member (e.g. named in Assigned To), and
   - **organization expansion**: records that reference their organization, at the organization's standing-grant
     baseline.
3. **Employee of a customer tenant ("WorkforceHome")** → resolved only by their tenant + object id: first a stored link to
   a systemuser, then a WorkforceHome contact; otherwise "registration required" (403).

### 1(c) Partner (external contact through CIAM)

The partner's identity resolves to one contact (`sprk_externalobjectid`, plane = External). Their record set is the
union of:

- grant rows naming **this contact**, and
- **organization-wide** grant rows for every organization in which the contact holds a current membership
  (`sprk_contactorganization`).

There is **no** standing-grant term and **no** organization-expansion term for partners. (This is a deliberate,
owner-approved difference from contact-only workforce users.) Partners never get a SharePoint Embedded permission; every
file is authorized by the BFF and streamed with the app identity.

### How the BFF combines the sources (every SPA principal)

The same pipeline runs for every BFF principal, so the rules are identical everywhere:

1. **Suppress (before combining).** On a record that is **Secure** or **Limited**, a contact counts only a grant that
   names it directly. Organization grants, standing membership and organization expansion add nothing there.
2. **Combine.** Each remaining source contributes rights per record; the **highest** wins (rights are OR-ed together).
3. **Veto (after combining), in this order:**
   1. **No Access list** — deny wins. A record denied to the contact, to one of its organizations, or (on Secure records)
      to the systemuser itself is removed outright, whatever any grant says. (For a licensed user on an ordinary record,
      an entry naming their contact or organization removes only what came through the linked contact.)
   2. **Restricted** (and an inactive record) — removes every contact-sourced entry. Only a systemuser's own access
      survives — and on a Restricted record, not even that for a systemuser flagged external (`sprk_isexternal = true`).
4. **Drop** any record left without Read.

Other rules that always apply:

- A record's effective **Secure** and **Access Permission** are the strictest values across the record and every record
  it is filed under (§6).
- A record whose flags cannot be read is treated as Secure, Limited, Restricted and inactive at once (fail closed).
- An inactive or unreadable contact gets an empty set.
- A grant with an expiry in the past confers nothing. A grant with **no** expiry date (only possible when written outside
  Spaarke) confers nothing until the daily 05:00 reconciliation stamps it with today + 90 days.

**Service requests**: a service request is a core record for filing, but not a grant root. Partners see only their own
service requests, and only where `ExternalAccess:PartnerServiceRequestsEnabled` is on.

**Users flagged external**: B2B guest systemusers are marked `sprk_isexternal = true` by the operator script
`scripts/Set-ExternalFlagForB2BGuests.ps1` (blank means internal). The flag matters on Restricted records (§3) and for
container roles.

### Caching (why a change can take a minute to show)

| Data | Cached for | Cleared early by |
|---|---|---|
| A contact's grant set | 60 seconds | Grant, contact grant, revoke, project closure, the Expiration command, the Assigned-To rule |
| A systemuser's impersonated record set | 2 minutes | BFF share, owner and team writes |
| Secure-record Read confirmations | 60 seconds | Membership cache invalidation |
| Internal document / record checks | 60 seconds | BFF share, owner and user-access writes |
| Membership and identity lookups | 2 minutes (current user: 10 minutes) | — |
| Contact active state, vetoes | Not cached | — |

A set built over a failed read is never cached.

**Where in the code**: `Infrastructure/ExternalAccess/AccessibleRecordSetService.cs` (composition; vetoes),
`WorkforcePrincipalResolver.cs` and `CallerPrincipalResolver.cs` (who the caller is), `ExternalParticipationService.cs`
(grant reads), `SubjectStandingGrantReader.cs` (standing grants), `Api/FileAccessEndpoints.cs` and
`Api/ExternalAccess/ExternalProjectDataEndpoints.cs` (files).

---

## 2. How is access set for each type of user — the owner field, Manage Access, or both?

**Short answer.** It depends on the user type:

- **MDA users**: **both** the out-of-the-box owner model (owner, business unit, role depth, teams) **and** shares —
  including shares written by Manage Access "+ User". Manage Access *grants* (to contacts) do not affect the MDA.
- **Workforce SPA users (licensed)**: their own Dataverse-style access (owner/membership, or Dataverse itself if the
  impersonated read is on) **plus** Manage Access grants to their linked contact.
- **Workforce SPA users (contact-only)** and **partners**: **Manage Access grants** (and, for contact-only workforce
  users, standing grants). The owner field gives them nothing.

| Source of access | MDA user | Workforce SPA — licensed | Workforce SPA — contact-only | Partner |
|---|---|---|---|---|
| Owner field, business unit, role depth, team membership | **Yes** (Dataverse) | Yes — Dataverse if the impersonated read is on; otherwise the membership walk (owner, owning team, owning BU, people lookups) | No | No |
| Dataverse share (Manage Access "+ User", platform Share button, Assigned-To share, secure-child mirroring) | **Yes** | Only if the impersonated read is on (or as a Secure-record Read confirmation) | No | No |
| Manage Access grant to the person's contact (`/grant`, `/invite-and-grant`, contact-to-colleague grants, Assigned-To grant) | No | **Yes**, through the linked contact | **Yes** | **Yes** |
| Organization-wide grant through a current organization membership | No | Yes, through the linked contact | Yes | Yes |
| Contact standing grant (`sprk_standinggrant` + `sprk_accesspermissiongrant` on the contact) | No | No | **Yes** | No |
| Organization expansion (organization's standing grant) | No | No | **Yes** | No |
| No Access list | Enforced by removing direct shares on Secure records | Veto | Veto | Veto |
| Access Permission / Secure on the record | Secure moves ownership; Restricted removes shares of external-flagged users | Narrows contact-sourced access | Narrows | Narrows |

### The tools that set access

| Tool | What it writes | Who it is for |
|---|---|---|
| **Manage Access → "+ Contact"** (the Access Grant modal) | A grant row. A contact with an email who is external goes through `/invite-and-grant` (onboard, then grant); an internal workforce contact, or one with no email, through `/grant`. | Partners and workforce contacts |
| **Manage Access → "+ User"** | A Dataverse share (`/share-user`), at the level's share mask (§4). | Internal systemusers |
| **Manage Access → organization** | An organization-wide grant row (no contact). | Every current member of an organization |
| **Contact Grants** (in the SPA) | A grant row issued by a contact to a colleague in their own organization (§4). A contact cannot grant themselves (409 `self_grant`). | Partners granting colleagues |
| **Assigned-To rule** (automatic) | For each person or organization named in an "Assigned …" column: a Collaborate grant row, or a Collaborate share if the contact is linked to an eligible systemuser. Runs after BFF saves, from the form's post-save step (Update Access), and every 5 minutes. Never lowers existing access; if staff remove it, it is not re-added. Policy applies: on a **Secure** record grants and shares are only *suggested* for staff to confirm (an existing automatic grant is kept); on **Restricted** no contact or organization grant is written (a linked internal user's share still is, unless they are flagged external); on **Limited** no organization grant. When an assignment is changed or cleared, the previous person's unmodified automatic access is removed (always on save; by the 5-minute job only when its revoke switch is on). | Assignees |
| **Close project** (`/close-project`, projects only) | Deactivates every contact and organization grant on the project and removes those people's container permissions on a Secure project's own container. Matters and work assignments have no close yet (#2337): end their access by revoking each grant. | Ending a project |
| **Contact form: Standing Grant** | `sprk_standinggrant` + baseline level on the contact. | Contact-only workforce users |
| **Owner / business unit** (platform Assign) | `ownerid`. | MDA users (and the licensed SPA membership walk) |

Every Manage Access call requires the caller to hold **Write and Share** on the record, checked as the caller. The one
exception is **Update Access** (`/assigned-access/sync`, which re-runs the Assigned-To rule): Write is enough.

> **"+ Contact" or "+ User" for an internal colleague?** Use **"+ User"**. "+ Contact" on a colleague's contact writes a
> grant, which gives a licensed colleague access in the SPA (through their linked contact) but **not** in the model-driven
> app. "+ User" writes a share, which the model-driven app honours. Only the Assigned-To rule makes this choice itself: it
> shares with the internal user a contact represents (`AssignedAccessMaterializer.ResolveTargetAsync`). A single "add
> person" entry that decides the same way on the server is proposed in #2338.

**Where in the code**: `Api/ExternalAccess/GrantExternalAccessEndpoint.cs`, `InviteAndGrantExternalUserEndpoint.cs`,
`InternalShareEndpoints.cs`, `ContactGrantEndpoints.cs`, `DelegationRuleFilter.cs`,
`Services/ExternalAccess/AssignedAccessMaterializer.cs`, the Access Grant modal in
`src/client/shared/Spaarke.UI.Components/src/components/AccessGrantModal/`.

---

## 3. Access Permission: Standard, Limited and Restricted

**Short answer.** Access Permission controls **which kinds of external (contact) access count** on a record. It does not
change what internal users get from their Dataverse roles, except that **Restricted** also shuts out internal users who
are flagged external.

- **Standard → Limited**: Limited removes the *indirect* routes for contacts. Only a grant naming the person directly
  counts; organization-wide grants, standing grants and organization expansion do not, and an organization-wide grant
  is refused when someone tries to add one.
- **Limited → Restricted**: Restricted removes *all* contact access, including direct grants, and every new grant to a
  contact or organization is refused. It also removes and refuses shares to internal users flagged external.

The column lives on Matter, Project and Work Assignment (default **Standard**). Children (To Do, Event, Communication,
Document) hold a copy for display (§6); a **Document** may be set stricter than its parent to hide it from partners, but
never looser.

| | MDA user | Workforce SPA — licensed | Workforce SPA — contact-only | Partner |
|---|---|---|---|---|
| **Standard** | Roles and shares as usual | As usual (own access + linked contact's grants) | All grant types count: direct, organization, standing, expansion | Direct and organization grants count |
| **Limited** | Unchanged | Own access unchanged; linked contact counts direct grants only | Direct grants only | Direct grants only; organization grants refused at write time |
| **Restricted** | Unchanged, **except** users flagged `sprk_isexternal`: their shares are removed (by the form's post-save Update Access call and by the 5-minute job) and new shares are refused; the platform Share button is hidden | Own access survives; everything through the linked contact is removed; a user flagged external gets nothing | Nothing | Nothing — even an explicit Full Access grant confers nothing; new grants are refused |

**Secure** behaves like **Limited** for contacts (direct grants only), on top of its ownership isolation (§8–§9).
When a record is both, **Restricted wins**.

### The No Access list

A No Access entry denies a **subject** (a contact, an organization, or a systemuser) a **record**. It is checked after
everything else is combined, so it beats every grant:

- At read time, a denied record is removed from the person's set (deny wins).
- At grant time (`/grant`, `/invite-and-grant`, contact grants), granting someone on the list is refused (422); if the
  list cannot be read, the grant is refused (503). A "+ User" share on a Secure record is refused with 403
  (`subject_no_access`), or 500 if the list cannot be checked.
- An entry naming a **systemuser** binds them only on **Secure** records. There it removes their direct shares within
  minutes, provided the entry's author holds Write on that record (otherwise the entry is reported as not enforced). On
  an ordinary record it removes only what they would get through their linked contact.
- An organization entry matches any record that references the organization.
- Access a person has through a **team** (team share or team ownership) is never revoked by the list; it is reported and
  hidden on Teams/SPA.
- The last person with access to a Secure record is never removed.

Entries are authored by people holding the **Spaarke Access Administrator** role.

### Who may change Access Permission

- On a core record with no parent: anyone with **Write** on the record, directly in the MDA (the column is not
  field-secured; this is an accepted design choice).
- On a work assignment or project filed under a parent: never looser than the parent's value (§4, "Parent floor").
- Managing grants and shares needs Write **and** Share.

**Where in the code**: `ExternalCallerContext.cs` (`RootRecordFlags`), `AccessibleRecordSetService.cs` (suppression and
vetoes), `ExternalGrantLifecycle.cs` (`DecideGrantPolicy`), `NoAccessListReader.cs`, `NoAccessShareEnforcer.cs`,
`RestrictedExternalShareRemover.cs`, option set `src/dataverse/solutions/SpaarkeMaster/OptionSets/sprk_accesspermission.xml`.

---

## 4. Access levels: View Only, Collaborate, Full Access

**Short answer.** The three levels are cumulative. **View Only** reads; **Collaborate** adds creating and editing (and
lets the holder grant colleagues); **Full Access** adds the Delete right and lets the holder grant up to Full Access.

### For contacts (grants in the SPA)

| Level | BFF rights | What it allows in the SPA |
|---|---|---|
| **View Only** | Read | See the record and its children; download documents and versions |
| **Collaborate** | Read, Create, Write | Everything above, plus upload documents, create to-dos, events and messages, edit; grant colleagues (up to Collaborate) |
| **Full Access** | Read, Create, Write, Delete | Everything above, plus the Delete right; grant colleagues up to Full Access |

In the SPA a person can delete only items **they created** (with Read on the parent), at any level; no SPA route uses
the Delete right of Full Access. The practical differences between Collaborate and Full Access are:

- a contact holding Full Access may grant colleagues up to Full Access (Collaborate: up to Collaborate);
- for internal users, a Full Access share includes Delete in Dataverse, and holding Write **and** Delete on a Secure
  record is what allows Remove Secure (§8).

### For internal users (shares)

| Level | Dataverse share rights | Mask |
|---|---|---|
| **View Only** | Read | `1` |
| **Collaborate** | Read, Write, Append, AppendTo, Share | `262167` |
| **Full Access** | Collaborate + Delete | `327703` |

No level includes **Assign**. Collaborate and Full Access include **Share**, so a person with Write can also use the
platform Share button. Dataverse still caps what a share can do by the person's role privileges (§5).

### Rules that apply to every grant and share

- **Grantor ceiling.** Nobody grants more than they hold. The grantor's own rights are read as them: Read + Write +
  Delete → up to Full Access; Read + Write → up to Collaborate; Read → View Only; less than Read → cannot grant (403).
  A request above the ceiling is **narrowed** (the response says `narrowed`), not refused. A share is also capped at the
  caller's own rights.
- **Never lower by accident.** If a request was narrowed and the person already holds more, the write is refused (409
  `would_lower_existing`). An explicit downgrade is allowed.
- **Expiry.** Default today + 90 days (Date Only, UTC). The Manage Access **Expiration** command sets one date on every
  active external grant of the record at once. Re-adding over a lapsed grant restores it with today + 90 days. An
  undated grant (written outside Spaarke) confers nothing until the daily reconciliation dates it.
- **Contacts granting colleagues.** A contact holding Collaborate or Full Access (after vetoes; direct grants only on a
  Secure or Limited record; nothing on Restricted) may grant an active colleague in their own organization, up to their
  own level and their own expiry date. Never organization-wide, never themselves. If the colleague already has a grant
  someone else issued, the request is refused (409 `managed_elsewhere`). They may revoke only what they issued.
- **Assigned-To** writes Collaborate automatically, subject to the record's policy (§2).

### Parent floor, child may be stricter

A work assignment or project filed under a matter or project is enforced at its **effective** values:

- Effective **Secure** is true if the record or any parent is Secure.
- Effective **Access Permission** is the strictest along the chain (Restricted > Limited > Standard).

A user may make the child **stricter** than its parent (that becomes the child's own value and survives if the parent
later loosens). Setting it **looser** than the parent is refused (409 `sdap.access.access_follows_parent`), and re-filing
never loosens. A filed child cannot be un-secured on its own; it follows its parent. Matters file under nothing.

**Where in the code**: `ExternalCallerContext.cs` (`ExternalAccessLevel`, `ToAccessRights`, `GrantCeilingFor`),
`Services/Access/RecordShareLevels.cs`, `ExternalGrantLifecycle.cs` (`GrantCeiling`, reason codes),
`ContactGrantorAuthorizationFilter.cs`, `Services/Access/SecureRootInheritance.Cascade.cs` (`AccessFollowsParent`).

---

## 5. How UAC access levels relate to Dataverse security roles

**Short answer.** They are separate systems that meet at four points:

1. **Roles decide who may manage access.** Manage Access needs Write and Share on the record, and those come from the
   person's roles (or from a share that carries Share).
2. **Roles set the grantor ceiling.** The level a person can grant is read from their own Dataverse rights on the record
   (roles plus shares), as in §4.
3. **A grant never changes Dataverse privileges.** A Manage Access grant (`sprk_externalrecordaccess`) affects only the
   SPA and BFF. Only shares ("+ User", Assigned-To for a linked user) change what the MDA allows, and a share still
   cannot exceed what the person's roles permit on that table.
4. **For licensed SPA users, roles count only when the impersonated read is on.** With it off, the membership walk
   ignores role depth.

Assigned-To automatic grants are not limited by anyone's role.

### Roles that matter to UAC (shipped in `src/dataverse/solutions/SpaarkeMaster/Roles/`)

| Role | What it holds (UAC-relevant) | Purpose |
|---|---|---|
| **Spaarke Core User** | Create, Read, Write, Delete, Assign, Append, AppendTo at Deep on matter, project, work assignment and document; **Share** at Deep on matter, project and work assignment (not document); Read on the grant table; no privilege on `sprk_noaccessentry` | The ordinary internal user; can manage access |
| **Spaarke Basic User** | Read and AppendTo at Deep on matter, project, work assignment and document; Read on the grant table; no Write or Share | Default role for B2B guest users; cannot manage access |
| **Spaarke Access Administrator** | Create, Read, Write, Append, AppendTo on `sprk_noaccessentry` at Global | Authors No Access entries; assigned together with a role that holds Share |
| **Secure Record Owner** | User (Basic) depth on the secure table set | Held only by the Secure Record Owners team; created by provisioning, not in the solution |

Partners are contacts, not systemusers, and hold no Dataverse role.

**Depth matters for Secure records** — see §9.

**Where in the code**: `ExternalCallerContext.cs` (`GrantCeilingFor`), `DelegationRuleFilter.cs`,
`CallerRecordAccessProbe.cs`, `InternalShareEndpoints.cs`, role XML under `src/dataverse/solutions/SpaarkeMaster/Roles/`.

---

## 6. How UAC works for a family of related records

**Short answer.** Grants live on **core records** (Matter, Project, Work Assignment). **Child records** inherit from the
one core record they are filed under. A grant on a matter does **not** reach its projects or work assignments — each core
record needs its own grant — but *restrictions* do flow down: a project or work assignment filed under a matter is at
least as strict as the matter.

```
                        Matter  (core: own grants; may be Secure; files under nothing)
                       /       \
              Project            Work Assignment     (core: own grants; Secure / Access Permission floor from parent)
                 |                     |
   Document (+ file), Invoice, Event, To Do, Communication, Analysis, Memo
   (children: inherit from the core record they are filed under, one hop)

   Budget, Agreement, Report Card: filing points only — a to-do under a budget inherits from the budget's matter/project
```

### How a child inherits

- Each child carries a **stamp** of its core record in `sprk_regardingmatter`, `sprk_regardingproject`,
  `sprk_regardingworkassignment` or `sprk_regardingservicerequest`, copied from whatever it is filed under when it is
  written. A 5-minute job repairs stale stamps.
- **External (SPA) access**: a child is visible if the person can read its core record. Documents and invoices roll up to
  **any** accessible core record they are linked to. To-dos and events are listed per core record, and a row also filed
  to a Secure record the person cannot read is hidden.
- **Access Permission on children** (To Do, Event, Communication, Document) is a display copy of the strictest value at
  the top of the filing. A Document may be stricter than its parent (to hide it from partners), never looser.
- **Ownership**: a child created through Spaarke is owned by the default owner team of its parent's business unit (with
  no parent, the acting user's business unit). If any parent is Secure, it is owned by the Secure Record Owners team. If
  the owner cannot be decided, the write is refused.

### Documents and files

| Record | Its files live in |
|---|---|
| A Secure core record | Its **own** SharePoint Embedded container |
| A record under a Secure core record (at any level) | That Secure record's container |
| Any other record | The container of the record's **own** owning business unit (one container per business unit) |

A document's file location follows from its record links; a Secure answer always wins. For a non-secure document, it is
the container of the document row's own business unit — set from its parent's business unit when Spaarke created it.

### What is in the Secure family

When a record becomes Secure, **28 child tables** move with it (`SecureChildLineage`; `config/secure-record-owner-role.json`
lists them plus the three core tables and the grant table — 32 in all), including documents and file versions, invoices, budgets and budget revisions,
events, to-dos, communications, memos, analyses, signals and decision records. Service requests are deliberately not in
the family; a service request is a core record of its own.

**Where in the code**: `Services/Dataverse/CoreAncestorResolver.cs` (core / child lists, stamps),
`Services/Dataverse/RecordOwnershipResolver.cs` (ownership), `Services/Access/SecureChildLineage.cs`,
`Infrastructure/Dataverse/RecordContainerResolver.cs` (containers), `Infrastructure/ExternalAccess/ExternalModuleRegistry.cs`.

---

## 7. What happens to the family when a parent is reassigned to a new business unit or owner?

**Short answer.** For an ordinary (non-Secure) record, **the children follow it**, within about two minutes plus the
run, once the owner has turned the switch on (task 194, #2339). Dataverse itself is configured not to cascade Assign,
Reparent or Share from matter, project or work assignment to any Spaarke child, so the BFF does it: the two-minute
`secure-child-reconciliation` job lists every ordinary matter, project, work assignment and service request changed since
its last run (an MDA Assign changes the record's `modifiedon`), walks its children and re-owns each child that a business unit's default
team owns to the owner `RecordOwnershipResolver` answers now (rule I-6). Until the switch is on, the job only reports
the moves it would make.

What moves, and what does not:

- **Moved:** every child (and grandchild — a To Do under an event under the matter) owned by a business unit's default
  owner team, to the default team of its primary parent's business unit. A child filed under several records in
  different business units takes the resolver's answer for its first (primary) parent, the same answer a new child would
  get. Each move is a conditional write (it is refused if the row changed since it was read), logged with the previous
  owner, and read back.
- **Never moved by this job:** a Secure child (owned by the Secure Record Owners team or any team in the Secure Record
  business unit), and nothing is moved into or out of isolation — that is the Secure pass's (§10). A child the rule would
  make Secure (it is also filed under a Secure record) is reported and left to the Secure pass.
- **Reported, not moved:** a user-owned child (one created outside the product keeps its creator, the accepted I-6
  interim), and a child owned by a team that is not a business unit's default team.
- **Service requests:** a row filed only under a service request (`sprk_regardingservicerequest`, a document's
  `sprk_relatedservicerequest`) follows the service request. Only this job walks those lookups: a service request is not
  in the Secure family, so the Secure pass never does.
- **Budget buckets** follow their budget (`sprk_budgetbucket.sprk_budget`), here and into isolation under a Secure budget.
- **Not covered:** the root's access-grant rows (`sprk_externalrecordaccess`). See
  `projects/unified-access-control-r2/notes/task-194-ordinary-parent-move.md`.
- **Files follow.** A filed document's file belongs in the container of the record it is filed under (that record's
  business unit, `RecordContainerResolver.DeriveDocumentContainersAsync`); only an unfiled document uses its own row's
  business unit. The job's 2-minute re-file pass moves a file whose container no longer matches — for a document filed
  directly under a changed matter, project or work assignment (whatever its owner), for every document this job
  re-owns (the re-own changes its `modifiedon`), and for every other document of the moved record's family that this job
  hands it (any owner, at any depth — never one held in isolation or filed under a Secure record). The hand-off is
  bounded by the re-file pass's carry (1000 rows); a record whose documents do not fit waits for the next run and is
  never dropped.

Consequences while the switch is off (and for what the job does not cover):

- **MDA users** see each child according to the child's own owner and business unit. After a parent moves to business
  unit B, its children stay in business unit A, so someone whose roles reach only business unit B may see the parent but
  not its children (and the reverse).
- **SPA users and partners** are not affected by the move: their access to children follows the core record's grants
  (§6), not the child's owner.

**Why the platform cannot do it.** Dataverse cascades Assign only through a table's single *parental* relationship. Most
Spaarke children can be filed under several kinds of parent (a document links to matter, project, work assignment and
more), so there is no one parent to cascade from, and every Spaarke relationship is NoCascade. A cascade would also copy
the parent's owner onto the child, while Spaarke's rule (I-6) gives a child its business unit's default owner team.
Spaarke does not use plugins (ADR-002), so the re-own is the scheduled job's.

**Switches** (app settings): `SecureChild:Reconciliation:OrdinaryMovesWritesEnabled` (default `false`, report-only;
turned on only after UAC reads a report-only run and the owner gives the go), `SecureChild:Reconciliation:MaxOrdinaryMovesPerRun`
(default 200 owner writes a run) and the shared `SecureChild:Reconciliation:MaxRootsPerRun` (50 records a run). The
recent-changes emergency stop (`RecentChangesWritesEnabled = false`) stops it too. A record not finished in one run is
carried to the next. Each run's report is `ResultJson.ordinaryMoves`. **Older moves:** besides its one-hour window, each
App Service instance sweeps every ordinary record once (50 a run, then it stops), and sweeps again when the switch is
turned on, so a reassignment made before this job existed, or before a restart, is corrected too
(`ResultJson.ordinaryMoves.sweep`).

**What *does* flow to the family** is a change of **Secure** or **Access Permission** — see §4 (parent floor) and §10.
Those changes are applied by the BFF when it makes them, and by the background jobs within minutes when they are made
some other way (for example directly in the MDA).

**Where in the code**: `SecureChildReconciler.ReconcileOrdinaryParentMoveAsync` and
`SecureChildReconciliationJob` (the ordinary-moves pass), relationship cascade settings in
`src/dataverse/solutions/SpaarkeMaster/Other/Relationships/` (all Spaarke child relationships are `NoCascade` for Assign,
Reparent and Share), `Infrastructure/Dataverse/AssignCascadeChildOwners.cs` (the only platform Assign cascades: from
matter and project to their teams, SharePoint document locations and SharePoint documents; a work assignment cascades
nothing), `RecordOwnershipResolver.ReparentAsync`, `docs/architecture/DATAVERSE-WRITE-PATH-ARCHITECTURE.md` (I-6).

---

## 8. How a record becomes a Secure Record

**Short answer.** Through Spaarke only — the **Secure** toggle in the create wizard, or the **Access → Make Secure**
command on a matter, project or work assignment. Both call `POST /api/v1/external-access/provision-project`, the only
code that sets `sprk_issecure` to true. The column is field-secured, so nobody can tick it directly (System
Administrators excepted, a platform limit).

| Who may | Rule |
|---|---|
| Make Secure from the create wizard (or any call without the `make-secure` transition) | The record's creator only |
| Make Secure from the form's **Access → Make Secure** command | A person with Write and Share on it; it shares the record to that person and to the creator, and names nobody else |
| Re-run on a record already flagged Secure | A person with Write and Share on it |
| Remove Secure | Only a person holding **Full Access** on it (Write **and** Delete in Dataverse), or its creator ("F3" rule) |

### What Make Secure does, in order

1. Find the **Secure Record** business unit and its named owner team **Secure Record Owners**, and prove the team has no
   members and the business unit has no users. If not, stop.
2. Set the flag and read it back.
3. Share the record with its creator first, so at least one person always keeps access.
4. Assign the record to the Secure Record Owners team. If that cannot be proven, undo.
5. Create the record's **own** SharePoint Embedded container.
6. Bring the family in: children move to the secure team and receive the record's shares; existing files are copied into
   the new container (with version history), verified, and the originals removed. Work assignments and projects filed
   under it become Secure records themselves (§10).

If a step cannot finish (for example some files are still copying), the call reports it and the 2-minute and 5-minute
jobs complete it. The flag is never cleared on a failure.

### Environment set-up it depends on

- A business unit named **Secure Record** (renamed from "Secure Project" on 2026-09-29; the name is how it is found).
- A named owner team **Secure Record Owners** in it, with no members, holding the **Secure Record Owner** role.
- No users in that business unit.

Full set-up and verification: [SECURE-PROJECT-ENVIRONMENT-SETUP.md](SECURE-PROJECT-ENVIRONMENT-SETUP.md).

**Where in the code**: `Api/ExternalAccess/ProvisionProjectEndpoint.cs`, `Infrastructure/Dataverse/SecureRecordOwnerTeam.cs`,
field security on `sprk_IsSecure` in each core table's `Entity.xml`.

---

## 9. What Secure Record changes for Dataverse roles and for Manage Access

**Short answer.** A Secure record is owned by a team nobody belongs to, in a business unit nobody is in. So **no one
reaches it through ownership or business-unit depth** — every person's access must come from an explicit share. That
holds **only if no one's role depth reaches the secure business unit**.

### Dataverse roles

| Depth of a role's Read privilege | Reaches a Secure record? |
|---|---|
| User (Basic) | No |
| Business Unit (Local) | No (nobody is in the secure business unit) |
| **Deep** | **Yes, if the person's business unit is an ancestor of the secure business unit** — e.g. the root business unit |
| **Global** | **Yes, always** |

Because the Secure Record business unit sits under the root business unit, any person in the root business unit with a
**Deep** Read role, or anyone with a **Global** Read role, can read every Secure record. The decided fix is to keep
people out of the root business unit (in business units that are *siblings* of the secure one). Narrowing depth is the
alternative. Never remove the Read privilege itself — a share confers nothing on a table the person has no Read
privilege for.

The **isolation census** (`[SECURE-CENSUS]`, every 15 minutes, read-only) reports anything that breaks this:

1. a non-administrator person holding Read that reaches the secure business unit on a core or family table;
2. the owner team not unique, a default team, or not empty;
3. the Secure Record Owner role held by anything other than that team;
4. any user in the secure business unit;
5. the owner role missing User-depth Read on a family table.

A principal holding System Administrator or System Customizer directly is exempt from clause 1 (clauses 2 and 4 still
count everyone). Two roles are accepted exceptions when assigned directly (a
team-held copy is still reported): **Spaarke Billing Administrator** (policy tables) and **Spaarke AI Analysis Admin**
(analysis tables).

### Manage Access on a Secure record

- **Internal users ("+ User")**: allowed at any level, under the grantor ceiling. People on the record's No Access list
  are refused. The last person with access cannot be removed (409). Children carry the record's shares — never more.
- **Contacts ("+ Contact")**: allowed, but **direct grants only** (Secure behaves as Limited for contacts).
- **Organization-wide grants**: refused (`org_grant_direct_only_record`).
- **Secure and Restricted**: every contact grant refused.
- **Licensed SPA users**: access through a lookup or the business unit counts only if Dataverse confirms Read, and then
  at Read only.
- **Files**: the record's own container. Partners go through the BFF only; internal users editing in Office get a
  just-in-time writer role that is removed when they lose Write.
- **Grant rows** on a Secure record are owned by the Secure Record Owners team.

**Where in the code**: `Infrastructure/Dataverse/SecureBuRoleDepthAssertion.cs` (census rules and exceptions),
`Services/ExternalAccess/SecureRecordIsolationCensusJob.cs`, `InternalShareEndpoints.cs`, `ExternalGrantLifecycle.cs`.

---

## 10. Moving a record into or out of Secure Record — the effect on its family

### Into Secure (Make Secure)

| Family member | What happens |
|---|---|
| The record | Owned by Secure Record Owners; its own container; creator shared |
| Its children (the 28 family tables) | Re-owned to the secure team; given the record's shares (a child under several Secure records gets only the shares they **all** have) |
| Its files | Copied with history into the record's container, verified, originals removed; leftovers finished by the 2-minute job |
| Work assignments / projects filed under it | Become Secure records themselves (own container, creator shared, parent's people passed on). Up to 3 nested levels inline; deeper levels, and anything changed outside Spaarke, within 5 minutes |
| Existing contact grants | Kept (no grant row is changed), but only direct grants count from now on; organization-wide grants stop counting |
| Assigned-To | New automatic access is only suggested for staff to confirm; existing automatic grants are kept |
| No Access entries on the record | Also bind every Secure record filed under it |

### Out of Secure (Remove Secure)

Only a **Full Access** holder or the **creator** may do it. A work assignment or project whose parent is Secure cannot be
un-secured on its own (409 `sdap.access.access_follows_parent`); it follows its parent.

The steps run in a fixed order so that the record is never open before it is ready:

1. Move the record to its new owner (the one requested, else the configured default, else the caller).
2. Release its children: re-owned to their business unit's team, mirrored shares removed. If this cannot finish, stop
   here with the record still Secure.
3. Revoke every share on the record.
4. End the shares it passed on to records filed under it.
5. Clear the Secure flag (last).
6. Restore what other Secure parents still pass on.
7. Work assignments / projects that were Secure only because of this record follow (50 inline; the rest within 5
   minutes).

Contact grants are not revoked by either move. Files of released children are moved out of the secure container by the
2-minute job's re-file pass, not by the Remove Secure call itself: a file moves only after its document has been
re-owned out of the secure business unit (step 2), and until the pass runs it stays in the secure container.

**Moving a child** out from under a Secure record counts as removing Secure for that child, so the same Full Access /
creator rule applies; a child moved outside Spaarke stays isolated until such a person releases it.

**Where in the code**: `Api/ExternalAccess/ProvisionProjectEndpoint.cs`, `Api/ExternalAccess/UnsecureProjectEndpoint.cs`,
`Services/Access/SecureRootInheritance.cs`, `Services/Access/SecureChildReconciler.cs`,
`Services/Documents/DocumentContainerRelocator.cs`, `Services/Access/SecureDesignationRemoval.cs`.

---

## 11. The model-driven app's Share button

**Short answer.** Spaarke uses Dataverse shares as the way to give **internal users** access, so the platform **Share**
button is kept and is a supported way to share. Spaarke writes the same kind of share itself (Manage Access "+ User",
Assigned-To, secure-child mirroring) and keeps everything consistent afterwards:

| Situation | What happens |
|---|---|
| Ordinary record | A platform share works like a "+ User" share. It counts in the MDA; in the SPA it counts only if the impersonated read is on. |
| **Restricted** record | The Share button is hidden on the form, and on grids when any selected row is Restricted (also whenever the setting cannot be read). A share to a user flagged external made any other way is removed by the form's post-save Update Access call and by the 5-minute job. |
| **Secure** record | Allowed. The record's children receive the new share within 2 minutes; an unshare is passed on the same way. |
| Person on the **No Access** list, Secure record | Their direct share is removed within minutes. |
| An Assigned-To share someone removed | Treated as a decision: it is not re-added. |

The platform Share button never gives a **contact** anything — partner and contact access comes only from grants.

The ribbon adds a Spaarke **Access** menu (Update Access, Make Secure, Remove Secure) next to Share, shown only to people
with Write and Share on the record. Because Collaborate and Full Access shares include the Share right, a person shared
at those levels can share onward with the platform button, still capped by their own roles.

**Where in the code**: `src/server/shared/Spaarke.Dataverse/DataverseWebApiService.cs` (the one share writer),
`InternalShareEndpoints.cs`, `RestrictedExternalShareRemover.cs`, `NoAccessShareEnforcer.cs`,
`Services/Access/SecureChildShareReconciliationJob.cs`, ribbon `src/dataverse/solutions/SpaarkeMaster/Entities/sprk_*/RibbonDiff.xml`
and `src/client/webresources/js/sprk_access_ribbon.js`.

---

## 12. How Dataverse teams are used

**Short answer.** UAC uses **owner teams** to own records; it does not use access teams or team templates. (The Matter
and Project tables have "auto-create access teams" enabled in their metadata, but UAC neither writes nor reads access
teams.)

| Team | Owns | Members |
|---|---|---|
| **Secure Record Owners** (named, non-default, in the Secure Record business unit) | Every Secure record, its family, and the grant rows on it | **None, ever** — that is what makes the record reachable only by shares. Spaarke refuses to add anyone to a team in that business unit. |
| A business unit's **default owner team** | Ordinary records, children created through Spaarke, grant rows on ordinary records, contacts created by registration (the customer business unit's team) | Everyone in the business unit (managed by the platform) |

Other ways teams matter:

- **MDA**: team membership and team-held roles and shares work natively.
- **Licensed SPA users (membership walk)**: a record owned by a team the person belongs to counts.
- **No Access list**: access through a team share or team ownership is reported and hidden on Teams/SPA, never revoked.
- **Records are never owned by the application identity**: Spaarke always resolves a team owner, and refuses the write if
  it cannot.
- `sprk_userentityassociation` is **not** team membership: it is Spaarke's own index of people ↔ records, rebuilt nightly
  by `MembershipReconciliationJob`.

**Where in the code**: `Infrastructure/Dataverse/SecureRecordOwnerTeam.cs`, `Services/Dataverse/RecordOwnershipResolver.cs`
(owner team lookups), `GrantExternalAccessEndpoint.ResolveGrantOwnerAsync`, `Services/Registration/RegistrationDataverseService.cs`
(team membership guard), `Services/Ai/Membership/MembershipResolverService.cs`.

---

## Building to UAC consistently — rules for developers

Follow these whenever you add a table, endpoint, wizard, job or UI that touches access. Each rule is what the existing
code already does; deviating needs an ADR conflict resolution (CLAUDE.md §6.5).

1. **Never decide access in the client.** The SPA, Code Pages and PCF controls ask the BFF; the BFF composes the record set
   (`AccessibleRecordSetService`) or asks Dataverse as the user.
2. **Fail closed.** An unreadable flag, grant, contact or list means *less* access, never more, and a set built over a
   failed read is never cached.
3. **Grants and shares go through the existing writers only**: the grant core (`GrantExternalAccessEndpoint.CreateGrantAsync`)
   and the one share writer (`DataverseWebApiService` Grant/Modify/Revoke). They apply the ceiling, never-lower,
   policy and No Access checks.
4. **`sprk_issecure` is written only by Make Secure / Remove Secure.** Generic writers are refused
   (`secure_flag_transition_only`).
5. **Records are owned by a team, never the app identity.** Use `RecordOwnershipResolver`; refuse when it cannot resolve.
6. **A new child table** must be added to the core-ancestor taxonomy (`CoreAncestorResolver`) and, if it holds record data,
   to the Secure family (`config/secure-record-owner-role.json` and `SecureChildLineage`) so that Make Secure moves it
   and the census guards it.
7. **New relationships stay `NoCascade`** for Assign, Reparent and Share; family-wide effects belong to the BFF and its jobs.
8. **No Dataverse plugins** (ADR-002). Anything done on save is a BFF write plus a backstop job.
9. **Files are streamed by the BFF** after an access check; partners never get a container permission.
10. **New SPA modules** declare their scope dimension (the core record or records they roll up to) in the module registry.

---

## Verification — answering "why can (or can't) this person see this record?"

1. **Identify where they are working.** MDA → Dataverse decides (step 2). SPA / Teams / file link → the BFF decides
   (step 3).
2. **MDA**: check the record's owner and business unit, the person's roles and their depth, their teams, and the record's
   shares (Share dialog, or `principalobjectaccess`). For a Secure record, the only legitimate routes are shares.
3. **SPA**: check, in order —
   - the record's **effective** Secure and Access Permission (including its parents);
   - the person's grants on the **core** record (`sprk_externalrecordaccess`: active, with a future expiry date);
   - their organization memberships and organization grants (ignored on Secure / Limited);
   - for contact-only workforce users, their standing grant;
   - the No Access list for the person, their organizations, and (Secure) the systemuser;
   - whether the contact is active and bound to the right identity plane.
4. **Allow for caching**: up to 60 seconds for grant changes and 2 minutes for share and team changes.
5. **Check the jobs** (App Insights, `spaarke-bff-*`): `[SECURE-CHILD-SHARES]`, `[SECURE-INHERIT]`, `[FOLLOW-PARENT]`,
   `[ASSIGNED-ACCESS-RECON]`, `[NO-ACCESS-RECON]` heartbeats with no failures; `[SECURE-CENSUS]` with no CRITICAL lines.

## Troubleshooting

| Symptom | Likely cause | Fix |
|---|---|---|
| Partner granted access but sees nothing | Grant has no expiry date or has expired; record is Restricted; person is on the No Access list; organization grant on a Secure / Limited record | Set an expiry date; check Access Permission and Secure; check the No Access list; grant the person directly |
| An organization grant is refused | The record (or a parent) is Secure or Limited | Grant the people directly, or change the policy if appropriate |
| A child record is visible on the parent in the SPA but not in the MDA | The child is user-owned or owned by a non-default team (reported, not moved), or the ordinary-moves switch is off, after an ordinary parent moved (§7) | Check `ResultJson.ordinaryMoves` of the `secure-child-reconciliation` job; reassign the child, or share it |
| Someone who should not see Secure records can | Their business unit is an ancestor of the secure one with a Deep Read role, or they hold Global Read | Check `[SECURE-CENSUS]`; move them out of the root business unit or narrow the role depth |
| Share button missing on a record | The record is Restricted (by design) | Use Manage Access "+ User" for internal users |
| Remove Secure refused | Caller is not a Full Access holder or the creator (403), or a parent is Secure (409 `access_follows_parent`) | Ask a Full Access holder; un-secure the parent instead |
| Change not visible yet | Caches (up to 2 minutes) or a background job not yet run (2–5 minutes) | Wait, then re-check; look for the job's log line |

## Related

- [uac-access-control.md](../architecture/uac-access-control.md) — architecture and design decisions behind UAC
- [external-access-spa-architecture.md](../architecture/external-access-spa-architecture.md) — the external SPA
- [SECURE-PROJECT-ENVIRONMENT-SETUP.md](SECURE-PROJECT-ENVIRONMENT-SETUP.md) — set up and verify Secure Record in an environment
- [EXTERNAL-ACCESS-ADMIN-SETUP.md](EXTERNAL-ACCESS-ADMIN-SETUP.md) — onboarding external users
- [access-inheritance.md](../data-model/access-inheritance.md), [child-access-permission.md](../data-model/child-access-permission.md) — data model
- [DATAVERSE-WRITE-PATH-ARCHITECTURE.md](../architecture/DATAVERSE-WRITE-PATH-ARCHITECTURE.md) — who writes what, and the no-plugin rule
