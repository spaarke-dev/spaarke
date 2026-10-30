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

    /// <summary>
    /// <see cref="DBNull.Value"/> CLEARS the column inside the same transaction — <c>UpdateAsync</c>'s convention
    /// (task 140, round 34 item 3: <c>set-record-share-expiry</c> severs a contact issuer in the same all-or-nothing
    /// write). The attribute is present with a null value (the SDK's "clear"), never the unserializable
    /// <see cref="DBNull"/> itself; its twin, a C# null, is still skipped (the test above).
    /// </summary>
    [Fact]
    public void BuildBulkUpdateTransaction_WhenAFieldValueIsDBNull_ClearsThatColumnInTheSameTransaction()
    {
        var updates = new List<(Guid id, Dictionary<string, object> fields)>
        {
            (FirstId, new Dictionary<string, object> { ["sprk_isdefault"] = false }),
            (SecondId, new Dictionary<string, object> { ["sprk_isdefault"] = false, ["sprk_name"] = DBNull.Value }),
        };

        var transaction = DataverseServiceClientImpl.BuildBulkUpdateTransaction(Table, updates);

        var targets = transaction.Requests.Cast<UpdateRequest>().Select(r => r.Target).ToList();
        targets.Should().HaveCount(2, "the clear travels in the ONE transaction, not a second write");
        targets[1].Attributes.Should().ContainKey("sprk_name");
        targets[1]["sprk_name"].Should().BeNull("a null attribute value is how the SDK clears a column");
        targets[0].Attributes.Should().NotContainKey("sprk_name");
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

    /// <summary>
    /// ebilling-r1 task 076 (review pass 2): a row version per update makes THAT update conditional
    /// (<c>Entity.RowVersion</c> + <c>IfRowVersionMatches</c>) inside the same one transaction; an update without one, or a call
    /// with no list, is sent as before.
    /// </summary>
    [Fact]
    public void BuildBulkUpdateTransaction_WithRowVersions_MakesExactlyThoseUpdatesConditional()
    {
        var transaction = DataverseServiceClientImpl.BuildBulkUpdateTransaction(Table, ThreeRows(), new long?[] { 41, null, 7 });

        var requests = transaction.Requests.Cast<UpdateRequest>().ToList();
        requests.Should().HaveCount(3, "still ONE transaction with one update per row");
        requests[0].ConcurrencyBehavior.Should().Be(ConcurrencyBehavior.IfRowVersionMatches);
        requests[0].Target.RowVersion.Should().Be("41");
        requests[1].ConcurrencyBehavior.Should().Be(ConcurrencyBehavior.Default, "a null entry leaves that update unconditional");
        requests[1].Target.RowVersion.Should().BeNull();
        requests[2].ConcurrencyBehavior.Should().Be(ConcurrencyBehavior.IfRowVersionMatches);
        requests[2].Target.RowVersion.Should().Be("7");

        DataverseServiceClientImpl.BuildBulkUpdateTransaction(Table, ThreeRows()).Requests.Cast<UpdateRequest>()
            .Should().OnlyContain(r => r.ConcurrencyBehavior == ConcurrencyBehavior.Default && r.Target.RowVersion == null,
                "without row versions the transaction is exactly the unconditional one");

        var mismatchedCount = () => DataverseServiceClientImpl.BuildBulkUpdateTransaction(Table, ThreeRows(), new long?[] { 1 });
        mismatchedCount.Should().Throw<ArgumentException>("a row version list that does not line up with the updates is refused before any I/O");
    }

    [Fact]
    public void IsRowVersionMismatch_RecognisesTheConcurrencyFault_DirectWrappedOrAsTheTransactionsInnerFault()
    {
        var direct = new FaultException<OrganizationServiceFault>(
            new OrganizationServiceFault { ErrorCode = DataverseServiceClientImpl.ConcurrencyVersionMismatch, Message = "The version of the existing record doesn't match" },
            new FaultReason("mismatch"));
        var inTransaction = new FaultException<OrganizationServiceFault>(
            new ExecuteTransactionFault
            {
                FaultedRequestIndex = 0,
                Message = "transaction failed",
                InnerFault = new OrganizationServiceFault { ErrorCode = DataverseServiceClientImpl.ConcurrencyVersionMismatch },
            },
            new FaultReason("transaction failed"));

        DataverseServiceClientImpl.IsRowVersionMismatch(direct).Should().BeTrue();
        DataverseServiceClientImpl.IsRowVersionMismatch(new InvalidOperationException("wrapped", direct)).Should().BeTrue();
        DataverseServiceClientImpl.IsRowVersionMismatch(inTransaction).Should().BeTrue();
        DataverseServiceClientImpl.IsRowVersionMismatch(new InvalidOperationException("Error code 0x80060882 ConcurrencyVersionMismatch")).Should().BeTrue();
    }

    [Fact]
    public void IsRowVersionMismatch_IsFalseForEveryOtherFailure()
    {
        var accessDenied = new FaultException<OrganizationServiceFault>(
            new ExecuteTransactionFault { FaultedRequestIndex = 1, ErrorCode = unchecked((int)0x80040220), Message = "Principal lacks write access" },
            new FaultReason("Principal lacks write access"));

        DataverseServiceClientImpl.IsRowVersionMismatch(accessDenied).Should().BeFalse();
        DataverseServiceClientImpl.IsRowVersionMismatch(new TimeoutException("The request channel timed out")).Should().BeFalse(
            "an unknown outcome is not a refusal: the caller must not treat it as 'nothing landed'");
    }

    // ebilling-r1 task 076 (review round 3): the catch in BulkUpdateIfRowVersionsMatchAsync throws what MapConditionalBulkFailure
    // returns, so these cases are the catch's behaviour: a mismatch is a DBConcurrencyException (the 409 path of the rule set
    // publish); anything else keeps the InvalidOperationException of BulkUpdateAsync (the 500 path).

    private static FaultException<OrganizationServiceFault> MismatchFault() => new(
        new OrganizationServiceFault { ErrorCode = DataverseServiceClientImpl.ConcurrencyVersionMismatch, Message = "The version of the existing record doesn't match" },
        new FaultReason("mismatch"));

    [Fact]
    public void MapConditionalBulkFailure_ForTheMismatchCodeOnTheFault_IsADbConcurrencyException()
    {
        var failure = MismatchFault();

        var mapped = DataverseServiceClientImpl.MapConditionalBulkFailure(Table, ThreeRows(), failure);

        mapped.Should().BeOfType<System.Data.DBConcurrencyException>();
        mapped.InnerException.Should().BeSameAs(failure);
        mapped.Message.Should().Contain(Table).And.Contain("no update was applied");
    }

    [Fact]
    public void MapConditionalBulkFailure_ForTheMismatchCodeOnAnInnerFault_IsADbConcurrencyException()
    {
        var inTransaction = new FaultException<OrganizationServiceFault>(
            new ExecuteTransactionFault
            {
                FaultedRequestIndex = 1,
                Message = "transaction failed",
                InnerFault = new OrganizationServiceFault { ErrorCode = DataverseServiceClientImpl.ConcurrencyVersionMismatch },
            },
            new FaultReason("transaction failed"));

        DataverseServiceClientImpl.MapConditionalBulkFailure(Table, ThreeRows(), inTransaction)
            .Should().BeOfType<System.Data.DBConcurrencyException>();
        DataverseServiceClientImpl.MapConditionalBulkFailure(Table, ThreeRows(), new InvalidOperationException("wrapped", inTransaction))
            .Should().BeOfType<System.Data.DBConcurrencyException>("the SDK may surface the fault wrapped");
    }

    [Fact]
    public void MapConditionalBulkFailure_ForTheMismatchGivenOnlyAsMessageText_IsADbConcurrencyException()
    {
        DataverseServiceClientImpl.MapConditionalBulkFailure(
                Table, ThreeRows(), new InvalidOperationException("Error code 0x80060882 ConcurrencyVersionMismatch"))
            .Should().BeOfType<System.Data.DBConcurrencyException>();
    }

    [Fact]
    public void MapConditionalBulkFailure_ForAnUnrelatedFault_IsTheInvalidOperationExceptionOfBulkUpdate_NamingTheFaultedRecord()
    {
        var accessDenied = new FaultException<OrganizationServiceFault>(
            new ExecuteTransactionFault { FaultedRequestIndex = 1, ErrorCode = unchecked((int)0x80040220), Message = "Principal lacks write access" },
            new FaultReason("Principal lacks write access"));

        var mapped = DataverseServiceClientImpl.MapConditionalBulkFailure(Table, ThreeRows(), accessDenied);

        mapped.Should().BeOfType<InvalidOperationException>("a failure that is not a refusal must not read as one: the caller answers it as a 500");
        mapped.InnerException.Should().BeSameAs(accessDenied);
        mapped.Message.Should().Contain(SecondId.ToString()).And.Contain("NO updates were applied");
    }

    [Fact]
    public void MapConditionalBulkFailure_ForAnExceptionWithNoFault_IsTheInvalidOperationException_SayingTheOutcomeIsUnknown()
    {
        var timeout = new TimeoutException("The request channel timed out");

        var mapped = DataverseServiceClientImpl.MapConditionalBulkFailure(Table, ThreeRows(), timeout);

        mapped.Should().BeOfType<InvalidOperationException>("an unknown outcome is not a refusal: it is never a DBConcurrencyException");
        mapped.InnerException.Should().BeSameAs(timeout);
        mapped.Message.Should().Contain("outcome is unknown");
    }
}
