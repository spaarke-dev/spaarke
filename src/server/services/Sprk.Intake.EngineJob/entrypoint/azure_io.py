"""Service Bus and Blob access over the hash-pinned Azure SDKs, with the job's own managed identity.

ADR-028: ManagedIdentityCredential(client_id=AZURE_CLIENT_ID) only. In Container Apps it reads IDENTITY_ENDPOINT and
IDENTITY_HEADER itself. No keys, connection strings or SAS; the only hosts reached are the MI token endpoint, the
staging account's blob endpoint and the Service Bus namespace (plus the App Insights ingestion endpoint for telemetry,
configured in main.py). No BFF endpoint is called.

The classes take SDK client objects in their constructors so tests can run this code against fakes that honour the
same conditional semantics (tests/fakes.py). `open_*` build the real clients.
"""
import contextlib
import json
from dataclasses import dataclass

from azure.core import MatchConditions
from azure.core.exceptions import ResourceExistsError, ResourceModifiedError, ResourceNotFoundError
from azure.servicebus import AutoLockRenewer, ServiceBusMessage, ServiceBusReceiveMode

PRIOR_DELIVERY_COUNT = "PriorDeliveryCount"  # JobContract.PriorDeliveryCountProperty


@dataclass(frozen=True)
class MarkerState:
    data: dict
    etag: str
    last_modified: object  # datetime (UTC)


class BlobStaging:
    """The stamp-private staging container."""

    def __init__(self, container_client):
        self._container = container_client

    def download(self, name):
        return self._container.get_blob_client(name).download_blob().readall()

    def download_to(self, name, path):
        with open(path, "wb") as f:
            f.write(self.download(name))

    def upload(self, name, data):
        """Deterministic output path: a replay of the same message overwrites, never duplicates (A2 §2.2)."""
        self._container.get_blob_client(name).upload_blob(data, overwrite=True)

    def create_marker(self, name, data):
        """Atomic claim: conditional create with If-None-Match: * (A2 §2.2, A1 §4).

        Returns the new marker's ETag if this call created it, None if a marker already existed.
        """
        try:
            result = self._container.get_blob_client(name).upload_blob(
                json.dumps(data).encode("utf-8"), overwrite=False, match_condition=MatchConditions.IfMissing)
            return result["etag"]
        except ResourceExistsError:
            return None

    def read_marker(self, name):
        blob = self._container.get_blob_client(name)
        try:
            props = blob.get_blob_properties()
            data = json.loads(blob.download_blob(etag=props.etag, match_condition=MatchConditions.IfNotModified)
                              .readall().decode("utf-8"))
        except (ResourceNotFoundError, ResourceModifiedError):
            return None
        except ValueError:
            data = {}  # unreadable marker: treated as claimed by someone else (takeover rules apply)
        return MarkerState(data=data if isinstance(data, dict) else {}, etag=props.etag,
                           last_modified=props.last_modified)

    def replace_marker(self, name, data, etag):
        """Compare-and-swap on the marker's ETag (If-Match). Returns the new ETag if this call won, else None."""
        try:
            result = self._container.get_blob_client(name).upload_blob(
                json.dumps(data).encode("utf-8"), overwrite=True, etag=etag,
                match_condition=MatchConditions.IfNotModified)
            return result["etag"]
        except (ResourceModifiedError, ResourceNotFoundError, ResourceExistsError):
            return None


class EngineQueue:
    """The receiver of intake-engine-requests and the two senders the job holds rights on.

    `request_sender` sends to the REQUEST queue (retry copies only); `completion_sender` sends to
    intake-engine-completions. The job has no right on any BFF queue (decisions.md 2026-11-25).
    """

    def __init__(self, receiver, request_sender, completion_sender):
        self._receiver = receiver
        self._request_sender = request_sender
        self._completion_sender = completion_sender

    def complete(self, message):
        self._receiver.complete_message(message)

    def abandon(self, message):
        self._receiver.abandon_message(message)

    def dead_letter(self, message, reason, description):
        """Explicit dead-letter (not lock expiry). Reason and description are fixed codes and ids only."""
        self._receiver.dead_letter_message(message, reason=reason, error_description=description)

    def schedule_retry(self, message, request, logical_delivery, due_utc):
        """A scheduled copy carrying the logical delivery count, as ServiceBusJobProcessor does (ISS-025)."""
        props = {}
        for key, value in (message.application_properties or {}).items():
            props[key.decode("utf-8", "replace") if isinstance(key, bytes) else key] = (
                value.decode("utf-8", "replace") if isinstance(value, bytes) else value)
        props[PRIOR_DELIVERY_COUNT] = logical_delivery
        copy = ServiceBusMessage(
            body=body_bytes(message), application_properties=props, message_id=request.idempotency_key,
            correlation_id=request.correlation_id, content_type="application/json", subject=request.job_type)
        self._request_sender.schedule_messages(copy, due_utc)

    def send_completion(self, request, body, created_at):
        message = ServiceBusMessage(
            body=body, message_id=request.idempotency_key, correlation_id=request.correlation_id,
            content_type="application/json", subject="IntakeEngineCompleted",
            application_properties={"JobType": "IntakeEngineCompleted", "Attempt": 1, "CreatedAt": created_at})
        self._completion_sender.send_messages(message)


def body_bytes(message):
    body = message.body
    if isinstance(body, (bytes, bytearray)):
        return bytes(body)
    return b"".join(bytes(part) for part in body)  # the SDK yields the AMQP data sections


def prior_delivery_count(message):
    """PriorDeliveryCount from a received message (keys and string values arrive as bytes). Clamped, never raises."""
    props = message.application_properties or {}
    raw = props.get(PRIOR_DELIVERY_COUNT.encode("ascii"), props.get(PRIOR_DELIVERY_COUNT))
    if isinstance(raw, bytes):
        raw = raw.decode("ascii", "replace")
    try:
        value = int(raw) if raw is not None and not isinstance(raw, bool) else 0
    except (TypeError, ValueError):
        value = 0
    return max(0, min(value, 10_000))


def logical_delivery_count(message):
    """PriorDeliveryCount + this message's broker delivery, 1-based like .NET's DeliveryCount.

    The Python SDK's `delivery_count` is the AMQP header's count of PRIOR unsuccessful deliveries (0 on the first),
    whereas ServiceBusReceivedMessage.DeliveryCount in .NET is 1 on the first, so 1 is added here.
    """
    return prior_delivery_count(message) + (message.delivery_count or 0) + 1


def open_credential(client_id):
    from azure.identity import ManagedIdentityCredential
    return ManagedIdentityCredential(client_id=client_id)


@contextlib.contextmanager
def open_staging(account_url, container, credential):
    from azure.storage.blob import BlobServiceClient
    with BlobServiceClient(account_url=account_url, credential=credential) as service:
        yield BlobStaging(service.get_container_client(container))


@contextlib.contextmanager
def open_queue(fqdn, request_queue, completion_queue, credential):
    """Peek-lock receiver (no prefetch: one execution takes one message), the two senders and an AutoLockRenewer.

    main.py registers the received message with the renewer for longer than the job's hard timeout. Service Bus caps
    a lock at 5 minutes, so without renewal a long engine run would lose its lock and be redelivered.
    """
    from azure.servicebus import ServiceBusClient
    renewer = AutoLockRenewer()
    with ServiceBusClient(fully_qualified_namespace=fqdn, credential=credential) as client, \
            client.get_queue_receiver(request_queue, receive_mode=ServiceBusReceiveMode.PEEK_LOCK,
                                      prefetch_count=0) as receiver, \
            client.get_queue_sender(request_queue) as request_sender, \
            client.get_queue_sender(completion_queue) as completion_sender:
        try:
            yield receiver, EngineQueue(receiver, request_sender, completion_sender), renewer
        finally:
            renewer.close()
