---
name: defender-storage-overwrite-rescan-2026-11-05
description: Defender for Storage on-upload scan on blob overwrite - documented triggers, undocumented tag/dedup/cap-result behaviour, on-demand single-blob rescan alternative
metadata:
  type: reference
---

## 2026-11-05: Defender for Storage overwrite -> rescan?
**Question**: Does rewriting identical bytes (Put Blob / Put Block List, If-Match) make Defender on-upload scanning write a fresh scan result with scan time >= new Last-Modified?
**Findings**: Documented YES for the trigger: scans fire on any BlobCreated/BlobRenamed, which Learn says includes "overwriting existing blobs" and PutBlockList; Event Grid raises BlobCreated for PutBlob/PutBlockList/CopyBlob only (not Set Metadata/Tags, not Put Block alone); Learn notes each commit can rescan the same data (no hash dedup documented). UNDOCUMENTED: whether overwrite clears old index tags, whether Defender replaces/merges tags, scan-time tag key, numeric latency, result when monthly cap (default 10,000 GB) hit. Limits: 50 GB/blob, 50 GB/min/account (excess queued, may go unscanned), filters/caps -> no scan result at all. Documented hazard: metadata updated soon after upload can make the scan fail, so set metadata in the same Put. Documented rescan alternative: on-demand single-blob scan (REST startMalwareScan 2024-10-01-preview, one at a time/account, no cap).
**Sources**: knowledge/defender-for-storage-malware-scan/README.md + SOURCE.md (full list)
**Open questions**: live probe needed on scanning-enabled account (tags after overwrite, tag replace vs merge, latency, cap-hit tag).
