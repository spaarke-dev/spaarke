"""The Job Contract envelope the engine job accepts, and the staging paths derived from it.

ADR-004 Amendment A2 §2.1: the JSON envelope is the contract (camelCase, as the BFF's JobContract.cs serialises it);
no shared types. Anything that is not a valid envelope is rejected with a code (never with message content), and the
caller dead-letters it `InvalidFormat` with no outputs.

Request (BFF -> intake-engine-requests), job type `IntakeEngineRun`:
  {"jobId": GUID, "jobType": "IntakeEngineRun", "subjectId": <runId>, "correlationId": "...",
   "idempotencyKey": "IntakeEngine:<runId>:<operation>:<dispatchAttempt>", "attempt": int, "maxAttempts": int,
   "payload": {"operation": "segment" | "redline", "runId": GUID, "dispatchAttempt": int},
   "createdAt": ISO-8601 [, "isAtMaxAttempts": bool]}
  `isAtMaxAttempts` is accepted because System.Text.Json serialises JobContract's computed property of that name.
  The payload is a CLOSED set of ids: no blob paths, no document text, no plan. Every path is derived here from the
  ids, so a message can never point the job at a blob of its choosing.

Staging layout (container INTAKE_STAGING_CONTAINER; the BFF writes `in/`, the job writes `out/` and `markers/`):
  {runId}/in/source.docx                                  the draft (one per run)
  {runId}/in/redline/{dispatchAttempt}/plan.json          the edit plan for one redline attempt
  {runId}/out/segment/{dispatchAttempt}/clauses.json      segment_clauses.py output
  {runId}/out/redline/{dispatchAttempt}/redline-internal.docx   tracked changes + comments + page-1 summary
  {runId}/out/redline/{dispatchAttempt}/redline-external.docx   tracked changes, none of our comments
  {runId}/out/redline/{dispatchAttempt}/clean.docx              every revision accepted
  {runId}/out/redline/{dispatchAttempt}/check-report.json       check_outputs.py output
  {runId}/out/redline/{dispatchAttempt}/refused-report.json     engine skip-and-report mode only
  {runId}/markers/{operation}/{dispatchAttempt}.json      the atomic per-message marker (1:1 with idempotencyKey)
"""
import json
import re
import uuid
from dataclasses import dataclass
from datetime import datetime

REQUEST_JOB_TYPE = "IntakeEngineRun"
COMPLETION_JOB_TYPE = "IntakeEngineCompleted"
OPERATIONS = ("segment", "redline")
MAX_BODY_BYTES = 64 * 1024
MAX_DISPATCH_ATTEMPT = 1000

_REQUIRED = {
    "jobId": str, "jobType": str, "subjectId": str, "correlationId": str, "idempotencyKey": str,
    "attempt": int, "maxAttempts": int, "payload": dict, "createdAt": str,
}
_OPTIONAL = {"isAtMaxAttempts": bool}
_PAYLOAD = {"operation": str, "runId": str, "dispatchAttempt": int}
# A correlation id is an identifier: GUIDs, W3C trace ids, BFF TraceIdentifiers. Nothing that could carry text.
_CORRELATION_RE = re.compile(r"^[A-Za-z0-9._:|-]{1,128}$")


class InvalidEnvelope(ValueError):
    """The message is not a valid engine request. `code` is a fixed identifier, safe to log and dead-letter with."""

    def __init__(self, code):
        super().__init__(code)
        self.code = code


@dataclass(frozen=True)
class StagingPaths:
    run_id: str
    operation: str
    dispatch_attempt: int

    @property
    def source(self):
        return f"{self.run_id}/in/source.docx"

    @property
    def plan(self):
        return f"{self.run_id}/in/redline/{self.dispatch_attempt}/plan.json"

    @property
    def out_prefix(self):
        return f"{self.run_id}/out/{self.operation}/{self.dispatch_attempt}/"

    def output(self, name):
        return self.out_prefix + name

    @property
    def marker(self):
        return f"{self.run_id}/markers/{self.operation}/{self.dispatch_attempt}.json"


@dataclass(frozen=True)
class EngineRequest:
    job_id: str
    job_type: str
    correlation_id: str
    idempotency_key: str
    operation: str
    run_id: str
    dispatch_attempt: int
    attempt: int
    max_attempts: int

    @property
    def paths(self):
        return StagingPaths(self.run_id, self.operation, self.dispatch_attempt)


def idempotency_key(run_id, operation, dispatch_attempt):
    """The producer's deterministic key (task 014 sends exactly this)."""
    return f"IntakeEngine:{run_id}:{operation}:{dispatch_attempt}"


def safe_correlation_id(value):
    """`value` if it is a well-formed identifier, else None. Used for broker properties of unvalidated messages."""
    if isinstance(value, bytes):
        try:
            value = value.decode("ascii")
        except UnicodeDecodeError:
            return None
    return value if isinstance(value, str) and _CORRELATION_RE.match(value) else None


def _canonical_guid(value, code):
    try:
        parsed = uuid.UUID(value)
    except (ValueError, AttributeError, TypeError):
        raise InvalidEnvelope(code) from None
    if str(parsed) != value.lower():  # only the hyphenated "D" form, as Guid.ToString() writes it
        raise InvalidEnvelope(code)
    return str(parsed)


def _check_types(obj, spec, prefix):
    for name, kind in spec.items():
        value = obj[name]
        # bool is an int subclass in Python; a JSON true is never a count.
        if kind is int and (isinstance(value, bool) or not isinstance(value, int)):
            raise InvalidEnvelope(f"wrong_type:{prefix}{name}")
        if kind is not int and not isinstance(value, kind):
            raise InvalidEnvelope(f"wrong_type:{prefix}{name}")


def parse_request(body):
    """Validate a request body (bytes) and return an EngineRequest, or raise InvalidEnvelope(code)."""
    if not isinstance(body, (bytes, bytearray)) or len(body) > MAX_BODY_BYTES:
        raise InvalidEnvelope("body_size")
    try:
        doc = json.loads(bytes(body).decode("utf-8"))
    except (UnicodeDecodeError, ValueError):
        raise InvalidEnvelope("not_json") from None
    if not isinstance(doc, dict):
        raise InvalidEnvelope("not_object")
    for name in _REQUIRED:
        if name not in doc:
            raise InvalidEnvelope(f"missing_field:{name}")
    unknown = sorted(set(doc) - set(_REQUIRED) - set(_OPTIONAL))
    if unknown:
        raise InvalidEnvelope("unknown_field")  # the names themselves are producer text: not echoed
    _check_types(doc, _REQUIRED, "")
    _check_types({k: v for k, v in doc.items() if k in _OPTIONAL}, {k: v for k, v in _OPTIONAL.items() if k in doc}, "")

    if doc["jobType"] != REQUEST_JOB_TYPE:
        raise InvalidEnvelope("wrong_job_type")
    job_id = _canonical_guid(doc["jobId"], "bad_job_id")
    if not _CORRELATION_RE.match(doc["correlationId"]):
        raise InvalidEnvelope("bad_correlation_id")
    if doc["attempt"] < 1 or doc["maxAttempts"] < 1:
        raise InvalidEnvelope("bad_attempt")
    try:
        datetime.fromisoformat(doc["createdAt"].replace("Z", "+00:00"))
    except ValueError:
        raise InvalidEnvelope("bad_created_at") from None

    payload = doc["payload"]
    for name in _PAYLOAD:
        if name not in payload:
            raise InvalidEnvelope(f"missing_field:payload.{name}")
    if set(payload) - set(_PAYLOAD):
        raise InvalidEnvelope("payload_unknown_field")  # ids only: content, paths or flags are refused
    _check_types(payload, _PAYLOAD, "payload.")
    operation = payload["operation"]
    if operation not in OPERATIONS:
        raise InvalidEnvelope("unknown_operation")
    run_id = _canonical_guid(payload["runId"], "bad_run_id")
    attempt = payload["dispatchAttempt"]
    if not 1 <= attempt <= MAX_DISPATCH_ATTEMPT:
        raise InvalidEnvelope("bad_dispatch_attempt")
    if doc["subjectId"].lower() != run_id:
        raise InvalidEnvelope("subject_mismatch")
    if doc["idempotencyKey"] != idempotency_key(run_id, operation, attempt):
        raise InvalidEnvelope("idempotency_key_mismatch")

    return EngineRequest(
        job_id=job_id, job_type=doc["jobType"], correlation_id=doc["correlationId"],
        idempotency_key=doc["idempotencyKey"], operation=operation, run_id=run_id, dispatch_attempt=attempt,
        attempt=doc["attempt"], max_attempts=doc["maxAttempts"])


def completion_body(request, result, created_at):
    """The completion Job Contract (camelCase, JobContract.cs shape) for `request` and an engines.EngineResult.

    Ids and codes only (task 014 treats it as untrusted and derives every path from runId): no blob path, no text.
    `jobId` is derived from the idempotency key so a re-sent completion is recognisably the same job.
    """
    payload = {
        "operation": request.operation,
        "runId": request.run_id,
        "dispatchAttempt": request.dispatch_attempt,
        "outcome": result.outcome,
        "checkOutcome": result.check_outcome,
        "refusedEdits": [{"edit": r.edit, "code": r.code} for r in result.refused_edits],
        "refusedBookmarks": [{"bookmark": b.bookmark, "code": b.code} for b in result.refused_bookmarks],
    }
    body = {
        "jobId": str(uuid.uuid5(uuid.NAMESPACE_URL, "spaarke:" + COMPLETION_JOB_TYPE + ":" + request.idempotency_key)),
        "jobType": COMPLETION_JOB_TYPE,
        "subjectId": request.run_id,
        "correlationId": request.correlation_id,
        "idempotencyKey": request.idempotency_key,
        "attempt": 1,
        "maxAttempts": 1,
        "payload": payload,
        "createdAt": created_at,
    }
    return json.dumps(body, separators=(",", ":")).encode("utf-8")
