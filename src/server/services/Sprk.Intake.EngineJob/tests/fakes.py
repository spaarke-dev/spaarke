"""Test doubles for the Azure SDK clients the entrypoint wraps, plus envelope and path helpers.

The fakes reproduce the service semantics the job relies on, so tests run the REAL azure_io and main code:
  - Blob: If-None-Match: * (MatchConditions.IfMissing) refuses an existing blob with ResourceExistsError; If-Match
    (IfNotModified) with a stale ETag raises ResourceModifiedError; overwrite=False with no condition also refuses.
  - Service Bus: a peek-locked message whose lock has expired cannot be settled (MessageLockLostError).
Only synthetic corpus files are read (NFR-10); `practice_path` refuses any answer-key path.
"""
import itertools
import json
import os
import threading
import uuid
from datetime import datetime, timedelta, timezone
from types import SimpleNamespace

from azure.core import MatchConditions
from azure.core.exceptions import ResourceExistsError, ResourceModifiedError, ResourceNotFoundError
from azure.servicebus import ServiceBusReceivedMessage
from azure.servicebus.exceptions import MessageLockLostError

HERE = os.path.dirname(os.path.abspath(__file__))
JOB_ROOT = os.path.dirname(HERE)
REPO_ROOT = os.path.abspath(os.path.join(JOB_ROOT, "..", "..", "..", ".."))
ENGINES_DIR = os.environ.get("INTAKE_ENGINES_DIR") or os.path.join(REPO_ROOT, "src", "accelerators", "core", "engines")
PRACTICE_DIR = os.environ.get("INTAKE_NDA_PRACTICE_DIR") or os.path.join(
    REPO_ROOT, "src", "accelerators", "practices", "nda")


def practice_path(*parts):
    path = os.path.join(PRACTICE_DIR, *parts)
    if "answer-key" in path.replace("\\", "/").split("/"):
        raise AssertionError("tests never read the accelerator answer-key (NFR-10)")
    return path


PRACTICE_INTRO = practice_path("skills", "nda-review", "assets", "summary-intro.md")
SUMMARY_INTRO = os.environ.get("INTAKE_SUMMARY_INTRO") or PRACTICE_INTRO
CORPUS = practice_path("evals", "corpus")
GOLDEN_PLAN = practice_path("evals", "expected", "nda-01-golden-plan.json")


def utcnow():
    return datetime.now(timezone.utc)


# --- envelopes ---------------------------------------------------------------------------------------------------

def envelope(operation="redline", run_id=None, dispatch_attempt=1, correlation_id=None, payload_extra=None,
             drop=(), **overrides):
    """A request body exactly as the BFF's JobContract serialises it (camelCase, incl. isAtMaxAttempts)."""
    run_id = run_id or str(uuid.uuid4())
    payload = {"operation": operation, "runId": run_id, "dispatchAttempt": dispatch_attempt}
    payload.update(payload_extra or {})
    body = {
        "jobId": str(uuid.uuid4()), "jobType": "IntakeEngineRun", "subjectId": run_id,
        "correlationId": correlation_id or str(uuid.uuid4()),
        "idempotencyKey": f"IntakeEngine:{run_id}:{operation}:{dispatch_attempt}",
        "attempt": 1, "maxAttempts": 3, "payload": payload, "createdAt": "2026-11-26T12:00:00+00:00",
        "isAtMaxAttempts": False,
    }
    body.update(overrides)
    for name in drop:
        body.pop(name, None)
    return json.dumps(body).encode("utf-8"), body


# --- blob --------------------------------------------------------------------------------------------------------

class FakeContainer:
    def __init__(self):
        self.blobs = {}  # name -> (bytes, etag, last_modified)
        self.writes = []  # every successful write, in order: (name, conditional kind)
        self._etags = itertools.count(1)
        self.lock = threading.Lock()

    def get_blob_client(self, name):
        return FakeBlob(self, name)

    def put(self, name, data, last_modified=None):
        """Seed a blob directly (the BFF's staging writes, or a marker left by another execution)."""
        self.blobs[name] = (data, f'"{next(self._etags)}"', last_modified or utcnow())

    def names(self, prefix=""):
        return sorted(n for n in self.blobs if n.startswith(prefix))


class FakeBlob:
    def __init__(self, container, name):
        self.c, self.name = container, name

    def upload_blob(self, data, overwrite=False, etag=None, match_condition=None, **_):
        with self.c.lock:
            existing = self.c.blobs.get(self.name)
            if match_condition == MatchConditions.IfMissing:
                if existing is not None:
                    raise ResourceExistsError("BlobAlreadyExists")
            elif match_condition == MatchConditions.IfNotModified:
                if existing is None:
                    raise ResourceNotFoundError("BlobNotFound")
                if existing[1] != etag:
                    raise ResourceModifiedError("ConditionNotMet")
            elif match_condition is None and not overwrite and existing is not None:
                raise ResourceExistsError("BlobAlreadyExists")  # the SDK adds If-None-Match: * itself
            new_etag = f'"{next(self.c._etags)}"'
            self.c.blobs[self.name] = (bytes(data), new_etag, utcnow())
            self.c.writes.append((self.name, match_condition))
            return {"etag": new_etag}

    def download_blob(self, etag=None, match_condition=None, **_):
        existing = self.c.blobs.get(self.name)
        if existing is None:
            raise ResourceNotFoundError("BlobNotFound")
        if match_condition == MatchConditions.IfNotModified and existing[1] != etag:
            raise ResourceModifiedError("ConditionNotMet")
        return SimpleNamespace(readall=lambda: existing[0])

    def get_blob_properties(self):
        existing = self.c.blobs.get(self.name)
        if existing is None:
            raise ResourceNotFoundError("BlobNotFound")
        return SimpleNamespace(etag=existing[1], last_modified=existing[2])


# --- service bus -------------------------------------------------------------------------------------------------

def received(body, delivery_count=0, prior=None, correlation_id="corr", message_id="mid"):
    """A received message as the SDK presents it: bytes property keys, 0-based AMQP delivery_count."""
    props = {b"JobType": b"IntakeEngineRun"}
    if prior is not None:
        props[b"PriorDeliveryCount"] = prior
    return SimpleNamespace(body=body, application_properties=props, delivery_count=delivery_count,
                           correlation_id=correlation_id, message_id=message_id, settled=None)


class LockableMessage(ServiceBusReceivedMessage):
    """A real ServiceBusReceivedMessage subtype (AutoLockRenewer accepts only those) with a short, real-time lock."""

    def __init__(self, body, lock_seconds, receiver, delivery_count=0):  # noqa: D107 - SDK __init__ needs AMQP frames
        self._test_body = body
        self._lock_seconds = lock_seconds
        self._locked_until = utcnow() + timedelta(seconds=lock_seconds)
        self._received_timestamp_utc = utcnow()
        self._settled = False
        self._receiver = receiver
        self._test_delivery_count = delivery_count
        self.auto_renew_error = None
        self.settled = None

    @property
    def body(self):
        return self._test_body

    @property
    def application_properties(self):
        return {b"JobType": b"IntakeEngineRun"}

    @property
    def delivery_count(self):
        return self._test_delivery_count

    @property
    def correlation_id(self):
        return "corr"

    @property
    def message_id(self):
        return "mid"

    @property
    def locked_until_utc(self):
        return self._locked_until

    @property
    def _lock_expired(self):
        return utcnow() >= self._locked_until

    def __repr__(self):
        return "LockableMessage()"


class FakeReceiver:
    def __init__(self, messages=()):
        self._messages = list(messages)
        self._running = True
        self.completed, self.dead_lettered, self.abandoned, self.renewals = [], [], [], 0

    def receive_messages(self, max_message_count=1, max_wait_time=None):
        taken, self._messages = self._messages[:max_message_count], self._messages[max_message_count:]
        return taken

    def _settle(self, message, kind):
        if isinstance(message, LockableMessage) and message._lock_expired:
            raise MessageLockLostError(message="lock expired")
        if getattr(message, "settled", None):
            raise AssertionError("message settled twice")
        message.settled = kind
        if isinstance(message, LockableMessage):
            message._settled = True

    def complete_message(self, message):
        self._settle(message, "completed")
        self.completed.append(message)

    def abandon_message(self, message):
        self._settle(message, "abandoned")
        self.abandoned.append(message)

    def dead_letter_message(self, message, reason=None, error_description=None):
        self._settle(message, "dead-lettered")
        self.dead_lettered.append((message, reason, error_description))

    def renew_message_lock(self, message):
        if message._lock_expired:
            raise MessageLockLostError(message="lock expired")
        message._locked_until = utcnow() + timedelta(seconds=message._lock_seconds)
        self.renewals += 1
        return message._locked_until


class FakeSender:
    def __init__(self, fail=False):
        self.sent, self.scheduled, self.fail = [], [], fail

    def send_messages(self, message):
        if self.fail:
            raise RuntimeError("send failed")
        self.sent.append(message)

    def schedule_messages(self, message, schedule_time_utc):
        if self.fail:
            raise RuntimeError("schedule failed")
        self.scheduled.append((message, schedule_time_utc))
        return [1]


def message_body(sb_message):
    return b"".join(bytes(part) for part in sb_message.body) if not isinstance(sb_message.body, bytes) \
        else sb_message.body


def app_prop(sb_message, name):
    props = sb_message.application_properties or {}
    return props.get(name, props.get(name.encode("ascii")))
