// -----------------------------------------------------------------------------
// ServiceBusEnvelopeSizeGuardTests.cs
//
// COMP-09 (customer-provisioning-orchestration-r1 Wave 7 completeness sweep,
// 2026-08-27) — proves the ServiceBusHandlerEnqueuer envelope-size guard
// throws InvalidOperationException BEFORE any Service Bus SendMessageAsync
// call when the serialized body exceeds ServiceBusModuleOptions.
// MaxEnvelopeBodyBytes.
//
// WHY THIS MATTERS:
//   H4b's BulkAppSettings envelope can carry a large per-env-settings map;
//   an operator adding dozens of large settings could push it over the
//   256 KB Service Bus Standard tier cap. Without the guard, the SDK's
//   MessageSizeExceededException surfaces with a generic message and no
//   diagnostic byte-count — worse, on Premium namespaces it would silently
//   succeed and later collapse if migrated to Standard. The guard fails
//   fast at enqueue with a diagnostic message + structured log line.
//
// SEAM STRATEGY (ADR-038 §5, docs/standards/TEST-ARCHITECTURE.md §5):
//   Direct call to the extracted internal helper
//   ServiceBusHandlerEnqueuer.EnsureBodyWithinCap — no Mock<ServiceBusSender>
//   and no live Service Bus. The helper is a pure guard; the wire-side
//   SendMessageAsync path is tested elsewhere (ServiceBusSmokeTests +
//   ReconcilerEnqueuePayloadAttemptTests).
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Provisioning.ControlPlane.Enqueue;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Enqueue;

public sealed class ServiceBusEnvelopeSizeGuardTests
{
    private static HandlerEnvelope MakeEnvelope(int approximateBodyBytes = 100)
    {
        // ParametersJson dominates the serialized body byte-count; pad it to
        // the requested size so the caller can drive the guard's arithmetic
        // deterministically.
        var padding = approximateBodyBytes > 0
            ? new string('x', approximateBodyBytes)
            : string.Empty;

        return new HandlerEnvelope
        {
            HandlerId = "H4b",
            RunId = "00000000-0000-0000-0000-000000000001",
            CustomerId = "size-guard-test",
            ParametersJson = "{\"pad\":\"" + padding + "\"}",
            EnqueuedAt = DateTimeOffset.UnixEpoch,
        };
    }

    [Fact]
    public void EnsureBodyWithinCap_UnderCap_DoesNotThrow()
    {
        var envelope = MakeEnvelope(approximateBodyBytes: 1024);
        var act = () => ServiceBusHandlerEnqueuer.EnsureBodyWithinCap(
            envelope,
            bodyByteCount: 1024,
            maxBytes: 224 * 1024,
            queueName: "sprk-provisioning-jobs",
            logger: NullLogger.Instance);

        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureBodyWithinCap_AtExactCap_DoesNotThrow()
    {
        // Boundary condition: bodyByteCount == maxBytes is compliant
        // (strict >  cap-check semantics).
        var envelope = MakeEnvelope();
        var act = () => ServiceBusHandlerEnqueuer.EnsureBodyWithinCap(
            envelope,
            bodyByteCount: 224 * 1024,
            maxBytes: 224 * 1024,
            queueName: "q",
            logger: NullLogger.Instance);

        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureBodyWithinCap_OverCap_ThrowsInvalidOperationException()
    {
        var envelope = MakeEnvelope();

        var act = () => ServiceBusHandlerEnqueuer.EnsureBodyWithinCap(
            envelope,
            bodyByteCount: 250_000,
            maxBytes: 224 * 1024,
            queueName: "sprk-provisioning-jobs",
            logger: NullLogger.Instance);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*HandlerEnvelope body for HandlerId=H4b*exceeds MaxEnvelopeBodyBytes*250000 > 229376*");
    }

    [Fact]
    public void EnsureBodyWithinCap_OverCap_ExceptionMentionsPremiumPathway()
    {
        // The message must guide operators to the two remediation paths
        // (Cosmos-blob indirection OR Premium namespace) — pure string
        // assertion so a future refactor doesn't accidentally strip the
        // remediation guidance.
        var envelope = MakeEnvelope();
        var act = () => ServiceBusHandlerEnqueuer.EnsureBodyWithinCap(
            envelope, bodyByteCount: 300_000, maxBytes: 224 * 1024,
            queueName: "q", logger: NullLogger.Instance);

        var ex = act.Should().Throw<InvalidOperationException>().Which;
        ex.Message.Should().Contain("256 KB");
        ex.Message.Should().Contain("Premium namespace");
        ex.Message.Should().Contain("Cosmos-side blob URL");
    }

    [Fact]
    public void ServiceBusModuleOptions_MaxEnvelopeBodyBytes_DefaultsTo_224KB()
    {
        // COMP-09 acceptance: the default must leave ~32 KB of headroom for
        // application-property overhead beneath the 256 KB Standard cap.
        // Any change to this default MUST be reviewed against the SB tier the
        // operator is deploying to.
        var options = new Sprk.Provisioning.ControlPlane.Modules.ServiceBusModuleOptions();
        options.MaxEnvelopeBodyBytes.Should().Be(224 * 1024);
    }
}
