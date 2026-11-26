---
status: test
disclosureId: disclosure-04
sourcePublication: US 8,697,359 B1
sourceTitle: CRISPR-Cas systems and methods for altering expression of gene products
sourceGranted: 2014-04-15
sourceRecord: public (granted US patent)
inventionType: biotechnology composition and method (not software)
attorneyReviewed: false
---

# Test disclosure 04: an engineered CRISPR-Cas9 system that edits genes in eukaryotic cells

> Test content for Phase 0 (spec FR-09). This is a short invention summary written for testing from the public record of the patent named above. It is not the inventor's text and does not reproduce the patent's claims. Draft from this disclosure only; do not look up or recall the published claims.

## Problem

Bacteria use CRISPR loci and CRISPR-associated (Cas) proteins as an immune system that cuts invading DNA. Earlier work showed that a type II system cuts DNA in bacteria and in a test tube, guided by RNA. Whether it could be engineered to find and cut a chosen gene inside the nucleus of a eukaryotic cell (for example a human or mouse cell) was not known: the protein has to be made in the cell, reach the nucleus, and find the target in packed chromosomal DNA.

## The idea

Build a non-naturally occurring CRISPR-Cas system and deliver it to a eukaryotic cell on one or more expression vectors:
- **Guide RNA:** a guide sequence designed to pair with a chosen target sequence in the cell's genome, joined to a tracr sequence; the two can be expressed as one chimeric RNA. The target must sit next to a short protospacer-adjacent motif (PAM) that the Cas protein recognises.
- **Cas9:** a type II Cas9 protein (for example from *Streptococcus pyogenes*), with its gene codon-optimised for expression in eukaryotic cells and joined to one or more nuclear localisation signals so the protein enters the nucleus.
- **Regulatory elements:** a promoter suited to short RNAs (for example an RNA polymerase III promoter such as U6) driving the guide, and a promoter suited to proteins (an RNA polymerase II promoter) driving Cas9. The two can be on the same vector or on separate vectors.

In the cell, the guide RNA directs Cas9 to the target, Cas9 cuts the DNA there, and the cell's repair of the cut disrupts or changes the gene. The expression of the targeted gene product is altered, for example decreased.

## Variations described

- Several guide sequences on one vector, so several genes are targeted at once (multiplexing).
- Cas9 variants with one cutting domain disabled, which nick one strand instead of cutting both.
- Supplying a repair template with the system so a chosen change is written into the target by homologous recombination.
- Host cells, cell lines and organisms containing the system, and kits holding the vectors.
- Uses: making model cells that carry a disease-associated mutation, and screening compounds against them.

## What the inventor considers new

A non-naturally occurring CRISPR-Cas system, on one or more vectors, with a guide RNA and a codon-optimised type II Cas9 carrying nuclear localisation signals, that cuts a chosen target in the genome of a eukaryotic cell and so alters the expression of a gene product; and methods of altering gene expression in a eukaryotic cell with it.
