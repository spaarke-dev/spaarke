"""Run the accelerator's engines UNCHANGED, through their documented CLIs, and map their exit codes to outcomes.

The engines (src/accelerators/core/engines/, COPYed into the image at build time, never vendored here) are standard
library + lxml only. They run as child processes of the same interpreter; nothing here imports them, so no engine
module ever sees an Azure SDK.

CLI contract used (accelerator, master after #3163; sequence as practices/nda/evals/run_local_e2e.py):
  segment_clauses.py DOCX                                   0 ok (JSON on stdout) | 2 bad input
  ooxml_redline.py PLAN DOCX REDLINE [--external E] [--clean C] [--dry-run]
                                                            0 ok | 2 bad input/plan | 3 plan does not fit
  summary_table.py PLAN REDLINE --out INTERNAL --intro MD   0 ok | 2 bad input | 3 plan inconsistent with document
  check_outputs.py PLAN INTERNAL CLEAN --original DOCX --external E
                                                            0 all checks pass | 1 a check failed | 2 bad input

ADR-015: engine stderr quotes clause text (an edit's find/after string, up to 50 characters). It is read ONLY to
derive the refused edit's index and a reason code, inside `refusal_from_stderr`; it is never logged, raised, stored
or sent. Stdout of segment_clauses (clause text) and check_outputs is written to private staging blobs only.

Skip-and-report mode (`skip_and_report=True`) follows the contract the accelerator proposed on 2026-11-26
(projects/spaarke-intake-pipeline-r1/notes/001-engine-container.md). It is OFF until their PR is on master: the
job refuses to start with it ON unless the installed engines advertise the flags (`engines_support_skip_mode`).
"""
import json
import os
import re
import subprocess
import sys
import time
from dataclasses import dataclass, field

OUTCOME_COMPLETED = "completed"
OUTCOME_PLAN_DOES_NOT_FIT = "plan_does_not_fit"
OUTCOME_INPUT_INVALID = "input_invalid"
OUTCOME_CONTRACT_ERROR = "contract_error"
CHECK_PASSED, CHECK_FAILED = "passed", "failed"

# The accelerator's closed reason-code set (skip-and-report contract). Default mode maps the engine's PlanError
# messages onto the same codes so the BFF sees one vocabulary; `plan_invalid` (the plan breaks the schema, or the
# summary does not match the document) and `unrecognised` (a message this version cannot classify) are the only
# default-mode additions.
REFUSAL_CODES = frozenset({
    "para_not_found", "para_not_unique", "not_found", "not_unique", "in_existing_markup", "word_boundary",
    "old_not_in_find", "crosses_markup", "crosses_field_or_drawing", "in_field_result", "nothing_after_heading",
    "bookmark_exists",
})
EDIT_REFUSAL_CODES = REFUSAL_CODES - {"bookmark_exists"}
BOOKMARK_REFUSAL_CODES = frozenset({"para_not_found", "para_not_unique", "bookmark_exists"})
PLAN_INVALID, UNRECOGNISED = "plan_invalid", "unrecognised"
_BOOKMARK_RE = re.compile(r"^[A-Za-z][A-Za-z0-9_]{0,39}$")

# Output file names (also the blob names under the run's out/ prefix: envelope.StagingPaths.output).
CLAUSES = "clauses.json"
INTERNAL = "redline-internal.docx"
EXTERNAL = "redline-external.docx"
CLEAN = "clean.docx"
CHECK_REPORT = "check-report.json"
REFUSED_REPORT = "refused-report.json"
REDLINE_OUTPUTS = (INTERNAL, EXTERNAL, CLEAN, CHECK_REPORT)

# Environment variables an engine child process never needs. The engines parse untrusted .docx; a parser exploit
# should not find the managed-identity endpoint, its header or the telemetry connection string in its environment.
_SCRUBBED_ENV_PREFIXES = ("AZURE_", "IDENTITY_", "MSI_", "APPLICATIONINSIGHTS_", "INTAKE_", "OTEL_")


class EngineRetryable(Exception):
    """A failure that may pass on another delivery (timeout, crash, killed process). `code` is a fixed identifier."""

    def __init__(self, code):
        super().__init__(code)
        self.code = code


@dataclass(frozen=True)
class RefusedEdit:
    edit: object  # int index, or None when the refusal names no edit (a plan-level or bookmark problem)
    code: str


@dataclass(frozen=True)
class RefusedBookmark:
    bookmark: str
    code: str


@dataclass
class EngineResult:
    outcome: str
    check_outcome: object = None  # "passed" / "failed" for a completed redline; None otherwise
    refused_edits: list = field(default_factory=list)
    refused_bookmarks: list = field(default_factory=list)
    outputs: dict = field(default_factory=dict)  # output name -> local file path; empty unless outcome completed


@dataclass(frozen=True)
class EngineSettings:
    engines_dir: str
    summary_intro: str
    skip_and_report: bool = False


# --- default-mode refusal parsing (defensive: any surprise yields `unrecognised`, never an exception) -------------

_EDIT_PREFIX = re.compile(r"^edit (\d+): (.*)$", re.S)
_PATTERNS = (
    # (regex over the message after any "edit N: " prefix, code or callable(match) -> code)
    (re.compile(r"^paragraph key .* matched (\d+) paragraphs$", re.S),
     lambda m: "para_not_found" if m.group(1) == "0" else "para_not_unique"),
    (re.compile(r"^(?:find|after) .* occurs (\d+) times in the visible paragraph text$", re.S),
     lambda m: "not_found" if m.group(1) == "0" else "not_unique"),
    (re.compile(r"^(?:find|after) .* is not in the paragraph's editable text", re.S), "in_existing_markup"),
    (re.compile(r"^(?:find|after|old) .* starts or ends inside a word", re.S), "word_boundary"),
    (re.compile(r"^old .* must occur exactly once inside find ", re.S), "old_not_in_find"),
    (re.compile(r"^span crosses existing ", re.S), "crosses_markup"),
    (re.compile(r"^(?:span crosses a field or drawing|cannot split inside a non-text run)", re.S),
     "crosses_field_or_drawing"),
    (re.compile(r"^(?:span is inside a field result|anchor ends inside a field result)", re.S), "in_field_result"),
    (re.compile(r"^find '\*' left nothing after the section number and heading$", re.S), "nothing_after_heading"),
    (re.compile(r"^bookmark .* already exists in the document", re.S), "bookmark_exists"),
)


def refusal_from_stderr(stderr):
    """Map ooxml_redline's exit-3 stderr to a RefusedEdit or RefusedBookmark. Only an index or bookmark name and a
    code survive; the "error" text is discarded.

    Engines that carry structured keys (accelerator commit 2759ef3a7d, on its way to master) write
    {"error": ..., "code": ..., "edit": i} or {..., "bookmark": name}; "code" is absent when the plan breaks the
    contract. Those keys are used when present and valid. Otherwise (master today) the "error" text is matched:
    placement refusals are raised per edit as "edit N: <reason>"; a plan that fails validate_plan also exits 3, with
    "edit N: ..." fragments joined by "; " whose reasons are not placement reasons, which is `plan_invalid` with no
    index, because no single edit is "the one that did not fit".
    """
    try:
        doc = json.loads(stderr.decode("utf-8", "replace").strip().splitlines()[-1])
        message = doc["error"]
        if not isinstance(message, str):
            return RefusedEdit(None, UNRECOGNISED)
    except (ValueError, KeyError, IndexError, TypeError, AttributeError):
        return RefusedEdit(None, UNRECOGNISED)
    if "code" in doc or "edit" in doc or "bookmark" in doc:
        return _structured_refusal(doc)
    index = None
    m = _EDIT_PREFIX.match(message)
    if m:
        index, message = int(m.group(1)), m.group(2)
    for pattern, code in _PATTERNS:
        hit = pattern.match(message)
        if hit:
            return RefusedEdit(index, code(hit) if callable(code) else code)
    return RefusedEdit(None, PLAN_INVALID)


def _structured_refusal(doc):
    code = doc.get("code")
    if code is None:
        return RefusedEdit(None, PLAN_INVALID)  # the plan breaks the contract
    if code not in REFUSAL_CODES:
        return RefusedEdit(None, UNRECOGNISED)
    bookmark = doc.get("bookmark")
    if bookmark is not None:
        if isinstance(bookmark, str) and _BOOKMARK_RE.match(bookmark):
            return RefusedBookmark(bookmark, code)
        return RefusedEdit(None, code)
    edit = doc.get("edit")
    if isinstance(edit, int) and not isinstance(edit, bool) and edit >= 0:
        return RefusedEdit(edit, code)
    return RefusedEdit(None, code)


# --- skip-mode report validation ----------------------------------------------------------------------------------

def parse_refused_report(data):
    """Validate the engine's skip-mode REPORT.json. Returns (edits, bookmarks) or None if it breaks the contract."""
    try:
        doc = json.loads(data.decode("utf-8"))
    except (UnicodeDecodeError, ValueError):
        return None
    keys = {"engine", "report_version", "edits_total", "edits_applied", "refused", "bookmarks_refused"}
    if not isinstance(doc, dict) or set(doc) != keys:
        return None
    if doc["engine"] != "ooxml_redline" or doc["report_version"] != 1:
        return None
    total, applied = doc["edits_total"], doc["edits_applied"]
    if not all(isinstance(v, int) and not isinstance(v, bool) and v >= 0 for v in (total, applied)):
        return None
    if not isinstance(doc["refused"], list) or not isinstance(doc["bookmarks_refused"], list):
        return None
    edits, bookmarks, seen = [], [], set()
    for r in doc["refused"]:
        if not isinstance(r, dict) or set(r) != {"edit", "code"}:
            return None
        i, code = r["edit"], r["code"]
        if not isinstance(i, int) or isinstance(i, bool) or not 0 <= i < total or i in seen:
            return None
        if code not in EDIT_REFUSAL_CODES:
            return None  # an unknown code is a contract change we have not adopted
        seen.add(i)
        edits.append(RefusedEdit(i, code))
    if applied + len(edits) != total:
        return None
    for b in doc["bookmarks_refused"]:
        if not isinstance(b, dict) or set(b) != {"bookmark", "code"}:
            return None
        if (not isinstance(b["bookmark"], str) or not _BOOKMARK_RE.match(b["bookmark"])
                or b["code"] not in BOOKMARK_REFUSAL_CODES):
            return None
        bookmarks.append(RefusedBookmark(b["bookmark"], b["code"]))
    return edits, bookmarks


# --- running -----------------------------------------------------------------------------------------------------

def _child_env():
    return {k: v for k, v in os.environ.items() if not k.upper().startswith(_SCRUBBED_ENV_PREFIXES)}


def _run(settings, script, args, deadline):
    """Run one engine script. Returns (exit code, stdout bytes, stderr bytes). Raises EngineRetryable on timeout."""
    remaining = deadline - time.monotonic()
    if remaining <= 0:
        raise EngineRetryable("engine_timeout")
    cmd = [sys.executable, "-I", os.path.join(settings.engines_dir, script), *args]
    try:
        p = subprocess.run(cmd, capture_output=True, timeout=remaining, env=_child_env(), check=False)
    except subprocess.TimeoutExpired:
        raise EngineRetryable("engine_timeout") from None
    if p.returncode < 0:  # killed by a signal (e.g. the OOM killer)
        raise EngineRetryable("engine_killed")
    return p.returncode, p.stdout, p.stderr


def engines_support_skip_mode(settings):
    """True when the installed engines advertise the skip-and-report flags in their --help."""
    checks = (("ooxml_redline.py", b"--skip-unplaceable"), ("summary_table.py", b"--refused"),
              ("check_outputs.py", b"--refused"))
    deadline = time.monotonic() + 60
    for script, flag in checks:
        rc, out, _ = _run(settings, script, ["--help"], deadline)
        if rc != 0 or flag not in out:
            return False
    return True


def run_segment(settings, source, workdir, deadline):
    """segment_clauses.py on the draft. Completed -> outputs {clauses.json}; exit 2 -> input_invalid."""
    rc, out, _ = _run(settings, "segment_clauses.py", [source], deadline)
    if rc == 2:
        return EngineResult(OUTCOME_INPUT_INVALID)
    if rc != 0:
        raise EngineRetryable("engine_failed")
    path = os.path.join(workdir, CLAUSES)
    with open(path, "wb") as f:
        f.write(out)
    return EngineResult(OUTCOME_COMPLETED, outputs={CLAUSES: path})


def run_redline(settings, plan, source, workdir, deadline):
    """dry-run, redline, summary, check (the nda-review sequence). Outputs are local files under `workdir`."""
    p = {name: os.path.join(workdir, name) for name in (INTERNAL, EXTERNAL, CLEAN, CHECK_REPORT, REFUSED_REPORT)}
    working = os.path.join(workdir, "redline-working.docx")
    skip = ["--skip-unplaceable", p[REFUSED_REPORT]] if settings.skip_and_report else []

    def redline_step(args):
        rc, _, err = _run(settings, "ooxml_redline.py", [plan, source, working, *args, *skip], deadline)
        if rc == 2:
            return EngineResult(OUTCOME_INPUT_INVALID)
        if rc == 3:
            # Default mode: one unplaceable edit refuses the whole plan. Skip mode: 3 only means the plan breaks
            # the schema (validate_plan); an edit that does not fit is reported, never an exit code.
            refusal = refusal_from_stderr(err) if not settings.skip_and_report else RefusedEdit(None, PLAN_INVALID)
            if isinstance(refusal, RefusedBookmark):
                return EngineResult(OUTCOME_PLAN_DOES_NOT_FIT, refused_bookmarks=[refusal])
            return EngineResult(OUTCOME_PLAN_DOES_NOT_FIT, refused_edits=[refusal])
        if rc != 0:
            raise EngineRetryable("engine_failed")
        return None

    for args in (["--dry-run"], ["--external", p[EXTERNAL], "--clean", p[CLEAN]]):
        stop = redline_step(args)
        if stop is not None:
            return stop

    refused_edits, refused_bookmarks, refused_args = [], [], []
    if settings.skip_and_report:
        try:
            with open(p[REFUSED_REPORT], "rb") as f:
                parsed = parse_refused_report(f.read())
        except OSError:
            parsed = None
        if parsed is None:
            return EngineResult(OUTCOME_CONTRACT_ERROR)
        refused_edits, refused_bookmarks = parsed
        refused_args = ["--refused", p[REFUSED_REPORT]]

    rc, _, _ = _run(settings, "summary_table.py",
                    [plan, working, "--out", p[INTERNAL], "--intro", settings.summary_intro, *refused_args], deadline)
    if rc == 2:
        return EngineResult(OUTCOME_INPUT_INVALID)
    if rc == 3:
        return EngineResult(OUTCOME_PLAN_DOES_NOT_FIT, refused_edits=[RefusedEdit(None, PLAN_INVALID)])
    if rc != 0:
        raise EngineRetryable("engine_failed")

    rc, out, _ = _run(settings, "check_outputs.py",
                      [plan, p[INTERNAL], p[CLEAN], "--original", source, "--external", p[EXTERNAL], *refused_args],
                      deadline)
    if rc not in (0, 1):
        return EngineResult(OUTCOME_CONTRACT_ERROR)  # 2 = check_outputs could not read our own outputs
    try:
        checks = json.loads(out.decode("utf-8"))["checks"]
        if not isinstance(checks, list):
            raise ValueError
    except (UnicodeDecodeError, ValueError, KeyError, TypeError):
        return EngineResult(OUTCOME_CONTRACT_ERROR)
    with open(p[CHECK_REPORT], "wb") as f:
        f.write(out)
    outputs = {name: p[name] for name in REDLINE_OUTPUTS}
    if settings.skip_and_report:
        outputs[REFUSED_REPORT] = p[REFUSED_REPORT]
    return EngineResult(OUTCOME_COMPLETED, check_outcome=CHECK_PASSED if rc == 0 else CHECK_FAILED,
                        refused_edits=refused_edits, refused_bookmarks=refused_bookmarks, outputs=outputs)
