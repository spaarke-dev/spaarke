using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceModel;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Spaarke.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Dataverse;

/// <summary>
/// Domain tests for <see cref="DataverseServiceClientImpl.BulkUpdateAsync"/>'s all-or-nothing contract
/// (unified-access-control-r2 task 096, ISS-005 / #970).
///
/// <para>The request is built and the failure is worded by two pure functions,
/// <see cref="DataverseServiceClientImpl.BuildBulkUpdateTransaction"/> and
/// <see cref="DataverseServiceClientImpl.DescribeBulkUpdateFailure"/>. They are tested directly because the
/// <c>ServiceClient</c> is built from configuration inside the class and <c>Execute</c> cannot be
/// overridden — capturing the request any other way needs reflection (B8) or transport mocking (B1).
/// Precedent: <see cref="AnalysisRegardingWriteTests"/>. The remaining wiring (build → execute → word the
/// failure) is three lines in <c>BulkUpdateAsync</c>, reviewed rather than tested.</para>
///
/// <para>In scope: the transaction's shape (one transaction, one update per row, in order, only supplied
/// fields); the wording of a Dataverse transaction fault (index + record + nothing applied), including when
/// the fault arrives wrapped; and the wording when no fault exists (outcome unknown, not partial — it must
/// NOT claim nothing was applied).</para>
///
/// KEEP path: tests/unit/domain/** (ADR-038 — pure domain logic).
/// </summary>
public class BulkUpdateTransactionTests
{
    private const string Table = "sprk_workspacelayout";

    private static readonly Guid FirstId = Guid.Parse("11111111-0000-0000-0000-000000000001");
    private static readonly Guid SecondId = Guid.Parse("22222222-0000-0000-0000-000000000002");
    private static readonly Guid ThirdId = Guid.Parse("33333333-0000-0000-0000-000000000003");

    private static List<(Guid id, Dictionary<string, object> fields)> ThreeRows() =>
    [
        (FirstId, new Dictionary<string, object> { ["sprk_isdefault"] = false }),
        (SecondId, new Dictionary<string, object> { ["sprk_isdefault"] = false }),
        (ThirdId, new Dictionary<string, object> { ["sprk_isdefault"] = false }),
    ];

    [Fact]
    public void BuildBulkUpdateTransaction_ForThreeRows_ReturnsOneTransactionWithOneUpdatePerRowInOrder()
    {
        var transaction = DataverseServiceClientImpl.BuildBulkUpdateTransaction(Table, ThreeRows());

        transaction.Requests.Should().HaveCount(3);
        transaction.Requests.Should().AllBeOfType<UpdateRequest>(
            "every row is an update inside the ONE transaction — nothing is sent outside it");

        var targets = transaction.Requests.Cast<UpdateRequest>().Select(r => r.Target).ToList();
        targets.Select(t => t.Id).Should().Equal(FirstId, SecondId, ThirdId);
        targets.Should().OnlyContain(t => t.LogicalName == Table);
        targets.Should().OnlyContain(t => (bool)t["sprk_isdefault"] == false);
    }

    [Fact]
    public void BuildBulkUpdateTransaction_WhenAFieldValueIsNull_WritesOnlyTheSuppliedFields()
    {
        var updates = new List<(Guid id, Dictionary<string, object> fields)>
        {
            (FirstId, new Dictionary<string, object> { ["sprk_sendstoday"] = 0, ["sprk_name"] = null! }),
        };

        var transaction = DataverseServiceClientImpl.BuildBulkUpdateTransaction(Table, updates);

        var target = transaction.Requests.Cast<UpdateRequest>().Single().Target;
        target.Attributes.Keys.Should().BeEquivalentTo(["sprk_sendstoday"],
            "a null value is skipped, so the column it names is left untouched rather than cleared");
    }

    [Fact]
    public void BuildBulkUpdateTransaction_WhenAFieldValueIsDBNull_ThrowsBeforeAnythingIsSent()
    {
        var updates = new List<(Guid id, Dictionary<string, object> fields)>
        {
            (FirstId, new Dictionary<string, object> { ["sprk_isdefault"] = false }),
            (SecondId, new Dictionary<string, object> { ["sprk_name"] = DBNull.Value }),
        };

        var act = () => DataverseServiceClientImpl.BuildBulkUpdateTransaction(Table, updates);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*index 1*sprk_name*UpdateAsync*",
                "DBNull cannot be serialized here; failing fast avoids a misleading 'outcome unknown' error for a request never sent");
    }

    [Fact]
    public void DescribeBulkUpdateFailure_WhenTheTransactionFaults_NamesTheIndexAndRecordAndSaysNothingWasApplied()
    {
        var fault = new FaultException<OrganizationServiceFault>(
            new ExecuteTransactionFault { FaultedRequestIndex = 1, Message = "Principal lacks write access" },
            new FaultReason("Principal lacks write access"));

        var message = DataverseServiceClientImpl.DescribeBulkUpdateFailure(Table, ThreeRows(), fault);

        message.Should().Contain("request index 1");
        message.Should().Contain(SecondId.ToString(), "the record at the faulted index is the one that failed");
        message.Should().Contain("Principal lacks write access");
        message.Should().Contain("NO updates were applied");
    }

    [Fact]
    public void DescribeBulkUpdateFailure_WhenTheFaultArrivesWrapped_StillNamesTheIndexAndSaysNothingWasApplied()
    {
        var fault = new FaultException<OrganizationServiceFault>(
            new ExecuteTransactionFault { FaultedRequestIndex = 2, Message = "Record is inactive" },
            new FaultReason("Record is inactive"));
        var wrapped = new InvalidOperationException("Dataverse request failed", fault);

        var message = DataverseServiceClientImpl.DescribeBulkUpdateFailure(Table, ThreeRows(), wrapped);

        message.Should().Contain("request index 2");
        message.Should().Contain(ThirdId.ToString());
        message.Should().Contain("NO updates were applied");
    }

    [Fact]
    public void DescribeBulkUpdateFailure_WhenTheFaultDoesNotIdentifyARequest_SaysAllOrNoneWithoutClaimingNothingWasApplied()
    {
        // A plain OrganizationServiceFault (e.g. throttling, or the batch-size limit) names no request. The
        // SDK throws the client-wide last error, so under concurrency it may even belong to another request —
        // only the atomicity guarantee is safe to state.
        var fault = new FaultException<OrganizationServiceFault>(
            new OrganizationServiceFault { Message = "Number of requests exceeded the limit" },
            new FaultReason("Number of requests exceeded the limit"));

        var message = DataverseServiceClientImpl.DescribeBulkUpdateFailure(Table, ThreeRows(), fault);

        message.Should().Contain("did not identify");
        message.Should().Contain("Number of requests exceeded the limit");
        message.Should().Contain("never a partial set");
        message.Should().NotContain("NO updates were applied");
    }

    [Fact]
    public void DescribeBulkUpdateFailure_WhenNoDataverseFaultExists_SaysTheOutcomeIsUnknownButNotPartial()
    {
        var timeout = new TimeoutException("The request channel timed out");

        var message = DataverseServiceClientImpl.DescribeBulkUpdateFailure(Table, ThreeRows(), timeout);

        message.Should().Contain("outcome is unknown");
        message.Should().Contain("never a partial set");
        message.Should().NotContain("NO updates were applied",
            "a timeout can land after the commit — claiming nothing was applied would be false");
    }
}
