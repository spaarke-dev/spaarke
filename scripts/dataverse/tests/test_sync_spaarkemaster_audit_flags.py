"""Tests for scripts/dataverse/Sync-SpaarkeMasterAuditFlags.py (D-206).

Synthetic Entity.xml fixtures in a temp folder; no Dataverse access.
Run: python -m unittest discover -s scripts/dataverse/tests
"""
import importlib.util
import json
import tempfile
import unittest
from pathlib import Path

_SCRIPT = Path(__file__).resolve().parents[1] / "Sync-SpaarkeMasterAuditFlags.py"
_spec = importlib.util.spec_from_file_location("sync_audit_flags", _SCRIPT)
sync = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(sync)


def _attr(name: str, audit: int) -> str:
    return (
        f'        <attribute PhysicalName="{name}">\r\n'
        f"          <Type>nvarchar</Type>\r\n"
        f"          <LogicalName>{name}</LogicalName>\r\n"
        f"          <IsAuditEnabled>{audit}</IsAuditEnabled>\r\n"
        f"        </attribute>\r\n"
    )


def _entity_xml(name: str, entity_audit: int, attrs: dict) -> bytes:
    body = "".join(_attr(n, a) for n, a in attrs.items())
    text = (
        '<?xml version="1.0" encoding="utf-8"?>\r\n'
        "<Entity>\r\n"
        f'  <Name LocalizedName="X" OriginalName="X">{name}</Name>\r\n'
        "  <EntityInfo>\r\n"
        f'    <entity Name="{name}">\r\n'
        f"      <attributes>\r\n{body}      </attributes>\r\n"
        f"      <IsAuditEnabled>{entity_audit}</IsAuditEnabled>\r\n"
        "    </entity>\r\n"
        "  </EntityInfo>\r\n"
        "</Entity>"
    )
    return b"\xef\xbb\xbf" + text.encode("utf-8")


def _write_solution(tmp_path: Path, entities: dict) -> Path:
    root = tmp_path / "Sol"
    for name, data in entities.items():
        d = root / "Entities" / name
        d.mkdir(parents=True)
        (d / "Entity.xml").write_bytes(data)
    return root


def _state(tmp_path: Path, state: dict) -> Path:
    p = tmp_path / "state.json"
    p.write_text(json.dumps(state), encoding="utf-8")
    return p


def _table(audit: bool, cols: dict, intersect: bool = False) -> dict:
    return {
        "tableAudit": audit,
        "intersect": intersect,
        "columns": {c: {"audit": a, "changeable": True} for c, a in cols.items()},
    }


class SyncAuditFlagsTests(unittest.TestCase):
    def setUp(self):
        self._td = tempfile.TemporaryDirectory()
        self.addCleanup(self._td.cleanup)
        self.tmp = Path(self._td.name)

    def test_flips_only_zero_to_one_and_preserves_bytes(self):
        tmp_path = self.tmp
        original = _entity_xml("sprk_Thing", 0, {"sprk_a": 0, "sprk_b": 0, "sprk_c": 1})
        root = _write_solution(tmp_path, {"sprk_Thing": original})
        # dev: table on, a on, b off, c off (c is already on in source: must stay on)
        state = _state(tmp_path, {"sprk_thing": _table(True, {"sprk_a": True, "sprk_b": False, "sprk_c": False})})

        code = sync.main(["--state", str(state), "--solution-root", str(root)])

        self.assertEqual(code, 0)
        new = (root / "Entities" / "sprk_Thing" / "Entity.xml").read_bytes()
        assert len(new) == len(original)
        changed = [(o, n) for o, n in zip(original, new) if o != n]
        assert changed == [(ord("0"), ord("1"))] * 2  # entity level + sprk_a only
        assert new.startswith(b"\xef\xbb\xbf") and new.count(b"\n") == new.count(b"\r\n")
        assert b"<LogicalName>sprk_b</LogicalName>\r\n          <IsAuditEnabled>0<" in new
        assert b"<LogicalName>sprk_c</LogicalName>\r\n          <IsAuditEnabled>1<" in new


    def test_never_touches_excluded_intersect_or_non_sprk(self):
        tmp_path = self.tmp
        ents = {n: _entity_xml(n, 0, {"sprk_a": 0}) for n in ("sprk_AIChatMessage", "sprk_AIChatSummary", "sprk_Link", "contact")}
        root = _write_solution(tmp_path, ents)
        state = _state(
            tmp_path,
            {
                "sprk_aichatmessage": _table(True, {"sprk_a": True}),
                "sprk_aichatsummary": _table(True, {"sprk_a": True}),
                "sprk_link": _table(True, {"sprk_a": True}, intersect=True),
                "contact": _table(True, {"sprk_a": True}),
            },
        )

        sync.main(["--state", str(state), "--solution-root", str(root)])

        for n, data in ents.items():
            assert (root / "Entities" / n / "Entity.xml").read_bytes() == data, n


    def test_check_mode_fails_then_passes_after_apply_and_writes_nothing(self):
        tmp_path = self.tmp
        original = _entity_xml("sprk_Thing", 1, {"sprk_a": 0})
        root = _write_solution(tmp_path, {"sprk_Thing": original})
        state = _state(tmp_path, {"sprk_thing": _table(True, {"sprk_a": True})})
        path = root / "Entities" / "sprk_Thing" / "Entity.xml"

        assert sync.main(["--state", str(state), "--solution-root", str(root), "--check"]) == 1
        assert path.read_bytes() == original  # --check never writes
        assert sync.main(["--state", str(state), "--solution-root", str(root)]) == 0
        assert sync.main(["--state", str(state), "--solution-root", str(root), "--check"]) == 0


    def test_source_on_dev_off_is_reported_not_changed(self):
        tmp_path = self.tmp
        original = _entity_xml("sprk_Thing", 1, {"sprk_a": 1})
        root = _write_solution(tmp_path, {"sprk_Thing": original})
        state = _state(tmp_path, {"sprk_thing": _table(False, {"sprk_a": False})})
        path = root / "Entities" / "sprk_Thing" / "Entity.xml"

        assert sync.main(["--state", str(state), "--solution-root", str(root), "--check"]) == 0
        assert sync.main(["--state", str(state), "--solution-root", str(root), "--check", "--strict"]) == 1
        sync.main(["--state", str(state), "--solution-root", str(root)])
        assert path.read_bytes() == original


    def test_unexpected_shape_errors_instead_of_guessing(self):
        tmp_path = self.tmp
        bad = _entity_xml("sprk_Thing", 0, {"sprk_a": 0}).replace(b"<IsAuditEnabled>0</IsAuditEnabled>\r\n        </attribute>", b"</attribute>")  # attribute without the node
        root = _write_solution(tmp_path, {"sprk_Thing": bad})
        state = _state(tmp_path, {"sprk_thing": _table(True, {"sprk_a": True})})
        assert sync.main(["--state", str(state), "--solution-root", str(root)]) == 2


if __name__ == "__main__":
    unittest.main()
