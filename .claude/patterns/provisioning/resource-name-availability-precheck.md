# Resource Name Availability Pre-Check Pattern

> **Last Reviewed**: 2026-08-24
> **Reviewed By**: customer-provisioning-orchestration-r1 task 202 (SKELETON — task 203 fills)
> **Status**: Skeleton

## When
Provisioning a resource with a GLOBAL namespace (Service Bus namespace, Storage account, Cognitive Services subdomain, Front Door) on a fresh sub. `az deployment sub what-if` does NOT catch global-namespace collisions.

## Read These Files (task 203 fills)
1. `projects/customer-provisioning-orchestration-r1/notes/lessons-learned-model1-prod-standup-2026-08-22.md` § F10 — original discovery (`-sb` reserved suffix; deploy failed at 16m35s).
2. `docs/guides/PROVISIONING-PREREQUISITES.md` PRQ-C-03 (global resource-name availability check).
3. `docs/guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md` §2.5 — existing naming collision check.

## Constraints
- Global namespace check MUST run at Step 2 preflight, BEFORE `az deployment sub create`.
- Reserved suffixes MUST be documented (Service Bus reserves `-sb`; others surface as they're discovered).
- Failing at Step 2 → operator adjusts `bicepparam` before ANY resource created. Failing at deploy time → 16+ minutes wasted before failure surfaces.

## Key Rules (task 203 fills detail)
1. For each resource with global namespace, call the resource's `checkNameAvailability` API:
   - Service Bus: `POST https://management.azure.com/subscriptions/{sub}/providers/Microsoft.ServiceBus/checkNameAvailability?api-version=2022-10-01-preview`
   - Storage: `POST https://management.azure.com/subscriptions/{sub}/providers/Microsoft.Storage/checkNameAvailability?api-version=2023-01-01`
   - Cognitive Services: check via `az cognitiveservices account list --query "[?name=='{proposedName}']"`
   - Front Door: `POST /providers/Microsoft.Cdn/checkFrontDoorNameAvailability`
2. Response `{"nameAvailable": false, "reason": "..."}` → HARD STOP with resource-specific remediation.
3. Reserved-suffix registry (per resource type) maintained in `scripts/provisioning-prereqs/reserved-suffixes.yaml` (task 203 authors).
4. Handler-side (H2a): if `what-if` returns green but `create` fails on name-collision, emit `h2a-global-name-collision` rejection code + point to this pattern.
