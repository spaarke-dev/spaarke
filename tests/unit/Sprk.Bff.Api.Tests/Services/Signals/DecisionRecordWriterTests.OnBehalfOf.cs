using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Sprk.Bff.Api.Services.Signals;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Signals;

/// <summary>
/// Ontology task 223 (DI-144, #2632): the writer's three "already recorded" lookups (the gate record, the condition-cleared record and the
/// billing writer filter) trust a row only when <see cref="WriterCreatedRecord"/> holds: <c>createdby</c> the writer AND
/// <c>createdonbehalfby</c> empty. A row stamped with the writer as creator but created on its behalf by another identity is not the
/// writer's own, so it never stops the genuine record being written. The must-not-fire side (a writer-created row with
/// <c>createdonbehalfby</c> empty is still found) is every existing lookup test, which seeds rows exactly that way.
/// </summary>
public sealed partial class DecisionRecordWriterTests
{
    /// <summary>Someone acting on the writer's behalf (an impersonating caller): <c>createdonbehalfby</c> on the row.</summary>
    private static readonly Guid Delegate = Guid.Parse("2230f123-0000-4000-8000-0000000000dd");

    private static Entity OnTheWritersBehalf(Entity row)
    {
        row["createdby"] = new EntityReference("systemuser", WriterUser);
        row["createdonbehalfby"] = new EntityReference("systemuser", Delegate);
        return row;
    }

    private static void AssertTheWriterRule(QueryExpression lookup)
    {
        lookup.Criteria.Conditions.Should().Contain(c =>
            c.AttributeName == "createdby" && c.Operator == ConditionOperator.Equal && c.Values.Single().Equals(WriterUser));
        lookup.Criteria.Conditions.Should().Contain(c =>
            c.AttributeName == WriterCreatedRecord.CreatedOnBehalfByColumn && c.Operator == ConditionOperator.Null);
    }

    [Fact]
    public async Task Gate_ARowCreatedOnTheWritersBehalfWithTheKey_IsNotTheGatesRecord_AndTheRecordIsWritten()
    {
        var h = MatterFiledCommunication();
        var onBehalf = OnTheWritersBehalf(ExistingRecord(Guid.NewGuid(), CommunicationId, GateDecisionKey.IdOf(CommunicationId, authorize: true)));
        h.Seeded.Add(onBehalf);

        var result = await h.Writer.WriteGateDecisionAsync(Gate(authorize: true));

        result.AlreadyRecorded.Should().BeFalse("a row created on the writer's behalf by another identity is not the writer's own");
        result.RecordId.Should().NotBe(onBehalf.Id);
        h.Created.Should().ContainSingle();
        AssertTheWriterRule(h.Lookups.Should().ContainSingle(q => q.EntityName == "sprk_decisionrecord").Subject);
    }

    [Fact]
    public async Task ConditionCleared_ARowCreatedOnTheWritersBehalfWithTheKeyAndCode_IsIgnored_AndItsConfirmerIsNotCopied()
    {
        var h = ChangedToDo(Colleague, SignalClosed.AddDays(-1));
        var onBehalf = OnTheWritersBehalf(KeyedClosureRow(createdBy: WriterUser, confirmedBy: Framed));
        h.Seeded.Add(onBehalf);

        var result = await h.Writer.WriteConditionClearedAsync(Cleared());

        result.AlreadyRecorded.Should().BeFalse("a row created on the writer's behalf is never the closure's record");
        result.RecordId.Should().NotBe(onBehalf.Id);
        result.ConfirmedByUserId.Should().Be(Colleague, "the person who changed the To Do, not the row's confirmer");
        AssertTheWriterRule(h.Lookups.Where(q => q.EntityName == "sprk_decisionrecord").Should().ContainSingle().Subject);
    }

    [Fact]
    public async Task Billing_ARowCreatedOnTheWritersBehalfWithTheKeyAndCode_IsNotAlreadyRecorded_AndTheRecordIsWritten()
    {
        var h = BillingHarness();
        var onBehalf = OnTheWritersBehalf(new Entity("sprk_decisionrecord", Guid.NewGuid())
        {
            ["sprk_regardinginvoice"] = new EntityReference("sprk_invoice", InvoiceId),
            ["sprk_steps"] = $"{{\"reviewId\":\"{BillingDecisionKey.IdOf("sprk_invoice", InvoiceId, BillingDecision):D}\",\"reviewHash\":\"{DecisionRecordWriter.BillingDecisionHash(Billing())}\",\"steps\":[{{\"code\":\"approve-invoice\"}}]}}",
            ["sprk_actioncode"] = "approve-invoice",
            ["sprk_confirmedby"] = new EntityReference("systemuser", Framed),
        });
        h.Seeded.Add(onBehalf);

        var result = await h.Writer.WriteBillingDecisionAsync(Billing());

        result.AlreadyRecorded.Should().BeFalse("a row created on the writer's behalf is never the decision's record");
        result.RecordId.Should().NotBe(onBehalf.Id);
        h.Created.Should().ContainSingle();
        h.Row.GetAttributeValue<EntityReference>("sprk_confirmedby").Id.Should().Be(Reviewer);
        AssertTheWriterRule(h.Lookups.Where(q => q.EntityName == "sprk_decisionrecord").Should().ContainSingle().Subject);
    }
}
