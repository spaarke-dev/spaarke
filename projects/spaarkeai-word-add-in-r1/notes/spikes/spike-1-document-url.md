# Spike-1: `Office.context.document.url` shape for SPE files in Word desktop

> **VERDICT: 🟢 GREEN (2026-09-11)** — see §22–§23. Live through the deployed BFF: the Word **web and desktop**
> captures (byte-identical) both resolve to the right `sprk_document`, and links 1–4 are all GREEN. The desk
> verdict below was 🟠 AMBER — see §9. It is kept as written, as the record of what was knowable before the live
> pass. Link 4 (the one the POML flagged as most likely to be assumed rather
> than checked) is **GREEN and closed**. Links 1–3 could not be closed: this session had **no live Office
> host**, and sideloading is no longer available in this environment, so the empirically-verifiable half
> is an operator pass (§8).
>
> **Three findings the task POML did not anticipate:**
> 1. 🔴 **Spaarke's own open flow is a hazard to Link 1** (§5 1c/1d) — it hands Word an abbreviated
>    protocol URL that opens the file in **Protected View** as a Restricted-Sites workaround. This is the
>    single biggest reason the verdict is not GREEN.
> 2. 🔴 **A live manifest defect, fixable today with no host** (§6.5) — `Word.Document.customXmlParts`
>    and `.settings` require **WordApi 1.4**; both Word manifests declare **1.3**. Same class as the
>    1.1-vs-1.3 mismatch task 011 caught.
> 3. 🟢 **The FR-02 stamp is a stronger PRIMARY identity mechanism than `document.url`** (§7), now that
>    the operator's dev-only decision has removed its only stated disqualifier.
>
> **Status**: desk + in-repo evidence complete 2026-09-09 (task 002, `task-execute`, rigor STANDARD,
> tier opus, effort high). **Nothing in this document is `EMPIRICALLY VERIFIED`** — see §3.

---

## 1. The question

Does `Office.context.document.url` return a usable value for a SharePoint-Embedded-backed file opened in
**Word on Windows desktop**, and does that value carry end-to-end through the FR-01 identity chain?

```
Office.context.document.url
   → base64url  →  Graph GET /shares/u!{enc}/driveItem
   → driveId + itemId
   → sprk_document  via the sprk_graphitemid_uk alternate key
```

Microsoft documents this API's behaviour for Word on the web. The **desktop** behaviour for SPE containers
is the unknown, and FR-01's primary path rests entirely on it. spec.md FR-01 (line 61), Unresolved
Questions (line 281); plan.md risk **R-1**.

---

## 2. What exists today

| Fact | Evidence |
|---|---|
| The Word adapter has **no identity mechanism at all**. `getItemId()` returns `` `word-doc-${hash(title-author-creationDate)}` `` — a hash over three mutable document properties. It is structurally incapable of resolving to a record, and two different documents with the same title/author/creation date collide. | `src/client/office-addins/shared/adapters/WordAdapter.ts:78-106` (the hash is built at `:97-102`) |
| The same method's own comment concedes the gap: *"Office.js doesn't directly expose the file URL in all scenarios."* | `WordAdapter.ts:96` |
| `_documentUrl` is a **cache field that is never populated from Office.js** — `getItemId()` returns it if set (`:82-84`), then the only writer stores the synthesized hash into it (`:103`). The field is named for a URL it never holds. | `WordAdapter.ts:82-84`, `:103` |
| The Word manifests declare **`WordApi` minVersion `1.3`** and nothing else. No `CustomXmlParts` requirement set is declared on either surface. | `src/client/office-addins/word/word-manifest.xml:43-47`; `src/client/office-addins/word/manifest.json:35-44` |
| XML manifest requests `ReadWriteDocument`. The unified JSON manifest declares `"authorization": { "permissions": { "resourceSpecific": [] } }` — **empty**. | `word-manifest.xml:56`; `manifest.json:28-32` |
| `sprk_graphitemid` is written **verbatim** from Graph's `driveItem.id`, with no normalization anywhere on the path. | Traced in full at §5 Link 4 |
| The BFF makes **no Graph `/shares/` call anywhere**. A repo-wide grep for `/shares/u!`, `"/shares/"` and `Shares[` across `src/server/**` returns zero hits. `SpeFileStore` exposes no share-resolution method (39 public methods enumerated; none resolves a sharing token). | `src/server/api/Sprk.Bff.Api/Infrastructure/Graph/SpeFileStore.cs:54-430` |
| Spaarke's live "open in Word desktop" flow hands Word **`ms-word:{webDavUrl}`** in the abbreviated protocol format, and the code documents that this opens the file in **Protected View**. | `src/server/shared/Spaarke.Core/Utilities/DesktopUrlBuilder.cs:37-61`; `src/server/api/Sprk.Bff.Api/Api/FileAccessEndpoints.cs:544-668` |

**Implication for task 012/013**: there is nothing to extend on either side. The client has no identity
capability, and the server has no share-resolution facade method. Both are new surface (spec.md New
Components table, line 176, already carries the §11 justification for `WordDocumentIdentityService`).

---

## 3. Method and provenance policy

Two labels are used, and they are never blurred:

- **`DESK-RESEARCHED`** — established without running anything. Two sub-forms, both cited inline:
  - *(types)* an exact declaration quoted from `@types/office-js@1.0.568`
    (`src/client/office-addins/node_modules/@types/office-js/index.d.ts`, with line numbers).
  - *(in-repo)* an exact `file:line` citation from this repository's source.
  A third sub-form — *(Learn)* a Microsoft Learn URL with its stated last-updated date — is used only
  where such a page was actually opened in this session; where it was not, the claim says so.
- **`EMPIRICALLY VERIFIED`** — host, host build number, platform, file source, and what was observed.

> ⚠️ **Nothing in this document carries the `EMPIRICALLY VERIFIED` label.** This session had no live
> Office host. Per the task POML's binding constraint — *"If the desktop behaviour cannot be established
> without a live host and no live host is available to you, the honest verdict is AMBER with the operator
> recipe attached — not GREEN"* — the verdict in §9 is AMBER and §8 is the recipe that closes it.
>
> **Sideloading is no longer available in this environment.** Task 011 hit the same wall and recorded it
> (`notes/011-word-manifest-migration.md` § Blocked: *"This agent has no interactive Office host or
> browser session available"*). Reaching a task pane now requires a deployed build — see §8.2.

**Reading an in-repo `file:line` is not the same as running it.** Where a conclusion depends on runtime
behaviour rather than on what the code says, that is stated explicitly rather than upgraded.

---

## 4. Summary — the chain at a glance

| Link | Question | Verdict | Basis |
|---|---|---|---|
| **1** | Does `document.url` return a usable value on Word **desktop** for an SPE file? | 🟠 **AMBER — the weakest link** | Types silent on platform; a repo-discovered Protected-View/`webDavUrl` hazard makes a negative plausible |
| **2** | Is that value resolvable via Graph `GET /shares/u!{enc}/driveItem` for an SPE `contentstorage` URL? | 🟠 **AMBER — unestablished** | No SPE-specific documentation located; **zero** in-repo precedent to lean on |
| **3** | Does the resolved driveItem carry `driveId` + `id` in the shape Spaarke stores? | 🟢 **GREEN (conditional on 2)** | Same Graph property Spaarke already writes; DTO gap noted |
| **4** | Does that pair resolve an `sprk_document` via `sprk_graphitemid_uk` **without touching the key**? | 🟢 **GREEN — closed** | Full write→read trace, no transformation on either side |

**Read this as**: the last link — the one the POML flagged as *"the most dangerous link in the chain and
the one most likely to be assumed rather than checked"* — is **fine**. The risk is entirely front-loaded
into links 1 and 2, and link 1 is worse than the POML assumed.

---

## 5. Link-by-link findings

### Link 1 — `Office.context.document.url` on Word desktop for an SPE file

#### 1a. What the type declarations say — and what they conspicuously do not

`DESK-RESEARCHED (types)` — `@types/office-js@1.0.568`, `index.d.ts:5754-5758`:

```ts
        /**
         * Gets the URL of the document that the Office application currently has open. Returns null if the URL is unavailable.
         */
        url: string;
```

Three things follow, and the third is a defect:

1. **"Returns null if the URL is unavailable"** — the API is documented to fail by returning `null`. There
   is no error, no exception, no status. `DESK-RESEARCHED (types)`, same lines.
2. **The declared type is `string`, not `string | null`.** The doc comment and the type contradict each
   other. TypeScript will therefore **not** force a null check at the call site. `DESK-RESEARCHED (types)`,
   `index.d.ts:5758`. → **Finding for task 013**: any consumer must treat this as `string | null | undefined`
   regardless of what the compiler allows. A missing guard here is a runtime crash, not a type error.
3. **The declaration carries no host-support annotation whatsoever.** The enclosing `Office.Document`
   interface is annotated only `**Applications**: Excel, PowerPoint, Project, Word`
   (`index.d.ts:5726-5732`) — an *application* list, not a *platform* list. Many other members in the same
   file carry an explicit `**Requirement sets**:` block (e.g. `getFilePropertiesAsync` at `:5891-5896`);
   `url` carries none. `DESK-RESEARCHED (types)`, `index.d.ts:5726-5758`.

`DESK-RESEARCHED (Learn)` — the Learn reference page
[`office.document`](https://learn.microsoft.com/en-us/javascript/api/office/office.document?view=word-js-preview)
(last updated **2026-08-31**) carries the identical one-sentence description and adds nothing about
platform. The property does **not** appear on the
[Common API requirement sets page](https://learn.microsoft.com/en-us/javascript/api/requirement-sets/common/office-add-in-requirement-sets)
(last updated **2025-10-10**) either.

> ### The silence is structural, and that is the finding
>
> `Office.context.document.url` has no requirement set **because Office requirement sets enumerate
> *methods*, and `url` is a *property*** — it is out of scope for that mechanism by construction.
> `DESK-RESEARCHED (Learn)`, requirement-sets page (2025-10-10), whose tables list methods only.
>
> This matters more than "the docs happen to be thin". It means **there is no mechanism by which
> Microsoft would ever document, or the manifest ever gate, this property's host support.** You cannot
> `isSetSupported()` your way to safety, and you cannot declare a requirement that guarantees it. The
> only way to know is to observe it. The POML's premise — documented for web, unknown for desktop — is
> **confirmed, and confirmed to be unresolvable by desk research at all.**

**Weak corroboration that "unavailable" is a reachable state in non-vanilla hosting**
`DESK-RESEARCHED (issue tracker — Tier 3, NOT documentation)`: the official `OfficeDev/office-js` tracker
carries [#3204](https://github.com/OfficeDev/office-js/issues/3204) (opened 2023-03-08, *"Status: fix
pending"*) — `document.url` returns empty for guest users, with `getFilePropertiesAsync` behaving
identically — and [#5711](https://github.com/OfficeDev/office-js/issues/5711) (opened 2025-05-15,
unresolved) — `getFilePropertiesAsync` fails with error 5001 on a **WOPI**-hosted document in Word on the
web. Neither is a Word-desktop-plus-SPE datapoint, and neither is documentation. They are recorded only
to establish that the `null` branch is real and reachable in non-standard hosting — **and SPE is
non-standard hosting**. Do not cite these as evidence for a verdict.

#### 1b. The four required cases

The task's acceptance criterion requires a per-case answer **for Word on Windows desktop specifically**.
Here is the honest state of each. No row is `EMPIRICALLY VERIFIED`.

| # | Case | Expected `document.url` | Confidence | Basis |
|---|---|---|---|---|
| (a) | **SPE-backed, opened via Spaarke's flow** (`ms-word:{webDavUrl}`) | **Unknown, and materially at risk.** Plausibly a *local cached/Protected-View path* rather than the SPE URL — see 1c | 🔴 Low | In-repo: `DesktopUrlBuilder.cs:37-47` |
| (b) | OneDrive / SharePoint-Online-opened file | A cloud `https://…` URL | 🟡 Medium — this is the well-trodden case the API was designed for, but not verified here | Types silent; no Learn page opened this session |
| (c) | Local-disk file | A local path (`C:\…`) or a `file:///` URL — **not** a Graph-resolvable URL either way | 🟡 Medium | Types silent; inference from "URL of the document the application currently has open" (`index.d.ts:5756`) |
| (d) | Never-saved document | `null` (the documented "unavailable" case), or possibly `""` | 🟡 Medium | `index.d.ts:5756` documents the `null` return; which of `null`/`""`/`undefined` is not specified |

Cases (c) and (d) matter less than they look: FR-01 is explicitly *conditional* identity, and its own
acceptance says *"a desktop-sourced document resolves to nothing and is treated as new"* (spec.md:61).
For (c) and (d), returning nothing **is the correct behaviour**. The only case that must work is (a).

#### 1c. 🔴 The hazard the POML did not anticipate — Spaarke's own open flow

This is in-repo evidence, and it is the single strongest argument against a GREEN verdict.

`DESK-RESEARCHED (in-repo)` — `src/server/shared/Spaarke.Core/Utilities/DesktopUrlBuilder.cs:37-47`:

```
/// Uses abbreviated protocol format: ms-word:https://...
///
/// The abbreviated format (without ofe|u|) bypasses Windows Security Zone
/// restrictions that block SharePoint Embedded /contentstorage/ URLs.
/// Files open in Protected View, allowing users to click "Enable Editing"
/// to switch to full edit mode.
///
/// Full format (ms-word:ofe|u|{encoded-url}) is blocked by Office when the
/// URL is in the Restricted Sites zone, which includes SPE contentstorage paths.
```

Three consequences, in order of severity:

1. **SPE `contentstorage` URLs are in the Restricted Sites zone**, and the canonical
   `ms-word:ofe|u|{url}` open-for-edit verb is *blocked* for them. Spaarke works around this with the
   abbreviated `ms-word:{url}` form. `DESK-RESEARCHED (in-repo)`, `DesktopUrlBuilder.cs:44-47, 60`.
2. **The file opens in Protected View.** `DESK-RESEARCHED (in-repo)`, `DesktopUrlBuilder.cs:42-43`.
   Protected View is a sandboxed, read-only rendering mode. Whether a task-pane add-in runs at all in
   Protected View, and what `document.url` reports for a document opened that way — before *and* after
   the user clicks *Enable Editing* — is **not established in this session and must be the operator
   pass's first observation** (§8.1 step 5). A document that Word treats as a downloaded local copy
   rather than a server-attached document would plausibly report a **local temp path**, at which point
   Link 2 has nothing to encode.
3. **Word is handed the `webDavUrl`, not the `webUrl`.** The endpoint prefers `driveItem.WebDavUrl`
   and only falls back to `WebUrl`:
   `DESK-RESEARCHED (in-repo)`, `FileAccessEndpoints.cs:620-629`:
   ```csharp
   // The webUrl returns Doc.aspx (Office Online URL) which doesn't work well with ms-word: protocol
   // We need to construct a direct file URL from the parent path + filename
   string? directFileUrl = null;
   // Prefer webDavUrl if available (direct file URL)
   if (!string.IsNullOrEmpty(driveItem.WebDavUrl)) { directFileUrl = driveItem.WebDavUrl; }
   ```
   → then `DesktopUrlBuilder.FromMime(urlForDesktop, mimeType)` at `:657`.

   **This creates a second silent-miss hazard, distinct from Link 4.** Spaarke stores `driveItem.webUrl`
   in `sprk_filepath` (`OfficeDocumentPersistence.cs:196` — `FilePath = webUrl, // SharePoint Embedded
   web URL (maps to sprk_filepath in Dataverse)`), but hands Word the **`webDavUrl`**. These are
   different URL forms for the same file — the code comment at `:621` says so explicitly. So the
   tempting shortcut *"skip Graph, just match `document.url` against `sprk_filepath`"* **does not work**
   and must not be implemented. Recording it here so task 012 does not rediscover it as a bug.

#### 1d. Microsoft's own SPE documentation corroborates the repo — and diverges from it in one place

`DESK-RESEARCHED (Learn)` —
[Open Office files (SharePoint Embedded)](https://learn.microsoft.com/en-us/sharepoint/dev/embedded/build/open-office-files),
last updated **2026-08-27** — the most current source located in this session, and the only one that
addresses SPE desktop opens at all. It independently confirms the two repo findings above:

- **SPE `webUrl` is a viewer URL, not a file path.** Verbatim: *"A supported Office web URL has this
  shape: `https://host/:w:r/contentstorage/sitecollection/_layouts/15/doc2.aspx?sourcedoc=guid&file=filename.docx&action=default&mobileredirect=true`"*.
  This is exactly what `FileAccessEndpoints.cs:621` says in its own words (*"The webUrl returns Doc.aspx
  (Office Online URL)"*) — two independent sources, same conclusion. **Consequence for Link 2**: feeding
  `webUrl` to `/shares` would encode a `doc2.aspx` *viewer* URL, not a file URL.
- **`webDavUrl` is the documented canonical file URL.** Verbatim: *"If you need the canonical file URL,
  use the DriveItem **`webDavUrl`** property instead of `webUrl`."* This validates Spaarke's preference
  at `FileAccessEndpoints.cs:625-629` and makes `webDavUrl` the strongest candidate input to `/shares`,
  and the strongest candidate for comparison against whatever `document.url` returns.

**The one divergence, and it is deliberate on Spaarke's side.** Microsoft's documented desktop-open
pattern is the **full** verb:
```text
ms-word:ofe|u|https://contoso.sharepoint.com/contentstorage/CSP_1234765465/Document%20Library/MyDocument.docx
```
Spaarke uses the **abbreviated** `ms-word:{url}` form instead, because — per `DesktopUrlBuilder.cs:44-47`
— the full form *"is blocked by Office when the URL is in the Restricted Sites zone, which includes SPE
contentstorage paths."* Microsoft's page does not mention the Restricted Sites interaction.

> **This divergence is itself a risk to Link 1 and must be probed both ways.** Spaarke is not opening
> documents the way Microsoft documents. The abbreviated form is what produces Protected View — and it
> is entirely possible that the full `ofe|u|` form (where it is not zone-blocked) yields a
> *server-attached* document with a working `document.url`, while the abbreviated form yields a
> *downloaded copy* with none. **§8.1 step 7 adds this A/B comparison**, because if that is the
> difference, Link 1 is fixable by a zone-policy change rather than being terminal.

> **Note**: there is a second, *dead* desktop-URL path. `DocumentCheckoutService.GetDesktopUrl()`
> (`src/server/api/Sprk.Bff.Api/Services/DocumentCheckoutService.cs:1161-1182`) documents the
> `ms-word:ofe|u|` format and then `return null; // TRACKED: GitHub #233`. It is not live. The single
> live flow is `GET /api/documents/{documentId}/open-links` → `desktopUrl` → `window.location.href`
> (`src/client/webresources/js/sprk_DocumentOperations.js:1575-1578`;
> `src/client/code-pages/DocumentRelationshipViewer/src/App.tsx:371-377`).

**Link 1 verdict: 🟠 AMBER.** Not established, and with a concrete repo-grounded mechanism by which it
could be negative. It cannot be closed from this session.

---

### Link 2 — base64url → Graph `GET /shares/u!{enc}/driveItem` for an SPE URL

#### The encoding recipe — 🟢 confirmed, no ambiguity

`DESK-RESEARCHED (Learn)` — [`shares: get`](https://learn.microsoft.com/en-us/graph/api/shares-get?view=graph-rest-1.0),
last updated **2025-07-23** (note: ~14 months stale, and it predates most SPE GA documentation). Verbatim:

> 1. First, use base64 encode the URL.
> 2. Convert the base64 encoded result to unpadded base64url format by removing `=` characters from the
>    end of the value, replacing `/` with `_` and `+` with `-`.
> 3. Append `u!` to be beginning of the string.

The §8 snippet implements exactly this, so the derived token can be eyeballed against a live Graph call.
**This is the only part of Link 2 that is settled.**

#### 🔴 The unresolved half — SPE

**The Graph `/shares` documentation is *completely silent* on SharePoint Embedded.**
`DESK-RESEARCHED (Learn)`, shares-get page (2025-07-23): the strings *"SharePoint Embedded"*,
*"contentstorage"* and *"container"* appear **nowhere** on it. There is no statement that a
`contentstorage/CSP_{guid}/…` URL is a valid input, and no statement that it is not.

> **Silence is silence.** It is not "probably works". This is the exact assumption-shaped gap the spike
> exists to prevent, and it must not be closed by inference. A related unreconciled gap: SPE content
> access is documented elsewhere as requiring `FileStorageContainer.Selected`, which the shares-get
> permissions table does not list at all — so it is not even clear that `/shares` *can* honour
> container-type permissions.

**Permissions** `DESK-RESEARCHED (Learn)`, shares-get page (2025-07-23):

| Permission type | Least privileged | Higher privileged |
|---|---|---|
| Delegated (work/school) | `Files.ReadWrite` | `Files.ReadWrite.All`, `Sites.ReadWrite.All` |
| Application | `Files.ReadWrite.All` | `Sites.ReadWrite.All` |

Two things follow, both bad for a resolve-only call:

1. **There is no read-only scope on this endpoint, in either mode.** The least-privileged option is a
   **write** scope. An identity resolver that only ever reads would have to hold `Files.ReadWrite` — a
   least-privilege regression that a security review should be told about up front rather than discover.
2. **Application permissions are listed as supported**, but the same page's Remarks say *"For OneDrive
   for Business and SharePoint, the Shares API always requires authentication and can't be used to access
   anonymously shared content without a user context."* The page does not reconcile these. §11 specifies
   OBO regardless, so this does not change the design — but it does mean the app-only escape hatch may
   not exist if OBO proves insufficient.

**The no-access status code is NOT DOCUMENTED.** `DESK-RESEARCHED (Learn)`, shares-get page — its entire
error section is a link to the generic error-responses article. No status table, no 403-vs-404
discussion. **Do not guess**: whether an inaccessible URL returns 403 (item exists, you can't see it) or
404 (existence-hidden) determines whether FR-01 can distinguish *"not a Spaarke document"* from *"not
yours"*, and those must produce different pane behaviour. §8.4 makes this an explicit operator observation.

Two further points worth stating precisely rather than glossed:

1. **There is zero in-repo precedent.** `DESK-RESEARCHED (in-repo)` — a repo-wide grep across
   `src/server/**` for `/shares/u!`, `"/shares/"` and `Shares[` returns **no hits**, and `SpeFileStore`'s
   39 public methods (`SpeFileStore.cs:54-430`) contain no share-token resolution. Spaarke has never
   called this endpoint. Every other SPE access in the codebase addresses items by explicit
   `(driveId, itemId)` — e.g. `GetDriveItemAsUserAsync(ctx, driveId, itemId, …)` (`SpeFileStore.cs:356`).
   So Link 2 is not "reuse an existing call"; it is a new Graph surface with no local track record.
2. **SPE containers are not conventional document libraries.** The POML's own knowledge block flags this,
   and `DesktopUrlBuilder.cs:44-47` corroborates that `contentstorage` paths already behave unusually at
   the Windows-zone layer. Extrapolating SharePoint-Online `/shares/` behaviour to SPE is exactly the
   class of assumption this spike exists to prevent.

#### 🟢 A documented alternative to the whole of Link 2 — SPE `urlTemplate`

`DESK-RESEARCHED (Learn)` — the SPE open-Office-files page (2026-08-27) documents that **SPE container
types expose a `urlTemplate` setting**, whose supported tokens include `{tenant-id}`, **`{drive-id}`**,
`{folder-id}`, **`{item-id}`**, `{site-domain}`, `{list-id}`, `{site-url}`.

**This is Microsoft's own documented answer to "get from an opened file back to identity", and it is a
different architecture from URL→`/shares` resolution.** Instead of taking an opaque URL and asking Graph
to reverse it, the container type is configured so that the URL *already carries* the drive id and item
id — the exact pair the server contract wants — with no encoding step, no `/shares` call, no
opaque-identifier assumptions, and no dependency on documentation that is silent about SPE.

It is **not** a drop-in replacement — `urlTemplate` governs redirect/launch behaviour for the container
type, and whether the resulting URL is what `Office.context.document.url` reports back on desktop is the
same unverified question as everything else in Link 1. But it is a genuinely different shape of solution,
it is documented *for SPE specifically* rather than extrapolated to it, and it belongs in the operator's
decision set alongside §7.

**Recorded as open question 10.** Not evaluated further here — it is a container-type configuration
change with a blast radius (`docs/architecture/SPAARKE-SPE-CONTAINER-TYPE-TOPOLOGY.md`: container types
are permanent, capped at 25 per tenant) well beyond an add-in spike's authority to propose.

#### Link 2 verdict

**🟠 AMBER — unestablished.** The encoding is settled; SPE acceptance is not, and the documentation
cannot settle it. If the operator pass finds the shares endpoint rejects SPE `contentstorage` URLs, the
POML's **second escalation trigger** fires and task 012 changes shape — with `urlTemplate` (above) and
§7's stamp as the two named alternatives that keep it from being a dead end.

---

### Link 3 — does the resolved driveItem carry `driveId` + `id` in Spaarke's shape?

**Conditional GREEN.** Conditional on Link 2 returning a driveItem at all.

`DESK-RESEARCHED (in-repo)` — this link is safe because it is the **same Graph property Spaarke already
writes**. A `driveItem` resolved through *any* route (`/shares/…/driveItem`, `/drives/{d}/items/{i}`,
an upload response) is the same resource with the same `id` and the same `parentReference.driveId`.
Spaarke's own write path proves the round-trip: `UploadSessionManager.cs:151-161` maps
`item.Id` → `FileHandleDto.Id` and `item.ParentReference?.DriveId` → `FileHandleDto.DriveId`.

**🔴 But `parentReference` is NOT in the documented default projection.** `DESK-RESEARCHED (Learn)`,
[shares: get](https://learn.microsoft.com/en-us/graph/api/shares-get?view=graph-rest-1.0) (2025-07-23) —
the documented `GET /shares/{id}/driveItem` example response is:
```json
{ "id": "9FFFDB3C-...", "name": "contoso project.docx", "eTag": "...", "file": {}, "size": 109112 }
```
`id` is present; **`parentReference` is absent.** Doc examples are illustrative rather than a
default-projection contract (the
[driveItem resource](https://learn.microsoft.com/en-us/graph/api/resources/driveitem?view=graph-rest-1.0)
page, last updated **2026-05-01**, marks only `publication` as explicitly not-returned-by-default, which
weakly implies the rest are) — but this is not a guess worth taking.

→ **Task 012 MUST `$select` explicitly**: `?$select=id,name,parentReference,webUrl`. It costs nothing and
removes the guess. A missing `parentReference` would strand the resolver at exactly the point where §5
Link 4's defensive `driveId` check needs it.

**One concrete gap to hand task 012**: the facade's read DTO is too narrow.
`SpeDriveItemSummary` (`src/server/api/Sprk.Bff.Api/Models/SpeFileStoreDtos.cs:76-85`) exposes
`Id, Name, Size, WebUrl, WebDavUrl, MimeType, ParentReferencePath, LastModifiedDateTime, CreatedDateTime`
— it carries **`ParentReferencePath` but no discrete `ParentDriveId`**. A resolver needs the drive id as
a first-class field (scraping it out of the `/drives/{driveId}/root:` path string would be exactly the
hand-rolled parsing ADR-044 forbids in the GUID case and that the same reasoning forbids here). Task 012
should add one field to `SpeDriveItemSummary`, or return a purpose-built resolve DTO. Either is
ADR-007-compliant; neither is a Graph type escaping `Infrastructure.Graph`.

**On identifier format** — worth stating because ADR-044 is in this task's constraint list and it is easy
to over-apply: **the SPE drive-item id and drive id are NOT GUIDs.** They are opaque strings. The
codebase says so in terms:

`DESK-RESEARCHED (in-repo)` — `ComposeCreateOnSavePromoter.cs:337-341`:
```
// ... The key uses the RAW DocumentSpeId
// string, identical to the read above (TryFindDocumentByGraphItemIdAsync): sprk_graphitemid is an
// opaque SPE drive-item id (a STRING, not a GUID), so the match is exact-string and ADR-044 GUID
// canonicalization does NOT apply (verified — the alt-key lookup keys on the raw string).
```

So ADR-044 / `cleanGuid` applies to the **Dataverse** GUIDs in this flow — `sprk_documentid`,
`sprk_matterid`, the SPE **container** id — and **must not** be applied to `driveId`/`itemId`, where
lower-casing an opaque case-bearing identifier would *corrupt* it. See §11 for where each rule lands.

**Microsoft agrees, by omission — and this closes a trap.** `DESK-RESEARCHED (Learn)`: Microsoft
**does not document the lexical format of either `id` or `driveId`.** The driveItem resource page
(2026-05-01) says only *"The unique identifier of the item within the Drive. Read-only."*, type `String`;
[itemReference](https://learn.microsoft.com/en-us/graph/api/resources/itemreference?view=graph-rest-1.0)
(last updated **2024-11-21**) says only *"Unique identifier of the drive instance that contains the
driveItem"*, type `String`, with the JSON example literally `"driveId": "string"`. The
[addressing guide](https://learn.microsoft.com/en-us/graph/onedrive-addressing-driveitems) (2025-08-06)
uses placeholder ids like `0123456789AB`, and the shares-get page's own examples use **GUIDs**
(`9FFFDB3C-5B87-…`) — contradicting the base32-looking `01ABCDEF…` form seen in practice.

> **Therefore: any code that pattern-matches `driveId` on a `b!` prefix, or `itemId` on a base32 shape,
> or that validates either as a GUID, is relying on undocumented behaviour and will break.** Treat both
> as fully opaque strings, exactly as the repository already does. This is a second, independent reason
> the §5 Link 4 exact-string match is the right design and must not be "improved" with parsing.

Also documented, and useful context for §7: `itemReference` carries a **`shareId`** — *"A unique
identifier for a shared resource that can be accessed via the Shares API"*. The **reverse** direction
(item → share token) is a first-class documented field; the forward direction this spike needs (arbitrary
URL → item, for SPE) is the undocumented one.

**Link 3 verdict: 🟢 GREEN, conditional on Link 2.**

---

### Link 4 — `sprk_graphitemid_uk` 🔴 *the link the POML called most dangerous*

**🟢 GREEN. Closed. No mismatch. No key change needed or proposed.**

This link is fully answerable from the repository with no live host, and it was the priority of this
spike. The answer: **the format matches exactly, because both sides are the same Graph `driveItem.id`
string and nothing transforms it on either side.**

#### The write path, hop by hop (all `DESK-RESEARCHED (in-repo)`)

| # | Hop | Evidence |
|---|---|---|
| 1 | Graph SDK `DriveItem.Id` → `FileHandleDto.Id`, verbatim (`item.Id!`) | `Infrastructure/Graph/UploadSessionManager.cs:151-152`; also `:303-304` |
| 2 | `FileHandleDto.Id` → returned as `ItemId` from the uploader (`return (true, driveId, result.Id, result.WebUrl, null);`) | `Services/Office/OfficeStorageUploader.cs:62-71` |
| 3 | `itemId` passed positionally into the persistence layer | `Services/Office/OfficeService.cs:454-462` |
| 4 | `itemId` → `UpdateDocumentRequest.GraphItemId` | `Services/Office/OfficeDocumentPersistence.cs:190-197` |
| 5 | `GraphItemId` → the Dataverse attribute, verbatim | `src/server/shared/Spaarke.Dataverse/DataverseServiceClientImpl.cs:787-788`:<br>`if (request.GraphItemId != null) document["sprk_graphitemid"] = request.GraphItemId;` |

**No `ToLower()`, no `Trim()`, no brace-stripping, no composition with `driveId`, no re-formatting at any
hop.** The value in `sprk_graphitemid` is byte-for-byte the `id` Graph returned.

#### The read path

`DESK-RESEARCHED (in-repo)` — `Services/Compose/ComposeRecordResolution.cs:181-206`:
```csharp
var key = new KeyAttributeCollection { { ComposeService.GraphItemIdAttribute, driveItemId } };
var entity = await _dataverse.RetrieveByAlternateKeyAsync(
    ComposeService.DocumentLogicalName, key, new[] { … }, cancellationToken);
```
and the Office path's mirror at `Services/Office/OfficeDocumentPersistence.cs:369-374`. The generic seam
builds a `RetrieveRequest` with `Target = new EntityReference(entityLogicalName, alternateKeyValues)` —
a raw pass-through, no normalization (`DataverseServiceClientImpl.cs:2399-2404`).

#### The four questions the POML asked about this link, answered

| Question | Answer |
|---|---|
| **Bare item id, or composite `driveId`+`itemId`?** | **Bare item id.** The key is a **single column**, `sprk_graphitemid`. `driveId` is stored separately in `sprk_graphdriveid` and is **not** part of the key. Confirmed independently by `Api/DocumentVersionEndpoints.cs:59-61`: *"the only unique index available for a `(driveId, itemId)` → document lookup is `sprk_graphitemid_uk`, which is keyed on the ITEM alone, leaving the supplied `driveId` unvalidated."* |
| **What casing?** | **Whatever Graph emitted** — preserved exactly. The codebase treats it as an opaque case-bearing string and matches exact-string (`ComposeCreateOnSavePromoter.cs:337-341`). |
| **Braced?** | **No.** It is not a GUID and never has braces. ADR-044 explicitly does not apply here. |
| **Does the Graph-returned identifier match what is stored?** | **Yes — identically.** Both are `driveItem.id`. A `/shares/…/driveItem` response and an upload response describe the same resource and carry the same `id`. |

#### The one real finding on this link (a defensive check, **not** a key change)

The key is keyed on the **item alone**. SPE drive-item ids are unique **within a drive**, not globally.
A single-column key therefore cannot, by construction, distinguish two items with the same id in
different drives. That is a pre-existing property of the shipped design, already named at
`DocumentVersionEndpoints.cs:59-61`, and it is **not** this spike's to change.

> **NFR-07 compliance, stated explicitly**: this report proposes **no** relaxation, widening, duplication
> or replacement of `sprk_graphitemid_uk`. Compose's transient-key dedup and promote-idempotency rest on
> it (`ComposeCreateOnSavePromoter.cs:333-341`, `ComposeIdentityKeyHealthCheck.cs:71`), and the third
> POML escalation trigger fires if anyone proposes otherwise. Nothing here fires it.

**The correct handling, for task 012**: after resolving a row by the alternate key, **compare the row's
`sprk_graphdriveid` against the `driveId` Graph returned, and treat a mismatch as "not our document"**
rather than as a hit. That is one `string.Equals` on the BFF side — a normalisation/validation
responsibility on the *caller*, which is exactly what the NFR-07 constraint permits ("report the mismatch
and the normalisation required on the BFF side; do not touch the key"). The read already fetches
`GraphDriveIdAttribute` alongside the id for precisely this kind of provenance check
(`ComposeRecordResolution.cs:196-202` — *"`sprk_graphdriveid` rides along for DRIVE PROVENANCE … the row
is the authority on WHERE its bytes live"*), so the column is already in hand at no extra round-trip.

**Link 4 verdict: 🟢 GREEN.** *If* links 1–3 deliver a `(driveId, itemId)` pair, link 4 resolves it
correctly today, with no schema, key, or format work required.

---

## 6. Candidate fallbacks

The POML requires each named candidate to be reported separately. **A negative on the primary is only
terminal if the alternatives also fail** — and one of them is strong enough that §7 recommends promoting
it above the primary.

### 6.1 `Office.context.document.getFilePropertiesAsync` — 🟡 probe it, and rank it ABOVE the sync property

`DESK-RESEARCHED (types)` — `index.d.ts:5891-5903`:
```ts
        /**
         * Gets file properties of the current document.
         *
         * @remarks
         *
         * **Requirement sets**: {@link …#methods-that-arent-part-of-a-requirement-set | Not in a set}
         *
         * You get the file's URL with the url property `asyncResult.value.url`.
         */
        getFilePropertiesAsync(options?: Office.AsyncContextOptions, callback?: (result: AsyncResult<Office.FileProperties>) => void): void;
```
and `index.d.ts:7346-7351`:
```ts
    interface FileProperties {
        /**
         * File's URL
         */
        url: string
    }
```

**Unlike the sync property, this one IS documented for Word on Windows.** `DESK-RESEARCHED (Learn)` —
the [Common API requirement sets page](https://learn.microsoft.com/en-us/javascript/api/requirement-sets/common/office-add-in-requirement-sets)
(last updated **2025-10-10**), under *"Methods that aren't part of a requirement set"*, lists
`Document.getFilePropertiesAsync` with minimum support: **Word on the web · Word on Windows (Microsoft 365
subscription, perpetual Office 2016) · Word on Mac · Word on iPad** (and the Excel/PowerPoint equivalents).

> **This is a genuine asymmetry, and it upgrades this candidate.** It is a *method*, so it appears in the
> requirement-sets machinery; the sync `.url` *property* does not and structurally cannot (§5 1a). So the
> async call is the **only** one of the two with any documented Word-desktop support statement at all.
> That does not tell us what it returns for an **SPE** file — no source does — but "documented to exist
> on Word for Windows" is strictly more than "documentation structurally cannot say".

- **Not in a requirement set** → cannot be gated by a `<Set>`, but the same Learn page states the
  mitigation verbatim: *"If your add-in requires any of these methods, use the `<Methods>` and `<Method>`
  elements in the add-in's manifest to declare that they are required, or perform the runtime check using
  an `if` statement."*
  ⚠️ **That guidance is XML-manifest-only.** The page does not state a unified-JSON-manifest equivalent,
  and this repo now ships a unified manifest (`word/manifest.json`). **Open question 11** — until it is
  answered, task 013 should use the runtime-check half of the guidance, which works on both manifests.
- The docs state **no behavioural difference** from the synchronous `.url`, and equally do not state that
  they are interchangeable. Whether they can disagree at runtime is unaddressed. Do not assume either
  position. Cheap to probe (it is in the §8 snippet).
- **What it returns for an unsaved document is not documented** — neither the `Office.Document` page
  (2026-08-31) nor the `FileProperties` type says anything about unsaved state. The `FileProperties.url`
  doc comment is three words: *"File's URL"* (`index.d.ts:7346-7351`).

**Result: probe it, and rank it above the sync property.** If §8 shows it returning a value where `.url`
returns `null`, that is a material finding and flips part of Link 1.

### 6.1b 🆕 `Word.Document.path` / `.fullName` — a real identity surface the POML did not list

`DESK-RESEARCHED (types + Learn)` — not named in the task POML's candidate list, and worth adding.

| Member | Documented as | Api set | Declaration |
|---|---|---|---|
| `Word.Document.fullName` | *"Gets the name of a document, including the path."* | **WordApiDesktop 1.4** | `index.d.ts:101151` — `readonly fullName: string;` |
| `Word.Document.path` | *"Gets the disk or the web path to the document (excludes the document name)."* | **WordApiDesktop 1.4** | `index.d.ts:101501` — `readonly path: string;` |

Source: [`Word.Document`](https://learn.microsoft.com/en-us/javascript/api/word/word.document?view=word-js-preview),
last updated **2026-09-02**.

**Why this is interesting**: *"the disk **or the web** path"* is precisely the distinction Link 1c turns
on. If Spaarke's abbreviated-protocol open produces a downloaded local copy, `path` would return a disk
path; if it produces a server-attached document, a web path. **`path` may therefore diagnose Link 1c even
in the case where it cannot solve it** — which is why §8.3 probes it.

**Why it is not a solution on its own** — the availability floor is narrow.
`DESK-RESEARCHED (Learn)`, [Word API requirement sets](https://learn.microsoft.com/en-us/javascript/api/requirement-sets/word/word-api-requirement-sets),
last updated **2026-04-21**:

| Requirement set | Word on the web | Windows (M365 sub / retail perpetual) | Windows volume-licensed / LTSC | Mac | iPad |
|---|---|---|---|---|---|
| **WordApiDesktop 1.4** | ❌ **Not applicable** | Version **2508** (Build 19127.20264) | ❌ **Not available** | 16.100.4 (25090553) | ❌ **Not available** |
| WordApi 1.4 | ✅ Supported | 2208 (15601.20148) | Office 2024: 2208 | 16.64 | 16.64 |
| WordApi 1.9 (latest GA) | ✅ Supported | 2411 (18227.20152) | Not available | 16.91 | 16.91 |

- **Word on the web: not applicable.** Desktop-only by construction — so this can never satisfy FR-19 parity.
- **Windows build 2508+** is recent; a meaningful share of the installed base may not have it.
  **Availability, not just capability, is the constraint.**
- **LTSC / volume-licensed Windows and iPad: not available at all.**

**Result: 🟡 a diagnostic worth probing, and a possible desktop-only supplementary path — never a
primary.** It also gives back a *path*, not a `driveId`/`itemId`, so it would still need Link 2 or a
string match to reach a record. And note it would raise the manifest floor further than §6.5's option 2
(`WordApiDesktop` 1.4 is a *different, narrower* set than `WordApi` 1.4 — do not conflate them).

### 6.2 `Office.context.document.settings` — 🔴 **not** an identity mechanism for this problem

`DESK-RESEARCHED (types)` — `index.d.ts:7887-7903`:
```ts
    /**
     * Represents custom settings for a task pane or content add-in that are stored in the host document as name/value pairs.
     *
     * @remarks
     *
     * **Applications**: Excel, PowerPoint, Word
     *
     * The settings created by using the methods of the Settings object are saved per add-in and per document.
     * That is, they are available only to the add-in that created them, and only from the document in which they are saved.
     * …
     * The developer is responsible for calling the saveAsync method after adding or deleting settings …
     */
```

Settings **do** persist into the Word document. But they fail this use case on two counts:

1. **Writing them requires mutating the user's open document from the client.** FR-02 is explicitly
   specified the other way — *"stamp … in the uploaded bytes **server-side** — never by mutating the
   user's open document"* (spec.md:62). Settings cannot be written server-side into a `.docx` package by
   the BFF the way a custom XML part can. This alone disqualifies it as the primary.
2. **"Saved per add-in and per document"** — and this is the decisive contrast with a custom XML part.
   `DESK-RESEARCHED (Learn)`,
   [Persisting add-in state and settings](https://learn.microsoft.com/en-us/office/dev/add-ins/develop/persisting-add-in-state-and-settings),
   last updated **2026-04-17**: settings are *"available only to the instance of the content or task pane
   add-in that created it"*, whereas of custom XML data the same page says *"**Other add-ins can also
   access data stored this way**"* and *"This data persists with the file, **independent of the add-in**"*.
   An identity stamp must be readable by whatever reads the file — including a future Spaarke surface
   that is not this add-in. Settings are the wrong scope by design.
3. ⚠️ **Tier-3 flag**: `OfficeDev/office-js` issue #6434 (*"Custom settings are not present in files
   retrieved using `getFileAsync()`"*) suggests settings and the retrieved byte stream can disagree.
   Issue-tracker only, unverified, recorded for completeness.
4. **`Word.Document.settings` (the Word-specific surface) is `[Api set: WordApi 1.4]`**
   (`index.d.ts:100899`) — so it carries the *same* manifest gap as §6.5. Another reason not to reach for it.

**Result: rejected as an identity mechanism**, on the scoping ground (2) independently of the
server-side-write ground (1). Retained only as a possible per-document UI-state cache (e.g. last-selected
tab), which is out of scope here.

### 6.3 Word JS API document surface — 🔴 no identity property; 🟡 but see the custom XML part

`DESK-RESEARCHED (types)`:
- **Nothing on any Word JS API object yields a Graph `driveId` or `itemId`.** A search of the full Word
  namespace of `index.d.ts` (lines 94729–168062) for `path|url|fullName|saved` returns only the members
  tabled in §6.1b plus `saved` (`WordApi 1.1`). `Word.DocumentProperties` — reached via
  `Word.Document.properties` (`WordApi 1.3`) or `builtInDocumentProperties` (`WordApiDesktop 1.4`) — is
  the classic Author/Title/Subject/Keywords surface and carries no server path or item id.
- What `Word.Document` *does* expose in the identity neighbourhood: `customXmlParts`
  (`[Api set: WordApi 1.4]`, `index.d.ts:100768-100773`), `settings` (`[Api set: WordApi 1.4]`,
  `index.d.ts:100899`), `customDocumentProperties` (`[Api set: WordApiDesktop 1.4]`,
  `index.d.ts:100761-100766`), `documentLibraryVersions` (`[Api set: WordApiDesktop 1.3]`,
  `index.d.ts:100775-100781`), and `path`/`fullName` (§6.1b).
- `WordAdapter.getItemId()` already uses this surface (`properties.load(['title','author','creationDate'])`,
  `WordAdapter.ts:92`) and that is precisely why it produces a hash instead of an identity. **The existing
  implementation is not a bad use of a good API; it is the best that API can do.**

**Result: no direct `driveId`/`itemId` exists anywhere on this surface.** The two members here that do
matter are `path`/`fullName` (§6.1b, diagnostic) and `customXmlParts` (§6.5, the FR-02 read) — and the
latter's requirement-set version is a live defect.

### 6.4 Manifest permission level — 🟡 one unresolved gap, owned elsewhere

`DESK-RESEARCHED (in-repo)`:
- XML manifest: `<Permissions>ReadWriteDocument</Permissions>` (`word-manifest.xml:56`) — the highest
  level, so the XML surface is not permission-gated for any of these APIs.
- **Unified JSON manifest**: `"authorization": { "permissions": { "resourceSpecific": [] } }`
  (`manifest.json:28-32`) — **empty**. Whether the unified manifest requires an explicit document-access
  entry to match the XML's `ReadWriteDocument`, and what a missing entry does to `document.url` or a
  custom-XML-part read, **was not established in this session**.

**Result: flagged, not fixed.** `word/manifest.json` is task 011's artifact and task 010 is concurrently
active in `word/**` — this spike modifies nothing there (§12). Handed to task 011's follow-up and
task 013 as an explicit pre-flight check.

### 6.5 🔴 Custom XML part — **the strongest candidate, and it carries a live requirement-set defect**

This is the FR-02 read mechanism and, per §7, the recommended **primary**. There are two APIs, and they
are **not** interchangeable:

| API | Requirement | Evidence |
|---|---|---|
| **Common** — `Office.context.document.customXmlParts` | Requirement set **`CustomXmlParts`** (unversioned), annotated `**Applications**: Word` | `DESK-RESEARCHED (types)`, `index.d.ts:5275-5281` (interface + `**Applications**: Word`), `:5286-5289` etc. (every member annotated `**Requirement set**: … CustomXmlParts`); the property hangs off `Office.Document` at `index.d.ts:5742-5746` |
| **Word-specific** — `Word.Document.customXmlParts` | **`[Api set: WordApi 1.4]`** | `DESK-RESEARCHED (types)`, `index.d.ts:100768-100773` |

> ### 🚨 Finding — a WordApi 1.4-vs-1.3 mismatch, the same class of defect task 011 caught
>
> **Both Word manifests declare `WordApi` minVersion `1.3`** (`word-manifest.xml:45`;
> `manifest.json:38-41`). **`Word.Document.customXmlParts` requires WordApi `1.4`.**
>
> If FR-02's client-side read is implemented against the **Word-specific** API, a host at exactly
> WordApi 1.3 will **install the add-in successfully and then fail at runtime** — which is exactly the
> failure mode task 011 caught when the manifest advertised WordApi 1.1 while `WordAdapter` required 1.3
> (`notes/011-word-manifest-migration.md`, § WordApi reconciliation).
>
> **Two compliant options for task 014, and they are not equal:**
>
> 1. ✅ **Use the common API `Office.context.document.customXmlParts` and add `CustomXmlParts` to the
>    manifest requirements.** Preferred. The requirement set is **unversioned**, so it does not raise the
>    WordApi floor at all, and it is annotated Word-only — which is our host. It also keeps the read
>    host-generic, matching the add-in's `IHostAdapter` convention.
> 2. ⚠️ **Use `Word.Document.customXmlParts` and raise the manifest to `WordApi` 1.4.** Works, but raises
>    the minimum host version for the *entire* add-in — a distribution cost paid by every feature to
>    benefit one.
>
> **What must not happen is shipping either API against the current `1.3`-only manifest.** Note that a
> requirement set declared in the manifest is a *hard install gate*: a host lacking it will refuse to
> install, which is the honest failure. That is strictly better than a runtime `undefined`.
>
> ⚠️ **`Word.Document.settings` is ALSO `[Api set: WordApi 1.4]`** (`index.d.ts:100899`). So the 1.3
> manifest under-declares for *two* members, not one. Whichever option is taken, audit for both.

#### ✅ Host support — RESOLVED, and it favours the common API

`DESK-RESEARCHED (Learn)` — [Common API requirement sets](https://learn.microsoft.com/en-us/javascript/api/requirement-sets/common/office-add-in-requirement-sets)
(2025-10-10) and [Word API requirement sets](https://learn.microsoft.com/en-us/javascript/api/requirement-sets/word/word-api-requirement-sets)
(2026-04-21). **This closes what was an open question**: both APIs are supported on Word web and Mac.

| API | Gate | Word web | Word Windows | Word Mac | Word iPad |
|---|---|---|---|---|---|
| `Office.context.document.customXmlParts` | **`CustomXmlParts`** (unversioned) | ✅ | ✅ M365 sub, **perpetual Office 2016** | ✅ | ✅ |
| `Word.Document.customXmlParts` | **WordApi 1.4** | ✅ | ✅ 2208 (15601.20148) | ✅ 16.64 | ✅ 16.64 |

**The common API is the wider net** — perpetual **Office 2016** versus a 2208-era floor. That is a
materially larger installed base for the same capability, and it settles the recommendation: **option 1.**

`DESK-RESEARCHED (Learn)`, common requirement-sets page, verbatim: *"**CustomXmlParts** — Minimum Office
application support: Word on the web / Word on Windows (Microsoft 365 subscription, perpetual Office
2016) / Word on Mac / Word on iPad."* → **FR-19 parity is not at risk from this mechanism.**

#### What the stamp buys — and two durability caveats that must not be buried

**It lives inside the file.** `DESK-RESEARCHED (Learn)`,
[Persisting add-in state and settings](https://learn.microsoft.com/en-us/office/dev/add-ins/develop/persisting-add-in-state-and-settings)
(2026-04-17), verbatim: *"The Open XML **.xlsx** and **.docx** file formats let your add-in embed custom
XML data in the Excel workbook or Word document. **This data persists with the file, independent of the
add-in.**"* This is why FR-02 can be written server-side into the uploaded bytes with no Office host, and
why it travels with the file.

⚠️ **Survival across download / re-upload / rename / move / Save-As-to-OOXML follows from "persists with
the file" plus OPC packaging — it is an inference from the packaging model, NOT a per-operation
documented guarantee.** Microsoft does not enumerate those operations anywhere located in this session.
Save-As to a **non**-OOXML format (`.txt`, `.rtf`, `.odt`) is undocumented and should be assumed lossy.

> ##### 🔴 Caveat 1 — Document Inspector can strip the stamp
> `DESK-RESEARCHED (Learn)`,
> [Using the Document Inspector](https://learn.microsoft.com/en-us/office/vba/Library-Reference/Concepts/using-the-document-inspector)
> (last updated **2025-01-11**): under *"For Word documents"* it lists a built-in module **"Custom XML
> data"** with a **Remove All** button. **A user running File → Info → Inspect Document can delete the
> identity stamp**, and Inspect-before-sending is common practice in legal work. This is a real,
> user-reachable un-stamping path and it must be stated in §7's trade-off, not discovered later.

> ##### 🔴 Caveat 2 — an unverified premise the whole approach rests on
> Microsoft's support page
> [Custom XML markup in Word](https://support.microsoft.com/en-us/office/custom-xml-markup-in-word-24bd455e-4b5d-402a-9265-8bb9af82a7d6)
> states: *"Custom XML markup is no longer supported in Word. When you open a document containing custom
> XML markup, Word removes it from the document."*
>
> **The standard reading is that this concerns in-body `w:customXml` *markup elements* (removed following
> the i4i litigation), NOT `/customXml/itemN.xml` package *parts*** — two different things that share a
> name. **But the support page itself does not draw that distinction**, and this reading was not
> independently confirmed in this session.
>
> **This was load-bearing: if the reading were wrong, FR-02 would collapse entirely**, and with it §7's
> recommendation.
>
> ##### ✅ RESOLVED 2026-09-09 — THE READING IS CORRECT. Premise CONFIRMED; task 014 is unblocked.
> Task 019 settled it **without a live host**, on two independent lines: (a) Microsoft's own i4i
> explainer (Gray Knowlton, GM Office PM, ms.date 2009-12-23) says what was removed is the *markup tags*
> and that *"Content Controls and XML data stored within DOCX or DOCM files will not be affected by this
> change"*; (b) in-repo forensics — a **third-party `http://customooxmlschemas.google.com/`** custom XML
> part survives **four** modern-Word save cycles intact in `commonpaper-cloud-service-agreement.docx`,
> while `w:customXml` markup occurs **0 times in all 48** Word files in the repo. §8 step 6b is retired.
> Evidence, limits, and the residual Document-Inspector risk: `notes/019-customxml-premise-and-manifest.md` §1.

**One implementation note** `DESK-RESEARCHED (Learn)`, same persisting-state page: *"`CustomXMLPart.namespaceUri`
is only populated if the top-level custom XML element contains the `xmlns` attribute"* and *"The XML
string must include an `xmlns` attribute."* Since the read is by namespace (`getByNamespaceAsync`), the
**server-side writer in task 014 MUST emit an explicit `xmlns` on the root element** or the client will
never find the part it just wrote. A silent, total failure with no error — exactly the class of bug this
spike was chartered to pre-empt.

**Result: 🟢 the strongest of the candidates**, subject to (a) the requirement-set fix above, and (b) the
caveat-2 confirmation, which is a genuine precondition rather than a formality.

---

## 7. Stamp-as-primary — a scope recommendation for the operator

> **Context that post-dates the task POML**: the operator has determined that **legacy/existing documents
> do not matter** (dev-only corpus). This removes the *single* stated disqualifier for FR-02's
> forward-only stamp — project CLAUDE.md Decisions, 2026-09-04, records the stamp as forward-only, and
> spec.md's Unresolved Questions (line 281) and the POML's first escalation trigger both treat
> forward-only as the reason the stamp cannot be the sole mechanism.

### The recommendation

**Promote FR-02's custom-XML stamp to the PRIMARY identity mechanism. Demote `document.url` → Graph
shares to an opportunistic fast path — and consider dropping it from r1 entirely.**

This is a **scope decision for the operator**, not one this spike takes. It is recorded here with its
reasoning because the POML's downstream consumers (tasks 012, 013, 014) are sized very differently
depending on the answer.

### Why the stamp is architecturally the better primary — independent of the legacy question

| | Custom-XML stamp (FR-02) | `document.url` → Graph shares (FR-01 primary) |
|---|---|---|
| **Identity is a property of…** | the **content** — it travels inside the file | the **location** — it describes where the file happens to sit |
| **Survives download → re-upload** | ✅ yes (inferred from OPC packaging — §6.5) | ❌ no — new URL, new item |
| **Survives rename / move / copy / Save-As (OOXML)** | ✅ yes (same inference) | ❌ no |
| **Survives being emailed and saved by someone else** | ✅ yes | ❌ no |
| **Survives Document Inspector → Remove All** | ❌ **no** (§6.5 caveat 1) | n/a — nothing to strip |
| **Readable by a future non-add-in Spaarke surface** | ✅ yes — *"independent of the add-in"* | ✅ yes |
| **Links in the resolution chain** | **1** (read part → `sprk_documentid`) | **3** (host URL → shares token → driveItem → alt key) |
| **Links currently unverified** | 1 (the client read; §6.5) | 3 (§5 Links 1–3) |
| **Depends on a host API for the WRITE** | ❌ no — written **server-side** into the uploaded bytes (spec.md:62) | n/a |
| **Depends on Graph at resolve time** | ❌ no round-trip | ✅ one Graph call per resolve, on the pane's cold path |
| **Depends on the caller's Graph permissions** | ❌ no | ✅ yes — inherits `/shares/` authorization behaviour (§5 Link 2) |
| **Lands on** | `sprk_documentid` — the **primary key** | `sprk_graphitemid_uk` — an alternate key, keyed on the item alone (§5 Link 4) |
| **Exposed to the Protected-View / `webDavUrl` hazard (§5 Link 1c)** | ❌ no | ✅ yes |

The decisive row is the third-from-last. `document.url` identifies **where a file is**; the stamp
identifies **what a file is**. For a legal-drafting workflow in which documents are routinely downloaded,
emailed, renamed and re-uploaded, identity-by-content is simply the correct model — and it would be the
right answer even if `document.url` worked perfectly on desktop.

The second decisive point: the stamp's **write** needs no Office host at all. It is server-side OOXML
package manipulation on bytes the BFF already holds during save. That removes the entire class of risk
this spike could not close.

### Honest arguments against, stated rather than buried

1. **The stamp's client-side read is itself unverified**, and carries the WordApi 1.4-vs-1.3 defect in
   §6.5. Promoting it does not eliminate the need for an operator pass — it **changes what the operator
   must verify** from three links to one. That is a real reduction, not zero.
2. **The stamp is only as good as the day it ships.** Any document that leaves Spaarke before FR-02
   deploys is unidentifiable by it, forever. The operator's "legacy doesn't matter" decision is what
   makes this acceptable — and it is a **dev-only** decision. **If this project's output ever reaches an
   environment with a real document corpus, this recommendation must be revisited**, because there the
   forward-only property returns as a hard limitation. Flagging explicitly so the decision is not
   silently inherited by a later production cut.
3. 🔴 **A stamp can be stripped, and one of the ways is a routine user action.** Three paths, in
   descending likelihood:
   - **Document Inspector** — File → Info → Inspect Document lists *"Custom XML data"* with a **Remove
     All** button (§6.5 caveat 1, `DESK-RESEARCHED (Learn)`, 2025-01-11). Inspecting before sending a
     document out is *common practice in legal work*, which is this product's domain. This is the most
     serious of the three and was not previously on the table.
   - **Save-As to a non-OOXML format** (`.rtf`, `.txt`, `.odt`) or a third-party editor that discards
     unknown package parts.
   - Deliberate package editing.

   None of these is silent-to-the-user, and all of them are narrower than `document.url`'s failure
   surface (which loses identity on *every* download). But "narrower" is not "zero", and **the pane must
   degrade honestly when the stamp is absent** — treat as unidentified, never guess.
4. ~~🔴 **The markup-vs-parts premise is unconfirmed (§6.5 caveat 2).**~~ **✅ RESOLVED 2026-09-09 by
   task 019 — CONFIRMED, and this objection to §7 is withdrawn.** Word's removal covers in-body
   `w:customXml` markup only, not `/customXml/itemN.xml` package parts. §8 step 6b is retired; see
   `notes/019-customxml-premise-and-manifest.md` §1. **One residual, and it is a design obligation
   rather than a blocker**: the Document Inspector's "Custom XML Data → Remove All" module can strip
   the stamp at any time, so a missing stamp must be treated as a normal re-stampable state, never as
   corruption.
5. **Server-side OOXML writing is not free.** It must not corrupt the package, and it interacts with
   ADR-049's Compose write path, which already owns `.docx` byte manipulation. Task 014 should reuse
   that machinery rather than introduce a second OOXML writer (root CLAUDE.md §11). It must also emit an
   explicit `xmlns` on the stamp's root element, or `getByNamespaceAsync` will never find it (§6.5).

### What this implies for the plan, if the operator accepts

- **Task 014 (FR-02) is promoted ahead of tasks 012/013** in the critical path, and grows: it owns the
  requirement-set decision in §6.5 and the manifest change that follows.
- **Task 012 (server resolver) shrinks or defers.** If the stamp is primary, the BFF resolve step is a
  `sprk_documentid` retrieve — the alternate key and the Graph `/shares/` call are not on the hot path
  at all. The `/shares/` resolver becomes optional.
- **Task 013 (client capability)** changes shape: `canGetDocumentUrl` is joined (or replaced) by
  `canReadIdentityStamp`, and `IHostAdapter` grows a `getStampedDocumentId()` alongside — or instead of —
  `getDocumentUrl()`.
- **The §8 operator pass is still required**, but its priority ordering changes: step 6 (custom XML part
  read) becomes the *first* thing to verify rather than the last.

---

## 8. Operator verification recipe

> ## 🚫 NOT TO BE COMMITTED
>
> **The snippet below MUST NOT be added to `src/` in any form** — not as a file, not as a debug view, not
> as a temporary `console.log` in an existing component. It lives here, in this report, and is pasted at
> runtime. The task POML's first constraint is explicit: *"A spike that ships debug code has changed the
> product."* This spike modified **zero** source files (§12).

### 8.1 Getting a Spaarke-sourced document open in Word desktop

1. Pick an `sprk_document` in the dev environment that **has a file** (`sprk_hasfile = true`) and a
   non-empty `sprk_graphitemid`. Note its `sprk_documentid`, `sprk_graphdriveid` and `sprk_graphitemid` —
   these are your expected answers for links 3 and 4.
2. Open it through **Spaarke's own flow**, not by any other route — the flow is the variable under test:
   `GET {bff}/api/documents/{documentId}/open-links` → take `desktopUrl` → navigate to it.
   In the UI this is the "Open in Desktop" action
   (`src/client/webresources/js/sprk_DocumentOperations.js:1559-1578`).
3. **Record the raw `desktopUrl` string before opening it.** It should look like
   `ms-word:https://{tenant}.sharepoint.com/contentstorage/CSP_{guid}/…`. This is the URL Word is being
   handed, and half of Link 1's answer is whether `document.url` gives it back to you.
4. Let Word open the document.
5. **🔴 First observation, before anything else: does it open in Protected View?**
   (§5 Link 1c predicts it will.) Record:
   - Does the task pane / add-in load **at all** in Protected View?
   - The title-bar / backstage: does Word show it as a **server** document or a **local/downloaded** copy?
   - Then click **Enable Editing** and re-observe. **Run the probe in both states if the pane loads in
     both** — a different answer before and after Enable Editing is itself a first-class finding.
6. Open the Spaarke task pane and run §8.3.
7. 🆕 **Then repeat steps 2–6 with the FULL protocol verb** (§5 1d). Take the same file URL and open it
   as `ms-word:ofe|u|{urlencoded webDavUrl}` — Microsoft's documented pattern — instead of Spaarke's
   abbreviated form. **If the full form is zone-blocked, record that** (it is what `DesktopUrlBuilder.cs`
   predicts, and confirming it is itself a result). **If it opens, compare `document.url` between the two
   opens.** A difference here means Link 1 is a zone-policy problem with a fix, not a platform dead end —
   the single most valuable thing this pass can discover.
8. ~~🆕 **Step 6b — the §6.5 caveat-2 check (10 minutes, gates §7).**~~ **✅ RESOLVED 2026-09-09 by
   task 019 — DO NOT RUN. The premise is CONFIRMED; this step is retired from the operator pass.**
   It was settled without a host, and more strongly than this step could have: a custom XML part in the
   **third-party namespace `http://customooxmlschemas.google.com/`** was found intact in
   `tests/unit/Sprk.Bff.Api.Tests/Fixtures/Compose/RealTemplates/commonpaper-cloud-service-agreement.docx`
   after **four** modern-Word save cycles (`cp:revision 4`, 1,820 `w:rsid` attributes, `w15:docId`,
   `mc:Ignorable="w14 w15 w16se w16cid w16 w16cex w16sdtdh wp14"` — Word is provably the last writer),
   while `w:customXml` **markup** elements occur **0 times across all 48** Word files in the repo.
   Corroborated by Microsoft's own i4i explainer (Gray Knowlton, ms.date 2009-12-23), which states that
   what was removed is the *markup tags*, and that *"Content Controls and XML data stored within DOCX or
   DOCM files will not be affected by this change."* Full evidence + limits:
   `notes/019-customxml-premise-and-manifest.md` §1.

   > **Replaced by a different, cheaper item**: run the unified manifest through the Teams manifest
   > validator or an M365 admin-center upload and confirm the `CustomXmlParts` capability name is
   > accepted — Microsoft publishes no allow-list of requirement-set names nameable in the unified
   > manifest, so that one point rests on construction + Microsoft's own Common-set examples. See
   > `notes/019-customxml-premise-and-manifest.md` §5.

### 8.2 How to reach a task pane now that sideload is gone

Sideloading is no longer available in this environment (§3; task 011 § Blocked). Options, best first:

| # | Route | What it costs | Caveat |
|---|---|---|---|
| **A** | **Deploy a build**, then use the deployed pane. `deploy-office-addins.yml` has a `workflow_dispatch` trigger (`.github/workflows/deploy-office-addins.yml:15`) — run it against this branch, confirm green via `gh run list --workflow=deploy-office-addins.yml`. | One CI run. | Deploy is **CI-only**, not an agent-run script (`src/client/office-addins/CLAUDE.md`, Build/deploy). A **manifest** change additionally needs M365 Admin Center re-registration at the new version — but this probe needs **no manifest change**, so re-registration should not be required. Confirm the already-registered Word add-in still resolves to the freshly deployed SWA content. |
| **B** | **Browser devtools console against the running task pane.** Once the pane is open (via A), attach devtools to the add-in webview and paste §8.3 directly. | Nothing extra. | On Word desktop, attaching to the Edge WebView2 pane requires devtools to be reachable for that host; if it is not, fall back to C or run the same probe in **Word on the web**, where devtools are ordinary browser devtools. A web-host answer does **not** substitute for the desktop answer, but it is a useful control. |
| **C** | **Script Lab** (Microsoft-published add-in, `Insert → Get Add-ins → Script Lab`) — runs arbitrary Office.js in the host **with no deploy and no sideload**. | Nothing, if it is available. | ⚠️ **I have not verified that Script Lab is available in this tenant** — it may be blocked by Integrated Apps policy, and an admin may need to allow it. If it is available this is by far the cheapest route. ⚠️ Also note Script Lab is a **different add-in with its own manifest**, so it answers "what does the host return for this document?" but **not** "what does *our* manifest's permission/requirement configuration allow?" — §6.4 and §6.5 must still be checked against our own build. |

### 8.3 The probe snippet

Paste into the task-pane console (or a Script Lab snippet) with a Spaarke-sourced document open.
It prints every candidate value and the derived sharing token, and it never throws.

```js
// Spike-1 probe — NOT FOR COMMIT. Paste into the add-in webview console or Script Lab.
(async () => {
  const out = { _meta: {}, sync: {}, async: {}, word: {}, stamp: {}, token: {} };

  // ---- 0. Host / platform / version context (record this verbatim with your results) ----
  try {
    out._meta = {
      host:        Office?.context?.host,                 // e.g. "Word"
      platform:    Office?.context?.platform,             // "PC" | "Mac" | "OfficeOnline" | "iOS" | "Android"
      officeVer:   Office?.context?.diagnostics?.version,  // host BUILD number — required for the provenance label
      officeHost:  Office?.context?.diagnostics?.host,
      contentLang: Office?.context?.contentLanguage,
      wordApi_1_3:    Office?.context?.requirements?.isSetSupported('WordApi', '1.3'),
      wordApi_1_4:    Office?.context?.requirements?.isSetSupported('WordApi', '1.4'),        // §6.5 — Word.Document.customXmlParts + .settings
      customXmlParts: Office?.context?.requirements?.isSetSupported('CustomXmlParts'),        // §6.5 — the COMMON API (recommended)
      wordApiDesktop_1_4: Office?.context?.requirements?.isSetSupported('WordApiDesktop', '1.4'), // §6.1b — Word.Document.path/.fullName
    };
  } catch (e) { out._meta.error = String(e); }

  // ---- 1. LINK 1, candidate A: the synchronous property ----
  try {
    const u = Office.context.document.url;
    out.sync = { value: u, type: typeof u, isNull: u === null, isEmpty: u === '' , length: u ? u.length : 0 };
  } catch (e) { out.sync = { threw: String(e) }; }

  // ---- 2. LINK 1, candidate B: the async call (§6.1 — may differ from the sync property) ----
  out.async = await new Promise(res => {
    try {
      Office.context.document.getFilePropertiesAsync(r => res({
        status: r.status,
        value:  r.value ? r.value.url : null,
        error:  r.error ? { name: r.error.name, message: r.error.message, code: r.error.code } : null,
      }));
    } catch (e) { res({ threw: String(e) }); }
  });

  // ---- 3. Word JS API document surface (§6.3) + path/fullName (§6.1b — the DIAGNOSTIC for Link 1c) ----
  try {
    await Word.run(async ctx => {
      const p = ctx.document.properties;
      p.load(['title', 'author', 'creationDate', 'lastAuthor', 'revisionNumber']);
      ctx.document.load('saved');
      // WordApiDesktop 1.4 — "the disk OR THE WEB path". Desktop-only; absent on web/LTSC/iPad.
      // Guarded separately below because loading an unsupported property throws the whole batch.
      await ctx.sync();
      out.word = {
        saved: ctx.document.saved,
        title: p.title, author: p.author, lastAuthor: p.lastAuthor,
        creationDate: p.creationDate, revisionNumber: p.revisionNumber,
      };
    });
  } catch (e) { out.word = { threw: String(e) }; }

  // ---- 3b. §6.1b — path / fullName. SEPARATE Word.run so a 1.4 miss can't poison section 3. ----
  //      A DISK path here vs a WEB path is the clearest signal for the Link 1c Protected-View question.
  try {
    await Word.run(async ctx => {
      ctx.document.load(['path', 'fullName']);
      await ctx.sync();
      out.word.path = ctx.document.path;          // "" or a disk path or a web path
      out.word.fullName = ctx.document.fullName;
      out.word.pathLooksLocal = /^[A-Za-z]:\\|^\/Users\//.test(String(ctx.document.path || ''));
    });
  } catch (e) { out.word.pathError = String(e); } // expected on web / LTSC / pre-2508 Windows

  // ---- 4. §6.5 — can we READ a custom XML part? (the FR-02 / stamp-as-primary question) ----
  //      Uses the COMMON API (unversioned CustomXmlParts requirement set) — the recommended one.
  const NS = 'http://spaarke.com/document-identity';  // placeholder — task 014 owns the real namespace
  out.stamp = await new Promise(res => {
    try {
      if (!Office.context.document.customXmlParts) return res({ unavailable: 'customXmlParts is undefined on this host' });
      Office.context.document.customXmlParts.getByNamespaceAsync(NS,
        r => res({ status: r.status, count: r.value ? r.value.length : 0,
                   error: r.error ? r.error.message : null }));
    } catch (e) { res({ threw: String(e) }); }
  });

  // ---- 4b. [RETIRED — step 6b was resolved 2026-09-09 by task 019; the premise is CONFIRMED and this
  //      write-probe is no longer needed. Kept only as a usage example of addAsync.]
  //      UNCOMMENT ONLY for the caveat-2 check, and only on a THROWAWAY .docx. The xmlns is
  //      MANDATORY — without it namespaceUri is not populated and getByNamespaceAsync never finds it.
  // Office.context.document.customXmlParts.addAsync(
  //   `<identity xmlns="${NS}"><documentId>00000000-0000-0000-0000-000000000001</documentId></identity>`,
  //   r => console.log('addAsync', r.status, r.error && r.error.message));
  //   → then: save · CLOSE WORD COMPLETELY · reopen · re-run section 4 · then download+re-upload and re-run.
  // Also confirm the Word-specific surface is/ISN'T present — this is the WordApi 1.4 gate in action:
  try {
    await Word.run(async ctx => {
      out.stamp.wordSpecificSurfacePresent = !!ctx.document.customXmlParts;
    });
  } catch (e) { out.stamp.wordSpecificSurfaceError = String(e); }

  // ---- 5. LINK 2 — derive the Graph sharing token from whichever URL we got ----
  const b64url = s => btoa(unescape(encodeURIComponent(s)))
                        .replace(/=+$/, '').replace(/\+/g, '-').replace(/\//g, '_');
  for (const [label, url] of [['fromSyncUrl', out.sync.value], ['fromAsyncUrl', out.async.value]]) {
    if (typeof url === 'string' && url.length) {
      out.token[label] = { url, sharingToken: 'u!' + b64url(url),
                           graphCall: `GET https://graph.microsoft.com/v1.0/shares/u!${b64url(url)}/driveItem` };
    } else {
      out.token[label] = { url, sharingToken: null, note: 'no usable URL to encode' };
    }
  }

  console.log('=== SPIKE-1 PROBE ===');
  console.log(JSON.stringify(out, null, 2));
  return out;
})();
```

### 8.4 What to record, and how to label it

Copy the whole JSON payload into a reply on this task. For the report to be upgraded from AMBER, each
claim needs the full `EMPIRICALLY VERIFIED` quintuple:

- **host** (Word) · **build number** (`_meta.officeVer`) · **platform** (`_meta.platform`) ·
  **file source** (SPE-backed Spaarke document / OneDrive / local disk / never-saved) · **observation**.

Run it for **all four** Link-1 cases in §5 1b — the SPE case is the one that matters, but the other three
are what let you tell "the API is broken" apart from "the API works and this file has no URL".

Then, for whichever URL came back non-null, run the printed `graphCall` (Graph Explorer or `curl` with a
delegated token) and record, in this order:

1. **The HTTP status.** A non-2xx on an SPE URL is the Link-2 answer and fires POML escalation trigger 2.
2. **Whether `parentReference.driveId` and `id` come back WITHOUT an explicit `$select`** — the documented
   example omits `parentReference` (§5 Link 3). Then re-run **with** `?$select=id,name,parentReference,webUrl`
   and confirm it does. Task 012 will `$select` regardless; this just records which was necessary.
3. 🔴 **The payoff — does the returned `id` `string-equal` the `sprk_graphitemid` you noted in §8.1 step 1?**
   Byte-for-byte, case included. **That single comparison converts §5 Link 4 from `DESK-RESEARCHED` to
   `EMPIRICALLY VERIFIED`** and is the highest-value line in this whole pass. Also compare
   `parentReference.driveId` against `sprk_graphdriveid` — that is the §5 Link 4 defensive check working.
4. **Try the same call with `webDavUrl` as the input**, not just whatever `document.url` returned. Per §5
   1d, `webDavUrl` is Microsoft's documented *canonical file URL* for SPE, so it is the likeliest input to
   succeed — and if `/shares` accepts `webDavUrl` but not the `document.url` value, that is a precise,
   actionable finding rather than a flat negative.
5. **Run it once as a user who LACKS access** to the target, and record the status code — **403 vs 404 is
   undocumented** (§5 Link 2) and FR-01's error handling depends on it.

---

## 9. Verdict

# 🟠 AMBER

**Not GREEN**, because the desktop behaviour could not be empirically confirmed and this session had no
live host — the POML forbids upgrading desk research to GREEN, and the negative-verification acceptance
criterion requires AMBER or RED in exactly this situation.

**Not RED**, because nothing was *disproven*. Link 4 — the link the POML singled out as most dangerous
and most likely to be assumed — is **positively closed** with no defect. Link 3 is sound. And a named,
architecturally-superior alternative (§7) exists whose write path depends on no host API at all. A RED
would fire the escalation trigger and force a scope conversation; the evidence does not support that.

**What AMBER means concretely here:**

- ✅ **Closed, no further work**: Link 4. `sprk_graphitemid_uk` needs no change, the formats match, and
  the only action is a defensive `driveId` comparison on the BFF side (§5 Link 4).
- ✅ **Closed by desk research**: FR-19 parity for the stamp — `CustomXmlParts` is supported on Word web,
  Windows (back to perpetual Office 2016), Mac and iPad (§6.5).
- 🟠 **Open, operator-closable in one pass**: Links 1 and 2. §8 is the recipe.
- 🚨 **New, needs a decision or a check before Phase 1** — four items, and the first is actionable today
  with no host at all:
  1. **The WordApi 1.4-vs-1.3 requirement-set gap** (§6.5). `Word.Document.customXmlParts` *and*
     `.settings` both need 1.4; both manifests declare 1.3. **Fixable now**; recommended fix is the
     common API + a `CustomXmlParts` declaration, which needs no WordApi bump at all.
  2. 🔴 **The markup-vs-parts precondition** (§6.5 caveat 2). Ten minutes of work that FR-02 entirely
     rests on, and it has never been checked.
  3. **The stamp-as-primary scope recommendation** (§7) — with the Document Inspector strip path
     (§6.5 caveat 1) now on the record as a real cost.
  4. **`urlTemplate` as an alternative to `/shares` entirely** (§5 Link 2, open question 10) — Microsoft's
     own documented SPE answer to this problem, and structurally simpler than the chain FR-01 specifies.

**Two things the desk research changed materially**, and neither was in the POML: Microsoft's SPE docs
**independently confirm** the repo's `webUrl`-is-a-viewer-URL / `webDavUrl`-is-canonical finding (§5 1d),
which strengthens Link 1c from a repo-only inference to a two-source one; and `Word.Document.path` /
`.fullName` exist as a **desktop-only diagnostic** (§6.1b) that may distinguish "Word opened a local copy"
from "Word opened a server document" — the exact question Link 1c turns on.

**The risk is not evenly distributed.** If the operator accepts §7, links 1–3 stop being on the critical
path and this AMBER largely resolves itself; the residual is §6.5, which is a manifest edit plus one
verification. If §7 is declined, §8 must run before task 012 starts, and a negative on Link 1 or 2 fires
a POML escalation trigger.

---

## 10. Consequences, FR by FR

Stated individually, as the POML requires. "Identity" below means a resolved `sprk_document`.

| FR | Consequence of this AMBER | If §8 returns a NEGATIVE on Link 1 or 2 and §7 is declined |
|---|---|---|
| **FR-01 — Conditional document identity** | **Directly gated.** The specified primary path is unverified at two of four links. The chain's *back half* (links 3–4) is proven sound, so a positive §8 result closes FR-01 as specified with no rework. FR-01's negative acceptance (*"a desktop-sourced document resolves to nothing and is treated as new"*) is safe either way — but only if a Graph **403** is distinguished from a genuine miss (§5 Link 2). | FR-01's primary path fails. The FR-02 stamp becomes the only mechanism — which, given the operator's dev-only/legacy-doesn't-matter decision, is now **survivable**, unlike when the spec was written. **Escalation trigger 1 fires** and the operator chooses. |
| **FR-07 — Profile section** | **Blocked on FR-01, not independently at risk.** Once identity resolves, the four fields (`sprk_filesummary`, `sprk_filetldr`, `sprk_filekeywords`, `sprk_documenttype`) are a plain record read — no part of FR-07 depends on *how* identity was obtained. Under §7 it would read from a stamp-resolved id instead; the view code is unchanged. | Profile is unavailable for any document not stamped by this release. In dev, that is every pre-release document — accepted per the operator decision. |
| **FR-09 — Related-to record card** | **Blocked on FR-01.** Same shape as FR-07. Note the *independent* landmine already recorded in project CLAUDE.md: the Office save path writes only the **direct** lookup family, so a card reading only `sprk_related*` is empty on every document the add-in created. That is FR-09's own defect and is **not** affected by this spike's outcome — it must be fixed regardless. | Card never renders for unidentified documents. Degrades cleanly (hide the card); no data-loss path. |
| **FR-10 — Open record / open Document record** | **Blocked on FR-01 for the *target*, not for the *mechanism*.** Spike-2 already settled the Dialog API question; FR-10 has nothing to open until identity resolves. No additional risk from this spike. | No record to open from the pane for unidentified documents. The action should be hidden, not shown-and-failing. |
| **FR-11 — Version-save with override** | 🔴 **The highest-consequence dependant — and the one with a real failure mode rather than a blank UI.** FR-11 defaults to saving a **new version** of the resolved record. If identity resolves to the **wrong** record, FR-11 writes a version onto someone else's document — silent data corruption, not a blank panel. This is exactly why §5 Link 4's *"the key is keyed on the ITEM alone, leaving `driveId` unvalidated"* finding must become a defensive `driveId` check in task 012 and not be waved through. **A wrong identity is far worse here than no identity.** | FR-11 falls back to always creating a new document — the current behaviour, and safe. It must **not** guess. The override path ("Save as new document") stays correct and must keep routing through link/graduate per NFR-08. |

**Cross-cutting**: nothing in this report changes NFR-07 (`sprk_graphitemid_uk` untouched) or NFR-08
(editable saves keep link/graduate — already fixed on this branch by `dd286200f`).

---

## 11. Implementation sketch for task 012 (facade level only — ADR-007)

Not an implementation. **Task 012 implements; this is shape only.** Sketched for the FR-01-as-specified
path; if the operator accepts §7 most of this becomes optional.

### Facade — one new method on `SpeFileStore`

```
SpeFileStore.ResolveSharedItemAsUserAsync(HttpContext ctx, string url, CancellationToken ct)
    → SpeSharedItemRef?   // ( DriveId, ItemId )  — null when Graph cannot resolve it
```

- **ADR-007**: the Graph SDK call (`GET /shares/u!{token}/driveItem`) lives in
  `Infrastructure/Graph/DriveItemOperations.cs` behind this facade method, exactly like the existing
  `GetDriveItemAsUserAsync` (`SpeFileStore.cs:356-362`). **No `Microsoft.Graph` type crosses the
  boundary** — the return is a Spaarke DTO. There is no `/shares/` call anywhere in the BFF today
  (§2), so this is genuinely new surface, not an extension.
- **Base64url encoding happens inside `DriveItemOperations`**, not in an endpoint and not on the client.
  One implementation, one place.
- **OBO, not app-only.** `IGraphClientFactory.ForUserAsync`, matching every other `…AsUserAsync` on the
  facade. The caller's own access is the boundary — the endpoint must not elevate to answer "does this
  URL belong to a Spaarke document?", or it becomes an oracle over items the caller cannot see.
- **DTO gap (§5 Link 3)**: `SpeDriveItemSummary` (`SpeFileStoreDtos.cs:76-85`) has no discrete
  `ParentDriveId`. Either add that field or return the purpose-built `SpeSharedItemRef` above. Do **not**
  parse the drive id out of `ParentReferencePath`.

### Endpoint — extends the existing `/api/documents` group

```
POST /api/documents/resolve-by-url     { url }  →  200 { documentId, matterId?, … } | 204 no-content
```

- **Placement (root CLAUDE.md §10)**: extends the existing `/api/documents` route group, per spec.md's
  Placement Justification (line 170) — *"Identity resolver extends `/api/documents` (existing Graph +
  Dataverse plumbing…)"*. **No new deployable, no new DI graph, no new package.** Publish-size delta
  expected ≈ 0; still measure per §10 (fresh master build, not the recorded baseline).
- **`POST`, not `GET`** — the URL is a request *body*, not a path segment. A cloud file URL in a route
  or query string is a logging and length hazard.
- **ADR-008**: an endpoint filter authorizes the **resolved `sprk_document` row** *after* resolution and
  *before* returning anything. Resolution order matters: resolve → authorize → respond. Returning a
  `documentId` the caller cannot read would leak existence.
- **ADR-019**: `ProblemDetails` for faults. A **miss is `204`, not `404`** — "this document is not from
  Spaarke" is a normal, expected answer for FR-01's conditional identity, not an error.

### The resolution steps, in order

1. `ResolveSharedItemAsUserAsync(url)` → `(driveId, itemId)`. Null → **204**. A Graph 403 must be
   **distinguished in logs** from a genuine miss even though both return 204 to the caller (§5 Link 2).
2. `RetrieveByAlternateKeyAsync("sprk_document", { sprk_graphitemid: itemId }, [ sprk_documentid,
   sprk_graphdriveid, … ])` — **the raw item-id string, verbatim, no transformation**
   (`ComposeRecordResolution.cs:181-206` is the existing shape to mirror). The alternate key reports
   not-found **by throwing** — `OfficeDocumentPersistence.cs:369-380` documents this and is the pattern
   to copy. Catch it and return 204; do not let it 500.
3. 🔴 **The defensive check from §5 Link 4**: `string.Equals(row.sprk_graphdriveid, driveId, Ordinal)`.
   Mismatch → treat as **not our document**, return 204. The column is already fetched in step 2 at no
   extra round-trip. **This is not a key change and touches nothing about `sprk_graphitemid_uk`
   (NFR-07).**
4. Project the response. **ADR-044 applies here and only here**: `sprk_documentid`, the matter/project
   id, and any container id are Dataverse GUIDs → **bare-lowercase via `cleanGuid`** at the boundary,
   and never interpolated raw into an OData key predicate. **`driveId` and `itemId` are opaque
   non-GUID strings — `cleanGuid` MUST NOT be applied to them**; lower-casing a case-bearing opaque
   identifier would corrupt it (`ComposeCreateOnSavePromoter.cs:337-341`).

### Test obligation (ADR-038 / root CLAUDE.md §10 bullet 6)

New endpoint ⇒ contract test under `tests/integration/contract/Api/` covering: hit, miss (204),
drive-mismatch (204 — the step-3 guard, and it must fail if the guard is removed), and the
unauthorized-caller path. Service registration must be unconditional if the endpoint maps unconditionally.

---

## 12. Compliance record

| Constraint | Status |
|---|---|
| No committed instrumentation; probe lives only in this report | ✅ **Zero** files added or modified under `src/**` **by this task**. The only file task 002 writes is this report. The probe exists solely as a fenced block in §8.3. |
| Did not touch `shared/adapters/**` or `word/**` (task 010 concurrent) | ✅ read-only |

> **⚠️ Reading `git status` in this worktree — do not misattribute.** `git status --porcelain` was
> **clean** at the start of this task and shows modifications under `src/client/office-addins/**` at the
> end. **None of them are task 002's.** They are **task 010** (FR-04 adapter consolidation), which ran
> concurrently in this shared worktree during this task's window: `shared/adapters/WordAdapter.ts`,
> `shared/adapters/__tests__/WordAdapter.test.ts`, `__tests__/HostAdapterFactory.test.ts` (new),
> `__tests__/zz-task010-parity.test.ts` (new), `word/taskpane/index.tsx`,
> `outlook/taskpane/index.tsx`, `shared/__mocks__/office-js.ts`, `jest.setup.js` — all squarely inside
> task 010's declared scope and none inside task 002's. `projects/…/current-task.md` is likewise task
> 010's (see deviation 3 below). This is the same concurrent-worktree drift task 009 recorded against
> 006/007/008; it is noted here so a later reader verifying acceptance criterion 9 does not read task
> 010's diff as this spike shipping debug code.
| Every claim provenance-labelled; nothing blurred | ✅ §3; no claim carries `EMPIRICALLY VERIFIED` |
| **NFR-07** — no relaxing/widening/duplicating `sprk_graphitemid_uk` | ✅ §5 Link 4 — explicitly none proposed; the fix is a caller-side `driveId` comparison |
| **ADR-044** — GUIDs bare-lowercase via `cleanGuid`, never raw in an OData key predicate | ✅ §11 step 4, with the explicit carve-out that drive/item ids are **not** GUIDs |
| **ADR-007** — Graph stays behind `SpeFileStore`; sketch only | ✅ §11 — facade method, Spaarke DTO, no Graph type crossing |
| Verdict not softened | ✅ AMBER, §9 |

**Deviation from the task file** (`<steps mode="directional">`, so noted rather than escalated):

1. **Step 8's scope was widened.** The POML predates the loss of sideloading; §8.2 adds the three
   deploy/Script-Lab routes, and §8.3 additionally probes the custom-XML-part read and the
   `isSetSupported` requirement-set checks — neither was in the POML, both are needed by §6.5/§7.
2. **§7 (stamp-as-primary) is new.** The POML treats the stamp as a fallback disqualified by being
   forward-only. The operator's dev-only decision post-dates it and removes that disqualifier, so the
   question was evaluated on its merits. §7 makes a **recommendation for operator decision**, not a
   change.
3. **`current-task.md` was not rewritten.** Task 010 is concurrently active in this worktree and the
   file is shared session state; a wholesale rewrite risked clobbering its progress. Task status is
   recorded in `TASK-INDEX.md` and this task's POML instead.

---

## 13. Open questions

1. **[Operator, blocking Phase 1] Accept §7 — promote the FR-02 stamp to primary identity?** Re-sizes
   tasks 012, 013 and 014. This is the single highest-value decision arising from this spike.
2. **[Operator pass, §8] Links 1 and 2** — the four `document.url` cases on Word desktop, and whether
   Graph `/shares/` accepts an SPE `contentstorage` URL.
3. **[Operator pass, §5 Link 1c] Protected View.** Does the add-in load at all? Does `document.url`
   differ before vs after *Enable Editing*? This is the mechanism most likely to make Link 1 negative.
4. **[Task 014, §6.5] Which custom-XML API, and the matching manifest change.** Common API +
   `CustomXmlParts` requirement set (recommended), or Word-specific + raise `WordApi` to 1.4. Shipping
   either against the current `1.3`-only manifest is the task-011 failure mode repeated.
5. ✅ **CLOSED — [Task 014 / FR-19, §6.5]** *Is `CustomXmlParts` supported in Word on the web and on Mac?*
   **Yes — both, plus iPad, plus perpetual Office 2016 on Windows.** `DESK-RESEARCHED (Learn)`, Common API
   requirement sets (2025-10-10). FR-19 parity is not at risk from this mechanism, and the common API's
   floor is *lower* than `WordApi 1.4`'s — which is what settles §6.5 on option 1.
6. ✅ ~~🔴 **[Task 014, §6.5 caveat 2 — PRECONDITION, not a question]**~~ **CLOSED 2026-09-09 by task 019.**
   Word's removal of *"custom XML markup"* applies **only to in-body `w:customXml` elements**, NOT to
   `/customXml/itemN.xml` package **parts**. Confirmed on two independent lines (Microsoft's i4i
   explainer + in-repo corpus forensics showing a foreign-namespace part surviving four Word save
   cycles). **FR-02 stands and §7 is not void.** Task 014 is GO. §8.1 step 6b is retired — do not run it.
   See `notes/019-customxml-premise-and-manifest.md` §1 and §6.
7. **[Task 011 follow-up, §6.4]** Does `word/manifest.json`'s empty
   `authorization.permissions.resourceSpecific` need a document-access entry to match the XML manifest's
   `ReadWriteDocument`? Not established here; not this spike's file to change.
8. **[Task 012, §5 Link 2]** Graph `/shares/` no-access status: **403 or 404 — undocumented.** Plus the
   least-privilege problem: the endpoint offers **no read-only scope** in either delegated or application
   mode (least privileged is `Files.ReadWrite`). FR-01 inherits both. Flag to security review early.
9. **[Task 014]** Reuse ADR-049's Compose OOXML write machinery for the server-side stamp rather than
   introducing a second `.docx` writer (root CLAUDE.md §11) — and emit an explicit `xmlns` on the stamp
   root, or `getByNamespaceAsync` will never find it (§6.5).
10. 🆕 **[Operator / architecture, §5 Link 2]** Evaluate SPE container-type **`urlTemplate`** as a
    first-class alternative to URL→`/shares` resolution. Microsoft's own SPE docs hand back `{drive-id}`
    and `{item-id}` directly — no encoding, no opaque-id assumptions, no `/shares` SPE silence. Blast
    radius is real (container types are permanent and capped at 25/tenant —
    `docs/architecture/SPAARKE-SPE-CONTAINER-TYPE-TOPOLOGY.md`), so this is an owner decision, not a task's.
11. 🆕 **[Task 013, §6.1]** The Learn guidance to declare not-in-a-set methods via `<Methods>`/`<Method>`
    is **XML-manifest-only**; no unified-JSON-manifest equivalent is stated. Until answered, use the
    runtime-check half of the guidance (`if (Office.context.document.getFilePropertiesAsync)`), which
    works on both manifests.
12. **[Recorded, not raised]** The `sprk_filepath`-vs-`webDavUrl` divergence (§5 Link 1c item 3) means the
    tempting "match `document.url` against `sprk_filepath`" shortcut silently fails. Noted so it is not
    rediscovered as a bug.

---

## 14. Other hosts (secondary — reported, not gated)

FR-01's acceptance is written against **Word desktop**. These are recorded for FR-19 parity only.

| Host | State |
|---|---|
| **Word on the web** | `document.url`'s web behaviour is **not documented either** — §5 1a shows the property is structurally outside the requirement-set mechanism on *every* host, so the POML's "documented for web" premise is generous. Still the most useful *control* in §8: a positive on web with a negative on desktop localises the fault to the desktop host rather than to the API. |
| **Word for Mac** | Not established. No platform-specific annotation exists for `url` on any host (§5 1a). Note the §5 Link 1c Protected-View mechanism is **Windows-specific** (Windows Security Zones), so Mac may behave differently — in either direction. |
| **Custom XML parts on web / Mac** | ✅ **Supported on both** (plus iPad, plus perpetual Office 2016 on Windows) via the common `CustomXmlParts` requirement set; the Word-specific `WordApi 1.4` route is also supported on both, at a higher floor. `DESK-RESEARCHED (Learn)`, Common API requirement sets (2025-10-10) + Word API requirement sets (2026-04-21). **This closes the FR-19 parity question for §7's mechanism.** |
| **`Word.Document.path` / `.fullName`** | ❌ **Not applicable on Word on the web**, and not available on Windows LTSC/volume-licensed or iPad; Windows M365 needs build **2508+**, Mac 16.100.4+ (§6.1b). Desktop-only by construction — usable as a diagnostic or a desktop-only supplement, **never** as a parity-bearing mechanism. |

---

*Spike-1, task 002, project `spaarkeai-word-add-in-r1`. Executed 2026-09-09 under `task-execute`,
rigor STANDARD, model tier opus, effort high. Verdict AMBER pending the §8 operator pass.*

---

## 19. 🟢 OPERATOR PROBE RESULT — 2026-09-10 (EMPIRICALLY VERIFIED)

The first empirically-verified data in this report. Everything in §1–§18 is DESK-RESEARCHED; this
section is not.

**`Office.context.document.url` returned, for a Spaarke-sourced document opened via the add-in:**

```
https://spaarke.sharepoint.com/contentstorage/CSP_585db4c8-8043-4676-965e-c92e45f07221/Document Library/Examiner report draft.docx
```

### Link 1 — GREEN. And §5's stated hazard did not materialise.

This is the **direct path form**, and it is NOT the `doc2.aspx` viewer URL. §2 recorded that Spaarke's
own open flow hands Word an abbreviated `ms-word:` URL and that files open in Protected View, and
predicted `document.url` might therefore report a local sandbox path. It does not. It returns a
server path in exactly the canonical shape Graph `/shares/u!{base64url}` expects.

A related worry raised in the main session — that the identifier would arrive as the viewer URL's
braced-uppercase `sourcedoc` GUID (a SharePoint UniqueId, a different identifier space from Graph's
`driveItem.id`, which §9 established is what `sprk_graphitemid` stores) — is also moot. That GUID
does not appear in `document.url` at all.

### 🔴 The finding this probe DID produce: raw spaces

The URL contains **two unencoded spaces** — `Document Library` and `Examiner report draft.docx`. A raw
space is not valid in a URL, and Graph's sharing-token construction is
`u!` + base64url(the URL) with padding stripped. Base64url-encoding the string **as returned** encodes
the spaces literally.

**This is task 012's first real implementation question, and it now has a concrete input rather than a
guess**: does `/shares/u!{token}` accept a token built over raw spaces, or must the URL be
percent-encoded (`%20`) before encoding? Both must be tried against this exact URL. Getting it wrong
produces a 400/404 that looks like "SPE is unsupported" rather than "the token was malformed" — which
would falsely condemn the whole FR-01 primary path.

### Still open

- **Which host produced this?** The acceptance criterion is Word **DESKTOP** specifically. If this came
  from Word on the web it is a valuable control but does not discharge §5 Link 1c.
- Protected View state at capture time (before vs after "Enable Editing").
- The abbreviated-vs-`ofe|u|` A/B (§8.1 step 7) — still the highest-value remaining comparison.
- **Link 2 remains DESK-RESEARCHED**: no `/shares` call has been made against an SPE
  `contentstorage` path. This probe supplies its input; it does not answer it.

### Corroborated in passing

The operator reports the document opens in Word for web, Word desktop, and as a download, and that a
document saved through the add-in also opens. The **downloaded** copy opens in read mode — consistent
with §2's Protected View prediction for that path.

---

## 20. Link 2 — route analysis after the probe (2026-09-10, verified against code)

Three candidate routes from `document.url` to a `sprk_document`, weighed against the actual code:

| Route | Mechanism | Verdict |
|---|---|---|
| **A — `/shares`** | `GET /shares/u!{base64url(document.url)}/driveItem` — Graph resolves the URL for you | ✅ **The one to test.** Undocumented for SPE, and carries the raw-space encoding question (§19) |
| **B — path addressing** | `GET drives/{driveId}/root:/{path}` — the BFF already addresses this way for writes (`ISpeFileOperations.cs:134,191`) | ❌ Needs `CSP_{guid}` → drive id, and **no code maps that**. Spaarke container ids are Graph's `b!…` form (test fixtures), not `CSP_{guid}`. `ChatWordExportEndpoints.cs:277`'s comment claiming the shape is `/contentstorage/{containerId}/Document/…` is wrong about the real URL |
| **C — Dataverse match** | Match `document.url` against a stored column | ❌ `sprk_filepath` stores Graph's **`WebUrl`** (`UploadSessionManager.cs:160` → `OfficeStorageUploader.cs:71`) — the *viewer* form — not the path form `document.url` returns. No `webDavUrl` column exists. Would need a new column, is forward-only, and breaks on rename/move — strictly weaker than the FR-02 stamp |

**Route A must run through a REGISTERED app.** Per `docs/architecture/SPAARKE-SPE-CONTAINER-TYPE-TOPOLOGY.md`
lines 115-116, only the owning app or a registered app reads container files. `az` CLI and Graph Explorer
are neither, so a `/shares` call from them would fail **regardless of whether the URL is valid** — a false
negative that would wrongly condemn FR-01's primary path. Link 2 is therefore effectively task 012's first
deliverable, exercised through the BFF.

### A shape check available today, through the BFF's own identity

`GET /api/documents/{documentId}/open-links` for the probed document returns
`DesktopUrl = ms-word:{directFileUrl}`, where `directFileUrl` is Graph's `webDavUrl` when present
(`FileAccessEndpoints.cs:625-628`) and is otherwise rebuilt from `WebUrl` with
`Uri.EscapeDataString(fileName)`. Strip `ms-word:` and compare against the probe's `document.url`.

A match proves `document.url` is exactly the canonical URL the BFF itself resolves for that item — with none
of the false-negative risk above. **Expect the normalisation points to surface right here**: the fallback
percent-encodes the filename (`%20`) while `document.url` carries raw spaces. That is the same encoding
question §19 raises for the `/shares` token, now visible inside the BFF's own code.

### Standing recommendation — unchanged, and reinforced

Every route above is **identity-by-location** and breaks when a file is renamed or moved. The FR-02 custom-XML
stamp is identity-by-content and does not. Treat FR-01's URL path as the fast path and the stamp as primary.

---

## 21. Shape check result — 2026-09-10 (EMPIRICALLY VERIFIED)

The operator ran the §20 shape check: the URL the **BFF itself** produces for the probed document via
open-links (`ms-word:` prefix removed) —

```
https://spaarke.sharepoint.com/contentstorage/CSP_585db4c8-8043-4676-965e-c92e45f07221/Document%20Library/Examiner%20report%20draft.docx
```

against the §19 probe value from `Office.context.document.url` —

```
https://spaarke.sharepoint.com/contentstorage/CSP_585db4c8-8043-4676-965e-c92e45f07221/Document Library/Examiner report draft.docx
```

**Result: MATCH after percent-decoding.** Host, `contentstorage`, the `CSP_{guid}` container segment,
`Document Library` and the filename are identical. The only difference is space encoding: the BFF emits
`%20`, the host API returns raw spaces.

What this proves:
- `document.url` is exactly the canonical URL the BFF already resolves for that item, through the BFF's
  own registered identity — so link 1's value is the right input for link 2, with no false-negative risk.
- The §19 encoding question is real and has now been observed on both sides of the same item. Task 012
  must normalise before building the `/shares` token and test both forms (see its SPIKE-1 criteria).

**Host caveat — still open:** the §19 capture came from **Word on the web**. Spike-1's criterion is
Word **desktop**, which has not yet been captured. Links 2 and 3 plus the desktop capture are moved into
task 012 as its first acceptance criteria (2026-09-10); when they pass, this report's verdict moves from
AMBER to GREEN.

---

## 22. 🟢 Links 2 and 3 — LIVE through the BFF (2026-09-10, EMPIRICALLY VERIFIED)

Task 012's resolver was deployed to `spaarke-bff-dev` (commit `8fec97b2d`, SHA-256 hash-verified) and called
through the BFF's own registered app, **OBO as a real user**. This is the only identity that can read SPE, so the
§20 false-negative risk does not apply. The evidence is the resolver's own per-spelling log line from App Insights,
and every row was unsampled (`itemCount = 1`). Full table: `notes/012-identity-resolver-decisions.md` §8.

| Link | Result | Evidence |
|---|---|---|
| **2 — `/shares` resolves an SPE `contentstorage` URL** | 🟢 **GREEN** | The §19 capture (raw spaces), encoded per path segment to `%20`, resolved on the first try: `Attempts: encoded=200` |
| **2 — encoding** | 🟢 **Answered** | Graph accepts the `%20` spelling. A double-encoded spelling (`%2520`) is refused, as is any path that does not exist, and in both cases with **403 accessDenied, not 404**. A token over raw spaces was never needed, so it was never tried. |
| **3 — driveItem id + drive id match the stored row** | 🟢 **GREEN** | The item id resolved `sprk_document` `8c135b45-5da8-f111-aaab-7ced8ddc4a05` (matter PAT-191111) through `sprk_graphitemid_uk`, and the caller-side drive comparison passed. Neither the mismatch nor the empty-drive Warning was logged. The document's own open-links points back at the same file. |

**A finding this spike did not anticipate:** Graph's 403 is overloaded. It means "not visible to you" *and* "no
such item" (and "malformed spelling"), because SharePoint does not disclose which. Task 012 therefore maps 403 to
"not resolvable", not to an outage (notes/012 §4).

**Verdict: still AMBER, with one criterion left**, the Word **desktop** capture of `document.url`. Links 1 (web), 2, 3
and 4 are GREEN. When the desktop value resolves through the same route, this report moves to GREEN.

---

## 23. 🟢 VERDICT: GREEN — the Word desktop capture resolves (2026-09-11, EMPIRICALLY VERIFIED)

**The capture.** The operator captured this in the Spaarke pane on Word **desktop**. `Office.context.diagnostics`
reported host `Word`, platform `PC`, build `16.0.20326.20132`. The command was
`JSON.stringify(Office.context.document.url)`:

```
"https://spaarke.sharepoint.com/contentstorage/CSP_585db4c8-8043-4676-965e-c92e45f07221/Document Library/Examiner report draft.docx"  (length 130)
```

**Byte-identical to the Word-on-web capture** of §19 (ordinal comparison, 130 characters, raw spaces).

**Resolved through the deployed route.** Commit `9750b4968` on `spaarke-bff-dev`, OBO as the operator,
2026-09-11T01:25Z: **HTTP 200, resolved → `8c135b45-5da8-f111-aaab-7ced8ddc4a05`, matter PAT-191111.**

**Link 1c, the desktop hazard in §5, did not materialise.** Desktop returns the same server path form as the web,
not a local or sandboxed path and not the viewer URL. The Protected View state at capture time was not recorded;
the document was open with the pane active.

**Final: links 1 (web AND desktop), 2, 3 and 4 are all GREEN. FR-01's primary path is viable.**

- §7's stamp-as-primary recommendation still stands on its own merit, because identity by content survives a
  rename or a move. It is no longer needed for viability.
- The URL path cannot be dropped in any case (notes/012 §1).
- **Limits of the evidence:** one document, one tenant, one Windows desktop build. Mac desktop and Office on iPad
  were not tested (§14, secondary hosts).
