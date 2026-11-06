#!/usr/bin/env python
"""Sync Dataverse audit flags from a dev audit-state file into solution Entity.xml source.

Owner decision D-206 (projects/spaarke-ontology-platform-r1/notes/decisions.md): every
Spaarke table and substantive column is audited. Dev carries that state; this script makes
the solution source carry it too, so a solution import never turns auditing back off and a
new environment gets the same settings.

State file (read-only input, treated as data):
    { tableLogicalName: { "tableAudit": bool, "intersect": bool,
                          "columns": { columnLogicalName: { "audit": bool, "changeable": bool } } } }

Rules (the script is one-directional and byte-preserving):
  * Only ever rewrites <IsAuditEnabled>0</IsAuditEnabled> to 1. A value is never turned off.
  * Entity level (child of <entity>): set to 1 where the state says tableAudit is true.
  * Attribute level (child of <attribute>, matched by <LogicalName>): set to 1 where the state
    column says audit is true.
  * Never touches the excluded tables (EXCLUDED_TABLES: deliberately off), intersect (N:N)
    tables, or tables whose name does not start with sprk_.
  * Everything else in the file (BOM, CRLF line endings, indentation, encoding) is kept
    byte-for-byte; only the single digit changes.

Usage:
    python scripts/dataverse/Sync-SpaarkeMasterAuditFlags.py --state audit-state.json            # apply
    python scripts/dataverse/Sync-SpaarkeMasterAuditFlags.py --state audit-state.json --check    # exit 1 if source disagrees
    python scripts/dataverse/Sync-SpaarkeMasterAuditFlags.py --state audit-state.json --report out.json

--check exits 1 when the state says audited and the source says 0 (what apply would change).
Source flags that are 1 where the state says 0 are listed (they are never changed) but do not
fail --check; add --strict to make them fail too.

No Dataverse access: this reads a local JSON file and edits local XML only.
"""
from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

# Deliberately not audited (D-206): Assistant conversations carry no invoice or decision data.
EXCLUDED_TABLES = frozenset({"sprk_aichatmessage", "sprk_aichatsummary"})

REPO_ROOT = Path(__file__).resolve().parents[2]
DEFAULT_SOLUTIONS_ROOT = REPO_ROOT / "src" / "dataverse" / "solutions"

ATTRIBUTE_RE = re.compile(rb"<attribute\b.*?</attribute>", re.S)
AUDIT_RE = re.compile(rb"(<IsAuditEnabled>)([01])(</IsAuditEnabled>)")
LOGICAL_NAME_RE = re.compile(rb"<LogicalName>([^<]*)</LogicalName>")
ENTITY_NAME_RE = re.compile(rb"<Name\b[^>]*>([^<]*)</Name>")


class SourceFormatError(Exception):
    """The XML does not have the shape this script relies on."""


def _flip(match: "re.Match[bytes]") -> bytes:
    return match.group(1) + b"1" + match.group(3)


def process_entity(data: bytes, table_state: dict) -> tuple[bytes, dict]:
    """Return (new bytes, stats) for one Entity.xml. Never turns a flag off."""
    stats = {
        "entity_flipped": False,
        "entity_node": False,
        "entity_source_on_dev_off": False,
        "attr_flipped": [],
        "attr_source_on_dev_off": [],
        "attr_not_in_state": [],
        "state_cols_without_node": [],
    }
    columns = {name.lower(): col for name, col in table_state.get("columns", {}).items()}
    seen_columns: set[str] = set()

    out: list[bytes] = []
    pos = 0
    pieces: list[tuple[str, bytes]] = []  # ("outside"|"attr", bytes)
    for m in ATTRIBUTE_RE.finditer(data):
        pieces.append(("outside", data[pos : m.start()]))
        block = m.group(0)
        names = LOGICAL_NAME_RE.findall(block)
        audits = AUDIT_RE.findall(block)
        if len(names) != 1 or len(audits) != 1:
            raise SourceFormatError(
                f"attribute block has {len(names)} LogicalName and {len(audits)} IsAuditEnabled (expected 1 each)"
            )
        name = names[0].decode("utf-8").lower()
        seen_columns.add(name)
        col = columns.get(name)
        if col is None:
            stats["attr_not_in_state"].append(name)
        else:
            current = audits[0][1] == b"1"
            if col.get("audit") and not current:
                block = AUDIT_RE.sub(_flip, block, count=1)
                stats["attr_flipped"].append(name)
            elif current and not col.get("audit"):
                stats["attr_source_on_dev_off"].append(name)
        pieces.append(("attr", block))
        pos = m.end()
    pieces.append(("outside", data[pos:]))

    entity_hits = 0
    for kind, chunk in pieces:
        if kind == "outside":
            hits = AUDIT_RE.findall(chunk)
            entity_hits += len(hits)
            if len(hits) > 1:
                raise SourceFormatError(f"{len(hits)} entity-level IsAuditEnabled nodes (expected at most 1)")
            if hits:
                stats["entity_node"] = True
                current = hits[0][1] == b"1"
                if table_state.get("tableAudit") and not current:
                    chunk = AUDIT_RE.sub(_flip, chunk, count=1)
                    stats["entity_flipped"] = True
                elif current and not table_state.get("tableAudit"):
                    stats["entity_source_on_dev_off"] = True
        out.append(chunk)
    if entity_hits > 1:
        raise SourceFormatError("more than one entity-level IsAuditEnabled node")

    stats["state_cols_without_node"] = sorted(c for c in columns if c not in seen_columns)
    return b"".join(out), stats


def entity_logical_name(data: bytes, folder_name: str) -> str:
    m = ENTITY_NAME_RE.search(data)
    from_xml = m.group(1).decode("utf-8").lower() if m else folder_name.lower()
    if from_xml != folder_name.lower():
        raise SourceFormatError(f"folder {folder_name} but <Name> says {from_xml}")
    return from_xml


def run(state_path: Path, solution_roots: list[Path], check: bool, strict: bool) -> tuple[int, dict]:
    state = json.loads(state_path.read_text(encoding="utf-8-sig"))
    state = {k.lower(): v for k, v in state.items()}

    report: dict = {
        "solutions": {},
        "tables_in_state_without_folder": [],
        "excluded_skipped": [],
        "intersect_skipped": [],
        "non_sprk_skipped": [],
        "entity_flips": 0,
        "attribute_flips": 0,
        "changed_files": [],
        "attrs_in_state_without_node": {},
        "attrs_in_source_not_in_state": {},
        "source_on_dev_off": {},
        "entities_without_entity_level_node": [],
        "tables_changed_off_state": [],
    }
    found_tables: set[str] = set()
    missing_by_table: dict[str, set[str]] = {}
    extra_by_table: dict[str, set[str]] = {}
    on_dev_off_by_table: dict[str, set[str]] = {}
    disagreements = 0

    for root in solution_roots:
        files = sorted(root.glob("Entities/*/Entity.xml"))
        report["solutions"][root.name] = len(files)
        for path in files:
            data = path.read_bytes()
            name = entity_logical_name(data, path.parent.name)
            if not name.startswith("sprk_"):
                report["non_sprk_skipped"].append(name)
                continue
            if name in EXCLUDED_TABLES:
                report["excluded_skipped"].append(name)
                found_tables.add(name)
                continue
            table = state.get(name)
            if table is None:
                report.setdefault("sprk_in_source_not_in_state", []).append(name)
                continue
            found_tables.add(name)
            if table.get("intersect"):
                report["intersect_skipped"].append(name)
                continue
            new, st = process_entity(data, table)
            if not st["entity_node"]:
                report["entities_without_entity_level_node"].append(name)
            if st["entity_flipped"]:
                report["entity_flips"] += 1
            report["attribute_flips"] += len(st["attr_flipped"])
            # A table can have an Entity.xml in more than one solution folder (sprk_Document is in SpaarkeMaster and
            # spaarke_containers), so these per-table findings MERGE across folders instead of the last folder read
            # overwriting the earlier ones (#2584): a state column lacks a node only if NO folder has one
            # (intersection); a source-only column and a source-on/dev-off flag count if ANY folder has it (union).
            missing = set(st["state_cols_without_node"])
            missing_by_table[name] = missing_by_table[name] & missing if name in missing_by_table else missing
            extra_by_table.setdefault(name, set()).update(st["attr_not_in_state"])
            on_dev_off_by_table.setdefault(name, set()).update(
                list(st["attr_source_on_dev_off"]) + (["<entity>"] if st["entity_source_on_dev_off"] else [])
            )
            if st["entity_flipped"] or st["attr_flipped"]:
                disagreements += (1 if st["entity_flipped"] else 0) + len(st["attr_flipped"])
                report["changed_files"].append(str(path.relative_to(REPO_ROOT)) if path.is_relative_to(REPO_ROOT) else str(path))
                if not check:
                    if new == data:
                        raise SourceFormatError(f"{path}: flips counted but bytes unchanged")
                    path.write_bytes(new)

    report["attrs_in_state_without_node"] = {t: sorted(c) for t, c in sorted(missing_by_table.items()) if c}
    report["attrs_in_source_not_in_state"] = {t: sorted(c) for t, c in sorted(extra_by_table.items()) if c}
    report["source_on_dev_off"] = {t: sorted(c) for t, c in sorted(on_dev_off_by_table.items()) if c}
    report["tables_in_state_without_folder"] = sorted(
        t for t, v in state.items() if t not in found_tables and not v.get("intersect") and t not in EXCLUDED_TABLES
    )
    report["intersect_in_state"] = sorted(t for t, v in state.items() if v.get("intersect"))
    report["excluded_in_state"] = sorted(t for t in EXCLUDED_TABLES if t in state)

    code = 0
    if check:
        if disagreements:
            code = 1
        if strict and report["source_on_dev_off"]:
            code = 1
    return code, report


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("--state", required=True, type=Path, help="dev audit-state JSON file")
    ap.add_argument(
        "--solution-root",
        action="append",
        type=Path,
        help="solution folder containing Entities/ (repeatable; default: every folder under src/dataverse/solutions)",
    )
    ap.add_argument("--check", action="store_true", help="do not write; exit 1 if the source disagrees with the state")
    ap.add_argument("--strict", action="store_true", help="with --check, also fail on source-on / dev-off flags")
    ap.add_argument("--report", type=Path, help="write the full report as JSON to this path")
    args = ap.parse_args(argv)

    roots = args.solution_root or sorted(p for p in DEFAULT_SOLUTIONS_ROOT.iterdir() if (p / "Entities").is_dir())
    try:
        code, report = run(args.state, roots, args.check, args.strict)
    except SourceFormatError as e:
        print(f"ERROR: {e}", file=sys.stderr)
        return 2

    if args.report:
        args.report.write_text(json.dumps(report, indent=2, sort_keys=True), encoding="utf-8")
    verb = "would flip" if args.check else "flipped"
    print(f"{verb}: {report['entity_flips']} entity-level, {report['attribute_flips']} attribute-level "
          f"in {len(report['changed_files'])} files")
    print(f"tables in state with no Entity.xml folder: {len(report['tables_in_state_without_folder'])}")
    print(f"source on / dev off (left alone): {sum(len(v) for v in report['source_on_dev_off'].values())}")
    if args.check:
        print("CHECK FAILED: source disagrees with state" if code else "CHECK OK")
    return code


if __name__ == "__main__":
    sys.exit(main())
