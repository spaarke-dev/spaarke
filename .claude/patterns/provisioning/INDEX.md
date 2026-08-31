# Provisioning Patterns Index

> **Last Reviewed**: 2026-08-24
> **Reviewed By**: customer-provisioning-orchestration-r1 task 202 (scaffolding only)
> **Status**: Skeleton — content filled by task 203

> Pointer-based pattern files for customer-provisioning L2/L3 handlers, prereqs, and operator workflows.
> Each file points to canonical source code — read the code, not descriptions.
> Follows the shared convention: max 25 lines, structure `When → Read These Files → Constraints → Key Rules`.

| Pattern | When to Load | Status |
|---------|--------------|--------|
| [manifest-driven-secret-catalog.md](manifest-driven-secret-catalog.md) | Adding a new provisioning handler that seeds KV secrets | Skeleton |
| [handler-registration-completeness.md](handler-registration-completeness.md) | Adding a new `IProvisioningHandler` implementation | Skeleton |
| [progressive-fail-fast-recovery.md](progressive-fail-fast-recovery.md) | Diagnosing BFF SIGABRT chain / IOptions ValidateOnStart cascades | Skeleton |
| [operator-rbac-bootstrap.md](operator-rbac-bootstrap.md) | Fresh sub + fresh KV data-plane bootstrap (F15/F18 pattern) | Skeleton |
| [keyvault-reference-identity-invariant.md](keyvault-reference-identity-invariant.md) | App Service KV ref binding correctness (T1 + F16/F16.5) | Skeleton |
| [resource-name-availability-precheck.md](resource-name-availability-precheck.md) | Global namespace collision prevention (F10 pattern) | Skeleton |
| [openai-quota-region-composition.md](openai-quota-region-composition.md) | Composing region + deployment set from auto-granted TPM (F1/F2/F4/F5) | Skeleton |
| [null-object-kill-switch-anti-pattern.md](null-object-kill-switch-anti-pattern.md) | Detecting ADR-032 F.1 asymmetric-registration bugs at design time | Skeleton |
| [bff-vs-provisioning-boundary.md](bff-vs-provisioning-boundary.md) | Deciding whether a lesson belongs in a BFF worktree vs a provisioning project | Skeleton |

## Structure

```
src/
├── server/api/Sprk.Bff.Api/**                # BFF (host of some L2 concepts historically)
├── server/api/Sprk.Provisioning.ControlPlane.*/  # L2 control-plane (4-project split: .Api / .Core / .Worker / .Tests)
├── server/shared/Spaarke.*/                  # shared libraries
infrastructure/
├── bicep/
│   ├── modules/                              # single-resource Bicep modules
│   ├── stacks/                               # tier-level compositions (model1-shared, model2-full, platform-controlplane)
│   ├── customer.bicep                        # per-customer stamp (Model 2)
scripts/
├── canonical-secret-catalog/manifest.yaml    # single source of truth for secrets (task 084 / FR-36)
├── provisioning-prereqs/prereqs.yaml         # single source of truth for manual prereqs (task 202)
├── provisioning/                             # per-handler helper scripts + operator ceremonies
docs/
├── guides/PROVISIONING-PREREQUISITES.md      # human-readable rendering of prereqs.yaml
├── guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md  # human operator narrative
├── adr/ADR-032-bff-nullobject-kill-switch.md # asymmetric-registration constraint
projects/customer-provisioning-orchestration-r1/  # the owning project
```

## Related Constraints

Patterns show **how** — constraints define **what**:

| Pattern Domain | Constraint File | Load When |
|----------------|-----------------|-----------|
| Provisioning handlers | `.claude/constraints/provisioning.md` (planned by task 203) | Adding provisioning-related code |
| BFF DI + services | `.claude/constraints/bff-extensions.md` § F (asymmetric-registration) | ANY BFF-touching change |
| Testing | `.claude/constraints/testing.md` | Tests + ArchTests |
| Auth | `.claude/constraints/auth.md` | UAMI RBAC, Graph, Dataverse App User |

## Related ADRs

| ADR | Relevance |
|-----|-----------|
| ADR-032 (Null-Object kill-switch) | Asymmetric-registration anti-pattern (§ F.1 + § F.2 + § F.3) |
| ADR-036 (Background-job infrastructure) | L2 dispatcher pattern (BackgroundService + ServiceBusSessionProcessor) |
| ADR-038 (Testing strategy) | Integration-heavy pyramid; new ArchTests I1-I6 |
| ADR-028 (Spaarke auth v2) | UAMI-outbound preference; Path X for L2 Dataverse |
| ADR-039 (Grounded execution + closed catalogs) | Handler catalog immutability |
| ADR-020 (AI model version pinning) | OpenAI pinned model versions in Bicep |

## Owning project

[projects/customer-provisioning-orchestration-r1](../../../projects/customer-provisioning-orchestration-r1/) — see spec.md, design.md, and per-task deviations.
