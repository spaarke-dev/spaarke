# ADR-003: Lean authorization with two seams (UAC data and file storage)
Status: Accepted
Date: 2025-09-27
Authors: Spaarke Engineering

## Context
We need flexibility to enforce Dataverse‑backed Unified Access Control (UAC) and integrate with SharePoint Embedded (SPE), without proliferating service interfaces. Over‑abstracted layers impede clarity and testability.

## Decision
- Implement a concrete AuthorizationService that evaluates an ordered set of small IAuthorizationRule policies.
- Keep one UAC seam: IAccessDataSource with DataverseAccessDataSource that returns coarse, per‑request snapshots for user and resource.
- Keep one storage seam: SpeFileStore that encapsulates Graph/SPE operations (no generic IResourceStore).
- Rules contain policy only; SDK/HTTP usage remains in adapters.

## Consequences
Positive:
- Fewer classes, clearer responsibilities, faster unit tests, and simple extension via new rules.
- No leakage of provider details to higher layers.
Negative:
- Slightly less generic than a policy engine, but far less boilerplate.

## Alternatives considered
- Multiple service interfaces per concern and generic policy engines. Rejected as premature complexity and harder for AI‑generated code to follow consistently.

## Operationalization
- Endpoints and workers call AuthorizationService before any SpeFileStore operation.
- Initial rules: ExplicitDenyRule, ExplicitGrantRule, TeamMembershipRule, RoleScopeRule, LinkTokenRule.
- Snapshots fetched via IAccessDataSource are cached per request to avoid chatty queries.
- Deny results return stable, machine‑readable reason codes (e.g., sdap.access.deny.team_mismatch).

## Exceptions
Tenant‑specific policies should be delivered as additional IAuthorizationRule implementations registered via DI, not new service layers.

## Success metrics
- Reduced service/interface count; lower defect rate in access checks.
- Stable performance for common queries; predictable authorization behavior.
