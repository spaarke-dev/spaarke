"""Golden parity (FR-01 a): the job's engine runs on the synthetic NDAs equal a direct run of the same engines.

Three comparisons, all on the accelerator's synthetic corpus and golden plan (run_local_e2e.py's CASES; NFR-10):
  1. In this interpreter: the job's runner (entrypoint.engines) against the engines' CLIs called directly, exactly as
     practices/nda/evals/run_local_e2e.py calls them. Proves the wrapper changes nothing.
  2. `check_outputs` passes on every redline output.
  3. Across hosts: this run against a reference manifest (INTAKE_PARITY_REFERENCE_MANIFEST) written by a run on another
     host. CI writes it on the runner and then runs this file INSIDE the built image
     (.github/workflows/build-intake-engine-image.yml), so "container output == local run" is tested there.

DEFINITION OF OUTPUT IDENTITY - the one switch, DOCX_IDENTITY:
  "zip_parts" (now)  For a .docx: the same set of part names, and for every part the same bytes and the same CRC-32.
                     Zip entry timestamps are NOT compared: on master the engines stamp every entry with the wall-clock
                     local time (zipfile.writestr with a str name), so two runs a second apart differ there and nowhere
                     else (notes/001-engine-container.md, escalation 2). Nothing is rewritten: the outputs are read as
                     written.
  "sha256"           Whole-file byte identity. Switch to it once the accelerator's reproducible-zip change (fixed
                     1980-01-01 entry dates, commit 2759ef3a7d) is on master.
  JSON outputs (clauses.json, check-report.json) are always compared whole-file.

  Write a manifest:  python tests/test_golden_parity.py --write-manifest PATH
"""
import binascii
import hashlib
import importlib.util
import json
import os
import subprocess
import sys
import tempfile
import time
import unittest
import zipfile

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from entrypoint import engines  # noqa: E402
from tests import fakes  # noqa: E402

DOCX_IDENTITY = "zip_parts"  # -> "sha256" once fixed zip entry dates are on master (see the module docstring)

REFERENCE_ENV = "INTAKE_PARITY_REFERENCE_MANIFEST"
REQUIRE_REFERENCE_ENV = "INTAKE_PARITY_REQUIRE_REFERENCE"


def _load_e2e():
    spec = importlib.util.spec_from_file_location("run_local_e2e", fakes.practice_path("evals", "run_local_e2e.py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)  # defines CASES and INJECTION_EDIT; main() runs only as a script
    return module


E2E = _load_e2e()


def case_plan(name, extra):
    """The plan run_local_e2e.py builds for a case: the golden plan, the file name, plus the case's extra edit."""
    with open(fakes.GOLDEN_PLAN, encoding="utf-8") as f:
        plan = json.load(f)
    plan["agreement"]["file_name"] = name
    if extra:
        plan["edits"].append(extra)
    return plan


def _settings():
    return engines.EngineSettings(engines_dir=fakes.ENGINES_DIR, summary_intro=fakes.SUMMARY_INTRO)


def run_job(out_root):
    """The job's runner on every case. Returns {case stem: {output name: path}}."""
    results = {}
    for name, extra in E2E.CASES:
        stem = name[:-5]
        work = os.path.join(out_root, stem)
        os.makedirs(os.path.join(work, "segment"))
        os.makedirs(os.path.join(work, "redline"))
        plan = os.path.join(work, "plan.json")
        with open(plan, "w", encoding="utf-8") as f:
            json.dump(case_plan(name, extra), f, ensure_ascii=False, indent=1)
        source = os.path.join(fakes.CORPUS, name)
        deadline = time.monotonic() + 300
        seg = engines.run_segment(_settings(), source, os.path.join(work, "segment"), deadline)
        red = engines.run_redline(_settings(), plan, source, os.path.join(work, "redline"), deadline)
        if seg.outcome != engines.OUTCOME_COMPLETED or red.outcome != engines.OUTCOME_COMPLETED:
            raise AssertionError(f"{stem}: segment {seg.outcome}, redline {red.outcome}")
        results[stem] = {**seg.outputs, **red.outputs, "_check": red.check_outcome}
    return results


def run_direct(out_root):
    """The engines' CLIs called directly, in run_local_e2e.py's sequence. Returns {stem: {output name: path}}."""
    py = [sys.executable]
    results = {}
    for name, extra in E2E.CASES:
        stem = name[:-5]
        work = os.path.join(out_root, stem)
        os.makedirs(work)
        plan = os.path.join(work, "plan.json")
        with open(plan, "w", encoding="utf-8") as f:
            json.dump(case_plan(name, extra), f, ensure_ascii=False, indent=1)
        src = os.path.join(fakes.CORPUS, name)
        eng = lambda s: os.path.join(fakes.ENGINES_DIR, s)  # noqa: E731
        p = {n: os.path.join(work, n) for n in (engines.INTERNAL, engines.EXTERNAL, engines.CLEAN)}
        red = os.path.join(work, "working.docx")
        seg = subprocess.run(py + [eng("segment_clauses.py"), src], capture_output=True, check=True, timeout=300)
        for args in ([plan, src, red, "--dry-run"],
                     [plan, src, red, "--external", p[engines.EXTERNAL], "--clean", p[engines.CLEAN]]):
            subprocess.run(py + [eng("ooxml_redline.py"), *args], capture_output=True, check=True, timeout=300)
        subprocess.run(py + [eng("summary_table.py"), plan, red, "--out", p[engines.INTERNAL],
                             "--intro", fakes.PRACTICE_INTRO], capture_output=True, check=True, timeout=300)
        chk = subprocess.run(py + [eng("check_outputs.py"), plan, p[engines.INTERNAL], p[engines.CLEAN],
                                   "--original", src, "--external", p[engines.EXTERNAL]],
                             capture_output=True, timeout=300)
        files = dict(p)
        for out_name, data in ((engines.CLAUSES, seg.stdout), (engines.CHECK_REPORT, chk.stdout)):
            files[out_name] = os.path.join(work, out_name)
            with open(files[out_name], "wb") as f:
                f.write(data)
        results[stem] = files
    return results


def fingerprint(path):
    """What output identity compares for one file (see DOCX_IDENTITY)."""
    with open(path, "rb") as f:
        data = f.read()
    whole = hashlib.sha256(data).hexdigest()
    if not path.endswith(".docx") or DOCX_IDENTITY == "sha256":
        return {"sha256": whole}
    with zipfile.ZipFile(path) as z:
        parts = {}
        for info in z.infolist():
            member = z.read(info.filename)
            parts[info.filename] = [hashlib.sha256(member).hexdigest(), info.CRC, binascii.crc32(member)]
    return {"parts": parts}


def manifest(results):
    return {"docxIdentity": DOCX_IDENTITY,
            "files": {f"{stem}/{name}": fingerprint(path)
                      for stem, outs in sorted(results.items()) for name, path in sorted(outs.items())
                      if not name.startswith("_")}}


class GoldenParityTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.tmp = tempfile.TemporaryDirectory()
        cls.job = run_job(os.path.join(cls.tmp.name, "job"))
        cls.manifest = manifest(cls.job)

    @classmethod
    def tearDownClass(cls):
        cls.tmp.cleanup()

    def test_three_synthetic_cases_all_outputs_present(self):
        self.assertEqual(sorted(self.job), sorted(n[:-5] for n, _ in E2E.CASES))
        for stem, outs in self.job.items():
            self.assertEqual(set(outs) - {"_check"}, {engines.CLAUSES, *engines.REDLINE_OUTPUTS}, stem)

    def test_check_outputs_passes_on_every_case(self):
        for stem, outs in self.job.items():
            self.assertEqual(outs["_check"], engines.CHECK_PASSED, stem)
            with open(outs[engines.CHECK_REPORT], "rb") as f:
                checks = json.loads(f.read())["checks"]
            self.assertTrue(checks and all(c["ok"] for c in checks), stem)

    def test_job_runner_output_equals_a_direct_engine_run(self):
        with tempfile.TemporaryDirectory() as tmp:
            direct = manifest(run_direct(tmp))
        self.assertEqual(self.manifest, direct)

    def test_docx_parts_are_compared_byte_for_byte(self):
        # Must-fire control for the zip_parts definition: one changed byte in one part is a difference.
        stem = sorted(self.job)[0]
        src = self.job[stem][engines.CLEAN]
        with tempfile.TemporaryDirectory() as tmp:
            altered = os.path.join(tmp, "altered.docx")
            with zipfile.ZipFile(src) as zi, zipfile.ZipFile(altered, "w", zipfile.ZIP_DEFLATED) as zo:
                for info in zi.infolist():
                    data = zi.read(info.filename)
                    if info.filename == "word/document.xml":
                        data = data.replace(b"<w:body>", b"<w:body> ", 1)
                    zo.writestr(info, data)
            self.assertNotEqual(fingerprint(src), fingerprint(altered))

    def test_matches_the_reference_manifest_from_another_host(self):
        path = os.environ.get(REFERENCE_ENV)
        if not path:
            if os.environ.get(REQUIRE_REFERENCE_ENV) == "1":
                self.fail(f"{REFERENCE_ENV} is required here but not set")
            self.skipTest(f"{REFERENCE_ENV} not set (CI sets it for the in-image run)")
        with open(path, encoding="utf-8") as f:
            reference = json.load(f)
        self.assertEqual(reference.get("docxIdentity"), DOCX_IDENTITY, "both sides must use the same definition")
        diffs = sorted(k for k in set(reference["files"]) | set(self.manifest["files"])
                       if reference["files"].get(k) != self.manifest["files"].get(k))
        self.assertEqual(diffs, [], "outputs differ from the reference run (POML: stop and record, never normalise)")


def _write_manifest(path):
    with tempfile.TemporaryDirectory() as tmp:
        data = manifest(run_job(tmp))
    with open(path, "w", encoding="utf-8") as f:
        json.dump(data, f, indent=1, sort_keys=True)
    print(f"wrote {len(data['files'])} fingerprints ({DOCX_IDENTITY}) to {path}")


if __name__ == "__main__":
    if len(sys.argv) == 3 and sys.argv[1] == "--write-manifest":
        _write_manifest(sys.argv[2])
    else:
        unittest.main()
