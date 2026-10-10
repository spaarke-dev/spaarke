# Task 181 — notify an internal user when they are given access (owner round 89 item 3)

> **POML**: [`tasks/181-internal-grant-notification.poml`](../tasks/181-internal-grant-notification.poml) (on the project branch)
> **Owner decision**: round 89 item 3, "Build it now" (`session27-owner-decisions-and-research.md`).
> **Code**: `Api/ExternalAccess/GrantAccessNotifier.cs` (new) · `GrantExternalAccessEndpoint.cs` · `InternalShareEndpoints.cs` ·
> `Services/NotificationService.cs` · `AccessGrantModal.tsx` · TrackingFieldTrio v1.0.46.

## 1. What it does

When an internal user is given access to a project, matter or work assignment, they get an in-app notification
(the model-driven app's bell) that links to the record:

> **You were given access to the matter "Smith v. Smith"**
> Gina Granter gave you Collaborate access.  · [Open matter]

Two triggers, both on the server, both **after** the write succeeded:

| Route | Who is told |
|---|---|
| `POST /grant` (a contact grant, not an organization grant) | every enabled person the contact **represents** |
| `POST /share-user` | the user the record was shared with |

It is sent only when the write **gave** access the person did not hold:

- `/grant`: a new grant, a lapsed key restored (round 80), or a higher level (`GrantUpsertOutcome.AccessGained`). A no-op
  re-grant, an expiry-only change or a lower level sends nothing.
- `/share-user`: the new mask adds a right the user did not hold (`(granted & ~current) != 0`). A same-level re-share
  (`unchanged`) and a lowered share send nothing.

Never sent to:

- the **granter** (their oid is resolved to a systemuser and excluded);
- an **external contact**: it represents no systemuser, so the `/grant` path finds nobody. The CIAM email of
  `/invite-and-grant` is unchanged;
- an **organization grant** (no fan-out, round 2 item 9);
- a user the secure record's **No Access** list walls off. The contact grant checks the contact's entries, not the
  user's, and the read path vetoes a walled user, so naming the record to them would leak it. Checked with
  `SecureShareNoAccessGuard.CheckRecordAndSecureParentsAsync`, the guard `/share-user` already runs. An unanswerable
  check tells nobody and reports a failure (ADR-003).

**Best effort.** Nothing in the notifier throws. A failed read or write is logged (`[GRANT-NOTIFY]`) and the route answers
`notificationFailed: true` (additive field on `GrantAccessResponse` and `ShareRecordWithUserResponse`, and an extension
on `/share-user`'s related-records-pending 500). The grant stands. All notifier calls use `CancellationToken.None`, so the
person is told even if the granter's browser has gone.

**The record name** is read app-only (`sprk_projectname` / `sprk_mattername` / `sprk_name`) inside the notifier, which the
routes call only after the write. If the read fails, the title says "a matter" and the link still works.

## 2. The modal

The dev notice "Internal notify (deep-link) is not yet available for internal workforce contacts (escalated; see project
notes)" is gone, and so is `notifyPending`.

| Outcome | Notice |
|---|---|
| success | `Granted access to N item(s).` |
| narrowed | `… Some were narrowed to your own access level on this record (you can only grant what you hold).` |
| server reports `notificationFailed` (200 body, or the share's related-records-pending 500) | `… Some people could not be notified; share the record link with them.` (warning) |

A batch with a failed notification keeps the modal open on Save once, as the related-records-pending warning does, so
the sentence is read. The modal sends nothing itself.

## 3. Reuse of task 100's mechanism (and one defect it had)

Task 100's channel is `NotificationService.CreateNotificationAsync` → a Dataverse `appnotification` owned by the
recipient, with `actionUrl = /main.aspx?etn=…&id=…&pagetype=entityrecord`. That is reused as is.

**Found while reusing it (fixed here):** the model-driven app's bell builds a clickable link only from the documented
`actions` array in `data` (Microsoft Learn, "Send in-app notifications within model-driven apps" → Notification
actions: `{ title, data: { url, navigationTarget } }`). `NotificationService` wrote only Spaarke's own `actionUrl` key,
which the bell ignores, so task 100's reminders were **not clickable** in the bell (only Spaarke's Daily Briefing reads
`actionUrl`). `NotificationService` now takes an optional `actionTitle`; with it, `data` also carries the URL action
(`navigationTarget: "inline"`). Callers that do not pass it are unchanged. The task-100 reminder job now passes
`"Open {record type}"`. The URL form `/main.aspx?…` is one the platform allows (a same-origin path beginning with `/`).

Coordination: open PR #1494 (task 132, appnotification option values) edits nearby lines of `NotificationService.cs`
and `GrantExpiryReminderJob.cs`; the hunks do not overlap. The notifier uses the default priority (Normal), which #1494
keeps valid.

## 4. Component justification (CLAUDE.md §11)

| New surface | Existing | Extension | Cost of doing nothing |
|---|---|---|---|
| `GrantAccessNotifier` (scoped, `ExternalAccessModule`) | `NotificationService` (channel), `GrantExpiryReminderJob` (a scheduled batch, not request-path), `AssignedAccessStore.ReadLinkCandidatesAsync` + `AssignedLinkCandidate.Represents` (who a contact represents), `InternalShareEndpoints.ClassifyEligibility` (person rule), `SecureShareNoAccessGuard` (walls). All reused; grep found no "you were given access" sender. | `NotificationService` is a generic channel used by AI and communications; putting recipient resolution, wall checks and access wording there would couple it to the access model. One class composes the reused pieces for the two routes. | Round 89 item 3 is unmet: an internal user given access is never told, and the modal keeps showing a dev message. |
| `NotificationService.CreateNotificationAsync(actionTitle)` (optional parameter) | the same method's `actionUrl` | This IS the extension: additive, default off. | The bell shows no link, so "linked to the record" fails for this task and for task 100's reminders. |
| `GrantAccessResponse.NotificationFailed`, `ShareRecordWithUserResponse.NotificationFailed` (additive fields) | `Narrowed`, the same additive pattern (task 139) | Extends the existing DTOs. | The modal cannot tell the granter to share the link (POML goal 4). |
| `GrantUpsertOutcome.AccessGained` | `GrantedLevel` / `Narrowed` on the same record | Extends the core's outcome. | `/grant` cannot tell a no-op re-grant from a new one, so it would notify on every re-grant. |
| `ExternalGrantRoot.NameColumnFor` / `LabelFor` | the private `RootSpec` table in `GrantExpiryReminderJob` | Lifted into the shared root map; the job now reads them from there (one copy). | A second copy of the three name columns and labels. |
| `AssignedLinkCandidate.Represents` | the materializer's inline lambda | Extracted; the materializer calls it. | The notifier would carry a second copy of task 141's representation rule. |

No new endpoint, package, Dataverse column or file surface.

**Considered and not reused:**
- `NoAccessShareEnforcer.UsersRepresentingAsync` — it OVER-matches by design: for a veto, every systemuser the contact
  might represent (any link, any contact bound to the oid) is the safe direction. Notifying is the opposite direction:
  over-matching names the record to someone who did not get access. The notifier therefore follows the READ path's rule
  (who the evaluator gives the contact's grants to), not the veto's.
- The `OutboxService` / `sprk_notificationoutbox` spine — its `kind` taxonomy is closed and the client's `KindRouter`
  drops unknown kinds, so a new kind reaches no screen without client work; task 100 recorded the same finding and the
  owner chose `appnotification` (the bell). Reusing task 100's channel is the POML's instruction.

**Who a contact represents — the read path's rule (fix round).** A user whose `sprk_primarycontact` names the contact is
honoured as is (the read path's primary rule). A user with NO link counts only when the binder's own oid decision
(`ContactBindingDecision.DecideBoundContact` over `IContactIdentityStore.FindContactsByOidAsync`, exactly what
`IdentityNormalizationService` asks) resolves the oid to THIS contact alone and active. Two contacts on the oid, or an
inactive one, resolve to no contact there, so nobody is told (not a failure). An inactive granted contact tells nobody.
A user whose link names ANOTHER contact is never this contact's (`AssignedLinkCandidate.Represents`). A failed binding
read is reported as a failure (could not tell).

**Name truncation** reuses `ChatHistoryManager.TruncateSurrogateSafe` (surrogate-safe cut + ellipsis). It is a pure
string helper in `Services/Ai/Chat`; ADR-013's facade rule concerns AI-capability types, and its ArchTest forbids only
those, so the call is not a CRUD→AI dependency in the ADR's sense. **CA1068:** `actionTitle` sits before the
`CancellationToken`; every caller passes named arguments, so the order change compiled without call-site edits.

## 5. Placement Justification (root CLAUDE.md §10, `.claude/constraints/bff-extensions.md`)

- **In the BFF, on the request path.** The notification must follow a write the BFF makes and must report its own failure
  in that response (POML goal 4), so it cannot move to a job or a Function (ADR-052: no F1–F5 signal; B2: it uses the
  grant core's outcome, the share route's confirmed mask and the BFF's No Access guard).
- **ADRs:** ADR-001 (no new route; existing Minimal API handlers gain a parameter), ADR-008 (the routes keep
  `DelegationRuleFilter`; nothing new is reachable without it), ADR-010 (concrete scoped class in the existing
  `ExternalAccessModule`), ADR-032 (every dependency is unconditional; no Null-Object), ADR-003 (an unanswerable wall
  check tells nobody), ADR-013 (no AI dependency).
- **Packages:** none. **CRUD→AI:** none. **Hot path:** BFF (`Api/ExternalAccess/**`, `Services/**`) and the shared
  `AccessGrantModal` + TrackingFieldTrio bundle. No SpaarkeAi, CI, skill or root-CLAUDE change.
- **Publish size:** +0.01 MB (§7). **CVE:** no vulnerable packages.

## 6. Known limits (stated, not hidden)

1. **The bell must be switched on** for the model-driven apps (task 100 §7, operator step). Without it the
   `appnotification` is written but not shown; the route still answers success (the write worked).
2. **A user without the model-driven app** (an unlicensed customer employee who uses only SPA/Teams, C7) has no bell.
   The row is written for them if they are a systemuser; they will not see it. No new channel was built (POML: only if
   the in-app notification cannot reach the user; it reaches every internal user who uses the model-driven app).
3. **The link opens the record in the model-driven app.** A user whose access comes ONLY from their contact's grant
   (no Dataverse share or role on the record) may not be able to open it there; contact-plane access is honoured by the
   BFF surfaces (Teams/SPA). The notification still tells them truthfully that they were given access.
4. **The granter is excluded by systemuser id.** If the caller's systemuser cannot be resolved, nobody is excluded: the
   worst case is a granter told about their own share.
5. **Latency.** A grant to a contact now adds a contact read and a link read (and, for a represented user, the wall check,
   the caller lookup, the name read and the write) to the response. A low-volume admin action; accepted.
6. **Category `access`.** The Daily Briefing groups by category; an unknown category falls back there as it does for any.

## 7. Verification

| Check | Result |
|---|---|
| Server tests (new `GrantAccessNotificationTests`, 10) | one notification for a linked-contact grant and for a user share, each linked to the record (title, body, `actions[0].data.url`); external contact → none; same-level re-share → none; same-level re-grant → none; lowered share → none; granter → none; walled user on a secure record → none (not a failure); a failed write → grant/share stands and `notificationFailed: true` (grant and share) |
| `NotificationServiceTests` | + `WithActionTitle_AddsTheBellsUrlAction`; the existing `actionUrl` test asserts no `actions` without a title |
| `GrantExpiryReminderJobTests` | `EachGrantableRoot_…LinksToIt` now asserts the URL action |
| Perturbations (applied, built, tested, restored) | 5/5 caught: no gain check on `/grant`; no gain check on `/share-user`; granter not excluded; walled user told; send failure swallowed |
| Targeted suites (notification, share, ceiling, Assigned-To, secure-root inheritance, child mirror, reminder) | 660/660 before the downgrade test was added |
| Jest `AccessGrantModal` | 10 suites, 196/196 (new `AccessGrantModal.notification.test.tsx`, 4; the old notify-pending test now asserts plain success and no "escalated; see project notes") |
| Publish size (`dotnet publish -c Release`, `Compress-Archive`, fresh short-path worktrees) | master `b71e83072` (`C:\wt181m`) **36.42 MB, 192 files** · branch (`C:\wt181`) **36.43 MB, 192 files** · delta **+0.01 MB** |
| CVE (`dotnet list package --vulnerable --include-transitive`) | no vulnerable packages (`Sprk.Bff.Api`) |
| TrackingFieldTrio | v1.0.46 in all five locations; `npm run build:prod` via `Invoke-PcfBuildProd.ps1` over a fresh `Spaarke.UI.Components` dist; bundle contains the new sentence, not the old notice; copied into `Solution/Controls/…` |
