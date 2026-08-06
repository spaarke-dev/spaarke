using System.ServiceModel;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// Pins the correctness core of the race-proof <c>sprk_communication</c> create (FR-C1 / NFR-02 / task 021):
/// <see cref="DataverseServiceClientImpl.IsAlternateKeyDuplicate"/>, which decides whether a create failure is
/// the task-020 UNIQUE alternate-key duplicate fault (<c>0x80060892</c>) — in which case the writer reconciles
/// to the canonical row instead of throwing.
///
/// <para>
/// Concrete regression each test protects: if the classifier stops recognizing the duplicate-key fault (typed
/// <c>OrganizationServiceFault.ErrorCode</c>, wrapped in the chain, or by the message fallback), the race-proof
/// create silently reverts to throwing on a lost race — re-opening the concurrent / cross-mailbox duplicate
/// window and letting a second <c>sprk_communication</c> row be created for the same email. Pure logic
/// (Exception → bool) tested through the public surface (ADR-038: the un-fakeable sealed <c>ServiceClient</c>
/// is NOT a mock boundary; the classification IS testable).
/// </para>
/// </summary>
public class AlternateKeyDuplicateClassifierTests
{
    // The platform duplicate-alternate-key fault code captured empirically via task 020 (HTTP 412,
    // "Entity Key ... violated"). unchecked because 0x80060892 overflows a signed int.
    private const int DuplicateKeyErrorCode = unchecked((int)0x80060892);

    private static FaultException<OrganizationServiceFault> TypedFault(int errorCode, string message)
        => new(new OrganizationServiceFault { ErrorCode = errorCode, Message = message }, new FaultReason(message));

    [Fact]
    public void IsAlternateKeyDuplicate_TypedFaultWithDuplicateKeyCode_ReturnsTrue()
    {
        var ex = TypedFault(DuplicateKeyErrorCode, "Entity Key Internet Message Id Key violated.");

        DataverseServiceClientImpl.IsAlternateKeyDuplicate(ex).Should().BeTrue(
            "the typed 0x80060892 fault is the deterministic duplicate-key signal the reconcile path keys on");
    }

    [Fact]
    public void IsAlternateKeyDuplicate_TypedFaultWithUnrelatedCode_ReturnsFalse()
    {
        // A different platform error (e.g. generic SQL error) with a benign message must NOT be swallowed
        // as a duplicate — that would mask a real create failure as an idempotent reconcile.
        var ex = TypedFault(unchecked((int)0x80040265), "A generic Dataverse platform error occurred.");

        DataverseServiceClientImpl.IsAlternateKeyDuplicate(ex).Should().BeFalse();
    }

    [Fact]
    public void IsAlternateKeyDuplicate_TypedFaultWrappedInInvalidOperation_ReturnsTrue()
    {
        // Mirrors the real wrapping seam: GenericEntityService.CreateAsync (and other callers) rewrap
        // ServiceClient faults in InvalidOperationException — the walker must still find the inner typed fault.
        var inner = TypedFault(DuplicateKeyErrorCode, "Entity Key violated.");
        var wrapped = new InvalidOperationException("Failed to create sprk_communication record", inner);

        DataverseServiceClientImpl.IsAlternateKeyDuplicate(wrapped).Should().BeTrue(
            "the classifier walks the InnerException chain so a wrapped fault is still recognized");
    }

    [Theory]
    [InlineData("Cannot insert duplicate key row. Entity Key Internet Message Id Key violated.")]
    [InlineData("Dataverse error 0x80060892 while creating record")]
    [InlineData("A duplicate value was supplied and already exists for this key.")]
    public void IsAlternateKeyDuplicate_MessageFallbackMatches_ReturnsTrue(string message)
    {
        // Untyped fallback: some SDK paths surface only a message string (no OrganizationServiceFault detail).
        var ex = new InvalidOperationException(message);

        DataverseServiceClientImpl.IsAlternateKeyDuplicate(ex).Should().BeTrue();
    }

    [Fact]
    public void IsAlternateKeyDuplicate_UnrelatedException_ReturnsFalse()
    {
        // A transient/connection failure must NOT be misclassified as a duplicate — the caller must surface it,
        // not silently treat it as an idempotent reconcile.
        var ex = new TimeoutException("The Dataverse request timed out.");

        DataverseServiceClientImpl.IsAlternateKeyDuplicate(ex).Should().BeFalse();
    }

    [Fact]
    public void IsAlternateKeyDuplicate_DuplicateWordAloneWithoutAlreadyExists_ReturnsFalse()
    {
        // Guards the message fallback against over-matching: "duplicate" on its own (without the
        // "already exists" co-occurrence and without the key/code signals) is NOT the alternate-key fault.
        var ex = new InvalidOperationException("Duplicate detection rule blocked the create.");

        DataverseServiceClientImpl.IsAlternateKeyDuplicate(ex).Should().BeFalse();
    }
}
