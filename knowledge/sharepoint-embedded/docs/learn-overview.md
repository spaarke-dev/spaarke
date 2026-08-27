---
source: https://learn.microsoft.com/en-us/sharepoint/dev/embedded/overview
fetched: 2026-05-14
refreshed: 2026-08-26
refreshed-by: sdap-SPE-admin-app-r2 (task 061), per knowledge/REFRESH-PROCEDURE.md
refresh-scope: >
  Added the SharePoint admin center "Apps" experience (GA early July 2026 — postdates the 2026-05-14
  curation entirely) and the Purview compliance boundary (SPE compliance is delivered through Purview,
  not through container-level app APIs — a decision-relevant finding for anyone considering building
  hold/retention/eDiscovery UI on top of SPE). Original 2026-05-14 overview snapshot retained below
  unchanged.
---

# SharePoint Embedded Overview | Microsoft Learn

> ## 🔄 2026-08-26 refresh summary
>
> - [The admin-center Apps experience — GA early July 2026](#-the-admin-center-apps-experience--ga-early-july-2026-verified)
>   postdates the original curation by nearly two months and materially changes how container types get
>   created in practice.
> - [The Purview compliance boundary](#the-purview-compliance-boundary--do-not-build-compliance-features-into-an-spe-app)
>   — a build-vs-don't-build decision other consumers of this corpus will likely face too.

Microsoft SharePoint Embedded is a cloud-based file and document management system suitable for use in any application. SharePoint Embedded is a new API-only solution that enables app developers to harness the power of the Microsoft 365 file and document storage platform for any app, and is suitable for enterprises building line-of-business applications and ISVs building multitenant applications.

SharePoint Embedded allows you to integrate advanced Microsoft 365 features into your apps including full-featured collaborative functions from Office, Purview's security and compliance tools, and Copilot capabilities.

> **Important**: Help us shape the future of SharePoint Embedded! Take our [quick survey](https://forms.microsoft.com/r/1YpGd2pAUS) and share your experience!

## App documents stay in their Microsoft 365 tenant

When a consumer uses a SharePoint Embedded application in their Microsoft 365 tenant, SharePoint Embedded creates another partition within their tenant. This storage partition doesn't have a user experience and the documents in the partition are only accessible via APIs. This means that all documents will be accessible to the developer's application, but the documents will only reside in the consumer's Microsoft 365 tenant. Within this new storage partition inside of a Microsoft 365 tenant, a SharePoint Embedded application can create many "File Storage Containers" for storing content.

## Introducing File Storage Containers

SharePoint Embedded applications use Microsoft Graph APIs to store files and documents in a new entity called a "File Storage Container" or Container for short. If you're an ISV, your app will create these containers in your customer's Microsoft 365 tenant, and if you're an enterprise, your app will create these containers in your own tenant. Each container provides a place to store files - you can think of them as similar to an API-only Document Library in SharePoint, but with some slight differences. Your app can create many of these containers inside each tenant that uses your app, and each container can be granted permissions separately storing many files with multiple terabytes of content.

SharePoint Embedded containers are dedicated to and accessible by just your app, so the files and documents your app depends on are isolated and secure within that tenant boundary.

## App-managed content experiences

By default, the content stored within a Microsoft 365 tenant by a SharePoint Embedded application is only accessible through that owning application. Applications using SharePoint Embedded also provide the user experience layer for accessing and managing content, using some of the rich content capabilities that Microsoft 365 offers such as:

- Core content management features like support for any file type and folder structure, searching, sharing, automatic versioning, recycle-bin, and more
- Collaboration features like view, edit, and co-authoring Office Word, Excel, and PowerPoint documents in Office Web and Desktop

SharePoint Embedded is used by several types of applications:

- Certain Microsoft products use SharePoint Embedded to manage customer content, such as Loop and Designer.
- ISVs can use SharePoint Embedded in their apps to manage content within their customer's Microsoft 365 tenant
- Enterprises can use SharePoint Embedded to manage and store content within their own Microsoft 365 tenant, but outside of regular Microsoft 365 entitlements

## Consumer Microsoft 365 settings apply to app documents

All documents stored in the SharePoint partition created by the SharePoint Embedded app are in the consumer's Microsoft 365 tenant and therefore are subject to the consumer's Microsoft 365 tenant settings.

This includes settings from Microsoft Purview compliance, risk, and security settings, documents can be opened from Office clients, and customers can use the Office web clients to view and collaborate on the documents. Choosing applications that are built on SharePoint Embedded provides the app consumer Microsoft Purview security and compliance capabilities on that app content, such as:

- eDiscovery
- Auditing
- Data loss prevention (DLP)
- Retention policies, sensitivity labels, conditional access

> See [The Purview compliance boundary](#the-purview-compliance-boundary--do-not-build-compliance-features-into-an-spe-app)
> below for what this means in practice for an application built on SPE — in particular, why building
> compliance UI on top of these APIs is usually the wrong move.

## Understanding the costs and billing for SharePoint Embedded content

Microsoft 365 customers have different entitlements related to storage, usage, and features depending on the licenses the customer has purchased.

The partition created in the consumer's Microsoft 365 tenant by a SharePoint Embedded app doesn't count towards other Microsoft 365 entitlements including the total amount of Microsoft SharePoint storage that can be used by your organization. Instead, the partition in the consumer's Microsoft 365 tenant by the SharePoint Embedded app are billed separately through an Azure subscription on a pay-as-you-go metered consumption model that's based on total storage in active and archived state and the number of API calls.

> **Note**: Learn more about billing for SharePoint Embedded, see [Billing Meters](https://learn.microsoft.com/en-us/sharepoint/dev/embedded/administration/billing/meters).

## Get Started with SharePoint Embedded

[Review the prerequisites](https://learn.microsoft.com/en-us/sharepoint/dev/embedded/administration/billing/billing)

Create a "File Storage Container" in 15 minutes or less:

- [Free trial: SharePoint Embedded for Visual Studio Code](https://learn.microsoft.com/en-us/sharepoint/dev/embedded/getting-started/spembedded-for-vscode)

Follow manual set-up on SharePoint Embedded from the following Microsoft Learning modules:

- [Microsoft Learning: SharePoint Embedded - overview & configuration](https://learn.microsoft.com/en-us/training/modules/sharepoint-embedded-setup)
- [Microsoft Learning: SharePoint Embedded - building applications](https://learn.microsoft.com/en-us/training/modules/sharepoint-embedded-create-app)

---

## 🔵 The admin-center Apps experience — GA early July 2026 — VERIFIED

**Source**: [MC1290827 — Create a line-of-business SharePoint Embedded app in SharePoint admin
center](https://mc.merill.net/message/MC1290827); `sdap-SPE-admin-app-r2`
`notes/spe-platform-research-2026-08-20.md` §4, `design.md` §4.2.

Microsoft shipped a unified SharePoint Embedded **Apps** page inside the SharePoint admin center — rolled
out end of June 2026, complete early July 2026. This entirely postdates the 2026-05-14 curation of this
corpus and is the single most consequential platform addition since then for anyone building an
SPE admin surface.

What it provides, directly in the Microsoft admin UI (no code):

- Create and install SPE apps **without Global admin consent**
- "Installed apps" (deployed in the org) vs "Owned apps" (created by the org) — two distinct views
- Create a new Entra app registration or attach an existing one, during app creation
- Assign up to **three owners** for app settings and billing
- Select a **permanent** billing type — "User org" or "Owner org" — at creation (not changeable after,
  consistent with the container-type billing-classification permanence documented in
  `learn-containertypes.md`)

**Decision-relevant framing for anyone building a custom SPE admin tool** (`sdap-SPE-admin-app-r2`
design.md §4.2, §4.2b): this experience directly overlaps container-type creation and registration
screens — the same functional area a custom admin app would otherwise need to build from scratch. It is
a credible alternative for container-type *lifecycle management specifically*. It does **not** cover
container-level operations (file browse, per-container permissions, columns, custom properties, quota) —
those remain squarely outside Microsoft's own admin surface and are where a custom app still adds real
value. Before scoping a "recreate this in our own admin app" project, check whether the admin-center
Apps page already covers the specific screen you're about to build.

## The Purview compliance boundary — do NOT build compliance features into an SPE app

**Source**: [Plan security, compliance, and
governance](https://learn.microsoft.com/en-us/sharepoint/dev/embedded/plan/security-compliance-governance);
[Create eDiscovery holds](https://learn.microsoft.com/en-us/purview/ediscovery-create-holds); [Retention
for SharePoint and OneDrive](https://learn.microsoft.com/en-us/purview/retention-policies-sharepoint);
`sdap-SPE-admin-app-r2` `notes/spe-platform-research-2026-08-20.md` §3c, `design.md` §4.2c.

**Finding: SharePoint Embedded compliance is delivered through Microsoft Purview, not through
container-level app APIs.**

- Retention policies scoped to **all SharePoint sites** apply to all SPE containers automatically.
- Purview **eDiscovery** can search, hold, and export SPE content. Tenant-wide, this needs no
  SPE-specific configuration (configure eDiscovery Search for all SharePoint sites). Scoped to specific
  containers, an admin chooses sites under the SharePoint sites workload and supplies the **container
  URL** — see `learn-containers.md` for how to obtain it, since this is the one piece of information an
  SPE app genuinely needs to surface for this workflow.
- **Legal holds** preserve copies into a hidden Recoverable Items folder — the same mechanism used for
  SharePoint/OneDrive; no SPE-specific hold API exists or is needed.
- **Retention labels** are supported on SPE content; Microsoft's own guidance favors retention
  policies/labels over eDiscovery holds for long-term retention unrelated to an active investigation.
- Container content inherits the **full compliance posture of the host tenant** — same DLP, same
  conditional access, same audit logging as any other SharePoint/OneDrive content.

**Practical implication for anyone building an SPE admin application**: resist building hold, retention,
or eDiscovery management screens on top of SPE APIs. It duplicates an existing, audited compliance
surface with a narrower, harder-to-audit one, and Microsoft is not asking application developers to
re-implement this — Purview already governs it uniformly across SharePoint, OneDrive, and SPE.
`sdap-SPE-admin-app-r2` explicitly evaluated and rejected building this
(`design.md` §4.2c: *"do NOT build hold/retention management into SPE Admin... it fails [Spaarke's
reuse-over-new-component rule] outright"*), and instead limited its own scope to: (1) in-app guidance
routing admins to the Purview compliance portal, and (2) surfacing the container's **URL**, which is the
one value Purview's own eDiscovery scoping UI actually needs and which SPE's container APIs do not
otherwise expose (see `learn-containers.md`).

**Not verified by any Spaarke project as of this refresh**: whether per-container hold/retention *state*
is queryable back from Graph for a read-only status column (e.g., "this container is under an active
legal hold"). If you need that, spike it — don't assume either way.
