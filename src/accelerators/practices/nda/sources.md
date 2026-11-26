# NDA content sources (design §8.5)

Checked 2026-10-31. MVP status: no third-party text is copied into this accelerator; positions are
written in our own words. Pin and hash a source here before adapting any of its text (provenance
record fields from OpenAgreements: `source_url`, `license`, `allow_derivatives`, `attribution_text`,
`source_sha256`).

| Used for | Source | Licence | How used | Pinned |
|---|---|---|---|---|
| Baseline structure (mutual) | Common Paper Mutual NDA **v1.0** (cover page + standard terms), https://commonpaper.com/standards/mutual-nda/1.0/mutual-nda-cover-page-and-standard-terms-docx | CC BY 4.0 | Structure and coverage of `positions/baseline-mutual.md`; wording ours. Also a tenant test document (not stored in the repo) | Fetched 2026-11-01; .docx 28,664 bytes; SHA-256 `18E07001C3EBFC749833E10836EDA4FE0D302AB0A7519A0388696BE3DF86B566` |
| Sample customer playbook | Spaarke POC session 2026-10-30 (`notes/poc-2026-10-30-cowork-nda-review.md` App. B) | Spaarke | `samples/acme-nda-disclosing.md` | n/a |
| Eval topic seeds | ContractNLI hypotheses (Koreeda & Manning, 2021) | CC BY 4.0 | `contractnli` field in `topics.yaml` (names only) | Pending: check names against the dataset release |
| Synthetic corpus | Authored by Spaarke | Spaarke | `evals/corpus/` via `build_corpus.py` | n/a |

**Not used (licence):** oneNDA (CC BY-ND: benchmark only, no derivatives); see design §8.5 "do not use".

**Attribution text (when Common Paper text is adapted):** "Based on the Common Paper Mutual
Non-Disclosure Agreement, available at commonpaper.com, licensed under CC BY 4.0."
