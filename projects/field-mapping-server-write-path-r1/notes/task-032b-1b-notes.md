# Task 032b-1b: invoice Assigned To targets people without conferring access

Branch `task/fm-032b-1b` (from `work/field-mapping-server-write-path-r1` 8bb685a52d).
Binding input: `notes/decisions.md`, last section ("Owner decision (corrected U7): invoice Assigned To targets people
without conferring access").

## 0. Finding to read first: the four display copies WERE targeted before 032b-1

The brief said to stop and report if the four attorney and paralegal columns were targeted through the registry before
032b-1. They were. Before 79b1aab39c the registry's `sprk_invoice` entry listed all six columns as Contact-typed:
`sprk_assignedto1/2` and `sprk_assignedtoattorney1/2`, `sprk_assignedtoparalegal1/2`.
`FilterToPeopleTargetingTermsAsync` admitted every Contact-typed registry column of the entity, so the Daily Briefing
selected an invoice through any of the six.

This is the change the owner already accepted as U7 on 2026-11-08 ("Daily Briefing stops selecting invoices through the
copied assignee columns"). The later correction restores Assigned To only. So this task did not change the four columns:
they stay untargeted, and a must-not-fire test pins it. If the owner wants them targeted again, that is a separate
decision and a one-line addition to the list.

## 1. What changed

- `MembershipOptionsDefaults.TargetingOnlyColumns` (internal static, keyed by table, next to `DisplayCopyColumns`):
  `sprk_invoice` -> `sprk_assignedto1`, `sprk_assignedto2`. Documented as conferring no access, not operator-configurable.
- `MembershipResolverService.FilterToPeopleTargetingTermsAsync` unions that list into its Contact column set. A column
  therefore resolves exactly as a registry Contact column did: it binds only through the caller's linked contact, with no
  email or name fallback (the C7 rule).
- `MembershipResolverService.CacheVersion` 7 -> 8: the query semantics changed under an unchanged options hash (the
  documented "bump REQUIRED" case), so a pre-deploy cached answer is not served for up to 2 minutes.
- The `DisplayCopyColumns` comment that called targeting "an open owner decision" now says it is decided and points at the
  list.

**Consumers.** Every consumer of the people-targeting surface reads it through `MembershipResolverService.ResolveAsync`
with `MembershipResolveOptions.People`, so the one filter restores all of them:
`DailyBriefingCollector` (invoice channel, via `PeopleTargetedSet`), `PortfolioService`, `BriefingService`, and the
`LookupUserMembership` playbook node with `targeting: "people"` (the path notification and briefing playbooks use).
The 032b-1 commits changed only `MembershipOptions.cs` among the registry readers (`git show --stat 79b1aab39c 4d24082a95`),
and the grep of `src` for the registry and for `sprk_assignedto1/2` finds no other targeting reader. `NotificationService`,
`OutboxService` and `AssignTaskNotifier` do not select by registry columns.

## 2. Invariants

- The registry has no invoice entry; `InvoiceDisplayCopyRegistryGuardTests` still passes unchanged.
- `TargetingOnlyColumnsGuardTests` (new): the list is disjoint from the canonical registry, the bound registry and the
  display copies; the only source files naming `TargetingOnlyColumns` are `MembershipOptions.cs` (definition) and
  `MembershipResolverService.cs`; and inside the resolver the access-conferring filter body does not name it. Must-fire
  controls: a registry seeded with the Assigned To columns yields two overlaps; a seeded `AssignedAccessMaterializer.cs`
  that names the list is flagged; an access filter that names it is flagged. Must-not-fire control: files that never name
  the list pass.
- A source-scan unit test under `tests/unit/domain/Membership` was chosen over an ArchTest: it needs no new project wiring
  and runs in the filtered suite. `AssignedAccessMaterializer` reads `AccessConferringRoles` only (grep: no reference to
  the list), and every grant path (`GrantExternalAccessEndpoint`, `InternalShareEndpoints`, the grant jobs) reaches
  membership through the registry-filtered planes.
- `NoAccessGrantResults_FromInvoiceAssignedTo`: `ResolveAsync(AccessConferringOnly)` and `ResolveByContactAsync` on an
  invoice return nothing and emit no `sprk_assignedto` condition.

## 3. Tests

`tests/unit/domain/Membership/InvoicePeopleTargetingAfterDisplayCopyTests.cs` (updated to the new rule) and
`TargetingOnlyColumnsGuardTests.cs` (new). The Daily Briefing tests run `PeopleTargetedSet` over the real resolver with a
Dataverse stub that returns only rows the emitted filter selects, so a column the query never names selects nothing.

| Case | Test |
|---|---|
| `sprk_assignedto1` / `sprk_assignedto2` contact linked to U targets U (Daily Briefing read, FetchXML) | `TheDailyBriefingRead_ReturnsAnInvoice_WhoseAssignedToIsTheUsersContact` x2, `AnInvoice_IsForTheUserWhoseContactIsNamedInAssignedTo` x2 |
| owner and creator still target | `AnInvoice_StillTargetsThroughOwnerAndCreator` |
| display-copy attorney column does not target (must-not-fire) | `AnInvoice_NeverTargetsThroughTheFourDisplayCopyColumns`, `TheDailyBriefingRead_DoesNotReturnAnInvoice_NamedOnlyInADisplayCopyColumn` x4 |
| no linked contact binds no Assigned To term (must-not-fire) | `AnInvoice_WithNoLinkedContact_BindsNoAssignedToTerm` |
| no access grant results | `NoAccessGrantResults_FromInvoiceAssignedTo` |
| control: a matter still targets through its Assigned column | `Control_AMatter_IsStillForThePersonNamedInItsAssignedColumn` |

## 4. §11 answers for the new list

- **Existing:** it overlaps with the access-conferring registry (`AccessConferringRoles`), which the people-targeting filter
  read before 032b-1, and with `DisplayCopyColumns`, which names columns that do not target.
- **Extension:** the registry cannot hold these columns, because anything in it confers access (U3 (c)); `DisplayCopyColumns`
  means "never targets". Neither can carry "targets, no access" without changing its meaning. The list is a second static
  table of the same shape, not a new abstraction, interface or registration.
- **Cost of doing nothing:** Office quick-create writes the invoice's Assigned To to `sprk_assignedto1`, and the Daily
  Briefing, portfolio and notification playbooks stop selecting that invoice for the person it was assigned to. This
  breaks the standing rule "notifications target Created By and Assigned To" (owner, 2026-11-08).

## 5. Placement

BFF, in-process, in the existing `Services/Ai/Membership` folder: one static table in `MembershipOptions.cs` and four lines
in the existing people-targeting filter. No endpoint, DI registration, package, background work, Dataverse column or file
surface is added. The list is not bound from configuration, so no operator setting can widen it.

## 6. Evidence

| Check | Before (list empty) | After |
|---|---|---|
| New and updated membership tests (`Domain.Membership`) | 8 failed, 16 passed | 24 passed |
| Filtered run (Membership, DailyBriefing, Notification, invoice and registry guards, AssignedAccess, push selection, sync-mode, seeder, portfolio) | | 1444 passed, 0 failed, 1 skipped (pre-existing) |
| ArchTests (seeder class, role privilege, membership) | | 55 passed |
| BFF build | | 0 warnings, 0 errors |
| `dotnet list package --vulnerable --include-transitive` | | no vulnerable packages |

Failing before: the two `IsForTheUserWhoseContactIsNamedInAssignedTo` rows, the two `TheDailyBriefingRead_Returns...` rows,
`AnInvoice_NeverTargetsThroughTheFourDisplayCopyColumns` (it also asserts the contact is bound twice), and three guard
tests that need the list's content.

## 7. Found in passing

- `task-032b-1-notes.md` section 4 still says `sprk_assignedto1/2` "are now plain data" with no targeting. That is true of
  the registry and push but no longer of targeting. Left as written (it is a dated record); this note supersedes it.
- `sprk_assignedto1/2` are still not display copies, so a Manual Refresh rule onto them would push (unchanged; no seeded
  profile targets them).
- Cache TTL is 2 minutes, so a deployed BFF serves the new invoice set within that window even without the version bump;
  the bump only removes the window.

rework: none
