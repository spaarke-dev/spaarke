# Phase 4 — live-environment acceptance items (task 061)

> These are the FR-28 claims that code and CI **cannot** establish. They are recorded here as UAT
> obligations rather than asserted by this task. Written 2026-09-08.

## Why these cannot be claimed from CI

design.md **§5.1a-2** is the reason, and it is load-bearing: *a child business unit does NOT isolate
anything from a role with `Deep` depth from the root*. Users currently sit in the **root** BU, and
`Secure Project` is a **child** of root — so today a Deep-depth role still reaches secure records
regardless of ownership or sharing. §5.2's BU restructure (move users into `Spaarke Operations`; make
`Secure Project` a **sibling**, not a descendant) is the fix, and it was reclassified to environment
setup by owner decision 2026-08-21.

**Consequence for this task**: the share path is now correct and tested, but "only the shared user can
read it" is not observable in dev until the restructure lands. Task 061 does not claim it.

## Acceptance items — owner/UAT to verify after the §5.2 restructure

| # | Assertion | How to verify | Depends on |
|---|---|---|---|
| U-1 | An Operations user with **no** share **cannot** read a provisioned secure project (MDA + SPA) | Sign in as a non-shared licensed user; open the project by URL; expect no access | §5.2 restructure |
| U-2 | The **creating** attorney **can** read and edit it immediately after the wizard finishes | Create a secure project; without any further action, open it | — (testable now, modulo U-1's caveat) |
| U-3 | A user named in `SharePrincipalIds` can read/edit but **cannot re-share** | Provision with a colleague id; as that colleague, attempt to share | — |
| U-4 | After `/unsecure-project`, the record is reachable by normal BU/role access and carries **zero** POA rows | Run the endpoint; query `principalobjectaccessset` for the record | — |
| U-5 | `/unsecure-project` is idempotent against a live row | Call twice; second call returns 200 `alreadyUnsecure: true` and writes nothing | — |
| U-6 | The delegation gate really 403s a caller without Write **on this route** | Call `/provision-project` and `/unsecure-project` as a user without Write | — |
| U-7 | The Secure Project **owner team holds the `Secure Project Owner` role** | Without it Dataverse refuses the assignment (design §5.1a) — U-2 failing with an assignment error is this, not a code fault | environment setup |

## The migration item the POML's escalation trigger names

**No migration is performed by this task.** If dev/UAT contains secure projects provisioned under the
**old per-project-BU model**, they carry a `sprk_securitybu` value and sit in a BU that is not the
canonical `Secure Project`. Task 061 neither detects nor moves them.

`ProvisionProjectEndpoint` already refuses to re-provision such a row (`ReasonLegacyPerProjectBu`), so
they fail closed rather than being silently mixed into the new model. **Inventory + remediation is a
UAT/runbook item**, per the POML's escalation trigger and CLAUDE.md §6.

## Not delivered by 061, and deliberately

- **Access teams (design §5.1b).** Preferred over per-user shares — one POA row per record, revocation
  by membership delete. Requires an entity **team template** on `sprk_project`, which is environment
  configuration. The seam takes a `DataversePrincipalRef`, so the migration is a change of principal
  at one call site rather than a rewrite. See `notes/phase4-061-provisioning-inventory.md` §4 D4.
- **Wizard copy for the reverse path** — task 068 owns the UI; 061 ships the server capability so 068
  has something to call.

---

## Publish size (task 061)

Both sides built in **fresh worktrees** and zipped with PowerShell `Compress-Archive -Optimal` — the
method `scripts/Deploy-BffApi.ps1` uses, and the discipline task 060's notes §8 established after an
in-place rebuild produced a false +4.95 MB.

| Build | Size | PDB |
|---|---|---|
| pre-061 `196d60d40` | 45.38 MB | 2,302.6 KB |
| post-061 `01f0996cc` | 45.38 MB | 2,306.2 KB |

**Task 061 delta: +0.01 MB.** Headroom to the 60 MB ceiling: **14.62 MB**. No new NuGet packages;
`dotnet list package --vulnerable --include-transitive` reports **0 vulnerable**. The PDBs agreeing to
within 4 KB is the corroboration that the measurement method is stable — the 3× PDB divergence that
produced task 060's false reading does not appear when both sides are fresh.
