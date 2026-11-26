"""Refused plans, skip-and-report mode, retries, dead-lettering, the completion message, correlation in logs, lock
renewal and configuration (FR-01 g; ADR-004 A2 §2.3-2.5; ADR-015)."""
import glob
import io
import json
import os
import re
import sys
import tempfile
import textwrap
import time
import unittest
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


class RefusedPlanTests(unittest.TestCase):
    """Default mode on the engines as on master: one unplaceable edit refuses the whole plan (exit 3)."""

    def _run(self, extra_edit):
        h = Harness()
        body, req = stage_redline(h, golden_plan(extra_edit))
        result = main.process_message(fakes.received(body), h.queue, h.staging, config())
        return h, req, result

    def test_unplaceable_edit_ends_plan_does_not_fit_with_index_and_code_and_nothing_written(self):
        first_para = golden_plan()["edits"][0]["para"]
        for find, code in ((SECRET_FIND, "not_found"), ("the", "not_unique")):
            with self.subTest(code=code):
                h, req, result = self._run({"para": first_para, "find": find, "comment": "probe"})
                self.assertEqual(result, engines.OUTCOME_PLAN_DOES_NOT_FIT)
                msg, doc = completion(h)
                self.assertEqual(doc["payload"]["outcome"], "plan_does_not_fit")
                self.assertEqual(doc["payload"]["refusedEdits"], [{"edit": 19, "code": code}])
                self.assertIsNone(doc["payload"]["checkOutcome"])
                self.assertEqual(h.container.names(req.paths.out_prefix), [])  # never applied, no output blob
                self.assertEqual([m.settled for m in h.receiver.completed], ["completed"])  # non-retryable
                self.assertEqual(h.request_sender.scheduled, [])
                self.assertEqual(json.loads(h.container.blobs[req.paths.marker][0])["state"], "completed")
                self.assertNotIn(SECRET_FIND.encode(), everything_sent(h))

    def test_the_engine_stderr_really_quotes_the_text_that_is_kept_out(self):
        # Must-fire control for the grep above: the engine's own stderr does contain the find string.
        plan = golden_plan({"para": golden_plan()["edits"][0]["para"], "find": SECRET_FIND, "comment": "probe"})
        with tempfile.TemporaryDirectory() as tmp:
            p = os.path.join(tmp, "plan.json")
            with open(p, "w", encoding="utf-8") as f:
                json.dump(plan, f)
            settings = config().engine_settings
            rc, _, err = engines._run(settings, "ooxml_redline.py",
                                      [p, os.path.join(fakes.CORPUS, "nda-01-bluefin-harbor-mutual.docx"),
                                       os.path.join(tmp, "r.docx"), "--dry-run"], time.monotonic() + 120)
        self.assertEqual(rc, 3)
        self.assertIn(SECRET_FIND[:30].encode(), err)


class RefusalMappingTests(unittest.TestCase):
    def stderr(self, error, **keys):
        return json.dumps({"error": error, **keys}).encode()

    def test_master_messages_map_to_the_closed_code_set(self):
        cases = [
            ("edit 3: paragraph key 'Term. This' matched 0 paragraphs", 3, "para_not_found"),
            ("edit 3: paragraph key 'Term. This' matched 2 paragraphs", 3, "para_not_unique"),
            ("edit 4: find 'x y' occurs 0 times in the visible paragraph text", 4, "not_found"),
            ("edit 4: after 'x y' occurs 3 times in the visible paragraph text", 4, "not_unique"),
            ("edit 5: find 'x' is not in the paragraph's editable text (inside a revision)", 5, "in_existing_markup"),
            ("edit 6: find 'x' starts or ends inside a word; extend it to whole words", 6, "word_boundary"),
            ("edit 6: old '3' starts or ends inside a word", 6, "word_boundary"),
            ("edit 7: old '3' must occur exactly once inside find '(3) years'", 7, "old_not_in_find"),
            ("edit 8: span crosses existing ins (a tracked change)", 8, "crosses_markup"),
            ("edit 9: span crosses a field or drawing; narrow the edit", 9, "crosses_field_or_drawing"),
            ("edit 9: cannot split inside a non-text run", 9, "crosses_field_or_drawing"),
            ("edit 1: span is inside a field result (it would be lost)", 1, "in_field_result"),
            ("edit 1: anchor ends inside a field result (the insertion would be lost when Word updates)", 1,
             "in_field_result"),
            ("edit 2: find '*' left nothing after the section number and heading", 2, "nothing_after_heading"),
            ("bookmark 'NDA_Sec10' already exists in the document; choose another name", None, "bookmark_exists"),
            ("edit 0: unknown key(s) ['text']; edit 2: 'para' is required", None, "plan_invalid"),
            ("plan must be a JSON object", None, "plan_invalid"),
        ]
        for error, index, code in cases:
            with self.subTest(error=error):
                r = engines.refusal_from_stderr(self.stderr(error))
                self.assertEqual((r.edit, r.code), (index, code))

    def test_garbage_is_unrecognised_never_an_exception(self):
        for raw in (b"", b"Traceback (most recent call last)", b'{"error": 5}', b"\xff\xfe", b"[]"):
            self.assertEqual(engines.refusal_from_stderr(raw).code, "unrecognised")

    def test_structured_keys_are_preferred_when_present(self):
        r = engines.refusal_from_stderr(self.stderr("edit 4: find 'x' ... clause text", code="crosses_markup",
                                                    edit=4))
        self.assertEqual((r.edit, r.code), (4, "crosses_markup"))
        b = engines.refusal_from_stderr(self.stderr("bookmark ...", code="bookmark_exists", bookmark="NDA_Sec10"))
        self.assertIsInstance(b, engines.RefusedBookmark)
        self.assertEqual((b.bookmark, b.code), ("NDA_Sec10", "bookmark_exists"))
        self.assertEqual(engines.refusal_from_stderr(self.stderr("edit 0: unknown key(s)")).code, "plan_invalid")
        self.assertEqual(engines.refusal_from_stderr(self.stderr("x", code="new_code", edit=1)).code, "unrecognised")
        self.assertEqual(engines.refusal_from_stderr(self.stderr("x", code=None)).code, "plan_invalid")
        bad_name = engines.refusal_from_stderr(self.stderr("x", code="bookmark_exists", bookmark="not a name"))
        self.assertEqual((bad_name.edit, bad_name.code), (None, "bookmark_exists"))


# Stand-ins for the accelerator's proposed skip-and-report contract (not on master yet). They read their behaviour
# from the plan file because the job scrubs INTAKE_* variables from an engine's environment.
FAKE_ENGINES = {
    "ooxml_redline.py": '''
        import json, shutil, sys
        a = sys.argv[1:]
        if "--help" in a:
            print("usage: ooxml_redline.py PLAN DOCX REDLINE [--external E] [--clean C] [--dry-run] "
                  "[--skip-unplaceable REPORT]"); sys.exit(0)
        plan, src, red = a[0], a[1], a[2]
        opt = {a[i]: a[i + 1] for i in range(3, len(a) - 1) if a[i].startswith("--") and a[i] != "--dry-run"}
        if "--skip-unplaceable" not in opt: sys.exit(9)
        report = json.load(open(plan))["fake_report"]
        json.dump(report, open(opt["--skip-unplaceable"], "w"))
        if "--dry-run" not in a:
            for out in (red, opt["--external"], opt["--clean"]): shutil.copy(src, out)
        print(json.dumps({"edits": 2, "refused": len(report.get("refused", []))}))
    ''',
    "summary_table.py": '''
        import shutil, sys
        a = sys.argv[1:]
        if "--help" in a: print("usage: summary_table.py PLAN REDLINE [--out O] [--intro I] [--refused R]"); sys.exit(0)
        if "--refused" not in a: sys.exit(2)
        shutil.copy(a[1], a[a.index("--out") + 1])
    ''',
    "check_outputs.py": '''
        import json, sys
        a = sys.argv[1:]
        if "--help" in a: print("usage: check_outputs.py PLAN INTERNAL CLEAN --original O [--refused R]"); sys.exit(0)
        if "--refused" not in a: sys.exit(2)
        print(json.dumps({"checks": [{"name": "plan", "ok": True, "detail": ""}]}))
    ''',
}


class SkipAndReportTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        for name, code in FAKE_ENGINES.items():
            with open(os.path.join(self.tmp.name, name), "w", encoding="utf-8") as f:
                f.write(textwrap.dedent(code))
        self.cfg = main.Config(**{**config().__dict__, "engine_settings": engines.EngineSettings(
            engines_dir=self.tmp.name, summary_intro=fakes.SUMMARY_INTRO, skip_and_report=True)})

    def tearDown(self):
        self.tmp.cleanup()

    def _run(self, report):
        h = Harness()
        body, req = stage_redline(h, {"fake_report": report})
        return h, req, main.process_message(fakes.received(body), h.queue, h.staging, self.cfg)

    def test_refused_edits_are_reported_as_codes_and_the_report_is_an_output(self):
        report = {"engine": "ooxml_redline", "report_version": 1, "edits_total": 2, "edits_applied": 1,
                  "refused": [{"edit": 1, "code": "crosses_markup"}],
                  "bookmarks_refused": [{"bookmark": "NDA_Sec10", "code": "bookmark_exists"}]}
        h, req, result = self._run(report)
        self.assertEqual(result, engines.OUTCOME_COMPLETED)
        _, doc = completion(h)
        self.assertEqual(doc["payload"]["refusedEdits"], [{"edit": 1, "code": "crosses_markup"}])
        self.assertEqual(doc["payload"]["refusedBookmarks"], [{"bookmark": "NDA_Sec10", "code": "bookmark_exists"}])
        self.assertEqual(doc["payload"]["checkOutcome"], "passed")
        self.assertIn(req.paths.output(engines.REFUSED_REPORT), h.container.names(req.paths.out_prefix))

    def test_an_unknown_code_is_a_contract_error_dead_lettered_poisoned(self):
        report = {"engine": "ooxml_redline", "report_version": 1, "edits_total": 1, "edits_applied": 0,
                  "refused": [{"edit": 0, "code": "something_new"}], "bookmarks_refused": []}
        h, req, result = self._run(report)
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
                    dict(good, refused=[{"edit": 2, "code": "bookmark_exists"}]),
                    dict(good, bookmarks_refused=[{"bookmark": "bad name", "code": "bookmark_exists"}])):
            self.assertIsNone(engines.parse_refused_report(json.dumps(bad).encode()))

    def test_the_mode_is_detected_from_the_engines_help(self):
        self.assertTrue(engines.engines_support_skip_mode(self.cfg.engine_settings))
        self.assertFalse(engines.engines_support_skip_mode(config().engine_settings))  # master engines

    def test_main_refuses_to_start_with_the_mode_on_and_master_engines(self):
        env = {"AZURE_CLIENT_ID": "cid", "INTAKE_SERVICEBUS_FQDN": "ns", "INTAKE_REQUEST_QUEUE": "q1",
               "INTAKE_COMPLETION_QUEUE": "q2", "INTAKE_STAGING_ACCOUNT_URL": "https://a",
               "INTAKE_STAGING_CONTAINER": "c", "INTAKE_ENGINE_SKIP_AND_REPORT": "true",
               "INTAKE_ENGINES_DIR": fakes.ENGINES_DIR, "INTAKE_SUMMARY_INTRO": fakes.SUMMARY_INTRO}
        with mock.patch.dict(os.environ, env, clear=True), \
                mock.patch.object(azure_io, "open_credential", side_effect=AssertionError("must not connect")):
            self.assertEqual(main.main(), 2)
        main.setup_logging(io.StringIO())


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
        self.assertFalse(cfg.engine_settings.skip_and_report)  # the seam is OFF by default
        self.assertGreater(cfg.lock_renewal_seconds, cfg.job_timeout_seconds)

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
