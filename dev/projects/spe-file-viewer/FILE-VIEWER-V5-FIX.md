# SPE File Viewer — Assessment, Root Cause, and Fix Plan

**Module:** SDAP (Spe.Bff.Api + PCF Dataset Grid)  
**Audience:** Product managers, senior full-stack C# developers, and AI code agents

---

## 1) Executive summary

Your architecture (PCF → BFF → Dataverse → Graph → SharePoint Embedded) is correct. The preview failures you observed stem from **ID mismatches** and **incomplete mapping checks** rather than a fundamental design flaw. The BFF expects a **Dataverse Document GUID**, resolves that to a **(driveId, itemId)** pair stored on the document row, and then calls Graph to obtain a short-lived preview/download URL.

**Root issue surfaced in logs:** the PCF sometimes passed a **Graph `driveItem.id`** (e.g., `01LBY…`) instead of the **Document GUID** to the BFF, causing “Document not found.” In addition, the BFF didn’t clearly distinguish “invalid id format” vs “mapping missing,” which obscured the diagnosis.

This doc gives a step-by-step correction and hardening (PCF + BFF), plus code samples the agent can paste into your repos.

---

## 2) Canonical identifiers and mapping

- **API Key (external):** Dataverse **Document GUID** — the only identifier the client should send to the BFF.
- **Storage Pointer (internal):** the tuple
  - `sprk_GraphDriveId` → SharePoint Embedded **driveId** (looks like `b!…`)
  - `sprk_GraphItemId`  → **driveItem.id** (looks like `01…`)

**Why this is mandatory**
- Security decisions (UAC, legal holds, matter relationships) live on the **document** not the blob.
- Storage topology can change; the **document GUID** remains stable.
- Prevents storage enumeration and keeps Graph scopes out of the browser.

---

## 3) Dataverse schema checklist

Make sure `sprk_Document` includes and populates:

- `sprk_GraphDriveId` (Single Line of Text, 200+ chars) — **required** once file is ready  
- `sprk_GraphItemId`  (Single Line of Text, 100+ chars) — **required** once file is ready  
- Recommended metadata: `sprk_FileName`, `sprk_FileSize`, `sprk_ContentType`, `sprk_ETag`

If you have legacy fields (`sprk_DriveId`, `sprk_DriveItemId`) hide them or map them to these two names in **one** place (BFF).

---

## 4) PCF control — client responsibilities

- Acquire a **BFF audience** token via MSAL. Do **not** request Graph scopes in the browser.
- Call the BFF endpoints using the **Document GUID**. Do **not** pass `driveItemId` to `/api/documents/*` routes.
- Render the returned preview handle in an `<iframe>` (or trigger a download for non-previewable types).

### 4.1 Obtain the Document GUID from the model-driven host

```ts
// In updateView (dataset or field control)
const formId = (context.mode as any)?.contextInfo?.entityId; // Dataverse GUID of current row
const documentId = props.documentId || formId;               // if control exposes an input, default to form id

if (!documentId) {
  showBanner("No document id found on the form.");
  return;
}
4.2 Request a preview URL from the BFF
ts
Copy code
// MSAL scope must be your exposed API scope, not ".default"
const token = await auth.acquireToken({ scopes: ["api://<BFF_APP_ID>/SDAP.Access"] });

const res = await fetch(`${BFF_BASE_URL}/api/documents/${documentId}/preview-url`, {
  headers: { Authorization: `Bearer ${token}` }
});
if (!res.ok) {
  const problem = await res.json().catch(() => ({}));
  showBanner(problem.title || "Preview failed");
  return;
}
const handle: { url: string; expiresAtUtc: string; contentType?: string } = await res.json();
iframe.src = handle.url; // short-lived; refresh when near expiry if needed
4.3 Optional UX guard
ts
Copy code
const driveId = getColumnValue(record, "sprk_graphdriveid");
const itemId  = getColumnValue(record, "sprk_graphitemid");
if (!driveId || !itemId) {
  showBanner("This file is initializing. Please try again in a moment.");
  // still call BFF with GUID; it will return precise ProblemDetails if mapping is missing
}
5) BFF (Spe.Bff.Api) — server responsibilities
Enforce UAC via an endpoint filter before any storage call.

Resolve GUID → (driveId,itemId) from Dataverse.

Call Graph to obtain preview or download handle; return ProblemDetails for bad inputs/missing mapping.

Include correlation + Graph request-id in logs; never leak app-only secrets.

5.1 Routes (Minimal API)
csharp
Copy code
var docs = app.MapGroup("/api/documents").RequireAuthorization().WithTags("Documents");

docs.MapGet("/{id}/preview-url", async (string id, DocumentPreviewService svc, CancellationToken ct) =>
{
    if (!Guid.TryParse(id, out var docId))
        return Results.Problem(title: "Invalid document id",
                               detail: "Id must be a GUID (Dataverse pk).",
                               statusCode: 400,
                               extensions: new(){{"code","invalid_id"}});
    var handle = await svc.GetPreviewAsync(docId, ct);
    return Results.Json(handle);
})
.AddEndpointFilter(new DocumentAuthorizationFilter(DocumentOperation.Read));

docs.MapGet("/{id}/download", async (string id, DocumentDownloadService svc, HttpContext ctx, CancellationToken ct) =>
{
    if (!Guid.TryParse(id, out var docId))
        return Results.Problem(title: "Invalid document id",
                               detail: "Id must be a GUID (Dataverse pk).",
                               statusCode: 400,
                               extensions: new(){{"code","invalid_id"}});
    // Option A: return a short-lived URL handle
    // var handle = await svc.GetDownloadHandleAsync(docId, ct);
    // return Results.Json(handle);

    // Option B: stream bytes directly
    await svc.StreamAsync(docId, ctx.Response, ct);
    return Results.Empty;
})
.AddEndpointFilter(new DocumentAuthorizationFilter(DocumentOperation.Read));
5.2 Data access — resolve storage pointers
csharp
Copy code
public interface IAccessDataSource
{
    Task<(string DriveId, string ItemId)> GetSpePointersAsync(Guid documentId, CancellationToken ct);
}

public sealed class DataverseAccessDataSource : IAccessDataSource
{
    private readonly IDataverseClient _dv;
    private static bool IsLikelyDriveId(string? v) => !string.IsNullOrWhiteSpace(v) && v.StartsWith("b!") && v.Length > 20;
    private static bool IsLikelyItemId(string? v)  => !string.IsNullOrWhiteSpace(v) && char.IsLetterOrDigit(v[0]) && v.Length > 20;

    public async Task<(string DriveId, string ItemId)> GetSpePointersAsync(Guid documentId, CancellationToken ct)
    {
        var row = await _dv.GetAsync("sprk_documents", documentId,
            select: new[] { "sprk_graphdriveid", "sprk_graphitemid" }, ct: ct);

        if (row is null) throw Problem("document_not_found", "Document row not found.", 404);

        var driveId = row.GetString("sprk_graphdriveid");
        var itemId  = row.GetString("sprk_graphitemid");

        if (!IsLikelyDriveId(driveId))
            throw Problem("mapping_missing_drive", "DriveId is not recorded or invalid for this document.", 409);
        if (!IsLikelyItemId(itemId))
            throw Problem("mapping_missing_item", "ItemId is not recorded or invalid for this document.", 409);

        return (driveId!, itemId!);
    }

    private static InvalidOperationException Problem(string code, string message, int http = 400)
        => new ($"{code}:{http}:{message}");
}
Map this exception to a proper ProblemDetails in your exception middleware. Or define a tiny SdapProblem : Exception with (code, status) and catch it centrally.

5.3 Preview service — Graph call
csharp
Copy code
public sealed class DocumentPreviewService
{
    private readonly IAccessDataSource _access;
    private readonly SpeFileStore _spe; // wraps Graph (OBO/app-only)

    public DocumentPreviewService(IAccessDataSource access, SpeFileStore spe) 
        => (_access, _spe) = (access, spe);

    public async Task<PreviewHandle> GetPreviewAsync(Guid docId, CancellationToken ct)
    {
        var (driveId, itemId) = await _access.GetSpePointersAsync(docId, ct);
        return await _spe.GetPreviewUrlAsync(driveId, itemId, ct); // Graph driveItem: preview
    }
}

public sealed record PreviewHandle(string Url, DateTime ExpiresAtUtc, string? ContentType);
Inside SpeFileStore.GetPreviewUrlAsync call driveItem: preview; for raw types fall back to @microsoft.graph.downloadUrl. Always log the Graph request-id.

6) Manifest correction (PCF)
If you flipped the control to control-type="virtual" without defining a dataset element, it won’t render. Use:

xml
Copy code
<control namespace="Spe" constructor="SpeFileViewer" version="1.0.5" control-type="standard">
  <!-- inputs (e.g., documentId) -->
</control>
Rebuild and import as v1.0.5; remove the broken v1.0.4 solution from the environment.

7) Upload/finalize — persist both IDs together
When the BFF completes an upload and receives the final DriveItem:

csharp
Copy code
// From Graph DriveItem result:
await _dv.UpdateAsync("sprk_documents", docId, new Dictionary<string, object>
{
    ["sprk_graphitemid"]  = item.Id,
    ["sprk_graphdriveid"] = item.ParentReference?.DriveId,
    ["sprk_filename"]     = item.Name,
    ["sprk_filesize"]     = item.Size,
    ["sprk_contenttype"]  = item.File?.MimeType,
    ["sprk_etag"]         = item.ETag
}, ct);
If DriveId is missing, retry once; then return mapping_missing_drive (409) so the PCF shows a precise message.

8) Error model and telemetry
Return ProblemDetails with stable code values and log correlation + Graph request IDs:

invalid_id — parameter is not a GUID

document_not_found — no DV row for GUID

mapping_missing_drive / mapping_missing_item — DV row exists, mapping incomplete

storage_not_found — Graph 404; file removed in storage

throttled_retry — transient Graph throttling with backoff

PCF should show a clear banner and include the correlation id in support logs.

9) Operational checklist
MSAL scope: api://<BFF_APP_ID>/SDAP.Access (named scope).

CORS: BFF allows Dataverse host origins.

JWT audience: enforce the BFF audience in Program.cs.

UAC: endpoint filter invokes AuthorizationService before storage calls.

Mapping: both sprk_GraphDriveId and sprk_GraphItemId populated before preview/download.

No plugins for preview: PCF calls BFF directly; plugins remain thin and contain no outbound HTTP.