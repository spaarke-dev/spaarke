using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Signals.Actions;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Signals;

/// <summary>
/// Task 071 / decision D-111: a reply arriving creates exactly ONE "Record the outcome of the budget inquiry" To Do for
/// the right person. Dataverse is faked at <see cref="IGenericEntityService"/> (module boundary, ADR-038).
/// </summary>
/// <remarks>
/// ADR-038 pairing: sprk_todo (sprk_assignedto contact, sprk_regardingservicerequest, sprk_duedate Date Only, statuscode
/// Open 1), sprk_matter.sprk_assignedtointernal (contact) and sprk_servicerequest were checked against spaarkedev1 on
/// 2026-10-09 by Dataverse describe (sprk_todo, sprk_servicerequest, sprk_communication) and by AssignedToDefaults's own
/// live check of sprk_matter (2026-10-02). The owner decision is uac-r2's resolver, faked here at its interface.
/// </remarks>
public sealed class InquiryReplyTodoCreatorTests
{
    private static readonly Guid Reply = Guid.NewGuid();
    private static readonly Guid Inquiry = Guid.NewGuid();
    private static readonly Guid Matter = Guid.NewGuid();
    private static readonly Guid Internal = Guid.NewGuid();
    private static readonly Guid Attorney = Guid.NewGuid();
    private static readonly Guid Team = Guid.NewGuid();

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class Rig
    {
        public Mock<IGenericEntityService> Db { get; } = new();
        public Mock<IRecordOwnershipResolver> Ownership { get; } = new();
        public List<Entity> Created { get; } = [];
        public bool TodoExists { get; set; }

        public Rig(
            int direction = 100000000, bool replyFiled = true, int requestDirection = 100000001, int? disposition = null,
            int state = 0, bool matterOnRequest = true, Guid? internalContact = null, Guid? attorney = null)
        {
            Db.Setup(d => d.RetrieveAsync("sprk_communication", Reply, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() =>
                {
                    var e = new Entity("sprk_communication", Reply) { ["sprk_direction"] = new OptionSetValue(direction) };
                    if (replyFiled)
                        e["sprk_regardingservicerequest"] = new EntityReference("sprk_servicerequest", Inquiry);
                    return e;
                });
            Db.Setup(d => d.RetrieveAsync("sprk_servicerequest", Inquiry, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() =>
                {
                    var e = new Entity("sprk_servicerequest", Inquiry)
                    {
                        ["sprk_name"] = "Budget inquiry",
                        ["sprk_direction"] = new OptionSetValue(requestDirection),
                        ["statecode"] = new OptionSetValue(state),
                    };
                    if (disposition is { } d2)
                        e["sprk_disposition"] = new OptionSetValue(d2);
                    if (matterOnRequest)
                        e["sprk_regardingmatter"] = new EntityReference("sprk_matter", Matter);
                    return e;
                });
            Db.Setup(d => d.RetrieveAsync("sprk_matter", Matter, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() =>
                {
                    var e = new Entity("sprk_matter", Matter);
                    if (internalContact is { } i)
                        e["sprk_assignedtointernal"] = new EntityReference("contact", i);
                    if (attorney is { } a)
                        e["sprk_assignedattorney1"] = new EntityReference("contact", a);
                    return e;
                });
            Db.Setup(d => d.RetrieveAsync("sprk_todo", InquiryDispositionService.TodoIdFor(Inquiry), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => TodoExists
                    ? new Entity("sprk_todo", InquiryDispositionService.TodoIdFor(Inquiry))
                    : throw new KeyNotFoundException("not found"));
            Db.Setup(d => d.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
                .Callback<Entity, CancellationToken>((e, _) => { Created.Add(e); TodoExists = true; })
                .ReturnsAsync((Entity e, CancellationToken _) => e.Id);

            Ownership.Setup(o => o.ResolveOwnerAsync(It.IsAny<RecordOwnershipContext>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(RecordOwnerResolution.Owned(Team));
        }

        public InquiryReplyTodoCreator Sut(CoreAncestorResolver? ancestors = null) => new(
            Db.Object,
            ancestors ?? CoreAncestorResolverFixtures.WithAncestors(("sprk_regardingmatter", Matter)),
            Ownership.Object,
            new Mock<ICommunicationDataverseService>().Object,
            new Clock(new DateTimeOffset(2026, 10, 9, 15, 0, 0, TimeSpan.Zero)),
            NullLogger<InquiryReplyTodoCreator>.Instance);
    }

    [Fact]
    public async Task A_reply_creates_one_todo_for_the_matters_assigned_to_internal_contact_due_today()
    {
        var rig = new Rig(internalContact: Internal, attorney: Attorney);

        var outcome = await rig.Sut().OnReplyArrivedAsync(Reply, default);

        outcome.Should().Be(InquiryReplyTodoOutcome.Created);
        var todo = rig.Created.Should().ContainSingle().Subject;
        todo.LogicalName.Should().Be("sprk_todo");
        todo.Id.Should().Be(InquiryDispositionService.TodoIdFor(Inquiry));
        todo.GetAttributeValue<string>("sprk_name").Should().Be("Record the outcome of the budget inquiry");
        todo.GetAttributeValue<EntityReference>("sprk_assignedto").Id.Should().Be(Internal, "Assigned To Internal wins over the first attorney");
        todo.GetAttributeValue<EntityReference>("sprk_regardingmatter").Id.Should().Be(Matter, "regarding the matter, as every To Do is");
        todo.Contains("sprk_regardingservicerequest").Should().BeFalse("ADR-024: one specific regarding lookup");
        todo.GetAttributeValue<string>("sprk_notes").Should().Be($"serviceRequestId={Inquiry:D}");
        todo.GetAttributeValue<EntityReference>("ownerid").Should().Match<EntityReference>(o => o.LogicalName == "team" && o.Id == Team);
        todo.GetAttributeValue<OptionSetValue>("statuscode").Value.Should().Be(1);
        todo.GetAttributeValue<DateTime>("sprk_duedate").Date.Should().Be(new DateTime(2026, 10, 9));
    }

    [Fact]
    public async Task A_second_reply_creates_no_second_todo()
    {
        var rig = new Rig(internalContact: Internal);
        var sut = rig.Sut();

        (await sut.OnReplyArrivedAsync(Reply, default)).Should().Be(InquiryReplyTodoOutcome.Created);
        (await sut.OnReplyArrivedAsync(Reply, default)).Should().Be(InquiryReplyTodoOutcome.AlreadyExists);

        rig.Created.Should().ContainSingle();
    }

    [Fact]
    public async Task Two_replies_racing_create_one_todo()
    {
        var rig = new Rig(internalContact: Internal);
        // Both pass the existence check; the loser's create is a duplicate key, after which the To Do exists.
        var checks = 0;
        rig.Db.Setup(d => d.RetrieveAsync("sprk_todo", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => ++checks == 1 ? throw new KeyNotFoundException() : new Entity("sprk_todo", InquiryDispositionService.TodoIdFor(Inquiry)));
        rig.Db.Setup(d => d.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("duplicate key"));

        (await rig.Sut().OnReplyArrivedAsync(Reply, default)).Should().Be(InquiryReplyTodoOutcome.AlreadyExists);
    }

    [Fact]
    public async Task Without_an_assigned_to_internal_contact_the_first_attorney_is_the_assignee()
    {
        var rig = new Rig(attorney: Attorney);

        await rig.Sut().OnReplyArrivedAsync(Reply, default);

        rig.Created.Single().GetAttributeValue<EntityReference>("sprk_assignedto").Id.Should().Be(Attorney);
    }

    [Fact]
    public async Task Without_anyone_to_name_the_todo_is_still_created_unassigned_and_owned_by_the_matters_team()
    {
        var rig = new Rig();

        var outcome = await rig.Sut().OnReplyArrivedAsync(Reply, default);

        outcome.Should().Be(InquiryReplyTodoOutcome.Created);
        var todo = rig.Created.Single();
        todo.Contains("sprk_assignedto").Should().BeFalse();
        todo.GetAttributeValue<EntityReference>("ownerid").Id.Should().Be(Team);
    }

    [Theory]
    [InlineData("outgoing")]
    [InlineData("unfiled")]
    [InlineData("inbound-request")]
    [InlineData("has-disposition")]
    [InlineData("closed")]
    public async Task Anything_but_an_inbound_reply_to_an_open_outbound_inquiry_creates_nothing(string scenario)
    {
        var rig = scenario switch
        {
            "outgoing" => new Rig(direction: 100000001),
            "unfiled" => new Rig(replyFiled: false),
            "inbound-request" => new Rig(requestDirection: 100000000),
            "has-disposition" => new Rig(disposition: 100000000),
            _ => new Rig(state: 1),
        };

        (await rig.Sut().OnReplyArrivedAsync(Reply, default)).Should().Be(InquiryReplyTodoOutcome.NotApplicable);
        rig.Created.Should().BeEmpty();
    }

    [Fact]
    public async Task No_owner_creates_nothing()
    {
        var rig = new Rig(internalContact: Internal);
        rig.Ownership.Setup(o => o.ResolveOwnerAsync(It.IsAny<RecordOwnershipContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RecordOwnerResolution.Refused("no_owner_source", "none"));

        (await rig.Sut().OnReplyArrivedAsync(Reply, default)).Should().Be(InquiryReplyTodoOutcome.Failed);
        rig.Created.Should().BeEmpty();
    }

    [Fact]
    public async Task A_failed_core_ancestor_stamp_creates_nothing()
    {
        var rig = new Rig(internalContact: Internal);

        (await rig.Sut(CoreAncestorResolverFixtures.Failing()).OnReplyArrivedAsync(Reply, default)).Should().Be(InquiryReplyTodoOutcome.Failed);
        rig.Created.Should().BeEmpty("an unstamped To Do is unreachable");
    }

    [Fact]
    public async Task A_dataverse_fault_never_escapes_to_email_capture()
    {
        var rig = new Rig();
        rig.Db.Setup(d => d.RetrieveAsync("sprk_communication", Reply, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("down"));

        (await rig.Sut().OnReplyArrivedAsync(Reply, default)).Should().Be(InquiryReplyTodoOutcome.Failed);
    }

    [Fact]
    public void The_todo_id_is_a_valid_version_5_uuid_and_deterministic()
    {
        var id = InquiryDispositionService.TodoIdFor(Inquiry);
        var text = id.ToString("D");

        text[14].Should().Be('5', "RFC 4122 version 5");
        "89ab".Should().Contain(text[19].ToString(), "RFC 4122 variant");
        InquiryDispositionService.TodoIdFor(Inquiry).Should().Be(id);
        InquiryDispositionService.TodoIdFor(Guid.NewGuid()).Should().NotBe(id);
    }

    [Theory]
    [InlineData("11111111-2222-3333-4444-555555555555", "231eac72-8c74-5a3d-be96-949700bd959b")]
    [InlineData("00000000-0000-0000-0000-000000000001", "864cb0f1-b3f1-5de3-909b-cdbe9d6398f9")]
    public void Known_answer_vectors_let_a_client_mirror_the_derivation(string serviceRequestId, string todoId)
    {
        // Namespace 6f1c0a52-3b7e-4d1a-9c2e-5a8d4b7e1f30; name "inquiry-outcome-todo:" + the lower-case id. The vectors were
        // computed independently (Python uuid.uuid5), so they pin the algorithm, not just this implementation.
        InquiryDispositionService.TodoIdNamespace.Should().Be(Guid.Parse("6f1c0a52-3b7e-4d1a-9c2e-5a8d4b7e1f30"));
        InquiryDispositionService.TodoIdFor(Guid.Parse(serviceRequestId)).Should().Be(Guid.Parse(todoId));
    }
}
