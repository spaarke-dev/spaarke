# Pattern — SPE Writer-Identity Matching (post-upload indexing dispatch)

> **Last Reviewed**: 2026-06-08
> **Status**: Verified (post Phase-3a UAT incident)
> **Loads**: when adding any background processing that reads SPE files, OR when wiring post-upload pipelines (indexing, analysis, classification, embedding), OR diagnosing "Access denied" on SPE file download from a Service Bus job handler.

## Rule (binding)

**The identity that READS an SPE file must match the identity that WROTE it.**

The Spaarke MI is intentionally NOT registered as a guest app on the SPE container type (per [`managed-identity-resource-rbac.md`](managed-identity-resource-rbac.md) — MI grants cover Key Vault, Cognitive Services, Cosmos, **not SPE**). Files are accessible only to identities on their SPE ACL — which by default means the writer.

## Canonical sources

- [docs/architecture/sdap-auth-patterns.md § Pattern 4 — Writer-Identity Matching Rule](../../../docs/architecture/sdap-auth-patterns.md)
- [.claude/constraints/auth.md § SPE File Access — Writer-Identity Matching](../../constraints/auth.md)
- [`Services/Ai/IPostUploadIndexingEnqueuer.cs`](../../../src/server/api/Sprk.Bff.Api/Services/Ai/IPostUploadIndexingEnqueuer.cs) — two-method interface that enforces the split

## Decision matrix

| File written by | Allowed reader / dispatch |
|---|---|
| **User (OBO)** — wizard upload, PCF/Code Page upload, SprkChat persist | **Sync OBO inline** via `IPostUploadIndexingEnqueuer.EnqueueIfApplicableAsync(request, httpContext, ct)`. The user is on the file's ACL; the same request scope is the only window where the user's OBO token is alive. |
| **MI (app-only)** — Office Add-in finalize, Email-to-Document, post-analysis re-index | **Async Service Bus** via `IPostUploadIndexingEnqueuer.EnqueueAppOnlyIfApplicableAsync(request, ct)` → `RagIndexingJobHandler` (runs under MI). MI wrote the file, MI can read it. |
| **User (OBO), read later under MI** | ❌ **NOT ALLOWED — will 403.** The 2026-06-08 Phase 3a UAT incident proved this empirically. |

## Failure-mode quick lookup

- "Access denied" downloading a Drive item in `RagIndexingJobHandler` / `DriveItemOperations.DownloadFileAsync` (which uses `_factory.ForApp()` = MI) → user-OBO-uploaded file routed through Service Bus + MI handler. Wrong dispatch — use the OBO sync method instead.
- "404 not found" from SPE for a file that EXISTS in the container → likely same root cause, different SPE error code.

## How to apply

When adding any new post-upload pipeline:

1. **Identify the writer**. Trace back to the upload endpoint. Is the file written via OBO (user identity in `HttpContext`) or via MI (`SpeFileStore.UploadSmallAsync` from a background context with no user)?
2. **Pick the matching dispatch**:
   - User OBO → `EnqueueIfApplicableAsync(request, httpContext, ct)`
   - MI → `EnqueueAppOnlyIfApplicableAsync(request, ct)`
3. **Verify in code review** — the writer-identity rule is in [`.claude/constraints/auth.md`](../../constraints/auth.md) and [`.claude/constraints/bff-extensions.md`](../../constraints/bff-extensions.md) checklists.

## Why this exists

Phase 3a of `spaarke-multi-container-multi-index-r1` (commit `d65dcc2f`) wired post-upload indexing through Service Bus assuming "async + retryable" was the goal. UAT proved the dispatch path couldn't read the file — MI 403'd on user-OBO-uploaded SPE files. The pattern that always worked (and still works today for the same use case via `/api/ai/rag/send-to-index` ribbon command) is sync OBO inline in the upload request scope. This pattern formalizes the dispatch choice so future post-upload work doesn't repeat the mistake.

## See also

- [Pattern 4 (sdap-auth-patterns.md)](../../../docs/architecture/sdap-auth-patterns.md) — the broader OBO-for-AI-file-access rule this pattern derives from
- [`managed-identity-resource-rbac.md`](managed-identity-resource-rbac.md) — what MI IS granted (KV, Cog Services, Cosmos) — and the implicit "not SPE" boundary
- [`projects/spaarke-multi-container-multi-index-r1/notes/upload-indexing-centralization-design.md`](../../../projects/spaarke-multi-container-multi-index-r1/notes/upload-indexing-centralization-design.md) — design doc + post-UAT lessons-learned
