# 🔔 BLOCKED — task 010, escalation trigger fired

> **Task**: `tasks/010-fix-mi-flag-gating-defect.poml` · **Raised**: 2026-08-20 · **Status**: awaiting owner decision
>
> This is the task's **own declared escalation trigger** firing exactly as written, on the file the POML calls
> *"the highest-blast-radius file in the library"*. Per root CLAUDE.md §6 (security-sensitive auth code) it is a
> legitimate stop, not a failure to push through.

---

## The trigger

> *"Correcting the gating breaks OBO in DataverseAccessDataSource — the OBO branch and the app-only branch are
> entangled there. STOP and report; this is the highest-blast-radius file in the library."*

**It is real, and I can now show exactly why.**

## What was found

`DataverseAccessDataSource.cs:53-77` uses **one `if`** to control **two unrelated things**:

```csharp
if (!string.IsNullOrEmpty(clientSecret) && !string.IsNullOrEmpty(tenantId) && !string.IsNullOrEmpty(clientId))
{
    _credential = new ClientSecretCredential(tenantId, clientId, clientSecret);   // (1) APP-ONLY credential
    _cca = ConfidentialClientApplicationBuilder                                    // (2) OBO confidential client
             .Create(clientId).WithClientSecret(clientSecret).Build();
}
else
{
    _credential = credential ?? new DefaultAzureCredential();                      // (1) app-only -> MI
    _cca = null;                          // (2) OBO DISABLED  <-- "No OBO support with managed identity"
}
```

The two concerns are independent in reality:

| Concern | What should gate it |
|---|---|
| **(1) `_credential`** — app-only token for `EnsureAuthenticatedAsync` | `Graph:ManagedIdentity:Enabled` — this is FR-A1's actual target |
| **(2) `_cca`** — the OBO exchange for **delegated user** access | Whether OBO config exists. **Nothing to do with the MI flag** |

### Why the prescribed fix would break OBO

Task 010 step 2 says to copy the gating shape from `DataverseWebApiService.cs`. That shape is a plain
`if (useManagedIdentity) { MI } else { secret }`. Applied here **verbatim**, `_cca` lands in the `else` branch,
so with `Graph:ManagedIdentity:Enabled=true` — which is the intended dev end-state, and is already set live —
`_cca` becomes `null`. Then at `:107`:

```csharp
if (_cca == null)
    throw new InvalidOperationException("OBO authentication requires client credentials to be configured. …");
```

**Every delegated Dataverse access-check throws.** That is the fail-closed blast radius this whole project is
built to avoid, and it would be introduced by the task meant to be a safe prerequisite.

`DataverseWebApiService` is a safe template only because it has **no OBO path**. `DataverseWebApiClient` is the
same — I already applied the gating fix there and it builds clean. `DataverseAccessDataSource` is the one file
in the group where the template does not transfer.

---

## Recommended resolution — decouple the two branches

Split the single `if` into two independent decisions:

```csharp
// (1) APP-ONLY credential — gated by the flag (FR-A1, the actual defect)
var useManagedIdentity = string.Equals(
    configuration["Graph:ManagedIdentity:Enabled"], "true", StringComparison.OrdinalIgnoreCase);

_credential = useManagedIdentity
    ? (credential ?? new DefaultAzureCredential(/* UAMI-pinned */))
    : new ClientSecretCredential(tenantId, clientId, clientSecret);

// (2) OBO confidential client — INDEPENDENT of the MI flag.
//     Built whenever OBO config exists, because DefaultAzureCredential cannot perform an OBO
//     exchange (ADR-028 A4) and the MI flag says nothing about delegated access.
_cca = HasOboConfig(tenantId, clientId, clientSecret)
    ? BuildCca(tenantId, clientId, clientSecret)
    : null;
```

**Why this is the right shape, not just a workaround**: it makes the code say what is actually true — app-only
auth and delegated auth are different concerns with different credentials. It is also precisely the seam Phase 2
needs: task 020 replaces `BuildCca`'s credential with the MI-FIC assertion **without touching the app-only
branch at all**. Leaving them entangled would force task 022 to untangle this under far more pressure, on the
migration with the highest blast radius.

### Why I stopped rather than just doing it

1. The POML explicitly declares this a **STOP** condition on this exact file.
2. It is **security-sensitive auth code on a fail-closed path** — root CLAUDE.md §6 requires human input.
3. The fix **changes the shape the task prescribed** (step 2 says "copy `DataverseWebApiService`"), so it is a
   scope judgment, not a mechanical edit.
4. Getting it wrong locks every user out of document and AI authorization, immediately and totally.

---

## What is already done and safe

| Item | Status |
|---|---|
| `DataverseWebApiClient.cs` gating fix (~20 lines, no OBO path) | ✅ Applied, builds clean |
| `parallel-safe` metadata on 010 + 011 | ✅ **Corrected** — see below |
| `DataverseAccessDataSource.cs` | ⛔ **Untouched, pending this decision** |
| Seam test (`CredentialSelectionSeamTests.cs`) | ⏸️ Deferred until the shape is settled |

### Secondary finding, already fixed

**Group A was misclassified as parallel-safe.** Both 010 and 011 declare `parallel-safe: true`, and TASK-INDEX
claimed *"different files"* — but **both modify `DataverseAccessDataSource.cs`**. Dispatched concurrently as
sub-agents they would collide. Corrected to `parallel-safe: false` on both, with the group re-marked `010 → 011`
sequential. Worth noting the classification error was in the authored metadata, so any future dispatcher would
have hit it.

---

## Decision needed

**Approve the decoupling** (recommended), or direct an alternative:

| Option | Consequence |
|---|---|
| **A — Decouple** (recommended) | `_credential` flag-gated, `_cca` independent. Fixes FR-A1 without touching OBO, and gives task 020 the exact seam it needs |
| **B — Defer `DataverseAccessDataSource` to Phase 2** | Task 010 completes with only `DataverseWebApiClient` fixed. The defect persists in the more important file, and task 022 inherits the entanglement |
| **C — Copy the template verbatim** | ❌ **Do not.** Breaks OBO the moment MI is enabled |

Nothing is deployed. The slot still runs the clean pre-task build.
