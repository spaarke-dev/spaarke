// -----------------------------------------------------------------------------
// FailureClassifierTests.cs
//
// L2 CONTROL-PLANE tests for the default <see cref="IFailureClassifier"/>
// implementation (task 061).
//
// TESTED BEHAVIORS:
//   - Classify(HandlerResult.Failure) pass-through: returns failure.Class.
//   - ClassifyException(OperationCanceledException) re-throws (does NOT map).
//   - ClassifyException(TimeoutException / HttpRequestException / SocketException)
//     -> Resumable.
//   - ClassifyException(unknown) -> Resumable (SAFE default; NEVER
//     QuarantineRequired — silent quarantine would strand runs).
//   - Null argument -> ArgumentNullException.
//
// ADR-038 KEEP category: tests/unit/ — no I/O, no external dependency.
// -----------------------------------------------------------------------------

using System.Net.Http;
using System.Net.Sockets;
using FluentAssertions;
using Sprk.Provisioning.ControlPlane.Handlers;
using Sprk.Provisioning.ControlPlane.Rollback;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Rollback;

public sealed class FailureClassifierTests
{
    private readonly FailureClassifier _sut = new();

    [Theory]
    [InlineData(FailureClass.Resumable)]
    [InlineData(FailureClass.RetryableWithCleanup)]
    [InlineData(FailureClass.QuarantineRequired)]
    [InlineData(FailureClass.SuccessfulButDrifted)]
    public void Classify_ReturnsPassThroughOfFailureClass(FailureClass cls)
    {
        var failure = new HandlerResult.Failure(cls, "some-code", "some diagnostic");

        _sut.Classify(failure).Should().Be(cls);
    }

    [Fact]
    public void Classify_NullFailure_ThrowsArgumentNullException()
    {
        var act = () => _sut.Classify(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ClassifyException_OperationCanceledException_RethrowsUnchanged()
    {
        var oce = new OperationCanceledException("shutdown");
        var act = () => _sut.ClassifyException(oce);

        act.Should().Throw<OperationCanceledException>()
            .Which.Should().BeSameAs(oce, "cancellation propagates unchanged per BackgroundService convention");
    }

    [Theory]
    [MemberData(nameof(TransientInfrastructureExceptions))]
    public void ClassifyException_TransientInfrastructure_MapsToResumable(Exception ex)
    {
        _sut.ClassifyException(ex).Should().Be(FailureClass.Resumable,
            "{0} is a transient infrastructure hiccup — operator retries after external precondition", ex.GetType().Name);
    }

    public static IEnumerable<object[]> TransientInfrastructureExceptions()
    {
        yield return new object[] { new TimeoutException("gateway timeout") };
        yield return new object[] { new HttpRequestException("connection refused") };
        yield return new object[] { new SocketException(10061) };
    }

    [Fact]
    public void ClassifyException_UnknownException_DefaultsToResumable_SafeDefault()
    {
        // SAFE default: any unknown exception maps to Resumable (operator
        // resumes). NEVER QuarantineRequired — silent quarantine on transient
        // bugs would strand runs. See FailureClassifier.cs safety-posture
        // header comment.
        var unknown = new InvalidOperationException("something novel");

        _sut.ClassifyException(unknown).Should().Be(FailureClass.Resumable,
            "unknown exceptions default to Resumable; silent auto-quarantine would strand runs");
    }

    [Fact]
    public void ClassifyException_NullException_ThrowsArgumentNullException()
    {
        var act = () => _sut.ClassifyException(null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
