# Coordination — `customer-provisioning-orchestration-r1` vs the D-12/D-13 remediation

> **Written 2026-09-28** in answer to the owner's question: *"how does this impact the
> customer-provisioning-orchestration-r1 project? … will it overwrite/revert the work done here?
> likewise for the code (and scripts)?"*
> **Short answer**: **the docs are fine; the CODE is not.** Details below, all measured.

---

## 1. State of that branch — measured, not assumed

| Fact | Value |
|---|---|
| Branch | `work/customer-provisioning-orchestration-r1` |
| Worktree | `C:/code_files/spaarke-wt-customer-provisioning-orchestration-r1` @ `0b799b592` |
| **Uncommitted work there** | ✅ **none** (clean) |
| Local vs its own remote | ✅ in sync (0 ahead) |
| **Unmerged commits vs master** | 🔴 **99** |
| **Behind master** | 🔴 **583** |
| Last remote activity | **2026-09-07** (3 weeks) |
| **Open PR** | ❌ **none** |

So it is **dormant but not abandoned**: 99 commits of real work (tasks 206–215, sessions 19–22), nothing
queued to merge, nothing at risk of being lost right now.

---

## 2. Will it overwrite or revert this work? — **No, not silently**

Git performs a **3-way merge** against the common ancestor:

| Case | Outcome |
|---|---|
| Lines only **I** changed | ✅ mine survive |
| Lines only **they** changed | their change applies |
| Lines **both** changed | 🔶 **conflict** — a human must resolve |

**There is no mechanism by which their merge silently reverts an edit I made.** A reversion would require
someone to resolve a conflict in favour of the older text.

### ⚠️ But there IS a silent failure mode, and it is the real risk

**Incoherent auto-merge.** Their 99 commits added content written **while the shared tier was still the
premise**. Where they touched *different lines* of a file I rewrote, git merges **cleanly** — and the result
is my *"the shared tier is RETIRED"* banner sitting alongside their new shared-tier-premised prose, **with
no conflict marker to flag it**.

That is premise rot arriving by merge. It is exactly the failure class this whole remediation exists to fix.

---

## 3. Docs + scripts — 9 files of true overlap

Measured against **only** the D-12 remediation commits (`6cfabe18f..HEAD`, 61 files), not the whole branch:

| File | Their unmerged diff vs master | Risk |
|---|---|---|
| 🔴 `.claude/skills/provision-environment/SKILL.md` | **+1467 / −139** | **Highest.** My 5 edits are tiny beside a near-total rewrite. A "take theirs" resolution silently restores the retired-tier `$validProfiles` guidance and drops the hard stop. |
| 🔶 `docs/guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md` | +62 / −3 | I replaced §3 wholesale — conflict likely, which is the *good* outcome. |
| 🔶 `projects/…/design.md` | +42 / −13 | Their additions predate D3 v4. |
| `projects/…/spec.md` | +9 / −8 | Small. |
| `projects/…/CLAUDE.md` | +1 / −1 | Trivial. |
| `projects/…/current-task.md` | — | Rolling log; theirs is newer for their work. Let theirs win. |
| `scripts/provisioning-prereqs/prereqs.yaml` | — | ⚠️ I made a **structural** fix here (the file did not parse at all). Must not be lost. |
| `.claude/adr/ADR-028-…md`, `docs/architecture/SPAARKE-SPE-CONTAINER-TYPE-TOPOLOGY.md` | — | Moderate. |

✅ **Reassuring measurement**: shared-tier mention counts in **their** branch versions equal **master's**
(design.md 30/30, deployment guide 15/15). Their 99 commits **did not add new shared-tier content** — they
inherited it. So a merge would not *import* fresh shared-tier claims; the exposure is limited to conflict
resolution and the incoherent-auto-merge case above.

---

## 4. 🔴 The CODE is a different story — direct collision on the exact targets

Their 99 commits touch **170** files under `Sprk.Provisioning.ControlPlane*` / `infrastructure/bicep` /
`.github/workflows`. Among them, **five of the exact files the D-12/D-13 code remediation must change**:

| File | Why I need it |
|---|---|
| 🔴 `Handlers/EntraAppReg/H3EntraAppRegHandler.cs` | **the D-13 fix** — delete the shared-app-registration branch |
| 🔴 `Handlers/EntraAppReg/…/H3EntraAppRegHandlerTests.cs` | the shared-path tests to delete |
| `Handlers/SubscriptionReadiness/H1SubscriptionReadinessHandler.cs` | branch sites B4/B5 |
| `Handlers/BicepInfraDeploy/H2aBicepInfraDeployHandler.cs` | silent default D1 |
| `Handlers/BicepInfraDeploy/{ArmDeploymentRunner,FileBicepTemplateInspector}.cs` | branch sites B1/B2 |

Plus `H1SubscriptionReadinessHandlerTests.cs`, `H2aBicepInfraDeployHandlerTests.cs`,
`ArmDeploymentRunnerRetryTests.cs`.

🔴 **Doing the code remediation on `work/unified-access-control-r2` guarantees a conflict against 99 commits
on the files with the highest blast radius in the change** — the identity handler and the Bicep-selection
handlers. And unlike the docs, a bad merge here is not a confusing paragraph; it is a provisioning run that
creates the wrong app registration or deploys the wrong stack.

---

## 5. Recommendation — **do not start the code remediation on this branch**

Three viable orders. All are the owner's call.

| | Approach | Trade-off |
|---|---|---|
| **A** ⭐ | **Close out `cpo-r1` first** — rebase/merge its 99 commits onto master, then do the D-12 code work on a clean base | Cleanest. ⚠️ that rebase is itself 583 commits of catch-up and must happen eventually regardless |
| **B** | **Do the D-12 code work *in* the `cpo-r1` worktree**, on the branch that owns the code | No cross-branch conflict; but it lands the decision's implementation inside a dormant branch, and inherits its 583-commit staleness |
| **C** | Proceed on `uac-r2` and accept the conflict | ❌ **Not recommended.** 99 commits × 5 high-blast-radius files, resolved later by someone without this context |

⚠️ **Direction matters, and it favours us.** `cpo-r1` is 583 behind, so it **must** rebase onto master
before it can merge. In that direction **my text is the incumbent** and their shared-tier-premised additions
replay on top — so the resolver sees the corrected text as the base rather than the thing to be overwritten.
That is the safer sequence, and it is the one they are already forced into.

---

## 6. Actions taken / still needed

- ✅ This note written; the risk is recorded rather than discovered at merge time.
- ⬜ **Owner decision on §5** before any D-12 code work starts.
- ⬜ Add a pointer in `projects/INDEX.md` so a future `/conflict-check` surfaces this pair.
- ⬜ When `cpo-r1` next resumes, its operator must read D-12 + D-13 **before** resolving any conflict in the
  9 doc files — particularly `provision-environment/SKILL.md`, where "take theirs" is the wrong answer.
