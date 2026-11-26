---
status: test
disclosureId: disclosure-01
sourcePublication: US 6,285,999 B1
sourceTitle: Method for node ranking in a linked database
sourceGranted: 2001-09-04
sourceRecord: public (granted US patent)
inventionType: software / computer-implemented
attorneyReviewed: false
---

# Test disclosure 01: ranking documents by the links that point to them

> Test content for Phase 0 (spec FR-09). This is a short invention summary written for testing from the public record of the patent named above. It is not the inventors' text and does not reproduce the patent's claims. Draft from this disclosure only; do not look up or recall the published claims.

## Problem

A search over a large hyperlinked collection (for example the web) can return thousands of documents that all contain the query words. Text matching alone cannot say which of them matter most. Counting the links that point to a document helps a little, but every link counts the same, so a page can be pushed up by many links from pages nobody values.

## The idea

Treat each link as a vote of importance, and weigh each vote by the importance of the page that casts it. A document's rank is built from the ranks of the documents that link to it. Each linking document shares its own rank among all the documents it links to, so a page with many outgoing links passes less to each of them.

Because every rank depends on other ranks, the ranks are computed together, by iteration:
1. Give every document a starting rank (for example, equal ranks).
2. For each document, set its new rank from the shares received from the documents that link to it.
3. Repeat, either until the ranks settle or for a fixed number of rounds; an approximation after a few rounds is already useful.

A damping factor is applied at each step: a fixed part of each document's rank does not come from links but is spread across all documents (or across a chosen set). One way to picture this is a reader who follows links at random and now and then jumps to a random page instead; the rank of a document is the long-run chance of finding the reader there. The damping term also keeps rank from getting trapped in groups of pages that link only to each other.

## How it is used

- A crawler collects the documents and the links between them. The link structure is stored as a graph: documents are nodes, links are directed edges.
- The ranks are computed ahead of time over the whole collection and stored with each document.
- At query time, documents that match the query are found by ordinary text matching, and the stored rank is used, alone or combined with a text-match score, to order the results shown to the user.

## Variations described

- Documents with no outgoing links ("dangling" documents) can be set aside during the iteration and given a rank afterwards.
- The random jump can be biased toward a chosen set of documents (for example a user's bookmarks or a topic's trusted pages), which gives a personalised or topic-specific ranking.
- The same ranking applies to any linked database, not only the web: for example a database of academic articles, patents or court cases that cite each other.

## What the inventors consider new

Ranking a node by the ranks of the nodes linking to it, each normalised by that linking node's number of outgoing links, with a damping term, computed iteratively over the whole linked database and used to order search results.
