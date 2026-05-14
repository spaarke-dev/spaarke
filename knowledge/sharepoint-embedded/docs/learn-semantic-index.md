---
source: https://learn.microsoft.com/en-us/microsoftsearch/semantic-index-for-copilot
fetched: 2026-05-14
---

# Semantic indexing for Microsoft 365 Copilot | Microsoft Learn

Microsoft 365 Copilot maps your organization's data into an advanced lexical and semantic index to power search relevance and accuracy. Copilot can access the context and relationships within your data by utilizing Microsoft Graph, enabling more contextually precise information retrieval.

## What is an index?

Microsoft 365 Copilot enhances search with an advanced lexical and semantic understanding of your organization's data.

The semantic index is generated from content in [Microsoft Graph](https://developer.microsoft.com/graph). It's used to aid in the production of contextually relevant responses to user queries. It allows organizations to search through billions of vectors (mathematical representations of features or attributes) and return related results. Combined with enhancements across Microsoft Graph, the semantic index connects you with relevant information in your organization. It's built on Microsoft's comprehensive approach to security, compliance, and privacy, and respects all organizational boundaries within your tenant.

Interactions with data in Microsoft Graph are based on keyword matching, personalization, and social matching. Keyword search queries against an index in the Microsoft Graph, which maps to locations in documents or a set of documents. Access to tenant data in Microsoft Graph is gated by role-based access control.

## How semantic indexing helps manage your data

Semantic index enhances the Microsoft 365 Copilot experience in both Microsoft 365 Chat and in the Microsoft 365 apps. It supports an enhanced content grounding and conceptual understanding of your online data that is automatically enabled by Microsoft. It does this by creating vectorized indices. A vector is a numerical representation of a word, image pixel, or other data point. The vector is arranged or mapped with close numbers placed in proximity to one another to represent similarity.

In practical terms, Microsoft 365 services such as Microsoft 365 Copilot can:

- Understand relationships between different forms of words (for example, tech, technology, technologies; USA, U.S.A, United States; dog, cat, pet).
- Capture synonyms to expand the amount of searchable information, including the intent of sentences, snippets, documents, and meetings.
- Identify related assets to your query or sample content.

## Features

### Microsoft 365 Copilot with Microsoft Graph

Semantic indexing provides the grounding data for knowledge retrieval via Microsoft Copilot by understanding the intent of your query and appending additional information to your Microsoft Copilot prompt.

Relevant information is obtained in the Microsoft Graph and the semantic index to provide the large language model (LLM) with more information to reason over. As an example, suppose you want Microsoft Copilot to locate an email where a colleague praised the design work of a vendor. Semantic indexing includes nearby words (for example, elated, excited, amazed) into the search to broaden the search area and give the best result.

When a user attaches a SharePoint document library or folder, or provides its URL in a Copilot prompt, the grounding step includes that scope and uses the library's column metadata alongside file content to constrain and rank results.

### Scoped queries with SharePoint libraries and folders

When a user attaches a SharePoint document library or folder (or provides its URL) in Copilot, Copilot can use the library's column metadata as additional signals to refine grounding, improve contextual relevance, and increase answer accuracy. This metadata understanding applies to queries scoped to a specific library or folder and is available on the web experience.

## How semantic indexing works

Semantic indexing powers Microsoft 365 Copilot's search results by enabling a conceptual understanding of your online data to complement the lexical understanding we also have. Indexing is automatically enabled by Microsoft.

Today, a semantic index is created for every subscription at the tenant and user level. It's an organization-wide index generated from text-based SharePoint Online files. However, it only surfaces the results to a user if the user already has access to the content controlled by role-based access control. Additionally, the SharePoint Online site must remain searchable. For SharePoint items, associated column metadata can be incorporated as signals during retrieval when a query is scoped to a specific library or folder.

## Enablement

Every Microsoft 365 Copilot customer now has a tenant-level index. The indexing process requires no administrative involvement.

## Data flows

User prompts from Microsoft 365 apps are sent to Copilot (1), and Copilot accesses the Microsoft Graph and semantic index for processing (2). Copilot sends the modified prompt to the Large Language Model (3), receives the LLM response (4), and then accesses the Microsoft Graph and semantic index for post-processing (5). Copilot then sends the response and app command back to Microsoft 365 apps. All requests are encrypted by HTTPS and customer data remains encrypted at rest.

## Supported content types

Microsoft Graph grounded responses can utilize semantic understanding of user mailbox and file types listed in the following table. In addition to content from supported file types, SharePoint item metadata can be used as relevance signals when users scope Copilot queries to a specific document library or folder.

| Content/file type | User level | Tenant level |
| --- | --- | --- |
| User Mailbox | Supported | Not applicable |
| Delegated Mailbox | Not supported | Not applicable |
| Shared Mailbox | Not supported | Not applicable |
| Archived Mailbox Data | Not supported | Not applicable |
| Archived SharePoint Data | Not supported | Not supported |
| Word documents (doc/docx) | Supported | Supported |
| PowerPoint (pptx) | Supported | Supported |
| PDF files | Supported | Supported |
| Web pages (aspx) | Supported | Supported |
| OneNote files (one) | Supported | Supported |
| Copilot connector data | Not applicable | Supported |

> **Note**: Files up to 512 MB are now supported for PDF, PPTX, and DOCX extensions. This enhancement allows Copilot users to effectively analyze, summarize, and generate insights from these large files.

## Index updates

When Microsoft Graph data is indexed for a customer for the first time, documents created by users are indexed in near real-time in the user's mailbox. New documents that are added to SharePoint Online sites that are accessible, via site inheritance, by two or more users are indexed daily. When an indexed user and tenant level document is updated, the changes are immediately indexed.

## Administration

We provide administrators with optional activities to prepare and manage semantic indexing via the Microsoft 365 admin center. There's no administrative involvement required to enable semantic indexing, as the service is automatically enabled by Microsoft. Semantic indexing is an improvement to Microsoft 365 Search and can't be disabled.

Administrators can choose to exclude files from semantic indexing by reviewing the considerations for excluding data with Microsoft Purview Data Loss Prevention (DLP). If a DLP solution isn't present, administrators can exclude SharePoint Online sites from the tenant level index. To benefit from metadata-aware scoped queries, ensure that the relevant SharePoint site or library remains searchable (Search and offline availability is set to allow search) so that both content and column metadata are available to Copilot when users attach that library or folder.

## Excluding SharePoint Online Sites

To exclude a SharePoint Online site from semantic indexing:

1. Browse to the site with appropriate administrator permissions.
2. Select **Settings** then **Site information** from the drop-down menu.
3. Select **View all site settings** to bring up the Site Settings page.
4. Select **Search and offline availability** under the **Search** category and select **No** for **Allow this site to appear in search results** to exclude it from both Microsoft Search and the semantic index search.

Microsoft Search and semantic indexing support the exclusion of SharePoint online content from the tenant-level index only.

## Configuring item insights

On the Search and Intelligence page in the Microsoft 365 admin center, Item insights are enabled by default. Turning off people or item insights reduces the Microsoft Search and semantic index experience.

- **People insights** provide a list of relevant people to a user based on their public collaborative work in Microsoft 365.
- **Item insights** allow recommendations for people in your organization based on their collaborative work in Microsoft 365.

## Incorporating third party information

Using Copilot connectors, organizations can bring organizational data or content from external sources into Microsoft Graph. Once in Microsoft Graph, that content is indexed so that Copilot may access it - while maintaining access controls for content. This expands the types of content sources that are searchable in your Microsoft 365 productivity apps and the broader Microsoft ecosystem.

## Privacy, compliance, and security

The permissions model within your Microsoft 365 tenant can help ensure that data won't unintentionally leak between users, groups, and tenants. Microsoft 365 Copilot presents only data that each individual can access using the same underlying controls for data access used in other Microsoft 365 services. When data is indexed, we continue to honor the user identity-based access boundary so that the grounding process only accesses content that the current user is authorized to access.

## Storage and processing

Data generated by indexing remains within your company's tenant, and complies with your security, compliance, identity, and privacy policies and processes. Semantic indexing works only with content to which your users already have permission and doesn't affect storage quotas.

User-level index information is stored where the user's mailbox is located. Tenant-level index information, on the other hand, is stored in an isolated and protected customer's tenant container. This container is located in the region where the SharePoint site is located, which can be the Home region or another region specified by the tenant admin. For customers within the European Union Data Boundary (EUDB), the index is stored in an EU/EFTA based datacenter. Processing other customers can take place either in a tenant region or in the United States.

## Microsoft Purview Customer Key (BYOK) support

Microsoft provides bring your own key (BYOK) support for enterprises that have enabled BYOK in their environment. Microsoft automatically enables semantic indexing for BYOK enabled customers without any administrative involvement.

## Information protection

In the context of search, there are no other ways to exclude data from semantic indexing using information protection capabilities. Semantic indexing inherits security and privacy settings from Microsoft Search, and data brought in from third party connectors are provided the same storage and protections as other Microsoft 365 data.

## Data minimization

Data minimization reduces the amount of available data your organization might access. Retaining and deleting content is often needed for compliance and regulatory requirements, but deleting content that no longer has business value also helps you manage risk and liability. Microsoft Purview Data Lifecycle Management can be used to delete content that is no longer needed.

## Reduce oversharing

It's important to note that indexing data doesn't change access permissions to content. For example, sharing content with a link that works with everyone in my organization doesn't make the information part of the tenant level index. Only users that select a link that they have access to will have the information added to their user index.

Recommendations:

- **Plan secure file collaboration**.
- **Right size user access to data to reduce the list** — reduce oversharing by inheriting exclusion lists for SharePoint Online sites and performing access control checks in real time.
- **Use sensitivity labels** — apply Microsoft Purview Information Protection sensitivity labels.
- **Limit access** — Microsoft Purview Data Loss Prevention is available in Microsoft 365 E5 and can be used to retroactively and temporarily limit access to documents that have been reported as overshared.
