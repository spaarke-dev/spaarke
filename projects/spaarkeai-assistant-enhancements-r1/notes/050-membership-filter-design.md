# Design + Plan — Membership-scoped grid filter (`behavior.membershipFilter`)

> **Status**: Approved to build (owner, 2026-07-22). Design grounded in a read-only code map (server membership service + DataGrid framework).
> **Scope**: (A) fix 050 "My Tasks" so "my" = **membership**, not `ownerid`; (B) generalize that as a reusable **DataGrid framework feature** any `sprk_gridconfiguration` can opt into.
> **Cost headline**: **No BFF change, no Dataverse schema change.** Client resolver + one config field + one async grid stage + widget plumbing.

---

## 1. Problem

050's "My Tasks" grid currently scopes with `ownerid eq-userid`. But in Spaarke, **"my" records = membership**, not ownership: a user is "on" a record through any of several contact/owner lookup fields (`ownerid`, `sprk_assignedto`, `sprk_assignedattorney1/2`, `sprk_assignedparalegal1/2`, `sprk_assignedtointernal`, …), resolved via `systemuser → sprk_primarycontact`. A static saved-query view **cannot** express this: `eq-userid` only works on systemuser fields, and Dataverse forbids dynamic placeholder values (the caller's contactid) in a stored view. So membership must be resolved **at query time**.

The right home for that logic is the **shared membership service** (single source of truth), surfaced to grids as a declarative config option — a sibling to the existing `behavior.parentContextFilter`.

## 2. What already exists (why this is cheap)

- **HTTP endpoint (LIVE, no change):** `GET /api/users/me/memberships/{entityType}` — authenticated (OBO, ADR-028), returns `{ ids: Guid[], byRole: {role: Guid[]}, count, … }`. `ids` = the record GUIDs the caller is a member of for that entity. Wired unconditionally (`Program.cs` AddMembership + `EndpointMappingExtensions.MapMembershipApi`). Query params: `roles`, `identityTypes`, `includeRelated`, `limit`, `continuationToken`. **No client caller exists today** (grep of `src/client`/`src/solutions` = none) — we'd be the first browser consumer.
- **Membership is config/metadata-driven, not hardcoded.** `MembershipFieldDiscoveryService` scans the entity's Lookup/Owner/Customer attributes and keeps those targeting a configured identity table (systemuser/contact/team/BU/account/organization). Per-entity tuning via `Membership:EntityOverrides:<entity>` (ExcludedFields / IncludedFields / FieldRoleOverrides) in appsettings — no code. The `systemuser→contact` hop is `IdentityNormalizationService` (primary: `sprk_primarycontact`; fallback: contact by AAD oid).
- **The server does the OR-across-N-fields work** and returns plain record ids → the client only needs `sprk_eventid IN (…ids…)`.
- **DataGrid already supports `IN(ids)`** via `overlayHostFilters` (`fetchXmlOverlay.ts` — `operator:'in'` emits a multi-`<value>` condition). So injecting membership needs **no new overlay** — reuse the `in` path.

## 3. Design — `behavior.membershipFilter`

### 3.1 Config (DataGridConfiguration.ts)
Add alongside `parentContextFilter` in `BehaviorConfig`:
```ts
export interface MembershipFilter {
  /** Primary-id attribute to IN-match. Default = entity primaryIdAttribute (e.g. sprk_eventid). */
  attribute?: string;
  /** Restrict to specific membership roles (→ ?roles=). Omit = all roles the user is on. */
  roles?: string[];
  /** Restrict identity types (→ ?identityTypes=). Omit = all. */
  identityTypes?: string[];
}
// BehaviorConfig:  membershipFilter?: MembershipFilter | true;
```
`true` = default (all roles, entity primary id).

### 3.2 Client resolver (~30 LOC, new)
`getMyMemberships(entityType, opts): Promise<string[]>` → `authenticatedFetch(\`${bff}/api/users/me/memberships/${entityType}?roles=…\`)` → `body.ids`. Fail-soft: on error return `null` (grid falls back to unfiltered/base query, logs — never breaks the render).
- Home: a small shared module (e.g. `@spaarke/ui-components/services/membership.ts`) OR the widget host. Prefer shared so both the widget and any code-page can use it.

### 3.3 DataGrid async stage (~25 LOC)
The fetchXml pipeline (`DataGrid.tsx` ~L885) is a sync `useMemo`: `savedQuery → overlayParentContextFilter → overlayHostFilters → augmentFetchXmlWithChips`. Add a gated async stage:
- New prop `membershipResolver?: (entityName, opts) => Promise<string[] | null>` — because `IDataverseClient` (5-method read contract) can't reach the BFF and we do NOT want to widen it. Host injects a resolver backed by `authenticatedFetch`.
- New `useEffect` keyed on `[entityNameForLoad, behavior.membershipFilter]`: when `membershipFilter` set + a resolver is present, resolve ids → `setMembershipIds`.
- Feed `membershipIds` into the L885 memo, overlaid as `IN(attribute, ids)` (reuse `overlayHostFilters`'s `in` condition, or a thin `overlayMembershipFilter`). Empty ids → an impossible-match condition (show empty, not all) so a member-of-nothing user sees an empty grid, not everyone's records.
- Precedent for a pre-query async stage: the existing config-load effect (`DataGrid.tsx` ~L751). Lazy-load already re-fetches on `fetchXml` identity change, so an ids-driven change re-queries correctly.

### 3.4 Widget plumbing (the one real MDA-path bit, ~15 LOC)
`DataverseEntityViewWidget` today mounts `<DataGrid>` with `XrmDataverseClient` and **no** `authenticatedFetch`. Wire a `membershipResolver` built from the host's `authenticatedFetch` (available in SpaarkeAi + code-pages) and pass it to `<DataGrid>`. `register-workspace-widgets.ts` factory passes it through.

## 4. Delivery — build the feature, apply to 050

Because (B) is barely more than (A), build the feature and set 050's config to use it.

1. **Config type** — `MembershipFilter` + `behavior.membershipFilter` in `DataGridConfiguration.ts` (+ `isValidDataGridConfiguration` stays shallow; add a doc note).
2. **Client resolver** — `getMyMemberships(...)` in the shared lib.
3. **DataGrid** — `membershipResolver` prop + async resolve effect + overlay of `IN(ids)`.
4. **Widget** — `DataverseEntityViewWidget` obtains `authenticatedFetch`, builds the resolver, passes it; factory/registration plumb it.
5. **050 config (data)** — update the "My Tasks (Assistant)" grid config (`ac05e4f1-…`): keep the curated **saved query** as `source` (switch from my inline to `{type:'savedquery', savedQueryId:'12a510e4-2517-f111-8343-7ced8d1dc988'}` — the "My Tasks Open" view for columns + eventtype/status), and add `behavior.membershipFilter: { roles: [...] }`. Remove the `ownerid eq-userid` shortcut. **Decision needed**: which roles for "my tasks" (all vs assignee/owner subset) — see §7.
6. **Tests** — resolver unit (fetch → ids, fail-soft null); overlay unit (ids → IN condition; empty → impossible-match); DataGrid effect (membershipFilter set + resolver → fetchXml carries IN); widget passes resolver.
7. **Deploy** — client rebuild + `sprk_spaarkeai` deploy; the grid-config edit is live data (no deploy). No BFF deploy.

## 5. Scope boundaries (honest)
- **Host must supply `authenticatedFetch`.** Works in SpaarkeAi + code-pages. A pure MDA form-subgrid (no BFF token) can't use `membershipFilter` — it degrades to base/`eq-userid`. Acceptable: membership grids live in the AI/workspace surfaces. Document this in the feature doc.
- **Size cap ~500 ids** (endpoint default; hard cap 5000). Dataverse `IN` lists are bounded similarly. Fine for tasks; a user on >500 records needs continuation-token paging — a documented follow-up (the service's transitive path already assumes this bound).
- **One round-trip per grid load** (the membership GET). Redis-cached server-side (5-min/user); consider a short client cache if a grid remounts frequently.

## 6. Graduation
This is a **DataGrid framework feature**, broader than 050. After it lands:
- Add a section to `docs/architecture/SPAARKE-DATAGRID-FRAMEWORK-ARCHITECTURE.md` (the `membershipFilter` config, the resolver-prop contract, the authenticatedFetch boundary) — mirror how `parentContextFilter` is documented.
- Consider a tiny standalone project/spec if the framework owners want it tracked separately; this doc is the seed.

## 7. Open decisions (owner)
1. **Roles for "My Tasks"**: all membership roles (owner + every assigned-contact role — broadest "anything I'm on") vs a subset (e.g. `owner` + `assignedTo*` only, excluding matter-team membership). The endpoint's `byRole`/`roles` param makes either precise. *Default proposed: all roles* (a task list should surface anything the user is responsible for), refine if too broad.
2. **Columns/eventtype**: reuse the "My Tasks Open" saved query (Deadline+Task+Reminder, eventstatus=Open) vs Task-only. *Default proposed: reuse the curated view as-is* (owner pointed to it), membership overlay handles the "my" part.

## 8. Reference — live 050 artifacts (dev)
- Grid config: `sprk_gridconfiguration` "My Tasks (Assistant)" = `ac05e4f1-8d85-f111-8075-7c1e5268570d` (currently inline; to switch to savedquery + membershipFilter).
- Saved query "My Tasks Open" = `12a510e4-2517-f111-8343-7ced8d1dc988` (Deadline+Task+Reminder, eventstatus=Open, **no owner filter**).
- Capability: Binding `5b1870b9-…` (list-tasks, surface_launch) + Action `57651aad-…`.
- Membership endpoint: `GET /api/users/me/memberships/sprk_event`.
