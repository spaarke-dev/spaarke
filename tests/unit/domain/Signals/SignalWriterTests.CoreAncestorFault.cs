using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Telemetry;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;
using Ownership = Sprk.Bff.Api.Services.Dataverse;

namespace Sprk.Bff.Api.Tests.Domain.Signals;

/// <summary>
/// ebilling-r1 task 049 (ontology F1-3 re-check F2): the writer's <c>CoreAncestorStatus.Error</c> case says WHICH kind of Error it
/// refused for. A fault (<see cref="Ownership.CoreAncestorResult.IsPermanent"/> false: a throttle, a 503, a metadata or row read that
/// failed) is thrown with the fault as the inner exception, so a caller that classifies by it (the after-commit steps) retries; a
/// permanent Error (the subject row is gone) is thrown with none. The reason, and so the log and the metric, is
/// <see cref="OntologyWriterFailureReason.CoreRecordUnresolved"/> either way.
/// </summary>
public partial class SignalWriterTests
{
    private static System.ServiceModel.FaultException<Microsoft.Xrm.Sdk.OrganizationServiceFault> RowDoesNotExist() =>
        new(new Microsoft.Xrm.Sdk.OrganizationServiceFault { ErrorCode = unchecked((int)0x80040217) }, "Does Not Exist");

    /// <summary>The writer over the real resolver, with a column probe that faults (a metadata read failure).</summary>
    private static SignalWriter BuildWithFaultingColumnProbe(FakeOrganizationService writerOrg, FakeSysadminClient sysadmin) =>
        new(new OntologyWriterDataverseClient(() => writerOrg, NullLogger<OntologyWriterDataverseClient>.Instance),
            sysadmin,
            new Ownership.CoreAncestorResolver(sysadmin, (_, _) => throw new TimeoutException("metadata unavailable"),
                NullLogger<Ownership.CoreAncestorResolver>.Instance),
            new RecordTypeCatalogStub(RecordTypeCatalogStub.SpaarkeDev1),
            new FakeOwnershipResolver(), Scopes(), new FakeTimeProvider(FirstRun), NullLogger<SignalWriter>.Instance);

    [Fact(DisplayName = "049 F2: a transient core-ancestor fault (the row read) is thrown with the fault as the inner exception; unresolved; nothing written")]
    public async Task WriteAsync_TransientRowReadFault_ThrowsWithAnInnerException()
    {
        var sysadmin = new FakeSysadminClient { ThrowOnRetrieveOf = "sprk_billingevent", RetrieveFault = new TimeoutException("Dataverse timed out") };
        var writerOrg = new FakeOrganizationService();

        var act = async () => await Build(writerOrg, sysadmin, new FakeTimeProvider(FirstRun)).WriteAsync(DoRequest("sprk_billingevent", LineId));

        var thrown = (await act.Should().ThrowAsync<SignalWriterEscalationException>()).Which;
        thrown.Reason.Should().Be(OntologyWriterFailureReason.CoreRecordUnresolved);
        thrown.InnerException.Should().NotBeNull("a fault a retry may clear is not a refusal of the subject");
        thrown.InnerException!.Message.Should().Contain("Failed to read core-ancestor lookups from sprk_billingevent");
        writerOrg.CreatedPayloads.Should().BeEmpty();
    }

    [Fact(DisplayName = "049 F2: a transient core-ancestor fault (the column probe) is thrown with the fault as the inner exception; unresolved; nothing written")]
    public async Task WriteAsync_TransientColumnProbeFault_ThrowsWithAnInnerException()
    {
        var sysadmin = new FakeSysadminClient();
        var writerOrg = new FakeOrganizationService();

        var act = async () => await BuildWithFaultingColumnProbe(writerOrg, sysadmin).WriteAsync(DoRequest("sprk_billingevent", LineId));

        var thrown = (await act.Should().ThrowAsync<SignalWriterEscalationException>()).Which;
        thrown.Reason.Should().Be(OntologyWriterFailureReason.CoreRecordUnresolved);
        thrown.InnerException.Should().NotBeNull();
        thrown.InnerException!.Message.Should().Contain("Could not read metadata for child target 'sprk_billingevent'");
        writerOrg.CreatedPayloads.Should().BeEmpty();
    }

    [Fact(DisplayName = "049 F2: a permanent core-ancestor Error (the row no longer exists) is thrown with NO inner exception; same reason")]
    public async Task WriteAsync_PermanentNotFound_ThrowsWithoutAnInnerException()
    {
        var sysadmin = new FakeSysadminClient { ThrowOnRetrieveOf = "sprk_billingevent", RetrieveFault = RowDoesNotExist() };
        var writerOrg = new FakeOrganizationService();

        var act = async () => await Build(writerOrg, sysadmin, new FakeTimeProvider(FirstRun)).WriteAsync(DoRequest("sprk_billingevent", LineId));

        var thrown = (await act.Should().ThrowAsync<SignalWriterEscalationException>()).Which;
        thrown.Reason.Should().Be(OntologyWriterFailureReason.CoreRecordUnresolved);
        thrown.InnerException.Should().BeNull("a row that is gone is a state the writer refuses, again on every attempt");
        writerOrg.CreatedPayloads.Should().BeEmpty();
    }

    [Fact(DisplayName = "049 F2: the planning path (DeriveCoreRecordAsync) throws the same shape: inner exception for a fault, none for a permanent Error")]
    public async Task DeriveCoreRecordAsync_FaultVersusPermanent_SameShape()
    {
        var fault = async () => await Build(new FakeOrganizationService(), new FakeSysadminClient { ThrowOnRetrieveOf = "sprk_todo" }, new FakeTimeProvider(FirstRun))
            .DeriveCoreRecordAsync("sprk_todo", TodoId, SignalWriter.LaneDo);
        (await fault.Should().ThrowAsync<SignalWriterEscalationException>()).Which.InnerException.Should().NotBeNull();

        var gone = async () => await Build(new FakeOrganizationService(), new FakeSysadminClient { ThrowOnRetrieveOf = "sprk_todo", RetrieveFault = RowDoesNotExist() }, new FakeTimeProvider(FirstRun))
            .DeriveCoreRecordAsync("sprk_todo", TodoId, SignalWriter.LaneDo);
        (await gone.Should().ThrowAsync<SignalWriterEscalationException>()).Which.InnerException.Should().BeNull();
    }
}
