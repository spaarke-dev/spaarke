"""Intake engine job entrypoint: one execution receives ONE message from intake-engine-requests, processes it, settles it
and exits (Container Apps Job, event-triggered by KEDA; replicaTimeout is the hard stop, replicaRetryLimit 0).

ADR-004 Amendment A2 §2 guarantees, and where each one lives:
  1 Envelope      envelope.parse_request; anything else -> dead-letter `InvalidFormat`, nothing written.
  2 Idempotency   the producer's deterministic idempotencyKey; an atomic marker blob (conditional create,
                  If-None-Match: *) taken BEFORE any output is written; outputs at deterministic paths derived from
                  the ids (envelope.StagingPaths), so a replay overwrites and never duplicates.
  3 Correlation   correlationId (with jobId, jobType, operation, runId, dispatchAttempt) on every log record and span
                  of the message (a LogRecord factory), and on every message sent.
  4 Outcome       `Completed` / `Failed` / `Poisoned` logged as `JobOutcome` and exported to App Insights; a completion
                  Job Contract to intake-engine-completions when the BFF waits on a result.
  5 Retries       logical delivery = PriorDeliveryCount + broker delivery; a retryable failure schedules a copy
                  JobContract.RetryDelay later (1, 2, 4, 8 minutes) and completes the original; at logical delivery
                  MaxDeliveryCount (5) it dead-letters `MaxRetriesExceeded` (`ProcessingError` for an unexpected
                  exception). Dead-letter reasons are the BFF's vocabulary (ServiceBusJobProcessor.cs).

Marker states (blob {runId}/markers/{operation}/{dispatchAttempt}.json):
  claimed   - taken by the execution processing the message
  completed - the completion was sent: a later delivery of the same message is a duplicate and does nothing
  released  - the execution scheduled a retry or dead-lettered: the next delivery may claim it (compare-and-swap)
A `claimed` marker older than the job timeout belongs to an execution that was killed (replicaTimeout), so it may be
taken over, also by compare-and-swap; a younger one means another execution may be running, so the message is
retried later rather than run twice at once.

Outcomes the completion carries (envelope.completion_body), all non-retryable:
  completed          the engines ran; checkOutcome passed / failed (a failed check is the run's result, task 014).
                     redline, skip-and-report mode (the default): refusedEdits = [{edit, code}] for every edit the
                     engine could not place (possibly all of them), refusedBookmarks likewise; refused-report.json is
                     uploaded beside the documents
  plan_does_not_fit  the plan breaks the engine contract (ooxml_redline or summary_table exit 3): both lists empty.
                     In default mode (switch off) also the first edit that does not fit: refusedEdits = [{edit, code}].
                     Nothing written
  input_invalid      an engine exit 2 (unreadable .docx or plan); nothing written
  contract_error     an engine broke its documented contract (report, exit-3 stderr, check_outputs exit 2): also
                     dead-lettered `Poisoned` so the DLQ alert fires
"""
import contextvars
import json
import logging
import os
import shutil
import sys
import tempfile
import time
from dataclasses import dataclass
from datetime import datetime, timedelta, timezone

from . import azure_io, engines
from .envelope import InvalidEnvelope, completion_body, parse_request, safe_correlation_id

LOGGER_NAME = "intake_engine"
log = logging.getLogger(LOGGER_NAME)

# JobContract.cs
MAX_DELIVERY_COUNT = 5
RETRY_BASE = timedelta(minutes=1)
RETRY_MAX = timedelta(minutes=15)

# Outcome telemetry names (JobOutcome.cs / JobStatus).
COMPLETED, FAILED, POISONED = "Completed", "Failed", "Poisoned"


def retry_delay(failed_delivery):
    """JobContract.RetryDelay: 1, 2, 4, 8 minutes after deliveries 1-4, capped at 15. Below 1 counts as 1."""
    exponent = min(max(failed_delivery, 1), 31) - 1
    delay = RETRY_BASE * (2 ** exponent)
    return RETRY_MAX if delay >= RETRY_MAX else delay


# --- configuration -----------------------------------------------------------------------------------------------

@dataclass(frozen=True)
class Config:
    client_id: str
    servicebus_fqdn: str
    request_queue: str
    completion_queue: str
    staging_account_url: str
    staging_container: str
    max_delivery_count: int = MAX_DELIVERY_COUNT
    job_timeout_seconds: int = 600
    # Kept back from the job timeout so the message can still be settled (retry copy or dead-letter) after a slow
    # engine run is cut off, before replicaTimeout kills the replica.
    settle_reserve_seconds: int = 60
    receive_wait_seconds: int = 30
    engine_settings: engines.EngineSettings = None

    @property
    def engine_budget_seconds(self):
        return max(self.job_timeout_seconds - self.settle_reserve_seconds, 1)

    @property
    def lock_renewal_seconds(self):
        # Longer than the whole execution can live, so the lock never lapses while this execution holds it.
        return self.job_timeout_seconds + self.settle_reserve_seconds


class ConfigError(Exception):
    pass


def _flag(value, default):
    v = (value or "").strip().lower()
    if not v:
        return default
    if v in ("0", "false", "no", "off"):
        return False
    if v in ("1", "true", "yes", "on"):
        return True
    raise ConfigError("INTAKE_ENGINE_SKIP_AND_REPORT must be true or false")


def load_config(env=None):
    env = os.environ if env is None else env
    required = ("AZURE_CLIENT_ID", "INTAKE_SERVICEBUS_FQDN", "INTAKE_REQUEST_QUEUE", "INTAKE_COMPLETION_QUEUE",
                "INTAKE_STAGING_ACCOUNT_URL", "INTAKE_STAGING_CONTAINER")
    missing = [k for k in required if not env.get(k)]
    if missing:
        raise ConfigError("missing settings: " + ", ".join(missing))  # setting names only
    if env["INTAKE_COMPLETION_QUEUE"] == env["INTAKE_REQUEST_QUEUE"]:
        raise ConfigError("the completion queue must not be the request queue")
    try:
        max_delivery = int(env.get("INTAKE_MAX_DELIVERY_COUNT", MAX_DELIVERY_COUNT))
        timeout = int(env.get("INTAKE_JOB_TIMEOUT_SECONDS", 600))
    except ValueError:
        raise ConfigError("INTAKE_MAX_DELIVERY_COUNT and INTAKE_JOB_TIMEOUT_SECONDS must be integers") from None
    if max_delivery < 1 or timeout < 120:
        raise ConfigError("INTAKE_MAX_DELIVERY_COUNT must be >= 1 and INTAKE_JOB_TIMEOUT_SECONDS >= 120")
    here = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    return Config(
        client_id=env["AZURE_CLIENT_ID"], servicebus_fqdn=env["INTAKE_SERVICEBUS_FQDN"],
        request_queue=env["INTAKE_REQUEST_QUEUE"], completion_queue=env["INTAKE_COMPLETION_QUEUE"],
        staging_account_url=env["INTAKE_STAGING_ACCOUNT_URL"], staging_container=env["INTAKE_STAGING_CONTAINER"],
        max_delivery_count=max_delivery, job_timeout_seconds=timeout,
        engine_settings=engines.EngineSettings(
            engines_dir=env.get("INTAKE_ENGINES_DIR", os.path.join(here, "engines")),
            summary_intro=env.get("INTAKE_SUMMARY_INTRO", os.path.join(here, "assets", "summary-intro.md")),
            # Owner 2026-11-26, "A with B as backup": skip-and-report is ON. Setting it false restores the engines'
            # all-or-nothing default mode (exit 3 on the first edit that does not fit); nothing else changes.
            skip_and_report=_flag(env.get("INTAKE_ENGINE_SKIP_AND_REPORT"), default=True)))


# --- logging and telemetry ---------------------------------------------------------------------------------------

_CONTEXT = contextvars.ContextVar("intake_engine_context", default=None)
_CONTEXT_FIELDS = ("correlationId", "jobId", "jobType", "operation", "runId", "dispatchAttempt", "messageId")
_base_factory = logging.getLogRecordFactory()


def _record_factory(*args, **kwargs):
    """Every log record (ours and the SDKs') carries the current message's ids as attributes (A2 §2.3)."""
    record = _base_factory(*args, **kwargs)
    ctx = _CONTEXT.get() or {}
    for name in _CONTEXT_FIELDS:
        if not hasattr(record, name):
            setattr(record, name, ctx.get(name, "-"))
    return record


class JsonLineFormatter(logging.Formatter):
    # Exceptions are logged as `exceptionType` only, never with exc_info: a message or a traceback could carry text
    # from a document, and the OpenTelemetry handler would export both to App Insights.
    EXTRA = ("event", "jobOutcome", "outcomeCode", "durationMs", "deliveryCount", "reason", "exceptionType")

    def format(self, record):
        line = {"ts": datetime.fromtimestamp(record.created, timezone.utc).isoformat(), "level": record.levelname,
                "logger": record.name, "message": record.getMessage()}
        for name in _CONTEXT_FIELDS + self.EXTRA:
            value = getattr(record, name, None)
            if value is not None:
                line[name] = value
        return json.dumps(line, separators=(",", ":"))


def setup_logging(stream=None):
    """One JSON-lines handler on the root logger, so the SDKs' warnings carry the message's ids too."""
    logging.setLogRecordFactory(_record_factory)
    handler = logging.StreamHandler(stream or sys.stdout)
    handler.setFormatter(JsonLineFormatter())
    root = logging.getLogger()
    root.handlers[:] = [handler]
    root.setLevel(logging.WARNING)
    log.handlers[:] = []
    log.setLevel(logging.INFO)
    log.propagate = True
    # The SDKs' own loggers: warnings only (their DEBUG/INFO lines include request URLs and headers).
    for name in ("azure", "azure.core.pipeline.policies.http_logging_policy", "azure.identity", "azure.servicebus",
                 "uamqp", "urllib3"):
        logging.getLogger(name).setLevel(logging.WARNING)
    return handler


def setup_telemetry(env=None):
    """Azure Monitor OpenTelemetry distro (the BFF's UseAzureMonitor() pipeline), when the stamp configures it."""
    env = os.environ if env is None else env
    connection_string = env.get("APPLICATIONINSIGHTS_CONNECTION_STRING")
    if not connection_string:
        return False
    from azure.monitor.opentelemetry import configure_azure_monitor
    configure_azure_monitor(connection_string=connection_string, logger_name=LOGGER_NAME,
                            enable_live_metrics=False)
    return True


def _tracer():
    try:
        from opentelemetry import trace
        return trace.get_tracer(LOGGER_NAME)
    except ImportError:  # pragma: no cover - the image always has it
        return None


def _outcome(status, code, started, **extra):
    log.info("JobOutcome %s %s", status, code, extra={
        "event": "JobOutcome", "jobOutcome": status, "outcomeCode": code,
        "durationMs": int((time.monotonic() - started) * 1000), **extra})


# --- processing one message --------------------------------------------------------------------------------------

def _now_iso(clock):
    return clock().astimezone(timezone.utc).isoformat()


def _marker(state, request, clock):
    return {"state": state, "jobId": request.job_id, "idempotencyKey": request.idempotency_key, "at": _now_iso(clock)}


def _claim(staging, request, cfg, clock):
    """Take the marker. Returns ("claimed", etag), ("duplicate", None) or ("busy", None)."""
    path = request.paths.marker
    etag = staging.create_marker(path, _marker("claimed", request, clock))
    if etag:
        return "claimed", etag
    current = staging.read_marker(path)
    if current is None:  # released and deleted between the two calls: one more conditional create
        etag = staging.create_marker(path, _marker("claimed", request, clock))
        return ("claimed", etag) if etag else ("busy", None)
    state = current.data.get("state")
    if state == "completed":
        return "duplicate", None
    stale = current.last_modified is not None and (
        clock() - current.last_modified > timedelta(seconds=cfg.lock_renewal_seconds))
    if state == "released" or stale:
        etag = staging.replace_marker(path, _marker("claimed", request, clock), current.etag)
        return ("claimed", etag) if etag else ("busy", None)
    return "busy", None


def _set_marker(staging, request, state, etag, clock):
    """Move OUR marker to `state` (compare-and-swap). Losing the race is logged, never raised."""
    if staging.replace_marker(request.paths.marker, _marker(state, request, clock), etag) is None:
        log.warning("Marker changed under this execution; not moved to %s", state, extra={"event": "MarkerLost"})


def _settle(action, what):
    try:
        action()
        return True
    except Exception as ex:  # noqa: BLE001 - a failed settlement is redelivered by the broker when the lock lapses
        log.warning("Could not %s the message; the broker redelivers it when its lock lapses", what,
                    extra={"event": "SettleFailed", "exceptionType": type(ex).__name__})
        return False


def _retry_or_dead_letter(message, request, queue, cfg, delivery, code, reason, clock, started):
    if delivery >= cfg.max_delivery_count:
        if _settle(lambda: queue.dead_letter(message, reason, f"{code}; delivery {delivery}"), "dead-letter"):
            _outcome(POISONED, reason, started, reason=reason, deliveryCount=delivery)
        return reason
    due = clock() + retry_delay(delivery)
    try:
        queue.schedule_retry(message, request, delivery, due)
    except Exception as ex:  # noqa: BLE001 - as ServiceBusJobProcessor: fall back to immediate redelivery
        log.warning("Could not schedule the retry; abandoning for immediate redelivery",
                    extra={"event": "RetryScheduleFailed", "deliveryCount": delivery, "exceptionType": type(ex).__name__})
        _settle(lambda: queue.abandon(message), "abandon")
        _outcome(FAILED, code, started, deliveryCount=delivery)
        return "abandoned"
    _settle(lambda: queue.complete(message), "complete")
    log.warning("Retry scheduled in %s after delivery %s of %s", retry_delay(delivery), delivery,
                cfg.max_delivery_count, extra={"event": "RetryScheduled", "deliveryCount": delivery})
    _outcome(FAILED, code, started, deliveryCount=delivery)
    return "retry_scheduled"


def _run_engines(request, staging, cfg, workdir, deadline):
    paths = request.paths
    source = os.path.join(workdir, "source.docx")
    staging.download_to(paths.source, source)
    out_dir = os.path.join(workdir, "out")
    os.makedirs(out_dir)
    if request.operation == "segment":
        return engines.run_segment(cfg.engine_settings, source, out_dir, deadline)
    plan = os.path.join(workdir, "plan.json")
    staging.download_to(paths.plan, plan)
    return engines.run_redline(cfg.engine_settings, plan, source, out_dir, deadline)


def process_message(message, queue, staging, cfg, clock=None, started=None, run_engines=None):
    """Process one received message end to end and settle it. Returns a short result code (for tests and logs)."""
    clock = clock or (lambda: datetime.now(timezone.utc))
    started = time.monotonic() if started is None else started
    run_engines = run_engines or _run_engines
    delivery = azure_io.logical_delivery_count(message)
    broker_correlation = safe_correlation_id(getattr(message, "correlation_id", None))
    token = _CONTEXT.set({"correlationId": broker_correlation or "-",
                          "messageId": safe_correlation_id(getattr(message, "message_id", None)) or "-"})
    try:
        try:
            request = parse_request(azure_io.body_bytes(message))
        except InvalidEnvelope as ex:
            log.error("Rejected a message that is not a valid engine request: %s", ex.code,
                      extra={"event": "InvalidEnvelope", "reason": ex.code, "deliveryCount": delivery})
            if _settle(lambda: queue.dead_letter(message, "InvalidFormat", ex.code), "dead-letter"):
                _outcome(POISONED, "InvalidFormat", started, reason=ex.code)
            return "invalid_format"

        _CONTEXT.set({"correlationId": request.correlation_id, "jobId": request.job_id, "jobType": request.job_type,
                      "operation": request.operation, "runId": request.run_id,
                      "dispatchAttempt": request.dispatch_attempt,
                      "messageId": safe_correlation_id(getattr(message, "message_id", None)) or "-"})
        log.info("Processing %s delivery %s", request.operation, delivery,
                 extra={"event": "Processing", "deliveryCount": delivery})
        tracer = _tracer()
        span_cm = tracer.start_as_current_span("IntakeEngineRun", attributes={
            "correlationId": request.correlation_id, "jobId": request.job_id, "jobType": request.job_type,
            "operation": request.operation, "runId": request.run_id},
            record_exception=False, set_status_on_exception=False) if tracer else _nullcontext()
        with span_cm:
            return _process_request(message, request, queue, staging, cfg, clock, started, run_engines, delivery)
    finally:
        _CONTEXT.reset(token)


class _nullcontext:
    def __enter__(self):
        return self

    def __exit__(self, *exc):
        return False


def _process_request(message, request, queue, staging, cfg, clock, started, run_engines, delivery):
    if delivery > cfg.max_delivery_count:  # a lock-loss redelivery past the bound: never run a sixth time
        if _settle(lambda: queue.dead_letter(message, "MaxRetriesExceeded", f"delivery {delivery}"), "dead-letter"):
            _outcome(POISONED, "MaxRetriesExceeded", started, deliveryCount=delivery)
        return "MaxRetriesExceeded"

    try:
        claim, etag = _claim(staging, request, cfg, clock)
    except Exception as ex:  # noqa: BLE001 - storage unreachable: nothing claimed, nothing to release
        log.warning("Could not take the marker", extra={"event": "MarkerError", "exceptionType": type(ex).__name__})
        return _retry_or_dead_letter(message, request, queue, cfg, delivery, "marker_error", "MaxRetriesExceeded",
                                     clock, started)
    if claim == "duplicate":
        log.info("Duplicate delivery: the marker is completed; nothing to do", extra={"event": "Duplicate"})
        _settle(lambda: queue.complete(message), "complete")
        _outcome(COMPLETED, "duplicate", started)
        return "duplicate"
    if claim == "busy":
        log.warning("Another execution holds the marker; retrying later", extra={"event": "MarkerBusy"})
        return _retry_or_dead_letter(message, request, queue, cfg, delivery, "marker_busy", "MaxRetriesExceeded",
                                     clock, started)

    workdir = tempfile.mkdtemp(prefix="intake-")
    try:
        deadline = started + cfg.engine_budget_seconds
        try:
            result = run_engines(request, staging, cfg, workdir, deadline)
            if result.outcome == engines.OUTCOME_COMPLETED:
                for name, local in sorted(result.outputs.items()):
                    with open(local, "rb") as f:
                        staging.upload(request.paths.output(name), f.read())
            created_at = _now_iso(clock)
            queue.send_completion(request, completion_body(request, result, created_at), created_at)
        except engines.EngineRetryable as ex:
            return _release_and_retry(message, request, queue, staging, cfg, delivery, etag, ex.code,
                                      "MaxRetriesExceeded", clock, started)
        except Exception as ex:  # noqa: BLE001 - unexpected: retried like the BFF's ProcessingError path
            log.error("Unexpected error while processing",
                      extra={"event": "ProcessingError", "exceptionType": type(ex).__name__})
            return _release_and_retry(message, request, queue, staging, cfg, delivery, etag, "processing_error",
                                      "ProcessingError", clock, started)
    finally:
        shutil.rmtree(workdir, ignore_errors=True)

    _set_marker(staging, request, "completed", etag, clock)
    refused = len(result.refused_edits)
    if result.outcome == engines.OUTCOME_CONTRACT_ERROR:
        if _settle(lambda: queue.dead_letter(message, "Poisoned", "contract_error"), "dead-letter"):
            _outcome(POISONED, result.outcome, started)
        return result.outcome
    _settle(lambda: queue.complete(message), "complete")
    status = COMPLETED if result.outcome == engines.OUTCOME_COMPLETED else FAILED
    _outcome(status, result.outcome if status == FAILED else f"check_{result.check_outcome or 'na'}", started,
             reason=",".join(sorted({r.code for r in result.refused_edits})) or None,
             deliveryCount=delivery)
    if refused:
        log.info("Refused edits: %s", refused, extra={"event": "RefusedEdits"})
    return result.outcome


def _release_and_retry(message, request, queue, staging, cfg, delivery, etag, code, reason, clock, started):
    try:
        _set_marker(staging, request, "released", etag, clock)
    except Exception as ex:  # noqa: BLE001 - an unreleased marker is taken over once it is older than the job timeout
        log.warning("Could not release the marker", extra={"event": "MarkerError", "exceptionType": type(ex).__name__})
    return _retry_or_dead_letter(message, request, queue, cfg, delivery, code, reason, clock, started)


# --- process entry -----------------------------------------------------------------------------------------------

def run_once(receiver, queue, renewer, staging, cfg, started, clock=None, run_engines=None):
    """Receive at most one message, keep its lock alive for the whole run, process it."""
    messages = receiver.receive_messages(max_message_count=1, max_wait_time=cfg.receive_wait_seconds)
    if not messages:
        log.info("No message waiting; exiting", extra={"event": "NoMessage"})
        return "no_message"
    message = messages[0]
    renewer.register(receiver, message, max_lock_renewal_duration=cfg.lock_renewal_seconds)
    return process_message(message, queue, staging, cfg, clock=clock, started=started, run_engines=run_engines)


def main(argv=None):
    started = time.monotonic()
    setup_logging()
    try:
        cfg = load_config()
    except ConfigError as ex:
        log.error("Configuration error: %s", ex, extra={"event": "ConfigError"})
        return 2
    setup_telemetry()
    try:
        credential = azure_io.open_credential(cfg.client_id)
        with azure_io.open_staging(cfg.staging_account_url, cfg.staging_container, credential) as staging, \
                azure_io.open_queue(cfg.servicebus_fqdn, cfg.request_queue, cfg.completion_queue, credential) as (
                    receiver, queue, renewer):
            result = run_once(receiver, queue, renewer, staging, cfg, started)
    except Exception as ex:  # noqa: BLE001 - e.g. Service Bus unreachable: a received message is redelivered
        log.error("Execution failed before the message was settled",
                  extra={"event": "ExecutionFailed", "exceptionType": type(ex).__name__})
        return 1
    log.info("Execution finished: %s", result, extra={"event": "ExecutionFinished"})
    return 0


if __name__ == "__main__":
    sys.exit(main())
