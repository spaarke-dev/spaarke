"""Envelope validation, the atomic marker and deterministic paths (FR-01 b, c; ADR-004 A2 §2.1-2.2)."""
import io
import json
import os
import sys
import unittest
from datetime import timedelta

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from entrypoint import azure_io, engines, main  # noqa: E402
from entrypoint.envelope import InvalidEnvelope, StagingPaths, parse_request  # noqa: E402
from tests import fakes  # noqa: E402

SENTINEL = "Confidential Information means the SENTINEL clause text"


def config():
    return main.Config(
        client_id="cid", servicebus_fqdn="ns.servicebus.windows.net", request_queue="intake-engine-requests",
        completion_queue="intake-engine-completions", staging_account_url="https://acct.blob.core.windows.net",
        staging_container="intake-staging",
        engine_settings=engines.EngineSettings(engines_dir=fakes.ENGINES_DIR, summary_intro=fakes.SUMMARY_INTRO))


def fake_engines(calls):
    """Stands in for main._run_engines: deterministic outputs, records each real run."""
    def run(request, staging, cfg, workdir, deadline):
        calls.append(request.idempotency_key)
        outputs = {}
        for name in (engines.INTERNAL, engines.EXTERNAL, engines.CLEAN, engines.CHECK_REPORT):
            path = os.path.join(workdir, name)
            with open(path, "wb") as f:
                f.write(f"{request.run_id}:{name}".encode())
            outputs[name] = path
        return engines.EngineResult(engines.OUTCOME_COMPLETED, check_outcome=engines.CHECK_PASSED, outputs=outputs)
    return run


class Harness:
    def __init__(self):
        self.container = fakes.FakeContainer()
        self.staging = azure_io.BlobStaging(self.container)
        self.receiver = fakes.FakeReceiver()
        self.request_sender, self.completion_sender = fakes.FakeSender(), fakes.FakeSender()
        self.queue = azure_io.EngineQueue(self.receiver, self.request_sender, self.completion_sender)
        self.calls = []
        self.logs = io.StringIO()
        main.setup_logging(self.logs)

    def process(self, message, **kw):
        kw.setdefault("run_engines", fake_engines(self.calls))
        return main.process_message(message, self.queue, self.staging, config(), **kw)


class EnvelopeTests(unittest.TestCase):
    def test_accepts_the_bff_shape_for_both_operations(self):
        for op in ("segment", "redline"):
            body, sent = fakes.envelope(operation=op, dispatch_attempt=2)
            req = parse_request(body)
            self.assertEqual((req.operation, req.run_id, req.dispatch_attempt), (op, sent["subjectId"], 2))
            self.assertEqual(req.correlation_id, sent["correlationId"])

    def test_rejects_with_a_code(self):
        cases = {
            "missing_field:correlationId": dict(drop=("correlationId",)),
            "missing_field:idempotencyKey": dict(drop=("idempotencyKey",)),
            "missing_field:payload": dict(drop=("payload",)),
            "wrong_job_type": dict(jobType="IntakeEngineCompleted"),
            "payload_unknown_field": dict(payload_extra={"text": SENTINEL}),
            "unknown_field": dict(clauseText=SENTINEL),
            "unknown_operation": dict(operation="summarise"),
            "wrong_type:attempt": dict(attempt=True),
            "bad_correlation_id": dict(correlation_id="not an id; " + SENTINEL),
        }
        for code, kw in cases.items():
            with self.subTest(code=code):
                body, _ = fakes.envelope(**kw)
                with self.assertRaises(InvalidEnvelope) as ctx:
                    parse_request(body)
                self.assertEqual(ctx.exception.code, code)
                self.assertNotIn("SENTINEL", ctx.exception.code)

    def test_rejects_a_blob_path_in_the_payload(self):
        body, _ = fakes.envelope(payload_extra={"sourceBlob": "other-run/in/source.docx"})
        with self.assertRaises(InvalidEnvelope):
            parse_request(body)

    def test_rejects_mismatched_ids(self):
        body, sent = fakes.envelope()
        for field, value, code in (("subjectId", "00000000-0000-0000-0000-000000000001", "subject_mismatch"),
                                   ("idempotencyKey", "IntakeEngine:x", "idempotency_key_mismatch")):
            doc = dict(sent, **{field: value})
            with self.assertRaises(InvalidEnvelope) as ctx:
                parse_request(json.dumps(doc).encode())
            self.assertEqual(ctx.exception.code, code)

    def test_rejects_non_json_and_oversized_bodies(self):
        for body, code in ((b"\xff\xfe", "not_json"), (b"[1]", "not_object"), (b"{" + b" " * 70000 + b"}",
                                                                               "body_size")):
            with self.assertRaises(InvalidEnvelope) as ctx:
                parse_request(body)
            self.assertEqual(ctx.exception.code, code)


class InvalidEnvelopeHandlingTests(unittest.TestCase):
    def test_dead_lettered_invalid_format_with_no_outputs_and_no_content(self):
        for kw in (dict(drop=("jobType",)), dict(jobType="SomethingElse"),
                   dict(payload_extra={"planText": SENTINEL}), dict(operation="summarise")):
            with self.subTest(kw=kw):
                h = Harness()
                body, _ = fakes.envelope(**kw)
                self.assertEqual(h.process(fakes.received(body)), "invalid_format")
                (_, reason, description), = h.receiver.dead_lettered
                self.assertEqual(reason, "InvalidFormat")  # explicit dead-letter, not lock expiry
                self.assertNotIn("SENTINEL", description)
                self.assertEqual(h.container.writes, [])  # no marker, no output
                self.assertEqual(h.calls, [])
                self.assertEqual(h.completion_sender.sent + h.request_sender.sent, [])
                self.assertNotIn("SENTINEL", h.logs.getvalue())


class MarkerTests(unittest.TestCase):
    def test_same_message_twice_runs_once_with_one_set_of_outputs(self):
        h = Harness()
        body, sent = fakes.envelope()
        self.assertEqual(h.process(fakes.received(body)), engines.OUTCOME_COMPLETED)
        outputs_after_first = {n: h.container.blobs[n][0] for n in h.container.names(f"{sent['subjectId']}/out/")}
        self.assertEqual(h.process(fakes.received(body, delivery_count=1)), "duplicate")

        self.assertEqual(len(h.calls), 1)
        markers = [w for w in h.container.writes if "/markers/" in w[0]]
        self.assertEqual(sum(1 for w in markers if w[1] is not None and w[1].name == "IfMissing"), 1)
        self.assertEqual({n: h.container.blobs[n][0] for n in h.container.names(f"{sent['subjectId']}/out/")},
                         outputs_after_first)
        self.assertEqual(len(h.completion_sender.sent), 1)
        self.assertEqual([m.settled for m in h.receiver.completed], ["completed", "completed"])

    def test_a_fresh_claim_by_another_execution_is_retried_later_not_run(self):
        h = Harness()
        body, sent = fakes.envelope()
        req = parse_request(body)
        h.container.put(req.paths.marker, json.dumps({"state": "claimed"}).encode())
        self.assertEqual(h.process(fakes.received(body)), "retry_scheduled")
        self.assertEqual(h.calls, [])
        self.assertEqual(len(h.request_sender.scheduled), 1)

    def test_a_claim_older_than_the_job_lifetime_is_taken_over(self):
        h = Harness()
        body, _ = fakes.envelope()
        req = parse_request(body)
        h.container.put(req.paths.marker, json.dumps({"state": "claimed"}).encode(),
                        last_modified=fakes.utcnow() - timedelta(seconds=config().lock_renewal_seconds + 5))
        self.assertEqual(h.process(fakes.received(body)), engines.OUTCOME_COMPLETED)
        self.assertEqual(len(h.calls), 1)
        self.assertEqual(json.loads(h.container.blobs[req.paths.marker][0])["state"], "completed")

    def test_a_released_marker_is_reclaimed(self):
        h = Harness()
        body, _ = fakes.envelope()
        req = parse_request(body)
        h.container.put(req.paths.marker, json.dumps({"state": "released"}).encode())
        self.assertEqual(h.process(fakes.received(body)), engines.OUTCOME_COMPLETED)
        self.assertEqual(len(h.calls), 1)


class PathTests(unittest.TestCase):
    def test_paths_are_derived_from_ids_only(self):
        p = StagingPaths("6f1c1d2e-0000-4000-8000-000000000001", "redline", 3)
        self.assertEqual(p.source, "6f1c1d2e-0000-4000-8000-000000000001/in/source.docx")
        self.assertEqual(p.plan, "6f1c1d2e-0000-4000-8000-000000000001/in/redline/3/plan.json")
        self.assertEqual(p.output("clean.docx"), "6f1c1d2e-0000-4000-8000-000000000001/out/redline/3/clean.docx")
        self.assertEqual(p.marker, "6f1c1d2e-0000-4000-8000-000000000001/markers/redline/3.json")
        s = StagingPaths("6f1c1d2e-0000-4000-8000-000000000001", "segment", 1)
        self.assertEqual(s.output("clauses.json"), "6f1c1d2e-0000-4000-8000-000000000001/out/segment/1/clauses.json")

    def test_outputs_land_at_the_derived_paths_and_a_replay_overwrites(self):
        h = Harness()
        body, sent = fakes.envelope()
        req = parse_request(body)
        h.process(fakes.received(body))
        expected = sorted(req.paths.output(n) for n in engines.REDLINE_OUTPUTS)
        self.assertEqual(h.container.names(req.paths.out_prefix), expected)
        # a released marker (a retry) re-runs and overwrites the same names; nothing new appears
        h.container.put(req.paths.marker, json.dumps({"state": "released"}).encode())
        h.process(fakes.received(body, delivery_count=1))
        self.assertEqual(h.container.names(req.paths.out_prefix), expected)
        self.assertEqual(len(h.container.names(sent["subjectId"] + "/")), len(expected) + 1)  # + the marker


if __name__ == "__main__":
    unittest.main()
