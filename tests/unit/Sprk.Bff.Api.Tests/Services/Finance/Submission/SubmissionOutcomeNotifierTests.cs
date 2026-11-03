using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Services.Finance.Submission;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Finance.Submission;

/// <summary>
/// ebilling-r1 task 095b: <see cref="SubmissionOutcomeNotifier"/>, the bell notification a submitter gets when a submission ends. The notification
/// writer is the real <see cref="Sprk.Bff.Api.Services.NotificationService"/>; only Dataverse is a double (<see cref="SubmissionBellWorld"/>).
/// </summary>
/// <remarks>
/// Maintain-class (ADR-038): the guarantee that a notification fault never reaches the job (<see cref="NotifyAsync_WhenTheWriterThrows_LogsTheWarningAndSwallowsIt"/>)
/// and what the person is told. Exactly-once is the handler's, proved in the handler and flow tests.
/// </remarks>
public sealed class SubmissionOutcomeNotifierTests
{
    private static readonly Guid SubmissionId = Guid.Parse("09500000-0000-4000-8000-0000000000b1");
    private static readonly Guid InvoiceId = Guid.Parse("09500000-0000-4000-8000-0000000000a1");

    [Fact(DisplayName = "095b: Accepted creates one bell notification for the submitter, naming the tracking number and linking to the invoice")]
    public async Task Accepted_CreatesOneNotification_LinkingToTheInvoice()
    {
        var world = new SubmissionBellWorld(SubmissionId);

        await world.Build().NotifyAsync(SubmissionId, SubmissionOutcomeKind.Accepted, InvoiceId);

        var bell = world.Created.Should().ContainSingle().Subject;
        bell.GetAttributeValue<EntityReference>("ownerid").Id.Should().Be(SubmissionBellWorld.Person);
        bell.GetAttributeValue<string>("title").Should().Be("Invoice INV-100 accepted");
        bell.GetAttributeValue<string>("body").Should().Be($"Tracking number INV-100.2. {SubmissionOutcomeNotifier.AcceptedSentence}");
        SubmissionBellWorld.ActionUrl(bell).Should().Be($"/main.aspx?etn=sprk_invoice&id={InvoiceId:D}&pagetype=entityrecord");
        world.Log.Entries.Should().Contain(e => e.Event == SubmissionOutcomeNotifierEvents.BellCreated && e.Level == LogLevel.Information);
    }

    [Theory(DisplayName = "095b: Failed and Refused create one notification each, with the outcome sentence and no invoice link")]
    [InlineData(SubmissionOutcomeKind.Failed, "Invoice INV-100 could not be processed", SubmissionOutcomeNotifier.FailedSentence)]
    [InlineData(SubmissionOutcomeKind.Refused, "Invoice INV-100 refused", SubmissionOutcomeNotifier.RefusedSentence)]
    public async Task FailedAndRefused_WithoutAnInvoice_LinkToTheSubmission(SubmissionOutcomeKind outcome, string title, string sentence)
    {
        var world = new SubmissionBellWorld(SubmissionId);

        await world.Build().NotifyAsync(SubmissionId, outcome, invoiceId: null);

        var bell = world.Created.Should().ContainSingle().Subject;
        bell.GetAttributeValue<string>("title").Should().Be(title);
        bell.GetAttributeValue<string>("body").Should().Be($"Tracking number INV-100.2. {sentence}");
        SubmissionBellWorld.ActionUrl(bell).Should().Be($"/main.aspx?etn=sprk_invoicesubmission&id={SubmissionId:D}&pagetype=entityrecord")
            .And.NotContain("sprk_invoice&", "no invoice exists to open");
    }

    [Fact(DisplayName = "095b: a refusal that holds an invoice for review says so and still links to it")]
    public async Task Blocked_IsHeldForReview_AndLinksToTheInvoice()
    {
        var world = new SubmissionBellWorld(SubmissionId, reasonCode: SubmissionReasonCodes.Blocked);

        await world.Build().NotifyAsync(SubmissionId, SubmissionOutcomeKind.Refused, InvoiceId);

        var bell = world.Created.Should().ContainSingle().Subject;
        bell.GetAttributeValue<string>("title").Should().Be("Invoice INV-100 held for review");
        bell.GetAttributeValue<string>("body").Should().EndWith(SubmissionOutcomeNotifier.HeldSentence);
        SubmissionBellWorld.ActionUrl(bell).Should().Contain($"etn=sprk_invoice&id={InvoiceId:D}");
    }

    [Fact(DisplayName = "095b: no person on the submission creates nothing and logs at Information")]
    public async Task NoPerson_CreatesNothing_AndLogsInformation()
    {
        var world = new SubmissionBellWorld(SubmissionId, withPerson: false);

        await world.Build().NotifyAsync(SubmissionId, SubmissionOutcomeKind.Accepted, InvoiceId);

        world.Created.Should().BeEmpty();
        world.Log.Entries.Should().ContainSingle(e => e.Event == SubmissionOutcomeNotifierEvents.NoRecipient && e.Level == LogLevel.Information);
        world.Log.Entries.Should().NotContain(e => e.Level >= LogLevel.Warning);
    }

    // Seeded proof (task 095b): remove the catch in NotifyAsync and this fails with the writer's exception; the Warning's id is the stable one.
    [Fact(DisplayName = "095b: NotificationService throws -> the notifier logs a Warning with its EventId and swallows it (never fails the job)")]
    public async Task NotifyAsync_WhenTheWriterThrows_LogsTheWarningAndSwallowsIt()
    {
        var world = new SubmissionBellWorld(SubmissionId) { CreateFault = new InvalidOperationException("Dataverse refused the notification") };

        var act = async () => await world.Build().NotifyAsync(SubmissionId, SubmissionOutcomeKind.Accepted, InvoiceId);

        await act.Should().NotThrowAsync();
        world.Created.Should().BeEmpty();
        var warning = world.Log.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning).Subject;
        warning.Event.Should().Be(SubmissionOutcomeNotifierEvents.BellNotCreated);
        warning.Event.Id.Should().Be(50396, "the id is stable: alerts and queries key on it");
        warning.Message.Should().NotContain("Dataverse refused", "only the exception type is logged (ADR-015)");
    }

    [Fact(DisplayName = "095b: a failed read of the submission is swallowed the same way")]
    public async Task NotifyAsync_WhenTheReadThrows_LogsTheWarningAndSwallowsIt()
    {
        var world = new SubmissionBellWorld(SubmissionId) { ReadFault = new TimeoutException() };

        await world.Build().NotifyAsync(SubmissionId, SubmissionOutcomeKind.Failed, invoiceId: null);

        world.Created.Should().BeEmpty();
        world.Log.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning && e.Event == SubmissionOutcomeNotifierEvents.BellNotCreated);
    }

    // Justification (beyond the task's listed cases): the privacy constraint is a closed set the task states, so it is pinned once.
    [Fact(DisplayName = "095b: the text carries only the firm invoice number, the tracking number and the outcome sentence (no amounts, findings or names)")]
    public void Compose_NamesOnlyTheNumbersAndTheSentence()
    {
        foreach (var (outcome, held) in new[]
                 {
                     (SubmissionOutcomeKind.Accepted, false), (SubmissionOutcomeKind.Failed, false),
                     (SubmissionOutcomeKind.Refused, false), (SubmissionOutcomeKind.Refused, true),
                 })
        {
            var (title, body) = SubmissionOutcomeNotifier.Compose(outcome, held, "INV-100", "INV-100.2");
            var sentence = body["Tracking number INV-100.2. ".Length..];

            body.Should().StartWith("Tracking number INV-100.2. ");
            new[]
            {
                SubmissionOutcomeNotifier.AcceptedSentence, SubmissionOutcomeNotifier.FailedSentence,
                SubmissionOutcomeNotifier.RefusedSentence, SubmissionOutcomeNotifier.HeldSentence,
            }.Should().Contain(sentence);
            title.Should().StartWith("Invoice INV-100 ");
            (title + body).Should().NotMatchRegex(@"[$€£]|\d+\.\d{2}\b", "no amount may appear");
        }
    }
}
