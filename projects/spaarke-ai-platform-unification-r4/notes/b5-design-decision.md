# B-5 Design Decision — PUT + If-Match ETag (vs PATCH + JSON Patch)

> **Task**: 054 (R4 / B-5 / FR-08)
> **Decision date**: 2026-05-26
> **Decided by**: Claude Code under task-execute FULL rigor
> **Spec reference**: OC-R4-08 (open at task time) → resolved Option A
> **Status**: Decided; code change follows in this commit

---

## Decision

**Option A — Keep PUT semantics; add HTTP `If-Match` ETag validation.**

The BFF `PUT /api/workspace/layouts/{id}` endpoint:

1. Emits an `ETag` response header on GET (single + list) derived from the
   layout's `ModifiedOn` value (task 053 / B-4) using the weak-validator
   shape `W/"<ModifiedOn ticks>"`.
2. Validates incoming `If-Match` on PUT. Comparison is against the
   layout's CURRENT (just-fetched) `ModifiedOn`. Mismatch → 412 Precondition
   Failed. Missing header → 428 Precondition Required (so clients can't
   accidentally skip concurrency checks — but see §5 "Backward compatibility"
   below for the soft-mode policy chosen instead).
3. Returns the updated DTO (carrying the NEW `ModifiedOn`) so the client
   has a fresh ETag for the next write.

Option B (PATCH + JSON Patch RFC 6902) was REJECTED for the reasons in §3.

---

## Rationale (Criterion: client-side complexity tradeoff)

| Factor | PUT + If-Match (A) | PATCH + JSON Patch (B) |
|---|---|---|
| Frontend already sends full layout? | ✅ Yes — current paths reconstruct full JSON | Would need a diff library or hand-rolled patch builder |
| New NuGet packages required? | ❌ None | ✅ `Microsoft.AspNetCore.JsonPatch` (~80 KB+ transitive) |
| Aligns with B-4's `modifiedOn` field? | ✅ Drop-in ETag source | Indirect — would still need If-Match for atomic check |
| Server change scope | Header read + 1 compare branch | New endpoint, new DTO, operation validator, error mapping |
| Client change scope | Send `If-Match` header from cached layout's `modifiedOn` | Diff full layout state; generate RFC 6902 ops |
| `sectionsJson` is opaque to the server | ✅ PUT treats it as a string blob — fine | ❌ Patches against an opaque string can't be richer than `replace` (degenerate case = full PUT anyway) |
| 412 semantics straightforward | ✅ Yes — RFC 7232 standard | ✅ Yes — but PATCH conflict UX more nuanced (merge vs reject) |
| §10 BFF Hygiene (publish-size) | ✅ Zero impact | ⚠ ~0.1–0.4 MB closer to the 60 MB ceiling |

Operator-feedback emphasis: PUT path keeps the server change ~30 lines and
the client change to a single header. PATCH would have been a 200+ line
effort spread across server + client + tests with **no additional semantic
gain** because the bulk of the layout payload (`sectionsJson`) is opaque to
the BFF and would be patched as a single `replace` operation.

---

## Rejected option: PATCH + JSON Patch (RFC 6902)

Considered and rejected. Even if the team later wants partial updates
(reorder one section without resending the full layout), the natural granular
target would be a `move` op INSIDE `sectionsJson` — which is server-opaque
JSON, so RFC 6902 can't patch into it. Either we'd parse `sectionsJson`
server-side (breaking the "opaque string" property) or we'd patch the wrapper
fields only — which adds no value over PUT.

If a future renderer (task 052 / C-4) introduces section-level granular
updates with the BFF parsing `sectionsJson`, **revisit this decision then**.
For now PUT + If-Match is the right tradeoff.

---

## API contract changes

### Response: GET (single + list) — adds `ETag` header

```
HTTP/1.1 200 OK
ETag: W/"638536008000000000"
Content-Type: application/json

{ "id": "...", "modifiedOn": "2026-05-26T10:00:00+00:00", ... }
```

The list endpoint emits ONE ETag derived from the most-recent layout's
`ModifiedOn`. For per-row ETag, clients read `modifiedOn` from each DTO
(already in the wire shape per task 053).

### Request: PUT — accepts `If-Match`

```
PUT /api/workspace/layouts/{id} HTTP/1.1
Content-Type: application/json
If-Match: W/"638536008000000000"

{ "name": "Updated", "layoutTemplateId": "2-column", ... }
```

- **Match** → 200 OK with updated DTO (new ETag in response).
- **Mismatch** → **412 Precondition Failed** (RFC 7232 §4.2).
  ProblemDetails body includes the server's current `modifiedOn` so the
  client can show "this workspace was edited elsewhere — refresh and retry".
- **Missing `If-Match`** → soft mode (see §5).

---

## Backward compatibility — the "missing If-Match" question

**Soft-mode chosen.** PUT without `If-Match` succeeds (200 OK, last-write-wins)
for backward compatibility with any caller that hasn't been updated. This is
the "weak default" mentioned in the task brief.

**Rationale**:
1. The frontend write paths (`workspaceLayoutMutations.ts`,
   `WorkspaceLayoutWizard/src/App.tsx`) are updated in this same commit to
   send `If-Match`, so production traffic will have headers.
2. Returning 428 Precondition Required by default would immediately break
   the deployed (un-updated) client until both sides ship together —
   risky for a non-deploying task per caller guardrails.
3. The integration test asserts the 412 path explicitly. The 428-on-missing
   path is documented but **NOT enforced** — a future "strict mode"
   appsettings toggle (`WorkspaceLayouts:RequireIfMatch=true`) would
   activate it. Out of scope for B-5.

If the operator ever flips that toggle, no client change is needed because
this commit already sends `If-Match` everywhere.

---

## ETag value derivation

**Weak ETag**: `W/"<ticks>"` where `<ticks>` is `ModifiedOn.UtcTicks`.

- **Weak (W/)** is the right choice because `ModifiedOn` is millisecond-grained
  in Dataverse but could be returned with slightly different formatting; weak
  validators compare semantic equivalence, not byte-identity.
- **Ticks** (Int64) is stable across serialization formats and avoids
  string-comparison pitfalls of ISO-8601 (timezone formatting variations).
- **No quote stripping needed** — the BFF parses the header as-is and
  compares ticks string.

Edge case: hard-coded system layouts have `ModifiedOn = DateTimeOffset.MinValue`
(Unix-epoch sentinel per task 053). Their ETag is
`W/"0"`. Update attempts against system layouts already return 403 Forbidden
BEFORE the If-Match check, so this is well-defined.

---

## Server-side: re-read post-write for canonical ETag

Per task 053's report ("B-5 may want to re-read post-write for canonical
Dataverse rowversion"): yes. The `UpdateLayoutAsync` service method now
re-reads the entity after `UpdateAsync` returns so the response DTO's
`ModifiedOn` reflects the Dataverse-stamped value (not the
`DateTimeOffset.UtcNow` placeholder from task 053). This guarantees the
client's next PUT will succeed on first try.

Cost: +1 Dataverse RetrieveAsync per update. Acceptable — updates are user-
gesture-frequency (not bulk).

---

## Test plan

1. **Unit/Integration**: PUT with matching `If-Match` → 200 OK + new ETag.
2. **Unit/Integration**: PUT with mismatched `If-Match` → 412 Precondition Failed.
3. **Unit/Integration**: PUT without `If-Match` → 200 OK (soft-mode policy).
4. **Unit/Integration**: GET emits ETag header derived from `ModifiedOn`.
5. **(Existing tests must still pass)** Concurrent edits → second rejected with 412.

---

## Files modified by this decision

- `src/server/api/Sprk.Bff.Api/Api/Workspace/WorkspaceLayoutEndpoints.cs` —
  Read `If-Match`; emit `ETag` on success and on GETs; map 412.
- `src/server/api/Sprk.Bff.Api/Services/Workspace/WorkspaceLayoutService.cs` —
  Add `ExpectedModifiedOn` param to `UpdateLayoutAsync`; re-read post-write
  for canonical `ModifiedOn`; return `Conflict` sentinel for mismatch.
- `src/solutions/SpaarkeAi/src/services/workspaceLayoutMutations.ts` — Send
  `If-Match` header from layout's cached `modifiedOn`.
- `src/solutions/WorkspaceLayoutWizard/src/App.tsx` — Send `If-Match` on edit.
- `src/solutions/SpaarkeAi/src/components/workspace/ManageWorkspacesPane.tsx` —
  Map 412 to "edit conflict — refresh and retry" UX.
- `tests/unit/Sprk.Bff.Api.Tests/Integration/Workspace/WorkspaceLayoutEndpointTests.cs`
  — concurrency tests + ETag round-trip test.

---

## Out of scope (deferred)

- **428 Precondition Required strict mode** — gate behind future
  `appsettings` toggle if/when ops want to enforce. Not blocking R4.
- **Strong ETag from a future Dataverse `rowversion` column** — current
  Dataverse schema doesn't expose one. If/when added, swap the W/ prefix
  for strong validation.
- **Front-end optimistic UI / retry on 412** — current behavior is "show
  error message, user refreshes manually". Could later auto-fetch latest +
  reapply user's changes; out of scope.
