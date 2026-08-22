# Task 002 — authorizing document download: what was fixed and what was found

> **Date**: 2026-08-21 · **Spec**: FR-01 · **Finding**: A-1 (High) — R1's January-2026 attack scenario

---

## 1. What was wrong

`GET /api/documents/{documentId}/download` carried **no per-document authorization filter**. The route
group's `RequireAuthorization()` answers only *"are you anyone?"*; the handler then streamed app-only
from SPE. Any authenticated caller could download any document by GUID.

## 2. The fix, and what deliberately did NOT change

```csharp
docs.MapGet("/{documentId}/download", GetDownload)
    .AddDocumentAuthorizationFilter("read")   // ← added
```

**The app-only SPE stream is not the defect and is unchanged.** Files written by the managed identity
are only readable by it — the Writer-Identity Matching Rule
([`.claude/constraints/auth.md`](../../../.claude/constraints/auth.md), Pattern 4): *"the Spaarke MI is
intentionally NOT registered as a guest app on the SPE container type."* Making the stream OBO would
break every background-written file. What was missing is the **Dataverse-level** answer to "may this
caller have this document?", which the filter supplies before any SPE call.

## 3. The second route — `/content` — had the identical hole

`GET /api/documents/{documentId}/content` streams the document's bytes
(`TypedResults.Stream(contentStream, contentType, fileName)`, `FileAccessEndpoints.cs:396`) from the same
app-only SPE path, on the same group, with the same missing gate.

**Closing `/download` alone would have left the attack scenario fully intact behind a different URL.**
The finding is the missing per-document authorization, not the route name, so both were closed together.

This is a deliberate one-route widening of the POML's scope, stated rather than done quietly: the task's
`<goal>` is that the R1 attack scenario *fails*, and it would not have. `/content` is consumed by
`FilePreviewDialog.tsx` and `DocumentCard.tsx`, so it is a live path, not a relic.

## 4. Why operation `"read"` and not `download_file` (Write)

This was the task's real judgment call, and the POML anticipates it with an escalation trigger ("do not
repurpose an unrelated key"). Two registered keys plausibly express "download this document":

| Candidate | Rights | Argument for |
|---|---|---|
| **`"read"`** ✅ chosen | Read | The **sibling download route already uses it**: `DataverseDocumentsEndpoints.cs` `GET /api/v1/documents/{id}/download` carries `AddDocumentAuthorizationFilter("read")`. So does `eml-render` on this very group. Task 001's characterization pinned precisely that these routes *disagree* — the fix is to make them agree |
| `driveitem.content.download` | **Write** | `OperationAccessPolicy.cs:37` records "download requires Write (security policy)", and task 006's `CanDownload` capability projects from this key |

**`"read"` won on precedent and blast radius.** Two existing routes already answer this exact question
with `"read"`; making a third answer it with `Write` would recreate the inconsistency A-1 is about, and
would newly deny download to every Read-only user on a live UI path
(`Spaarke.DocumentOperations/src/hooks/useDocumentActions.ts:145`). The conservative choice also
fails safe in the reversible direction: tightening later is a one-word change, loosening after users
have lost access is a regression report.

### ⚠️ Residual inconsistency — for owner decision

Enforcement now says **Read**; the `CanDownload` capability says **Write**. They disagree.

The practical effect is benign (the UI hides a download button that would in fact work for a Read-only
user — fail-closed in the safe direction), but it is exactly the kind of divergence task 006's
acceptance criterion 5 exists to prevent. **One of the two should change**, and which one is a product
decision, not an implementation one:

- **Option A** — enforcement stays `"read"`, and `CanDownload` is re-pointed at `driveitem.preview`-class
  rights. Makes downloading a read operation everywhere.
- **Option B** — enforcement moves to `driveitem.content.download`, and the sibling route + `eml-render`
  move with it. Honours the written "download requires Write" policy, but denies download to Read-only
  users on three routes.

Recorded as an open item; not resolved here because either answer is defensible and the wrong one is a
user-visible access change.

## 5. Routes NOT changed — filed, not fixed

Four more routes on the same group have no per-document filter. They were left alone because they mint
**URLs** rather than stream bytes, which is a different blast radius and a separate decision — and
because `preview-url` in particular is on many client paths:

| Route | Returns |
|---|---|
| `/{documentId}/preview-url` | ephemeral SPE preview URL |
| `/{documentId}/view-url` | SPE view URL |
| `/{documentId}/office` | Office-online URL |
| `/{documentId}/preview` | preview payload |

A URL handed to an unauthorized caller is still a disclosure — arguably a worse one, since it outlives
the request. **This should be assessed as its own task.** Not folding it into 002 keeps the change
reviewable and avoids a wide UI-affecting change inside a task scoped to one route.

## 6. Test coverage

`tests/integration/auth/UnifiedAccessControl/EndpointAuthorizationCharacterizationTests.cs`

| Test | Change |
|---|---|
| `GetDownload_ForCallerWithoutDocumentAccess_DeniedForInsufficientRights` | **Flipped** from the A-1 characterization. Asserts 403 **and** the reason code — a bare 403 could come from the wrong cause (the `unknown_operation` denial that made eml-render *look* gated before task 003) |
| `GetContent_ForCallerWithoutDocumentAccess_DeniedForInsufficientRights` | **New** — the second route |
| `DownloadContentAndEmlRender_AgreeOnAuthorizationForSameCallerAndDocument` | **Flipped** from `..._DisagreeOn...`. The disagreement WAS the finding; this now catches a future route added to this group without a filter, which is how A-1 arose |
| `Get_WhenUnauthenticated_Returns401` | Unchanged — the authentication floor is preserved |

**Non-vacuity verified empirically**: removing the `/content` filter fails 2 of the 17 tests; restored,
17/17. The `/download` half is unambiguous by construction — the pre-fix behaviour was a recorded 409,
and the test now requires 403 plus a specific reason code.

## 7. Follow-on obligations

| # | Obligation | Owner |
|---|---|---|
| 1 | Resolve the enforcement-vs-capability divergence in §4 (Option A or B) | Owner decision → wrap-up or a new task |
| 2 | Assess the four URL-minting routes in §5 for the same missing gate | New task |
| 3 | Task 012 modifies this same file (share links) — it must not remove these filters, and the share-link route at `:640-668` deserves the same question asked of it | **task 012** |
