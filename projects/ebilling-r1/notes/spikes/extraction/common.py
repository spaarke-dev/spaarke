"""Shared helpers for the task 005 extraction spike (ebilling-r1).

Spike code only (D-53): calls Azure services directly over REST with Entra tokens from
`az account get-access-token`; never through the BFF; never reads keys.
ADR-015: logs and committed outputs carry identifiers, sizes, timings and outcome codes.
Full raw service responses go to the git-ignored evaluation-set/local/spike-005/ folder.
"""
from __future__ import annotations

import json
import os
import re
import subprocess
import time
import urllib.error
import urllib.request
from datetime import date, datetime
from pathlib import Path

HERE = Path(__file__).resolve().parent
PROJECT = HERE.parents[2]                      # projects/ebilling-r1
EVAL = PROJECT / "notes" / "evaluation-set"
LOCAL = EVAL / "local"
LOCAL_RAW = LOCAL / "spike-005"                # git-ignored (local/.gitignore = "*")
COMMITTED_RAW = HERE / "raw"                   # synthetic items only, stripped of document text
RUNS = HERE / "runs"                           # per-arm timing / usage / outcome records (no text)
RESULTS = HERE / "results"

CU_ENDPOINT = "https://spaarke-docintel-invoices-dev.cognitiveservices.azure.com"
OPENAI_ENDPOINT = "https://spaarke-openai-dev.openai.azure.com"
GA = "2025-11-01"
PREVIEW = "2026-06-01-preview"                 # spike only (D-53)
DI_API = "2024-11-30"
AOAI_API = "2025-04-01-preview"

_token: dict[str, tuple[str, float]] = {}


def token(resource: str = "https://cognitiveservices.azure.com") -> str:
    cached = _token.get(resource)
    if cached and cached[1] > time.time() + 300:
        return cached[0]
    out = subprocess.run(
        ["az", "account", "get-access-token", "--resource", resource, "--query",
         "[accessToken,expires_on]", "-o", "tsv"],
        capture_output=True, text=True, check=True, shell=(os.name == "nt"))
    tok, exp = out.stdout.split()
    _token[resource] = (tok, float(exp))
    return tok


def http(method: str, url: str, body: bytes | dict | None = None, content_type: str = "application/json",
         headers: dict | None = None, timeout: int = 600, retries: int = 4):
    """Returns (status, headers, parsed-json-or-bytes). Retries 429/5xx with backoff."""
    data = body
    if isinstance(body, (dict, list)):
        data = json.dumps(body).encode("utf-8")
    for attempt in range(retries + 1):
        req = urllib.request.Request(url, data=data, method=method)
        req.add_header("Authorization", f"Bearer {token()}")
        if data is not None:
            req.add_header("Content-Type", content_type)
        for k, v in (headers or {}).items():
            req.add_header(k, v)
        try:
            with urllib.request.urlopen(req, timeout=timeout) as resp:
                raw = resp.read()
                hdrs = dict(resp.headers)
                status = resp.status
        except urllib.error.HTTPError as e:
            raw = e.read()
            hdrs = dict(e.headers or {})
            status = e.code
        except (urllib.error.URLError, TimeoutError, ConnectionError) as e:  # transient network
            if attempt < retries:
                time.sleep(5 * (attempt + 1))
                continue
            return 599, {}, {"error": {"code": "network", "message": type(e).__name__}}
        if status in (429, 500, 502, 503, 504) and attempt < retries:
            wait = float(hdrs.get("Retry-After") or hdrs.get("retry-after") or 5 * (attempt + 1))
            time.sleep(min(wait, 60))
            continue
        try:
            parsed = json.loads(raw) if raw else None
        except ValueError:
            parsed = raw
        return status, hdrs, parsed
    return status, hdrs, parsed


# ---------------------------------------------------------------- items

def load_gt(item_id: str) -> dict:
    for sub in ("synthetic-invoices", "fee-applications", "real-invoices"):
        p = EVAL / "ground-truth" / sub / f"{item_id}.json"
        if p.exists():
            return json.loads(p.read_text(encoding="utf-8"))
    raise FileNotFoundError(item_id)


def load_full_gt(item_id: str) -> dict:
    """Full label (names / narratives as billed) for real items; the committed label for synthetic."""
    p = LOCAL / "ground-truth-full" / f"{item_id}.json"
    return json.loads(p.read_text(encoding="utf-8")) if p.exists() else load_gt(item_id)


def pii_cleared(gt: dict) -> bool:
    """Manifest PII rule used by this spike (ADR-015): synthetic items are cleared; a real item is
    cleared when its pii_scan disposition is a reasoned clearance, not 'REVIEW: ...'."""
    meta = gt["meta"]
    if meta.get("licence_status") == "synthetic":
        return True
    disp = ((meta.get("pii_scan") or {}).get("disposition") or "").strip()
    return bool(disp) and not disp.upper().startswith("REVIEW")


def is_local_only(gt: dict) -> bool:
    return gt["meta"]["storage"]["location"] == "local-only"


def pdf_path(gt: dict) -> Path:
    if gt["set"] == "B":
        return LOCAL / "synthetic" / f"{gt['id']}.pdf"
    return EVAL / gt["meta"]["storage"]["path"]


def spike_items(include_uncleared: bool = False) -> list[dict]:
    out = []
    for sub in ("fee-applications", "synthetic-invoices", "real-invoices"):
        for p in sorted((EVAL / "ground-truth" / sub).glob("*.json")):
            gt = json.loads(p.read_text(encoding="utf-8"))
            if gt["meta"].get("spike") and (include_uncleared or pii_cleared(gt)):
                out.append(gt)
    return out


# ---------------------------------------------------------------- persistence

def save_raw(arm: str, gt: dict, payload, stripped=None) -> None:
    p = LOCAL_RAW / arm / f"{gt['id']}.json"
    p.parent.mkdir(parents=True, exist_ok=True)
    p.write_text(json.dumps(payload, ensure_ascii=False), encoding="utf-8")
    if stripped is not None and not is_local_only(gt):
        q = COMMITTED_RAW / arm / f"{gt['id']}.json"
        q.parent.mkdir(parents=True, exist_ok=True)
        q.write_text(json.dumps(stripped, ensure_ascii=False, indent=None, separators=(",", ":")), encoding="utf-8")


def load_raw(arm: str, item_id: str):
    p = LOCAL_RAW / arm / f"{item_id}.json"
    return json.loads(p.read_text(encoding="utf-8")) if p.exists() else None


def append_run(arm: str, record: dict) -> None:
    RUNS.mkdir(parents=True, exist_ok=True)
    with (RUNS / f"{arm}.jsonl").open("a", encoding="utf-8") as f:
        f.write(json.dumps(record) + "\n")


def read_runs(arm: str) -> list[dict]:
    p = RUNS / f"{arm}.jsonl"
    if not p.exists():
        return []
    recs = {}
    for line in p.read_text(encoding="utf-8").splitlines():
        if line.strip():
            r = json.loads(line)
            recs[r["id"]] = r            # last record per item wins (re-runs of failures)
    return list(recs.values())


def utcnow() -> str:
    return datetime.utcnow().strftime("%Y-%m-%dT%H:%M:%SZ")
