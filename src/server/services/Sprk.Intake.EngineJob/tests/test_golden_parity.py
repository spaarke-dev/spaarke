"""Golden parity (FR-01 a): the job's engine runs on the synthetic NDAs equal a direct run of the same engines.

All on the accelerator's synthetic corpus and golden plan (run_local_e2e.py's CASES; NFR-10), in skip-and-report
mode, the job's default:
  1. In this interpreter: the job's runner (entrypoint.engines) against the engines' CLIs called directly in
     run_local_e2e.py's sequence with the skip-mode flags the job adds (--skip-unplaceable, --refused). Two separate
     runs, compared WHOLE-FILE (sha256) for every output. Proves the wrapper changes nothing and the outputs are
     reproducible here.
  2. `check_outputs` passes on every redline output.
  3. Against reference manifests written by other runs (INTAKE_PARITY_REFERENCE_MANIFEST, one path or several joined
     by os.pathsep). Each manifest declares the identity it was written with, and this run is fingerprinted the same
     way. CI (.github/workflows/build-intake-engine-image.yml) passes two, and this file then runs INSIDE the built
     image:
       - one written on the runner (another host): `zip_parts`;
       - one written by an earlier container of the SAME image: `sha256`.

DEFINITION OF OUTPUT IDENTITY for a .docx (JSON outputs are always compared whole-file):
  "sha256"     Whole-file byte identity. Only between runs on the same interpreter build, i.e. the same image: the
               engines write fixed zip entry dates (#3237), but the deflate bytes depend on the zlib build
               (notes/009-engine-changes.md).
  "zip_parts"  The same part names, and for every part the same bytes and the same CRC-32; compressed bytes and zip
               headers are not compared. For references produced elsewhere (another host, another zlib).
  Nothing is rewritten or normalised: the outputs are read as written.

  Write a manifest:  python tests/test_golden_parity.py --write-manifest PATH [--identity sha256|zip_parts]
                     (default zip_parts: safe for any reader)
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

SAME_HOST = "sha256"      # runs in this interpreter / this image
CROSS_HOST = "zip_parts"  # a reference produced elsewhere
IDENTITIES = (SAME_HOST, CROSS_HOST)

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
    """The engines' CLIs called directly: run_local_e2e.py's sequence plus the job's skip-mode flags."""
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
        p = {n: os.path.join(work, n) for n in (engines.INTERNAL, engines.EXTERNAL, engines.CLEAN,
                                                 engines.REFUSED_REPORT)}
        red = os.path.join(work, "working.docx")
        skip = ["--skip-unplaceable", p[engines.REFUSED_REPORT]]
        refused = ["--refused", p[engines.REFUSED_REPORT]]
        seg = subprocess.run(py + [eng("segment_clauses.py"), src], capture_output=True, check=True, timeout=300)
        for args in ([plan, src, red, "--dry-run", *skip],
                     [plan, src, red, "--external", p[engines.EXTERNAL], "--clean", p[engines.CLEAN], *skip]):
            subprocess.run(py + [eng("ooxml_redline.py"), *args], capture_output=True, check=True, timeout=300)
        subprocess.run(py + [eng("summary_table.py"), plan, red, "--out", p[engines.INTERNAL],
                             "--intro", fakes.PRACTICE_INTRO, *refused], capture_output=True, check=True, timeout=300)
        chk = subprocess.run(py + [eng("check_outputs.py"), plan, p[engines.INTERNAL], p[engines.CLEAN],
                                   "--original", src, "--external", p[engines.EXTERNAL], *refused],
                             capture_output=True, timeout=300)
        files = dict(p)
        for out_name, data in ((engines.CLAUSES, seg.stdout), (engines.CHECK_REPORT, chk.stdout)):
            files[out_name] = os.path.join(work, out_name)
            with open(files[out_name], "wb") as f:
                f.write(data)
        results[stem] = files
    return results


def fingerprint(path, identity):
    """What output identity compares for one file (see the module docstring)."""
    if identity not in IDENTITIES:
        raise ValueError(f"unknown identity {identity!r}")
    with open(path, "rb") as f:
        data = f.read()
    if not path.endswith(".docx") or identity == "sha256":
        return {"sha256": hashlib.sha256(data).hexdigest()}
    with zipfile.ZipFile(path) as z:
        parts = {}
        for info in z.infolist():
            member = z.read(info.filename)
            parts[info.filename] = [hashlib.sha256(member).hexdigest(), info.CRC, binascii.crc32(member)]
    return {"parts": parts}


def manifest(results, identity):
    return {"docxIdentity": identity,
            "files": {f"{stem}/{name}": fingerprint(path, identity)
                      for stem, outs in sorted(results.items()) for name, path in sorted(outs.items())
                      if not name.startswith("_")}}


def differences(a, b):
    return sorted(k for k in set(a["files"]) | set(b["files"]) if a["files"].get(k) != b["files"].get(k))


class GoldenParityTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.tmp = tempfile.TemporaryDirectory()
        cls.job = run_job(os.path.join(cls.tmp.name, "job"))

    @classmethod
    def tearDownClass(cls):
        cls.tmp.cleanup()

    def test_three_synthetic_cases_all_outputs_present(self):
        self.assertEqual(sorted(self.job), sorted(n[:-5] for n, _ in E2E.CASES))
        for stem, outs in self.job.items():
            self.assertEqual(set(outs) - {"_check"},
                             {engines.CLAUSES, *engines.REDLINE_OUTPUTS, engines.REFUSED_REPORT}, stem)

    def test_check_outputs_passes_on_every_case(self):
        for stem, outs in self.job.items():
            self.assertEqual(outs["_check"], engines.CHECK_PASSED, stem)
            with open(outs[engines.CHECK_REPORT], "rb") as f:
                checks = json.loads(f.read())["checks"]
            self.assertTrue(checks and all(c["ok"] for c in checks), stem)
            self.assertIn("refusals", [c["name"] for c in checks], stem)  # --refused reached check_outputs

    def test_job_runner_output_is_byte_identical_to_a_direct_engine_run(self):
        with tempfile.TemporaryDirectory() as tmp:
            direct = manifest(run_direct(tmp), SAME_HOST)
        self.assertEqual(differences(manifest(self.job, SAME_HOST), direct), [])

    def test_each_identity_detects_a_changed_part(self):
        # Must-fire control: one changed byte in one part is a difference under both definitions.
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
            for identity in IDENTITIES:
                self.assertNotEqual(fingerprint(src, identity), fingerprint(altered, identity), identity)

    def test_matches_the_reference_manifests(self):
        paths = [p for p in os.environ.get(REFERENCE_ENV, "").split(os.pathsep) if p]
        if not paths:
            if os.environ.get(REQUIRE_REFERENCE_ENV) == "1":
                self.fail(f"{REFERENCE_ENV} is required here but not set")
            self.skipTest(f"{REFERENCE_ENV} not set (CI sets it for the in-image run)")
        for path in paths:
            with self.subTest(reference=path):
                with open(path, encoding="utf-8") as f:
                    reference = json.load(f)
                identity = reference.get("docxIdentity")
                self.assertIn(identity, IDENTITIES)
                self.assertEqual(differences(reference, manifest(self.job, identity)), [],
                                 "outputs differ from the reference run (POML: stop and record, never normalise)")


def _write_manifest(path, identity):
    with tempfile.TemporaryDirectory() as tmp:
        data = manifest(run_job(tmp), identity)
    with open(path, "w", encoding="utf-8") as f:
        json.dump(data, f, indent=1, sort_keys=True)
    print(f"wrote {len(data['files'])} fingerprints ({identity}) to {path}")


if __name__ == "__main__":
    args = sys.argv[1:]
    if len(args) in (2, 4) and args[0] == "--write-manifest" and (len(args) == 2 or args[2] == "--identity"):
        _write_manifest(args[1], args[3] if len(args) == 4 else CROSS_HOST)
    else:
        unittest.main()
