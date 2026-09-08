# Task 025 — test-integrity remediation (H6 · M3 · M7)

> Written 2026-09-08 (session 4). **Perturbation-first**, per the POML: apply the break, confirm the
> zero-failure baseline, then write the test that makes it fail.

## 1. The four H6 seams and where they actually live

The findings doc cites `ExternalParticipationService.cs:471` for seam 3; the file has grown since
2026-08-24 and that line is now unrelated. Re-derived by symbol, not by line number:

| # | Seam | Real location (2026-09-08) | Perturbation applied |
|---|---|---|---|
| 1 | `FindPermissionByEmail` — the A-13 revoke matcher | `SpeContainerMembershipService.cs:356` | `string.Equals(upn, email, …)` → `false` |
| 2 | `CallerRecordAccessProbe.GetCallerRightsAsync` — **the project's central gate** | `CallerRecordAccessProbe.cs` | return `Read \| Write` unconditionally |
| 3 | Task 007's expiry filter **call sites** | `ExternalParticipationService.cs:813` (contact) and `:1000` (org) — the builders at `:61`/`:68` are what the tests assert | inline the pre-fix filter (no `ExpiryPredicate`) at the contact call site |
| 4 | `ListExternalMembersAsync` Graph call | `SpeContainerMembershipService.cs:226` | wrap the `GetAsync` in `try { } catch { return []; }` |

## 2. 🔴 A finding detail that is WRONG — M3's "zero call sites"

M3 states: *"the const-indirection mechanism the test documents matches **ZERO** real call sites —
`entity.associate_document` is found only by an unanchored regex accidentally matching an
`AssociateOperation = "…"` declaration."*

**The first half is wrong; the second half is right.** Verified empirically:

- Const-indirection **does** match a real call site: `BulkDownloadAuthorizationFilter.cs:170`
  `Operation = ReadOperation,` with `ReadOperation = "read"` declared at `:88` of the same file —
  exactly the shape the mechanism was written for. So the mechanism is **not** dead.
- `entity.associate_document` **is** nonetheless only accidentally covered. Its genuine call site is
  `EntityAccessFilter.cs:266` — `HasRequiredRights(rights, AssociateOperation)`, i.e. const
  indirection **through `HasRequiredRights`**, a mechanism the gate does not scan at all. The gate
  finds the operation only because the unanchored `Operation\s*=\s*"…"` pattern matches the const
  DECLARATION at `:87`. Rename the const, or move the declaration to another file, and the gate
  silently loses the operation while still reporting it covered.

So M3's *consequence* stands (accidental coverage, an anchoring bug, and a whole missing mechanism)
but its *count* does not. Recorded because this project's notes have been wrong repeatedly and the
correction matters: a future reader told "the mechanism matches zero" would reasonably delete it.

## 3. The missing mechanism, measured

`HasRequiredRights(...)` call sites are invisible to the gate. Actual counts (2026-09-08):

- `PermissionsEndpoints.cs` — **14** direct-literal call sites (lines 264–285). The findings doc says
  16; the extra two are a doc-comment mention at `:249` (correctly excluded by `IsCodeLine`) and
  presumably drift since 2026-08-24. **14 is the measured number.**
- `EntityAccessFilter.cs:266` — 1 const-indirection call site through `HasRequiredRights`.

## 4. 🔴 H6 seam 2 was ALREADY FIXED — one day after the review filed it

The POML says of `CallerRecordAccessProbe.GetCallerRightsAsync`: *"Its test must fail if the method
returns blanket rights. **Nothing else in this task matters more.**"*

**It already does.** Perturbing the gate to `return Read | Write` unconditionally fails **9 tests**:

| Test class | Failures |
|---|---|
| `DelegationProbeFailClosedTests` (7 cases) | no credential provider · incomplete BFF identity ×3 · no caller token · no environment URL · legacy identity keys with no secret |
| `EndpointAuthorizationCharacterizationTests` (2 cases) | `/external-access/grant` and `/close-project` deny a caller without Write on the target |

`tests/integration/auth/UnifiedAccessControl/DelegationProbeFailClosedTests.cs` was written by
**task 045 on 2026-08-25 — the day AFTER the 2026-08-24 review that filed H6**. Its own header
diagnoses the identical problem in the identical words (*"Every fixture in the suite SUBSTITUTES this
type … Substituting at a seam proves the CALLER, never the CALLEE"*). So the finding was stale before
task 025 was ever authored.

It is also honest about its limits — it proves the DENY paths, not that a real OBO exchange works
(that needs a live tenant; task 034 owns it). That limit is correct and is left as-is.

**Consequence for this task**: the item the POML ranks above all others is DONE, and no test needs to
be written for it. Recorded rather than quietly skipped, because "nothing else matters more" would
otherwise read as unaddressed. The remaining H6 work is seams 1, 3 and 4.

## 5. M7: the file does not merely assert false claims — it cannot fail

`tests/unit/Sprk.Bff.Api.Tests/Api/ExternalAccess/ExternalAccessEndpointTests.cs`, 1,004 lines,
**53** `[Fact]`/`[Theory]`, marked `[Trait("status", "repaired")]`.

Its class summary claims coverage of five endpoints. **No test invokes any of them.** Repo-grep for
`await`/`HttpClient`/`PostAsJsonAsync`/`CreateClient` finds exactly two hits, and both are
`await cacheMock.Object.RemoveAsync(...)` — a test calling its own mock and asserting the mock did
what the test just configured.

The archetype, which M7 names:

```csharp
var request = new GrantAccessRequest(ContactId: Guid.Empty, …);
(request.ContactId == Guid.Empty).Should().BeTrue("handler returns 400 when ContactId is empty GUID");
```

Three faults in five lines: it asserts `Guid.Empty == Guid.Empty` on a value the test itself just
set (a tautology that cannot fail); the quoted claim is **false** —
`GrantExternalAccessEndpoint.cs:77` reads `var isOrgGrant = request.ContactId == Guid.Empty;`, so an
empty ContactId is the documented ORG-GRANT signal and returns 200; and the file sits on a **non-KEEP
path**, which `tests/CLAUDE.md` calls "anti-pattern by construction".

Real coverage of these routes already exists at
`tests/integration/auth/UnifiedAccessControl/EndpointAuthorizationCharacterizationTests.cs` — which
demonstrably exercises the routes, since two of its cases fail under the seam-2 perturbation.

## 6. The measured baseline (the POML's step 0)

Clean suite, no perturbations: **12,191 passed / 0 failed / 58 skipped**.

| Perturbation applied | Suite result | Failures attributable |
|---|---|---|
| Seams **1 + 2 + 3 + 4** together | 12,182 passed / **9 failed** | all 9 are seam 2 |
| Seams **1 + 3 + 4** (seam 2 reverted) | **12,191 passed / 0 failed** | **zero** |

Running the union first and then isolating cost two suite runs instead of four, and gives the same
evidence: seams 1, 3 and 4 are executed by **no test** (confirmed 2026-09-08, not inherited from the
2026-08-24 review), and seam 2 is covered nine times over.

