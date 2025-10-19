# Spaarke Email → .EML Export (Dataverse → SharePoint Embedded)

## 1) Project Summary

**Goal**  
Enable users in the Power Apps model-driven app to select an Email record, preview what will be exported, and generate a standards-compliant `.eml` file (and optionally separate original attachments) that are uploaded to SharePoint Embedded (SPE) and indexed as Spaarke Document records—without relying on Graph “get .eml” APIs, plugins, classic workflows, Power Automate, or web-resource JavaScript.

**Problem**  
Dataverse stores Email content, activity parties, and attachments (as base64) after server-side sync, but it does not preserve the original raw `.eml/.msg`. We need to reconstruct the `.eml` with correct RFC 5322/MIME structure so Outlook/Gmail render it exactly as expected, including inline images (`cid:`), body alternatives (text/plain + text/html), and file attachments.

**Solution**  
- UI: A **PCF modal** launched from the Email command bar (“Save Email”) to show a checklist of items to be created (the `.eml`, plus optional original attachments).  
- Server boundary: A **Dataverse Custom API** (thin) invoked by the PCF, which forwards the request to a **Minimal API/BFF endpoint** running under app identity.  
- Processing: The **BFF** reads Email + Parties + Attachments from Dataverse, constructs a compliant MIME tree (using a MIME library), serializes to CRLF `.eml`, uploads to **SharePoint Embedded** through a single SPE facade, and writes **Document** rows in Dataverse (flat storage model) with back-links to the source Email and its “regarding” record.  
- Standards: Adheres to our ADRs (no plugins, no flows, PCF over naked JS, minimal APIs, single storage seam, endpoint-filter authorization).

---

## 2) Non-Functional & ADR Alignment

- **No Plugins / No Power Automate:** All heavy work runs in the BFF. Dataverse hosts only a thin Custom API and the Document index.  
- **Prefer PCF over web resources:** The modal UX is a PCF dialog; no legacy form JS.  
- **Minimal API + Endpoint Filters:** Authorization is enforced at the BFF endpoint; Dataverse privilege checks are mirrored in the Custom API.  
- **Single Storage Seam for SPE:** A `SpeFileStore` facade isolates all file I/O from business logic.  
- **Flat Storage:** Dataverse holds Document metadata/links; bytes live in SPE.  
- **Idempotent & Testable:** Export is deterministic; re-parse tests validate MIME integrity.

---

## 3) Architecture Overview

- **Model-Driven App**  
  - Email form command bar button: **“Save Email”**  
  - Opens **PCF Modal: “Create Document”** (checklist: `.eml`, Attachment 1..N)

- **Dataverse Custom API** `sprk_ExportEmailToEml`  
  - Validates caller & inputs, forwards to BFF (server-to-server), returns results.

- **BFF Minimal API (ASP.NET Core, .NET 8)**  
  - Endpoint: `POST /api/email/{emailId}/export-eml`  
  - Steps:  
    1. Load Email (subject, description, dates, messageid…), Parties (From/To/Cc/Bcc), Attachments (filename, mimetype, body base64, inline flags, contentId).  
    2. Build MIME: `multipart/alternative` (text/plain + text/html) → optional `multipart/related` (inline images) → optional `multipart/mixed` (file attachments).  
    3. Serialize as CRLF `.eml` bytes; upload to **SPE**; create **Document** rows.  
    4. Return created documents (IDs, URLs) to the Custom API → PCF.

- **Security**  
  - PCF button visible only for users with Email Read + Document Create.  
  - Custom API and BFF re-enforce authorization (defense in depth).  
  - BFF uses app identity; no client secrets/tokens in the browser.

---

## 4) Data Sources and Field Mapping

**Email (`email`)**  
- `subject` → `Subject` header (RFC 2047 encoding when needed)  
- `description` → HTML body (used for `text/html`; also stripped for `text/plain`)  
- `actualend` or `createdon` → `Date` header (RFC 2822)  
- `messageid` → `Message-ID` header (or generate stable one if absent)

**Activity Party (`activityparty`)**, filtered by `activityid = {emailId}`; map by `participationtypemask`:  
- `1 (Sender)` → `From` (and optionally `Sender`)  
- `2 (ToRecipient)` → `To`  
- `3 (CcRecipient)` → `Cc`  
- `4 (BccRecipient)` → `Bcc`  
Mailbox resolution: prefer `addressused`; fallback to `partyid.emailaddress1`. Display name from `partyid.name`/`fullname` (RFC 2047 when non-ASCII).

**Attachments (`activitymimeattachment`)**, filtered by `objectid = {emailId}`:  
- `filename` → `Content-Disposition: attachment; filename*=` (RFC 2231 for non-ASCII)  
- `mimetype` → `Content-Type`  
- `body` (base64) → binary content  
- `isinline` (bool) + `attachmentcontentid` (string) → inline image with `Content-Id: <cid>`; ensure HTML references `cid:cid`.

---

## 5) MIME Construction Rules (Key for Render Parity)

- Always set `MIME-Version: 1.0`.  
- `multipart/alternative` for `text/plain` and `text/html`.  
- If any `isinline` attachments exist, wrap alternative inside `multipart/related` and append inline parts (Content-Id must match HTML `cid:`).  
- If any non-inline attachments exist, wrap the whole body in `multipart/mixed` and append attachments.  
- Use CRLF (`\r\n`) newlines on serialization; choose Quoted-Printable or Base64 encodings appropriately.  
- Encode non-ASCII headers: RFC 2047 (Subject, display names); RFC 2231 (filename*).

---

## 6) Components & Code Examples

### 6.1 PCF Modal (TypeScript)

```ts
async function onSubmit() {
  const payload = {
    emailId,
    includeEml: true,
    includeAttachmentIds: selectedAttachmentIds,
    folderPath: userFolderChoice,
    fileNamePrefix: userPrefix || "",
    regardingId
  };

  const req = {
    getMetadata: () => ({
      boundParameter: null,
      operationName: "sprk_ExportEmailToEml",
      operationType: 0
    }),
    requestParams: payload
  };

  const resp = await (Xrm.WebApi as any).execute(req);
  if (resp.ok) {
    const result = await resp.json();
  } else {
    // Handle error
  }
}
```

### 6.2 Dataverse Custom API (C#; thin)

```csharp
public class ExportEmailToEmlHandler : ICustomApiHandler
{
    public async Task<OrganizationResponse> Execute(OrganizationRequest req, IServiceProvider sp)
    {
        var http = sp.GetRequiredService<HttpClient>();
        var dto = MapToBffDto(req);
        var bffResp = await http.PostAsJsonAsync(
            $"/api/email/{dto.EmailId}/export-eml", dto, req.CancellationToken);

        if (!bffResp.IsSuccessStatusCode)
            throw new InvalidPluginExecutionException(await bffResp.Content.ReadAsStringAsync());

        var result = await bffResp.Content.ReadFromJsonAsync<ExportResult>();
        return MapToOrgResponse(result);
    }
}
```

### 6.3 BFF Minimal API (C#; .NET 8)

```csharp
app.MapPost("/api/email/{emailId:guid}/export-eml",
    async (Guid emailId, ExportRequest req,
           IDataverseReader dv, IMimeBuilder mime, ISpeFileStore spe, IDocumentWriter docs,
           IAuthorizationService authz, HttpContext http, ILogger<Program> log) =>
{
    await authz.DemandAsync(http.User, Permissions.EmailReadAndDocumentCreate(emailId));

    var src = await dv.LoadEmailBundleAsync(emailId);

    byte[] emlBytes = mime.BuildEml(new MimeInput {
        Email = src.Email, Parties = src.Parties, Attachments = src.Attachments
    });

    var existing = await docs.TryFindExistingEmlAsync(emailId, req.FolderPath);
    if (existing is not null && !req.ForceNew)
        return Results.Ok(existing);

    var emlName = NameBuilder.Safe($"{req.FileNamePrefix}{src.Email.Subject}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.eml");
    var emlUpload = await spe.UploadAsync(new SpeUpload(emlName, req.FolderPath, new MemoryStream(emlBytes),
        metadata: new() { ["source"]="Dataverse.Email", ["emailId"]=emailId.ToString(), ["messageId"]=src.Email.MessageId ?? "" }));

    var emlDoc = await docs.CreateDocumentAsync(new DocumentCreate {
        EmailId = emailId, RegardingId = req.RegardingId,
        FileName = emlName, Url = emlUpload.WebUrl, Kind = "eml"
    });

    var extraDocs = new List<DocumentResult>();
    if (req.IncludeAttachmentIds?.Any() == true)
    {
        foreach (var a in src.Attachments.Where(x => req.IncludeAttachmentIds.Contains(x.AttachmentId) && !x.IsInline))
        {
            var upload = await spe.UploadAsync(new SpeUpload(a.FileName, req.FolderPath, new MemoryStream(a.Bytes)));
            var doc = await docs.CreateDocumentAsync(new DocumentCreate {
                EmailId = emailId, RegardingId = req.RegardingId,
                FileName = a.FileName, Url = upload.WebUrl, Kind = "attachment"
            });
            extraDocs.Add(doc);
        }
    }

    return Results.Ok(new ExportResult { Primary = emlDoc, Extras = extraDocs });
})
.AddEndpointFilter<AuthzFilter>();
```

### 6.4 MIME Builder (C#; using MimeKit)

```csharp
public sealed class MimeBuilder : IMimeBuilder
{
    public byte[] BuildEml(MimeInput input)
    {
        var msg = new MimeMessage();
        MapParties(msg, input.Parties);
        msg.Subject = EncodeSubject(input.Email.Subject);
        msg.Date = ToRfc2822(input.Email.Date);
        msg.MessageId = string.IsNullOrWhiteSpace(input.Email.MessageId)
            ? MimeUtils.GenerateMessageId("spaarke.local") : input.Email.MessageId;

        var textPart = new TextPart("plain") { Text = HtmlToText(input.Email.HtmlBody ?? "") };
        var htmlPart = new TextPart("html")  { Text = input.Email.HtmlBody ?? "" };
        var alt = new Multipart("alternative") { textPart, htmlPart };

        MimeEntity body = alt;

        var inlines = input.Attachments.Where(a => a.IsInline).ToList();
        if (inlines.Any())
        {
            var related = new MultipartRelated { alt };
            foreach (var a in inlines)
            {
                var p = new MimePart(a.MimeType ?? "application/octet-stream")
                {
                    Content = new MimeContent(new MemoryStream(a.Bytes), ContentEncoding.Base64),
                    ContentId = EnsureAngleBrackets(a.ContentId),
                    ContentDisposition = new ContentDisposition(ContentDisposition.Inline),
                    FileName = a.FileName
                };
                related.Add(p);
            }
            body = related;
        }

        var files = input.Attachments.Where(a => !a.IsInline).ToList();
        if (files.Any())
        {
            var mixed = new Multipart("mixed") { body };
            foreach (var a in files)
            {
                var p = new MimePart(a.MimeType ?? "application/octet-stream")
                {
                    Content = new MimeContent(new MemoryStream(a.Bytes), ContentEncoding.Base64),
                    ContentDisposition = new ContentDisposition(ContentDisposition.Attachment),
                    ContentTransferEncoding = ContentEncoding.Base64,
                    FileName = a.FileName
                };
                mixed.Add(p);
            }
            body = mixed;
        }

        msg.Body = body;

        using var ms = new MemoryStream();
        var opts = FormatOptions.Default.Clone();
        opts.NewLineFormat = NewLineFormat.Dos;
        msg.WriteTo(opts, ms);
        return ms.ToArray();
    }
}
```

---

## 7) Important Considerations

- **Transport headers:** Not preserved; export is content-faithful, not forensic.  
- **Encoding:** RFC 2047/2231 for headers; CRLF line endings.  
- **Inline `cid:` correctness:** Ensure matches between HTML and headers.  
- **Idempotency:** Avoid duplicates per `emailId`.  
- **Large files:** Stream uploads; support chunking to SPE.  
- **AuthZ:** Enforce in both Custom API and BFF endpoint.  
- **Diagnostics:** Log timing, sizes, counts, hashes.  
- **ALM:** Bundle components in unified solution.  
- **UX:** Show inline items as informational only.  
- **Localization:** Preserve Unicode file names safely.  
- **SPE Strategy:** One container per client account; enforce via UAC mapping.

---

## 8) Knowledge Resources

- Power Apps PCF documentation  
- Dataverse Web API reference (Email, ActivityParty, ActivityMimeAttachment)  
- Dataverse Custom API authoring  
- ASP.NET Core Minimal APIs and Endpoint Filters  
- MimeKit docs (multipart layering, encodings)  
- SharePoint Embedded integration reference  
- RFCs 5322, 2047, 2231, 2387  

---

## 9) Build Plan (Tasks for AI Agent)

### Task 0 — Repo & Solution Scaffolding
Create `/modules/email-export-eml/` with subfolders and solution projects.  

### Task 1 — Data Readers (Dataverse S2S)
Implement `IDataverseReader` returning Email, Parties, Attachments.  

### Task 2 — MIME Builder
Build `IMimeBuilder` with tests for all permutations (inline, multi-attachments, non-ASCII).  

### Task 3 — SPE Facade & Document Writer
Upload `.eml` and attachments to SPE, create Dataverse Document rows, handle idempotency.  

### Task 4 — Minimal API Endpoint
Wire `POST /api/email/{emailId}/export-eml`; integrate Reader → MIME → SPE → Document.  

### Task 5 — Dataverse Custom API
Create `sprk_ExportEmailToEml` definition and handler.  

### Task 6 — PCF Modal (UI/UX)
Checklist modal; call Custom API; show result.  

### Task 7 — Telemetry & Policies
Add structured logging, retry policies, size limits.  

### Task 8 — CI/CD & Rollout
Automate builds, tests, and deploy; feature flag for pilot users.

---

## 10) Test Plan

- Unit: MIME builder variants, field mappings, encodings.  
- Integration: full export cycle in sandbox.  
- Idempotency: re-run export returns existing file.  
- Security: permission enforcement verified.  
- Performance: acceptable latency under load.  
- UX: PCF dialog correctness and status display.

---

## 11) Deliverables

- `pcf/EmailExportDialog`  
- `Spaarke.EmailExport.Api`  
- `Spaarke.EmailExport.Domain`  
- `Spaarke.EmailExport.Infrastructure`  
- `Spaarke.EmailExport.Tests`  
- Dataverse solution (Custom API, command button, roles)  
- `docs/README.md`, `docs/links.md`, `docs/runbook.md`

---

## 12) Definition of Done

- “Save Email” button works end-to-end.  
- `.eml` file is standards-compliant and opens cleanly in Outlook/Gmail.  
- Document rows created and linked correctly.  
- Telemetry healthy; all tests pass; no plugins, flows, or legacy JS.
