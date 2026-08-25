# Graph Endpoint setting — WIRE or DELETE

> **Task 021** (spec FR-C02) · 2026-08-23 · **Decision: DELETE**
> Escalation trigger evaluated and did **NOT** fire — no sovereign-cloud or per-environment
> requirement exists anywhere in the repo.

---

## 1. Premise check — right defect, wrong entity, and worse than described

The POML says:

> *"`ContainerTypeConfig` carries no endpoint field and `CreateGraphClient` hardcodes the base
> address."*

Both true, and both beside the point. **The field is not on the container-type config at all — it is
on `sprk_speenvironment`**, surfaced by `EnvironmentConfig.tsx`, not by any container-type screen. So
the POML's `<relevant-files>` named `ConfigDtos.cs`; the change is actually in `EnvironmentDtos.cs`
and `EnvironmentEndpoints.cs`.

More important, the POML frames it as *"a field that nothing reads"* — implying a cosmetic input with
a hardcoded default. **It is considerably worse than that.** The field has a complete, working
persistence pipeline:

| Stage | Present? |
|---|---|
| Rendered in the Settings screen with a default | ✅ `EnvironmentConfig.tsx:421` |
| Validated as HTTPS on **create** | ✅ `EnvironmentEndpoints.cs:454` |
| Validated as HTTPS on **update** | ✅ `EnvironmentEndpoints.cs:471` |
| Written to Dataverse on create | ✅ `sprk_graphendpoint`, `:536` |
| Written to Dataverse on update | ✅ `:559` |
| Read back in the `$select` | ✅ `:36` |
| Mapped to both response DTOs | ✅ `EnvironmentDtos.cs` `ToSummary()` + `ToDetail()` |
| **Consumed by anything that builds a Graph client** | ❌ **zero references in `Infrastructure/` or `Services/`** |

An administrator can change it, save successfully, reload, and see their change persisted — and it
affects nothing. That is not a cosmetic defect; it is durable state that lies, with a full round-trip
to make the lie convincing. It is the §2.4 systemic defect in its most confident form.

**It also had no test coverage at all** — a validated, persisted field, and not one test named it.

---

## 2. 🔒 The field is safe only because it is dead

`IsValidHttpsUrl` (`EnvironmentEndpoints.cs:479`) is the entire validation:

```csharp
return Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;
```

**Any** HTTPS URL passes. So had this field been wired as written, an administrator — or anyone who
could write an environment record — could point the BFF's Graph base address at
`https://attacker.example`, and the BFF would send **app-only Graph tokens** there. That is
token exfiltration, not a misconfiguration.

The field is currently harmless *because nothing reads it*. That makes it a landmine: it is one
well-meaning "let's finish wiring this up" commit away from being a credential leak, and the commit
would look like a bug fix.

This alone settles the decision. The POML's own constraint anticipated it — *"an unvalidated endpoint
field is an SSRF vector"* — but treated it as a condition on the WIRE branch. It is better read as an
argument against that branch.

---

## 3. Decision: **DELETE**

| Argument | Weight |
|---|---|
| **No requirement exists.** Nothing in `src/`, `docs/`, or `.claude/` mentions sovereign cloud (`graph.microsoft.us`, `microsoftgraph.chinacloudapi.cn`, GCC High, DoD). The only hits are inside `node_modules`. | Decisive — the escalation trigger's named condition is absent |
| **It would let an admin override a measured decision.** Task 020 pinned the container base address to `/beta` because `storageUsedInBytes` does not exist in the v1.0 schema, and guarded it with contract tests. A per-environment override lets someone set `…/v1.0` and silently lose the storage feature — reintroducing exactly what 020 protected. | Decisive |
| **Wiring it safely reduces it to nothing.** A correct allow-list is `{v1.0, beta}` — the two hosts the code already knows about. That is not a configuration surface, it is a two-value choice the code should make from measurement, not from a text box an admin types into. | Strong |
| **§11 cost-of-doing-nothing.** Nothing breaks without it. No behaviour depends on it, because nothing reads it. | Strong |
| **Security (§2).** | Strong |

**Escalation trigger did not fire.** Its condition is *"if a real operational need for a per-config
endpoint override is discovered (e.g. a sovereign-cloud customer)"*. None was — this was checked, not
assumed.

If a sovereign-cloud customer ever appears, the right shape is **not** this field. It is a cloud
selector (`Commercial | GCC High | DoD | China`) that picks a *whole* set of endpoints — Graph, login
authority, SharePoint host, Key Vault — because those move together. A lone Graph-host text box would
produce a half-migrated configuration that fails in ways nobody can diagnose.

---

## 4. What was removed

**Server**

| File | Removed |
|---|---|
| `EnvironmentDtos.cs` | `GraphEndpoint` from 4 DTOs; `GraphApiBaseUrl` from the Dataverse record; both `ToSummary()` / `ToDetail()` mappings; the dangling XML doc blocks |
| `EnvironmentEndpoints.cs` | `sprk_graphendpoint` from the `$select`; the create-response echo; **both** HTTPS validation blocks; the create body field; the update dictionary entry |

**Client**

| File | Removed |
|---|---|
| `EnvironmentConfig.tsx` | The `Field` + `Input`, the form-state property, the default value, the `formStateFromEnv` mapping, the trim, and both upsert payload properties (13 references) |
| `types/spe.ts` | `graphEndpoint` from `SpeEnvironment` and `SpeEnvironmentUpsert` |
| `BuContextPicker.tsx` | The `graphEndpoint: ""` stub |

`IsValidHttpsUrl` was **kept** — `rootSiteUrl` still uses it.

**Signposts left at both removal sites**, naming the date, the task, and this note. The constraint is
that a future reader must not mistake the absence for an oversight and "helpfully" restore it.

---

## 5. ⚠️ Acceptance criterion 4 is partially met — read this

> *"Negative (if DELETED): no orphaned endpoint value remains in stored config."*

**The `sprk_graphendpoint` column still exists on `sprk_speenvironment`, and existing rows still hold
values.** No code reads or writes it any more, but the data is there.

Deleting the column is a **Dataverse schema change**, which requires a solution update and is
irreversible for the stored values. That is an operator action, not a code change, and doing it
unannounced inside a code task would be exactly the kind of unilateral destructive step this project's
live-tenant rules forbid.

**Operator action required to fully close AC-4:**

1. Confirm no other consumer reads `sprk_graphendpoint` (this project's grep found none outside the
   code removed here).
2. Delete the `sprk_graphendpoint` column from `sprk_speenvironment` via the solution.
3. Publish the solution.

Until step 3, the column is orphaned-but-documented. The code-side signposts (§4) are what prevent a
future reader treating it as live config.

---

## 6. Deviation from step 4 — no WireMock test, deliberately

The POML's step 4 asks for *"WireMock coverage proving the outcome — either the field changes the
request host (WIRE) or the base address is centrally fixed (DELETE)."*

For the DELETE outcome there is nothing runtime to prove. The proof is the **absence of references**,
which the compiler and grep establish more strongly than a test could. The only test one could write
is a reflection assertion that `EnvironmentDetailDto` has no `GraphEndpoint` property — a **DTO-shape
test**, precisely the scaffolding class [ADR-038](../../../docs/adr/ADR-038-testing-strategy.md) bans
and task **042** is chartered to delete. Writing one to satisfy the step literally would mean adding
work for task 042 to undo.

The half of step 4 that *does* have value already exists: task 020's
`SpeAdminGraphVersionContractTests` pins the base address centrally and fails loudly if anyone changes
it. That is the "centrally fixed" assertion, and it predates this task.

`<steps mode="directional">`, so the binding contract is the goal, constraints, and acceptance
criteria — none of which require a test.

---

## 7. Gates

- **BFF build ✅** — 0 errors, 7 pre-existing warnings.
- **Tests ✅** — **10,652 passing**, 0 failed, 97 skipped (unchanged — **no test referenced this
  field**, which is itself part of the finding).
- **ArchTests ✅** — 36/36.
- **Publish ✅** — **43.67 MB compressed incl. PDBs, 0 MB delta**. Ceiling 60 MB. No package change.
- **Code page ✅** — `vite build`, 13.48 s. Bundle marginally smaller.
- **Client type-check ✅** — 0 errors in the touched files.

**Placement justification (root CLAUDE.md §10):** this change only *removes* surface — no new
endpoint, service, DI registration, package, or background work. Net reduction in BFF API surface and
in Dataverse write traffic.

⚠️ **Not verified against a running app.** The `<ui-tests>` need a deployment; the removal is verified
by compilation, grep, and the full test suite. Same standing gap as tasks 001 / 003 / 012 / 030.
