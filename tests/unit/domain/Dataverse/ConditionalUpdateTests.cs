using System;
using System.Collections.Generic;
using System.Data;
using System.ServiceModel;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Dataverse;

/// <summary>
/// Domain tests for <see cref="DataverseServiceClientImpl.UpdateIfRowVersionMatchesAsync"/> (#2143, unified-access-control-r2):
/// the SDK conditional update a re-file's owner write and its put-back go through.
///
/// <para>The request is built and a failure is mapped by two pure functions,
/// <see cref="DataverseServiceClientImpl.BuildConditionalUpdate"/> and
/// <see cref="DataverseServiceClientImpl.MapConditionalUpdateFailure"/>, tested directly because the <c>ServiceClient</c> is
/// built from configuration inside the class (precedent: <see cref="BulkUpdateTransactionTests"/>). The remaining wiring
/// (build → execute → map) is reviewed rather than tested.</para>
///
/// <para>In scope: the request is conditioned on the version given (<c>IfRowVersionMatches</c>); <c>UpdateAsync</c>'s field
/// rules (null skips, <see cref="DBNull"/> clears, a lookup is written as given); Dataverse's version mismatch
/// (<c>ConcurrencyVersionMismatch</c>, 0x80060882) — bare or wrapped — becomes <see cref="DBConcurrencyException"/>, the
/// exception the Web API conditional update throws for 412; any other failure stays <c>UpdateAsync</c>'s
/// <see cref="InvalidOperationException"/>.</para>
///
/// KEEP path: tests/unit/domain/** (ADR-038 — pure domain logic).
/// </summary>
public class ConditionalUpdateTests
{
    private const string Table = "sprk_document";
    private static readonly Guid RowId = Guid.Parse("21430000-0000-4000-8000-0000000000d1");
    private static readonly Guid TeamId = Guid.Parse("21430000-0000-4000-8000-0000000000e1");

    private static FaultException<OrganizationServiceFault> Fault(int code) =>
        new(new OrganizationServiceFault { ErrorCode = code, Message = "fault" }, new FaultReason("fault"));

    [Fact]
    public void BuildConditionalUpdate_IsConditionedOnTheVersionGiven()
    {
        var request = DataverseServiceClientImpl.BuildConditionalUpdate(
            Table, RowId, "8812345", new Dictionary<string, object> { ["ownerid"] = new EntityReference("team", TeamId) });

        request.ConcurrencyBehavior.Should().Be(ConcurrencyBehavior.IfRowVersionMatches,
            "without it Dataverse ignores the version and the write is unconditional");
        request.Target.RowVersion.Should().Be("8812345");
        request.Target.LogicalName.Should().Be(Table);
        request.Target.Id.Should().Be(RowId);
    }

    [Fact]
    public void BuildConditionalUpdate_KeepsUpdateAsyncsFieldRules_NullSkips_DbNullClears_ALookupIsWrittenAsGiven()
    {
        var lookup = new EntityReference("sprk_project", Guid.Parse("21430000-0000-4000-8000-0000000000f1"));

        var target = DataverseServiceClientImpl.BuildConditionalUpdate(Table, RowId, "7", new Dictionary<string, object>
        {
            ["sprk_relatedproject"] = lookup,
            ["sprk_matter"] = DBNull.Value,
            ["sprk_name"] = null!,
        }).Target;

        target.Attributes.Keys.Should().BeEquivalentTo(new[] { "sprk_relatedproject", "sprk_matter" },
            "a null value is skipped, so the column it names is left untouched");
        target["sprk_relatedproject"].Should().BeSameAs(lookup);
        target.Attributes["sprk_matter"].Should().BeNull("DBNull clears the column — the SDK's null, never the DBNull itself");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void BuildConditionalUpdate_WithoutAVersion_Throws_BeforeAnyIo(string? version)
    {
        var act = () => DataverseServiceClientImpl.BuildConditionalUpdate(
            Table, RowId, version!, new Dictionary<string, object> { ["ownerid"] = new EntityReference("team", TeamId) });

        act.Should().Throw<ArgumentException>().WithParameterName("rowVersion");
    }

    [Fact]
    public void MapConditionalUpdateFailure_ForTheVersionMismatchFault_IsADbConcurrencyException()
    {
        var fault = Fault(unchecked((int)0x80060882));

        var mapped = DataverseServiceClientImpl.MapConditionalUpdateFailure(Table, RowId, "7", fault);

        mapped.Should().BeOfType<DBConcurrencyException>().Which.InnerException.Should().BeSameAs(fault);
        mapped.Message.Should().Contain(RowId.ToString("D")).And.Contain("not applied");
    }

    [Fact]
    public void MapConditionalUpdateFailure_ForTheVersionMismatchFaultWrapped_IsStillADbConcurrencyException()
    {
        var wrapped = new InvalidOperationException("outer", Fault(unchecked((int)0x80060882)));

        DataverseServiceClientImpl.MapConditionalUpdateFailure(Table, RowId, "7", wrapped)
            .Should().BeOfType<DBConcurrencyException>();
    }

    [Fact]
    public void MapConditionalUpdateFailure_ForAnUntypedFaultCarryingTheVersionMismatchCode_IsADbConcurrencyException()
    {
        // K1 (#2143): the ServiceClient can surface a fault without its typed OrganizationServiceFault detail — the code then
        // appears only as hex text in the message (DecisionCommitService.IsVersionMismatch's precedent). Bare and wrapped.
        var untyped = new Exception("The version of the existing record doesn't match the RowVersion property provided. "
            + "ErrorCode: 0x80060882");

        DataverseServiceClientImpl.MapConditionalUpdateFailure(Table, RowId, "7", untyped)
            .Should().BeOfType<DBConcurrencyException>().Which.InnerException.Should().BeSameAs(untyped);
        DataverseServiceClientImpl.MapConditionalUpdateFailure(Table, RowId, "7", new InvalidOperationException("outer", untyped))
            .Should().BeOfType<DBConcurrencyException>();
    }

    [Fact]
    public void MapConditionalUpdateFailure_ForAnUntypedFaultCarryingAnotherCode_IsNotAConcurrencyRetry()
    {
        // Must-not-fire: the text fallback matches the version-mismatch code only (0x80060883, version not provided, is not it).
        var other = new Exception("The RowVersion property must be provided. ErrorCode: 0x80060883");

        DataverseServiceClientImpl.MapConditionalUpdateFailure(Table, RowId, "7", other)
            .Should().BeOfType<InvalidOperationException>();
    }

    [Fact]
    public void MapConditionalUpdateFailure_ForAnyOtherFault_IsUpdateAsyncsInvalidOperationException_NotAConcurrencyRetry()
    {
        // 0x80060883 (ConcurrencyVersionNotProvided) and a privilege failure are not "the row changed": a caller that re-read
        // and retried on them would loop on a permanent failure.
        foreach (var other in new Exception[]
                 {
                     Fault(unchecked((int)0x80060883)),
                     Fault(unchecked((int)0x80040220)),
                     new TimeoutException("throttled"),
                 })
        {
            var mapped = DataverseServiceClientImpl.MapConditionalUpdateFailure(Table, RowId, "7", other);

            mapped.Should().BeOfType<InvalidOperationException>().Which.InnerException.Should().BeSameAs(other);
        }
    }
}
