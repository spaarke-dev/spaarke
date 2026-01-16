# sdap-fileviewer-open-in-desktop-and-preview-ux-spec.md

## 1. Title
SDAP FileViewer – Open in Desktop Edit Mode + Preview Performance &amp; UX Enhancements

## 2. Document Version
Version: 1.0
Author: Spaarke Engineering
Status: Design Approved
Scope: FileViewer PCF, SDAP BFF, SPE integration
Out of Scope: Office.js Add-In (“Share via Spaarke”), Document Intelligence Pane

# 3. Purpose &amp; Problem Statement
The current FileViewer PCF uses an embedded Office Web App for edit mode. This causes:


Exposure of unwanted Word commands (Delete, Share)


SPE Copilot limitations


Narrow, cramped editing UX inside the model-driven form


Slow load time on first preview due to App Service / SPE / Office cold start


Black-screen flash during Office preview initialization


Limited ability to control the overall editing experience


This spec replaces embedded “Edit in Web” with “Open in Desktop”, and implements a new loading-state UX for preview.
The FileViewer should:


Embed preview only (no edits in iframe)


Provide a single edit mode: Open in Desktop Word/Excel/PowerPoint


Show a polished, branded loading skeleton rather than a black box


Reduce perceived latency via warm-up patterns


Fully align with SDAP ADR architecture (SPE as flat storage, BFF endpoints, PCF simplicity)

# 4. Goals
### 4.1 Replace Embedded Editing


Remove all iframe-based embedded Office editing.


Add “Open in Desktop” as the primary edit action.


### 4.2 Improve Preview UX


Replace the black background with:


White background


Loading skeleton


Spinner and/or “Loading document…” message




Prevent flickering by showing iframe only after iframe.onload.




          
            
          
        
  
        
    

4.3 Performance Improvement


Address slow initial load caused by:


App Service cold start


First SPE Graph request


First Office preview load in the session




Add warming strategies:


App Service “Always On”


Lightweight warm-up endpoint hit on form-load


Pre-fetch preview URL early in the PCF lifecycle


Ensure DI/Graph clients are properly singletoned




### 4.4 Maintain Security Design


Users cannot delete SPE files from Word (desktop mode does not expose file delete).


Users cannot “share” via Word (SPE does not support share links).


Only SDAP can manage delete or share operations.



# 5. Non-Goals
### Not included in this implementation:


Building the Office.js “Share via Spaarke” add-in


Adding Spaarke AI pane inside Word desktop


Modifying Dataverse Document/UAC model


Modifying SPE container-level permissions


These are separate roadmap items.

# 6. High-Level Architecture
FileViewer PCF → SDAP BFF → SpeFileStore → Microsoft Graph → SPE
                             ↑
                       PCF Pre-load


# 7. Detailed Design
## 7.1 BFF Endpoint: /files/{driveId}/{itemId}/open-links


          
            
          
        
  
        
    

Purpose
To return:


desktopUrl for Word/Excel/PowerPoint desktop


webUrl as optional fallback


MIME type + filename (for analytics or custom UI)


### Route Definition
GET /files/{driveId}/{itemId}/open-links

### Response DTO
{
  "fileName": "example.docx",
  "mimeType": "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
  "webUrl": "https://.../example.docx",
  "desktopUrl": "ms-word:ofe|u|https://.../example.docx"
}



Implementation Notes


Uses SpeFileStore.GetFileMetadataAsync().


Constructs desktop protocol link based on MIME.


Must pass through endpoint-level authorization filters (ADR-003, ADR-008).


GraphServiceClient + HttpClient MUST be singletons.


### Pseudo-Code (C#)
app.MapGet("/files/{driveId}/{itemId}/open-links", async (
    string driveId,
    string itemId,
    ISpeFileStore speFileStore,
    HttpContext ctx) =&gt;
{
    var file = await speFileStore.GetFileMetadataAsync(driveId, itemId, ctx.RequestAborted);

    var desktopUrl = DesktopUrlBuilder.FromMime(file.WebUrl, file.MimeType);

    return Results.Ok(new {
        fileName = file.Name,
        mimeType = file.MimeType,
        webUrl = file.WebUrl,
        desktopUrl = desktopUrl
    });
});


## 7.2 FileViewer PCF – Preview + Desktop Edit
### 7.2.1 Behavior Summary


Preview:


Always embedded iframe


Only visible after iframe fires onload


White background


Loading overlay during initialization




Edit:


Single button: Open in Desktop


Calls BFF /open-links endpoint


Opens desktopUrl via protocol link






          
            
          
        
  
        
    

7.2.2 State Machine
loading → ready → error

### 7.2.3 Updated Rendering Logic


Default to state = "loading".


Render:


Root container with white background.


Loading overlay.




Once previewUrl is retrieved:


Create iframe but keep overlay visible.


On iframe.onload: set state = "ready" and re-render.




Only show iframe after it is ready.


### Pseudo-code (TypeScript)
private state: "loading" | "ready" | "error" = "loading";

private async loadPreviewUrl() {
    this.state = "loading";
    this.previewUrl = await this.apiClient.getPreviewUrl(this.documentId);
    this.render();
}

private render() {
    this._container.innerHTML = "";

    const root = document.createElement("div");
    root.className = "sprk-file-viewer-root";
    this._container.appendChild(root);

    if (this.state === "loading") {
        const overlay = document.createElement("div");
        overlay.className = "sprk-file-viewer-overlay";
        overlay.innerText = "Loading document…";
        root.appendChild(overlay);
    }

    if (this.previewUrl) {
        const frame = document.createElement("iframe");
        frame.className = "sprk-file-viewer-frame";
        frame.src = this.previewUrl;

        frame.onload = () =&gt; {
            this.state = "ready";
            this.render();  // re-render without overlay
        };

        root.appendChild(frame);
    }
}



          
            
          
        
  
        
    

CSS (Injected/Scoped)
.sprk-file-viewer-root {
  position: relative;
  width: 100%;
  height: 100%;
  background: #ffffff;
}

.sprk-file-viewer-frame {
  width: 100%;
  height: 100%;
  background: #ffffff;
  border: 0;
}

.sprk-file-viewer-overlay {
  position: absolute;
  inset: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  background: #ffffff;
  font-size: 14px;
  z-index: 1;
}

### Avoid repeated iframe loads
Do not set iframe.src if:


The file has not changed


The URL has not expired


This prevents flickering and black frames.

## 7.3 Performance Enhancements
### 7.3.1 Back-end: App Service Warm-Up


Enable Always On for SDAP App Service.


Add a lightweight /ping or /health endpoint.


Optional external warm-up agent hitting /ping every 5 minutes.




          
            
          
        
  
        
    

7.3.2 Pre-Warm on Form Load
During form onLoad:


Use a small JS snippet or custom API call to “touch” the BFF.


Goal: warm Graph authentication + warm container.


### 7.3.3 Pre-fetch Preview URL Early in PCF
Move preview-URL fetch to earliest possible lifecycle:


init() or early updateView()


Avoid calling it only after full DOM construction


### 7.3.4 Ensure Graph Client is DI-Singleton
In SDAP:


Register GraphServiceClient via IHttpClientFactory or DI as a singleton.


Never create new GraphServiceClient per request.




7.3.5 SPE Preview Caching (Small Gains)
Preview URL is short-lived but can be reused within that TTL.
Cache minimally in-memory for the PCF session.

# 8. Security Considerations
### 8.1 Deskop Word Edit Mode


Users cannot delete the file via desktop Word UX.


Users cannot share the file (SPE does not allow user-level sharing).


### 8.2 Delete Path


Only SDAP service principal can delete SPE files.


File delete triggered only from Dataverse “Delete Document”.




          
            
          
        
  
        
    

8.3 Authorization


BFF uses endpoint-level filters to:


Resolve Document from {driveId, itemId}


Enforce UAC/Dataverse permission rules




### 8.4 Sharing


User cannot generate a share link from Word.


Sharing remains controlled exclusively by Spaarke UAC/SDAP/Dataverse.



# 9. ADR Alignment



ADR
Alignment




ADR-001
Minimal API + BFF endpoint only; no new compute hosts


ADR-005
Flat storage; no folder structure; SPE remains headless


ADR-006 / ADR-014
FileViewer remains PCF with refined UX pattern


ADR-007
All Graph calls through SpeFileStore


ADR-003 / ADR-008
Authorization through endpoint filters


ADR-016
Improved performance (pre-warm, caching, throttling-safe)




# 10. Rollout Plan


Implement BFF /open-links.


Update FileViewer PCF:


Remove embedded edit mode


Add state machine and loading overlay


Update UI to “Open in Desktop”




Add App Service “Always On”.


Add /ping route and optional external warm-up job.


Validate:


Fast preview warm-up after first load


Proper rendering with no black background


Desktop edit opens reliably




Deploy to test → pilot → production.



# 11. Future Enhancements (Separate Specs)


Office.js Add-in:


“Share via Spaarke”


“Spaarke AI” Document Intelligence Pane




Document Intelligence in FileViewer (PCF + Custom Page)


Advanced multi-document RAG in SDAP


Power Pages–based external guest view



End of Specification
sdap-fileviewer-open-in-desktop-and-preview-ux-spec.md

If you'd like, I can now draft the companion spec:


spaarke-office-addin-sharing-v1.md


or spaarke-word-ai-pane-spec.md
depending on which feature is next.
