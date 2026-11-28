"""Refused edits (skip-and-report mode, the default, and the engines' default mode behind the switch), exit-3
parsing, retries, dead-lettering, the completion message, correlation in logs, lock renewal and configuration
(FR-01 g; ADR-004 A2 §2.3-2.5; ADR-015). Refusal tests run the real engines on the synthetic nda-01."""
import glob
import io
import json
import os
import re
import sys
import tempfile
import time
import unittest
import zipfile
from datetime import datetime, timedelta, timezone
from unittest import mock

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from azure.servicebus import AutoLockRenewer  # noqa: E402

from entrypoint import azure_io, engines, main  # noqa: E402
from entrypoint.envelope import parse_request  # noqa: E402
from tests import fakes  # noqa: E402
from tests.test_envelope_and_marker import Harness, config, fake_engines  # noqa: E402

FIXED_NOW = datetime(2026, 11, 26, 12, 0, 0, tzinfo=timezone.utc)
# Text that must never leave the job in a log line or a message: a refused edit's find string.
SECRET_FIND = "Recipient shall hold the Zanzibar formula in confidence"
JOB_CONTRACT_KEYS = {"jobId", "jobType", "subjectId", "correlationId", "idempotencyKey", "attempt", "maxAttempts",
                     "payload", "createdAt"}


def everything_sent(h):
    """All bytes that left the job: completions, retry copies, dead-letter reasons/descriptions, logs."""
    parts = [fakes.message_body(m) for m in h.completion_sender.sent + h.request_sender.sent]
    parts += [fakes.message_body(m) for m, _ in h.request_sender.scheduled]
    parts += [f"{r} {d}".encode() for _, r, d in h.receiver.dead_lettered]
    parts.append(h.logs.getvalue().encode())
    return b"\n".join(parts)


def completion(h):
    (msg,) = h.completion_sender.sent
    return msg, json.loads(fakes.message_body(msg))


def stage_redline(h, plan, docx="nda-01-bluefin-harbor-mutual.docx", run_id=None, attempt=1):
    body, sent = fakes.envelope(operation="redline", run_id=run_id, dispatch_attempt=attempt)
    req = parse_request(body)
    with open(os.path.join(fakes.CORPUS, docx), "rb") as f:
        h.container.put(req.paths.source, f.read())
    h.container.put(req.paths.plan, json.dumps(plan).encode())
    return body, req


def golden_plan(extra_edit=None, file_name="nda-01-bluefin-harbor-mutual.docx"):
    with open(fakes.GOLDEN_PLAN, encoding="utf-8") as f:
        plan = json.load(f)
    plan["agreement"]["file_name"] = file_name
    if extra_edit:
        plan["edits"].append(extra_edit)
    return plan


def default_mode_config():
    """The switch off: the engines' all-or-nothing default mode."""
    cfg = config()
    return main.Config(**{**cfg.__dict__, "engine_settings": engines.EngineSettings(
        engines_dir=cfg.engine_settings.engines_dir, summary_intro=cfg.engine_settings.summary_intro,
        skip_and_report=False)})


def run_redline_message(plan, cfg=None):
    h = Harness()
    body, req = stage_redline(h, plan)
    result = main.process_message(fakes.received(body), h.queue, h.staging, cfg or config())
    return h, req, result


def blob(h, path):
    return h.container.blobs[path][0]


class SkipAndReportTests(unittest.TestCase):
    """The default: the engines apply every edit that fits and report the rest by index and closed code (real engines,
    synthetic nda-01)."""

    def test_one_unplaceable_edit_is_reported_and_every_other_edit_applied(self):
        first_para = golden_plan()["edits"][0]["para"]
        for find, code in ((SECRET_FIND, "not_found"), ("the", "not_unique")):
            with self.subTest(code=code):
                h, req, result = run_redline_message(golden_plan({"para": first_para, "find": find,
                                                                  "comment": "probe"}))
                self.assertEqual(result, engines.OUTCOME_COMPLETED)
                _, doc = completion(h)
                self.assertEqual(doc["payload"]["outcome"], "completed")
                self.assertEqual(doc["payload"]["checkOutcome"], "passed")
                self.assertEqual(doc["payload"]["refusedEdits"], [{"edit": 19, "code": code}])
                self.assertEqual(doc["payload"]["refusedBookmarks"], [])
                self.assertEqual(sorted(h.container.names(req.paths.out_prefix)),
                                 sorted(req.paths.output(n) for n in (*engines.REDLINE_OUTPUTS,
                                                                      engines.REFUSED_REPORT)))
                report = json.loads(blob(h, req.paths.output(engines.REFUSED_REPORT)))
                self.assertEqual((report["edits_total"], report["edits_applied"], report["refused"]),
                                 (20, 19, [{"edit": 19, "code": code}]))
                # check_outputs ran with --refused: its report has the `refusals` check, and it passed.
                checks = json.loads(blob(h, req.paths.output(engines.CHECK_REPORT)))["checks"]
                self.assertIn(("refusals", True), [(c["name"], c["ok"]) for c in checks])
                # summary_table ran with --refused: the internal copy has the by-hand table.
                with zipfile.ZipFile(io.BytesIO(blob(h, req.paths.output(engines.INTERNAL)))) as z:
                    self.assertIn("Changes to make by hand".encode(), z.read("word/document.xml"))
                self.assertEqual([m.settled for m in h.receiver.completed], ["completed"])
                self.assertNotIn(SECRET_FIND.encode(), everything_sent(h))

    def test_every_edit_refused_is_still_a_success_with_outputs(self):
        plan = golden_plan()
        for edit in plan["edits"]:
            edit["para"] = "Zanzibar clause that is not in this agreement"
        h, req, result = run_redline_message(plan)
        self.assertEqual(result, engines.OUTCOME_COMPLETED)
        _, doc = completion(h)
        self.assertEqual(doc["payload"]["refusedEdits"],
                         [{"edit": i, "code": "para_not_found"} for i in range(len(plan["edits"]))])
        self.assertEqual(sorted(h.container.names(req.paths.out_prefix)),
                         sorted(req.paths.output(n) for n in (*engines.REDLINE_OUTPUTS, engines.REFUSED_REPORT)))
        self.assertIn(doc["payload"]["checkOutcome"], ("passed", "failed"))

    def test_a_plan_that_breaks_the_contract_ends_plan_does_not_fit_with_no_code_and_nothing_written(self):
        plan = golden_plan()
        plan["edits"][0]["find"] = ""  # blank anchors fail validate_plan: exit 3 with no "code"
        h, req, result = run_redline_message(plan)
        self.assertEqual(result, engines.OUTCOME_PLAN_DOES_NOT_FIT)
        _, doc = completion(h)
        self.assertEqual((doc["payload"]["outcome"], doc["payload"]["refusedEdits"],
                          doc["payload"]["refusedBookmarks"], doc["payload"]["checkOutcome"]),
                         ("plan_does_not_fit", [], [], None))
        self.assertEqual(h.container.names(req.paths.out_prefix), [])
        self.assertEqual([m.settled for m in h.receiver.completed], ["completed"])  # non-retryable
        self.assertEqual(h.request_sender.scheduled, [])

    def test_a_contract_error_is_dead_lettered_poisoned_with_nothing_written(self):
        h = Harness()
        body, req = stage_redline(h, golden_plan())
        result = main.process_message(fakes.received(body), h.queue, h.staging, config(),
                                      run_engines=lambda *_: engines.EngineResult(engines.OUTCOME_CONTRACT_ERROR))
        self.assertEqual(result, engines.OUTCOME_CONTRACT_ERROR)
        self.assertEqual([r for _, r, _ in h.receiver.dead_lettered], ["Poisoned"])
        self.assertEqual(completion(h)[1]["payload"]["outcome"], "contract_error")
        self.assertEqual(h.container.names(req.paths.out_prefix), [])

    def test_report_validation(self):
        good = {"engine": "ooxml_redline", "report_version": 1, "edits_total": 3, "edits_applied": 2,
                "refused": [{"edit": 2, "code": "not_unique"}], "bookmarks_refused": []}
        self.assertIsNotNone(engines.parse_refused_report(json.dumps(good).encode()))
        for bad in (dict(good, edits_applied=3), dict(good, extra=1), dict(good, report_version=2),
                    dict(good, refused=[{"edit": 2, "code": "not_unique", "text": "clause"}]),
                    dict(good, refused=[{"edit": 3, "code": "not_unique"}]),
                    dict(good, refused=[{"edit": 2, "code": "something_new"}]),
                    dict(good, refused=[{"edit": 2, "code": "bookmark_exists"}]),
                    dict(good, bookmarks_refused=[{"bookmark": "bad name", "code": "bookmark_exists"}])):
            self.assertIsNone(engines.parse_refused_report(json.dumps(bad).encode()))


class DefaultModeTests(unittest.TestCase):
    """The switch off: one unplaceable edit refuses the whole plan (exit 3), and the refusal is read from the
    "code"/"edit" keys of the engine's stderr JSON line, never from its "error" text."""

    def test_exit3_code_and_edit_come_from_the_engine_stderr_and_nothing_is_written(self):
        first_para = golden_plan()["edits"][0]["para"]
        for find, code in ((SECRET_FIND, "not_found"), ("the", "not_unique")):
            with self.subTest(code=code):
                h, req, result = run_redline_message(golden_plan({"para": first_para, "find": find,
                                                                  "comment": "probe"}), default_mode_config())
                self.assertEqual(result, engines.OUTCOME_PLAN_DOES_NOT_FIT)
                _, doc = completion(h)
                self.assertEqual(doc["payload"]["outcome"], "plan_does_not_fit")
                self.assertEqual(doc["payload"]["refusedEdits"], [{"edit": 19, "code": code}])
                self.assertIsNone(doc["payload"]["checkOutcome"])
                self.assertEqual(h.container.names(req.paths.out_prefix), [])  # never applied, no output blob
                self.assertEqual([m.settled for m in h.receiver.completed], ["completed"])  # non-retryable
                self.assertEqual(json.loads(h.container.blobs[req.paths.marker][0])["state"], "completed")
                sent = everything_sent(h)
                self.assertNotIn(SECRET_FIND.encode(), sent)
                self.assertNotIn(b"visible paragraph text", sent)  # the "error" text of either case

    def test_the_engine_stderr_really_carries_the_keys_and_the_text_that_is_kept_out(self):
        # Must-fire control for the two tests above: the engine's own exit-3 line has "code"/"edit" AND quotes the
        # find string in "error".
        plan = golden_plan({"para": golden_plan()["edits"][0]["para"], "find": SECRET_FIND, "comment": "probe"})
        with tempfile.TemporaryDirectory() as tmp:
            p = os.path.join(tmp, "plan.json")
            with open(p, "w", encoding="utf-8") as f:
                json.dump(plan, f)
            rc, _, err = engines._run(default_mode_config().engine_settings, "ooxml_redline.py",
                                      [p, os.path.join(fakes.CORPUS, "nda-01-bluefin-harbor-mutual.docx"),
                                       os.path.join(tmp, "r.docx"), "--dry-run"], time.monotonic() + 120)
        self.assertEqual(rc, 3)
        line = json.loads(err.decode("utf-8").strip().splitlines()[-1])
        self.assertEqual((line["code"], line["edit"]), ("not_found", 19))
        self.assertIn(SECRET_FIND[:30], line["error"])


class Exit3ParsingTests(unittest.TestCase):
    def stderr(self, error="edit 4: find 'x y' ... clause text", **keys):
        return json.dumps({"error": error, **keys}).encode()

    def outcome(self, raw, skip=False):
        r = engines.exit3_result(raw, skip)
        return (r.outcome, [(e.edit, e.code) for e in r.refused_edits],
                [(b.bookmark, b.code) for b in r.refused_bookmarks])

    def test_structured_keys(self):
        self.assertEqual(self.outcome(self.stderr(code="crosses_markup", edit=4)),
                         ("plan_does_not_fit", [(4, "crosses_markup")], []))
        self.assertEqual(self.outcome(self.stderr(code="bookmark_exists", bookmark="NDA_Sec10")),
                         ("plan_does_not_fit", [], [("NDA_Sec10", "bookmark_exists")]))
        for skip in (False, True):  # no "code": the plan breaks the contract, in either mode
            self.assertEqual(self.outcome(self.stderr("edit 0: 'find' must not be blank"), skip),
                             ("plan_does_not_fit", [], []))

    def test_anything_off_contract_is_a_contract_error_never_an_exception(self):
        cases = [b"", b"Traceback (most recent call last)", b"\xff\xfe", b"[]", b'{"error": 5}', b'{"code": "x"}',
                 self.stderr(code="something_new", edit=1), self.stderr(code=None, edit=1),
                 self.stderr(code="not_found"), self.stderr(code="not_found", edit=-1),
                 self.stderr(code="not_found", edit=True), self.stderr(code="not_found", edit="1"),
                 self.stderr(code="bookmark_exists", edit=1),
                 self.stderr(code="not_found", bookmark="NDA_Sec10"),
                 self.stderr(code="bookmark_exists", bookmark="not a name"),
                 self.stderr(code="bookmark_exists", bookmark="NDA_Sec10", edit=1)]
        for raw in cases:
            with self.subTest(raw=raw):
                self.assertEqual(self.outcome(raw)[0], "contract_error")
        # Skip mode reports refusals in REPORT.json; a refusal code on its exit-3 line breaks that contract.
        self.assertEqual(self.outcome(self.stderr(code="not_found", edit=1), skip=True)[0], "contract_error")


class RetryTests(unittest.TestCase):
    def _fail(self, exc):
        def run(*_):
            raise exc
        return run

    def test_retryable_failure_on_deliveries_1_to_4_schedules_a_copy_and_completes_the_original(self):
        for prior in (None, 1, 2, 3):
            logical = (prior or 0) + 1
            with self.subTest(delivery=logical):
                h = Harness()
                body, sent = fakes.envelope()
                msg = fakes.received(body, prior=prior)
                result = h.process(msg, clock=lambda: FIXED_NOW,
                                   run_engines=self._fail(engines.EngineRetryable("engine_timeout")))
                self.assertEqual(result, "retry_scheduled")
                (copy, due), = h.request_sender.scheduled
                self.assertEqual(fakes.app_prop(copy, "PriorDeliveryCount"), logical)
                self.assertEqual(due, FIXED_NOW + timedelta(minutes=2 ** (logical - 1)))
                self.assertEqual(copy.message_id, sent["idempotencyKey"])
                self.assertEqual(copy.correlation_id, sent["correlationId"])
                self.assertEqual(copy.content_type, "application/json")
                self.assertEqual(copy.subject, "IntakeEngineRun")
                self.assertEqual(fakes.message_body(copy), body)
                self.assertEqual(msg.settled, "completed")
                self.assertEqual(h.completion_sender.sent, [])
                marker = parse_request(body).paths.marker
                self.assertEqual(json.loads(h.container.blobs[marker][0])["state"], "released")

    def test_the_retry_copy_is_claimed_again_on_its_next_delivery(self):
        h = Harness()
        body, _ = fakes.envelope()
        h.process(fakes.received(body), run_engines=self._fail(engines.EngineRetryable("engine_failed")))
        self.assertEqual(h.process(fakes.received(body, prior=1)), engines.OUTCOME_COMPLETED)
        self.assertEqual(len(h.calls), 1)

    def test_fifth_delivery_dead_letters_max_retries_exceeded(self):
        h = Harness()
        body, _ = fakes.envelope()
        msg = fakes.received(body, prior=4)
        self.assertEqual(h.process(msg, run_engines=self._fail(engines.EngineRetryable("engine_timeout"))),
                         "MaxRetriesExceeded")
        (_, reason, description), = h.receiver.dead_lettered
        self.assertEqual(reason, "MaxRetriesExceeded")
        self.assertEqual(description, "engine_timeout; delivery 5")
        self.assertEqual(h.request_sender.scheduled, [])

    def test_an_unexpected_error_on_the_last_delivery_is_processing_error(self):
        h = Harness()
        body, _ = fakes.envelope()
        h.process(fakes.received(body, prior=4), run_engines=self._fail(RuntimeError(SECRET_FIND)))
        self.assertEqual([r for _, r, _ in h.receiver.dead_lettered], ["ProcessingError"])
        self.assertNotIn(SECRET_FIND.encode(), everything_sent(h))

    def test_a_delivery_past_the_bound_is_not_run(self):
        h = Harness()
        body, _ = fakes.envelope()
        self.assertEqual(h.process(fakes.received(body, prior=5)), "MaxRetriesExceeded")
        self.assertEqual(h.calls, [])

    def test_a_failed_schedule_falls_back_to_abandon(self):
        h = Harness()
        h.request_sender.fail = True
        body, _ = fakes.envelope()
        msg = fakes.received(body)
        self.assertEqual(h.process(msg, run_engines=self._fail(engines.EngineRetryable("engine_failed"))), "abandoned")
        self.assertEqual(msg.settled, "abandoned")

    def test_retry_delay_matches_job_contract(self):
        self.assertEqual([main.retry_delay(d) for d in (0, 1, 2, 3, 4, 5, 9)],
                         [timedelta(minutes=m) for m in (1, 1, 2, 4, 8, 15, 15)])

    def test_logical_delivery_is_one_based_like_dotnet(self):
        self.assertEqual(azure_io.logical_delivery_count(fakes.received(b"", delivery_count=0)), 1)
        self.assertEqual(azure_io.logical_delivery_count(fakes.received(b"", delivery_count=1, prior=2)), 4)
        self.assertEqual(azure_io.logical_delivery_count(fakes.received(b"", prior=b"3")), 4)
        self.assertEqual(azure_io.logical_delivery_count(fakes.received(b"", prior=b"junk")), 1)


class CompletionAndLogTests(unittest.TestCase):
    def test_completion_message_shape_and_destination(self):
        h = Harness()
        body, sent = fakes.envelope(dispatch_attempt=2)
        h.process(fakes.received(body))
        msg, doc = completion(h)
        self.assertEqual(h.request_sender.sent + h.request_sender.scheduled, [])  # never the request queue
        self.assertEqual(msg.message_id, sent["idempotencyKey"])
        self.assertEqual(msg.correlation_id, sent["correlationId"])
        self.assertEqual(msg.content_type, "application/json")
        self.assertEqual(msg.subject, "IntakeEngineCompleted")
        self.assertEqual(set(doc), JOB_CONTRACT_KEYS)
        self.assertEqual((doc["jobType"], doc["subjectId"], doc["correlationId"], doc["idempotencyKey"]),
                         ("IntakeEngineCompleted", sent["subjectId"], sent["correlationId"], sent["idempotencyKey"]))
        self.assertEqual(doc["payload"], {
            "operation": "redline", "runId": sent["subjectId"], "dispatchAttempt": 2, "outcome": "completed",
            "checkOutcome": "passed", "refusedEdits": [], "refusedBookmarks": []})
        self.assertNotIn(b"/out/", fakes.message_body(msg))  # no paths: the BFF derives them from runId

    def test_every_log_line_of_a_message_carries_its_correlation_id(self):
        h = Harness()
        body, sent = fakes.envelope()
        h.process(fakes.received(body))
        lines = [json.loads(line) for line in h.logs.getvalue().splitlines()]
        self.assertGreaterEqual(len(lines), 2)
        self.assertTrue(all(line["correlationId"] == sent["correlationId"] for line in lines), lines)
        outcome = [line for line in lines if line.get("event") == "JobOutcome"]
        self.assertEqual([o["jobOutcome"] for o in outcome], ["Completed"])

    def test_outcome_names(self):
        h = Harness()
        body, _ = fakes.envelope()
        h.process(fakes.received(body, prior=4), run_engines=RetryTests()._fail(engines.EngineRetryable("x")))
        self.assertIn('"jobOutcome":"Poisoned"', h.logs.getvalue())


class LockRenewalTests(unittest.TestCase):
    LOCK_SECONDS = 2
    RUN_SECONDS = 5

    def _run(self, renewer):
        h = Harness()
        body, _ = fakes.envelope()
        msg = fakes.LockableMessage(body, self.LOCK_SECONDS, h.receiver)
        h.receiver._messages = [msg]

        def slow(request, staging, cfg, workdir, deadline):
            time.sleep(self.RUN_SECONDS)  # longer than the lock
            return fake_engines(h.calls)(request, staging, cfg, workdir, deadline)

        result = main.run_once(h.receiver, h.queue, renewer, h.staging, config(), time.monotonic(), run_engines=slow)
        return h, msg, result

    def test_a_run_longer_than_the_lock_is_renewed_and_completed_once(self):
        with AutoLockRenewer() as renewer:
            h, msg, result = self._run(renewer)
        self.assertEqual(result, engines.OUTCOME_COMPLETED)
        self.assertGreaterEqual(h.receiver.renewals, 2)
        self.assertEqual(h.receiver.completed, [msg])
        self.assertEqual(h.receiver.dead_lettered + h.receiver.abandoned, [])

    def test_control_without_renewal_the_lock_is_lost(self):
        class NoRenewal:
            def register(self, *a, **k):
                pass

        h, msg, _ = self._run(NoRenewal())
        self.assertIsNone(msg.settled)  # complete raised MessageLockLost: the broker would redeliver
        self.assertIn("SettleFailed", h.logs.getvalue())


class ConfigAndBoundaryTests(unittest.TestCase):
    BASE = {"AZURE_CLIENT_ID": "cid", "INTAKE_SERVICEBUS_FQDN": "ns.servicebus.windows.net",
            "INTAKE_REQUEST_QUEUE": "intake-engine-requests", "INTAKE_COMPLETION_QUEUE": "intake-engine-completions",
            "INTAKE_STAGING_ACCOUNT_URL": "https://a.blob.core.windows.net", "INTAKE_STAGING_CONTAINER": "s"}

    def test_config_from_the_bicep_settings(self):
        cfg = main.load_config(dict(self.BASE, INTAKE_MAX_DELIVERY_COUNT="5", INTAKE_JOB_TIMEOUT_SECONDS="600"))
        self.assertEqual((cfg.max_delivery_count, cfg.job_timeout_seconds), (5, 600))
        self.assertTrue(cfg.engine_settings.skip_and_report)  # skip-and-report is ON by default
        self.assertGreater(cfg.lock_renewal_seconds, cfg.job_timeout_seconds)
        for value, expected in (("false", False), ("off", False), ("true", True), ("", True)):
            env = dict(self.BASE, INTAKE_ENGINE_SKIP_AND_REPORT=value)
            self.assertEqual(main.load_config(env).engine_settings.skip_and_report, expected, value)

    def test_config_errors(self):
        for env in ({k: v for k, v in self.BASE.items() if k != "INTAKE_COMPLETION_QUEUE"},
                    dict(self.BASE, INTAKE_COMPLETION_QUEUE="intake-engine-requests"),
                    dict(self.BASE, INTAKE_ENGINE_SKIP_AND_REPORT="maybe"),
                    dict(self.BASE, INTAKE_JOB_TIMEOUT_SECONDS="ten")):
            with self.assertRaises(main.ConfigError):
                main.load_config(env)

    def test_no_engine_module_imports_an_azure_sdk_and_the_entrypoint_imports_no_engine(self):
        for path in glob.glob(os.path.join(fakes.ENGINES_DIR, "*.py")):
            with open(path, encoding="utf-8") as f:
                self.assertNotRegex(f.read(), re.compile(r"^\s*(import|from)\s+azure", re.M), path)
        engine_modules = {os.path.basename(p)[:-3] for p in glob.glob(os.path.join(fakes.ENGINES_DIR, "*.py"))}
        for path in glob.glob(os.path.join(fakes.JOB_ROOT, "entrypoint", "*.py")):
            with open(path, encoding="utf-8") as f:
                imported = set(re.findall(r"^\s*(?:import|from)\s+([A-Za-z_]\w*)", f.read(), re.M))
            self.assertEqual(imported & engine_modules, set(), path)

    def test_engine_children_do_not_inherit_identity_or_telemetry_settings(self):
        with mock.patch.dict(os.environ, {"IDENTITY_HEADER": "h", "IDENTITY_ENDPOINT": "e", "AZURE_CLIENT_ID": "c",
                                          "APPLICATIONINSIGHTS_CONNECTION_STRING": "x", "MSI_SECRET": "s"}):
            env = engines._child_env()
        self.assertFalse({"IDENTITY_HEADER", "IDENTITY_ENDPOINT", "AZURE_CLIENT_ID",
                          "APPLICATIONINSIGHTS_CONNECTION_STRING", "MSI_SECRET"} & set(env))


if __name__ == "__main__":
    unittest.main()
