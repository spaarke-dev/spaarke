# .claude/patterns/provisioning/

> **Purpose**: Customer-provisioning pattern files (max-25-line pointers to canonical source).
> **Status**: Skeleton — content filled by task 203.
> **Owning project**: [customer-provisioning-orchestration-r1](../../../projects/customer-provisioning-orchestration-r1/)

**Start here**: [INDEX.md](INDEX.md) — pattern-file index + loading strategy.

## Files in this folder

| File | Purpose |
|---|---|
| [INDEX.md](INDEX.md) | Pattern index + loading strategy + structure + related constraints + related ADRs |
| [manifest-driven-secret-catalog.md](manifest-driven-secret-catalog.md) | Adding a new handler that seeds KV secrets |
| [handler-registration-completeness.md](handler-registration-completeness.md) | Adding a new `IProvisioningHandler` |
| [progressive-fail-fast-recovery.md](progressive-fail-fast-recovery.md) | Diagnosing BFF SIGABRT chain |
| [operator-rbac-bootstrap.md](operator-rbac-bootstrap.md) | Fresh sub / fresh KV data-plane bootstrap (F15/F18) |
| [keyvault-reference-identity-invariant.md](keyvault-reference-identity-invariant.md) | App Service KV ref binding correctness (T1 / F16 / F16.5) |
| [resource-name-availability-precheck.md](resource-name-availability-precheck.md) | Global namespace collision prevention (F10) |
| [openai-quota-region-composition.md](openai-quota-region-composition.md) | Region + deployment set composition (F1 / F2 / F4 / F5) |
| [null-object-kill-switch-anti-pattern.md](null-object-kill-switch-anti-pattern.md) | ADR-032 F.1 asymmetric-registration detection (IActionSeam case study) |
| [bff-vs-provisioning-boundary.md](bff-vs-provisioning-boundary.md) | Class-B routing decision (BFF-owned lesson → BFF-owning worktree) |

## Convention (mirrors sibling pattern folders)

Every pattern file is max 25 lines: `When → Read These Files → Constraints → Key Rules`. Pattern files POINT to canonical source code, they don't describe it. Code is the source of truth; patterns are pointers.

See [`.claude/patterns/INDEX.md`](../INDEX.md) for the top-level pattern index and general pattern conventions.
