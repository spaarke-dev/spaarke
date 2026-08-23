# Task 030 — lifecycle constraints in the UI

> **Task 030** (spec FR-C13) · 2026-08-23 · **partially delivered — 🔔 escalation trigger fired on the quota**
> Client-only. No server file changed. No destructive or irreversible action performed.

---

## 1. Premise check — the constraint values are sound, the scope is not

Unlike the previous eleven, this POML's **facts** hold up. Every constraint it names is confirmed in
`knowledge/sharepoint-embedded/docs/learn-containertypes.md` (Microsoft Learn, fetched 2026-05-14):

| POML claim | Source | Verdict |
|---|---|---|
| owning app ↔ container type is 1:1 | `:11`, `:95` | ✅ |
| container-type ID immutable | `:13` | ✅ |
| trial cannot convert to production | `:21` | ✅ |
| standard ↔ pass-through cannot convert | `:22` | ✅ — and it is **bidirectional**; the POML states only one direction |
| max 25 per tenant | `:75` | ⚠️ correct, but the sentence sits under *"Standard container types (nontrial)"* — it reads as the production ceiling, not a total |
| at most one trial | `:61` | ✅ |
| only trial types deletable | `:109` | ✅ |

**Five constraints the POML did not name** are in the same source, and two of them are the sharpest
traps in the flow:

- 🔴 **A trial container type expires after 30 days** (`:69`) and is not renewable. Nothing in the app
  said so. This is the purest form of the defect class this project exists to remove — not "discovered
  by failing" but *never discovered*, until a working type stops working.
- 🔴 **A trial type cannot be registered on another consuming tenant** (`:71`) — yet the page offered a
  **Register** button for it. An action that could only fail.
- A standard type needs a billing profile attached in PowerShell (`:89`–`:93`), which this app cannot do.
- A trial is capped at 5 containers × 1 GB (`:67`–`:68`).
- Deleting a container type first requires permanently deleting every container of that type,
  including from the deleted-container collection (`:109`).

**Where the POML is wrong is its scope**, not its facts: three of its five constraints cannot be
satisfied client-only, because the data they need never reaches the client. See §3.

---

## 2. 🔴 Found while verifying — row selection has never worked

Not in the POML, found by tracing the identity field the constraints hang off.

The BFF serialises the identifier as **`id`** (`ContainerTypeDto`, `[JsonPropertyName("id")]`). The
client declares it as **`containerTypeId`** (`types/spe.ts:184`). Nothing converts between them, and
`speApiClient.containerTypes.list()` **casts** the response rather than parsing it — so TypeScript
never saw the mismatch:

```ts
return get<{ items: ContainerType[]; count: number }>(…).then(r => r.items);
```

Consequences, all silent:

| Site | Effect |
|---|---|
| `getRowId={(ct) => ct.containerTypeId}` | `undefined` for **every** row — duplicate React keys |
| `onRowClick(item.containerTypeId)` | sets `selectedTypeId` to `undefined` |
| `hasSelectedType` | **permanently false** |
| `RegisterWizard initialTypeId` | **always `null`** |

Clicking a row appeared to do nothing. The signature shape again — a lower layer returning something
the upper layer reads as benign absence. **Fixed** in this task, contained to the screen
(`normalizeContainerType`), because row selection is a prerequisite for the conditional Register.

---

## 3. 🔴 The server drops three fields the constraints need

`owningAppId`, `azureTenantId`, and `expiryDateTime` are **absent from both** the domain record and
the DTO — they are not merely unmapped at the edge:

```csharp
record SpeContainerTypeSummary(Id, DisplayName, Description, BillingClassification, CreatedDateTime);
record ContainerTypeDto        { Id, DisplayName, Description, BillingClassification, CreatedDateTime }
```

The client asks for all three (`types/spe.ts:182-199`), so today:

- the grid's **"Owning App" column is blank for every row**;
- the grid's **"Registered" badge reads "No" for every row** (`isRegistered` is never sent, and
  `ct.isRegistered === true ? "Yes" : "No"` resolves the absence to "No");
- `ContainerTypeDetail.tsx:737` renders `owningAppId ?? "—"`;
- `ContainerTypeDetail.tsx:740` guards the **trial-expiry warning** on `expiryDateTime`, so the
  warning about the 30-day expiry **can never render**.

Fixing this means editing `SpeAdminGraphService.cs` (the god-file) and `ContainerTypeDtos.cs` — outside
this task's declared `<outputs>`, and inside territory tasks **023** and **025** already own.

---

## 4. 🔔 Escalation — the quota cannot be computed honestly

The POML's trigger, verbatim:

> *"If any constraint value cannot be determined from the API (e.g. tenant-wide container-type count is
> not readable under the app's permissions, making the 25-limit quota display impossible), STOP and
> escalate — displaying a guessed quota is worse than displaying none."*

**It fires, on exactly the named case.** Container-type LIST runs delegated as the signed-in user
(`ListContainerTypesForUserAsync` → `IGraphClientFactory.ForUserAsync`), and task 012 proved the BFF
cannot observe whether that user holds the Entra role that widens visibility tenant-wide — the `wids`
claim never reaches the token, even for a **confirmed** role holder. So the list is a **lower bound**
on the tenant's true count, never a census. `22 of 25 remaining` would be a guess wearing a fact's
clothing.

### But the two limits are not symmetric, and that is usable

| Limit | Can the visible list prove it? |
|---|---|
| **one trial per tenant** | **Yes, in the blocking direction.** Seeing a trial type *proves* one exists → block with certainty. Not seeing one proves nothing → must not claim the slot is free. |
| **25 per tenant** | **No, in either direction.** A lower bound can neither prove the ceiling is reached nor that headroom exists. |

So the delivered behaviour asserts only what the data supports: it **states both documented limits**,
reports the observed count **labelled for what it is**, blocks trial creation when a trial is visible,
and **never publishes a "remaining" number**. `describeProductionQuota()` returns `atLimit: false`
unconditionally, with a comment saying why.

That is a deliberate deviation from acceptance criterion 3 (*"Remaining quota is displayed … at the
limit, creation is blocked"*). **Half of it is met** — the limits are displayed and trial creation is
blocked on proof. The "remaining" figure is **knowingly not implemented**, per the trigger.

### Options

| | Action | Cost |
|---|---|---|
| **A — accept as delivered (recommended)** | State limits + observed count + scope caveat; block only on proof. | AC-3 partially met. No "remaining" figure anywhere. |
| **B — make the count authoritative** | Have the BFF read container types with an identity holding the SPE Administrator role, so the list is a true census. | New app-only path for a **delegated-only** API (403 on both versions — task 010). Not currently possible. |
| **C — display remaining from the visible count** | Compute `25 − observed`. | Publishes a guess as a fact. This is the defect class the project removes. **Rejected.** |

---

## 5. What was delivered

| Constraint (POML) | Status |
|---|---|
| 1 — immutable ID + owning app, no edit affordance | ✅ **already satisfied**; verified, not assumed. `ContainerTypeSettingsForm` edits only sharing capability, versioning, version limit, max storage, item search. ID renders as a badge; owning app as read-only text. No disabled-looking input exists. |
| 2 — permanence stated before submit | ✅ **delivered in full**, and broadened to the five unnamed constraints in §1 |
| 3 — quota | ⚠️ **partial by decision** — see §4 |
| 4 — delete conditional on trial | ❌ **not delivered** — see below |
| 5 — 1:1 owning app explicit in creation | ⚠️ **stated, not shown** — the statement is in the flow; the owning app *value* cannot be displayed (§3) |

### Constraint 4 has nothing to make conditional

There is **no container-type delete affordance anywhere** — not in the page, not in the detail panel —
and **no `containerTypes.delete` in the API client**. The constraint asks that an existing affordance
become conditional; the affordance does not exist. Building one means a new BFF DELETE endpoint plus
destructive verification against a live tenant holding real working documents (project CLAUDE.md
live-tenant safety). Both are outside a client-only task.

What *was* done instead: the creation flow now states plainly, before submit, that a standard or
pass-through type **can never be deleted** — which is the decision point where that fact actually
changes an admin's behaviour.

### Files changed (all client, all inside `<outputs>`)

| File | Change |
|---|---|
| `containerTypeLifecycle.ts` **(new)** | The sourced constraint data — every fact carries a line reference into the knowledge corpus. Pure data, no JSX, so it is assertable in tests and re-checkable when the corpus refreshes (task 061). |
| `CreateContainerTypeDialog.tsx` | Per-classification consequences shown live before submit, severity-coded; quota bar; acknowledgment gate for the classifications that can never be deleted; blocks submit when a limit is proven. |
| `ContainerTypesPage.tsx` | `normalizeContainerType` (§2); Register disabled when nothing is selected **or** the selection is trial, with a tooltip naming the reason; feeds the visible list to the dialog. |

**Design note — the acknowledgment gate is proportionate, not blanket.** Trial is deletable and
expires, so showing the consequences is enough. Standard and pass-through can *never* be deleted, so
those require an explicit checkbox. Gating everything equally would train the reflex it is meant to
interrupt.

### Gates

- **Build ✅** — `vite build`, 3,380 modules, 15.47 s.
- **Type-check** — **0 errors in the three touched files.** 38 pre-existing errors elsewhere in the
  app (unrelated: unresolved `@lexical/*`, `@azure/msal-browser`, `@hello-pangea/dnd` in the shared
  libraries, plus `"tinted"`/`"tint"` typos in `containers/`). Not introduced here, not in scope.
- **No server file touched** — no publish-size, NuGet, or CVE surface. No `.cs` change.
- **No destructive action** — AC-7 holds; nothing was created, modified, or deleted in any tenant.

### ADR compliance

- **ADR-021** ✅ — Fluent v9 semantic tokens only; no hex literals. Severity is carried by icon **and**
  wording, not colour alone.
- **ADR-050** — n/a as scoped. The constraint applies to *confirmation dialogs*; the only confirmation
  this task would have added belongs to the delete affordance that does not exist. The creation dialog
  is a pre-existing Fluent `Dialog`, not converted here — converting it is a `SprkModal` migration, not
  a lifecycle-constraint change, and `SprkModal` is not currently exported from
  `@spaarke/ui-components`'s public index.

---

## 6. Handoff

| Item | Owner |
|---|---|
| `owningAppId` / `azureTenantId` / `expiryDateTime` on summary + DTO (§3) — unblocks constraints 1, 5, the "Registered" badge, and the trial-expiry warning | **023** or **025** (both already in `SpeAdminGraphService.cs` + DTOs) |
| Container-type DELETE endpoint + trial-conditional affordance (§5) | new task, or **050**'s wave — needs a throwaway type |
| `speApiClient` casts responses instead of parsing them (§2) — the class of bug, not just this instance | **042** (test retirement) or a follow-up |
| Quota census (§4 option B) | blocked on the delegated-only constraint from task 010 |
