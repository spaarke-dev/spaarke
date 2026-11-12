using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.ExternalAccess;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Tests.AccessControl;
using Sprk.Bff.Api.Tests.AccessControl.IdentityBinding;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Signals;

/// <summary>
/// Task 193 (#2338, deliberate correction): the personal To Do assignee (D-134) answers "who is the Assigned To" through the
/// ONE contact -> user rule (<see cref="ContactUserRepresentation"/>) instead of reading the link alone. It now reads the
/// contact: an inactive contact represents nobody, and a user with no link whose oid the contact's binding carries counts —
/// only where the binder's oid decision resolves that oid to this contact alone. Its own count policy (exactly one eligible
/// user) and its own Restricted input (not read: external-flagged users never count) are unchanged; those, and the
/// link-only cases, stay pinned by DecisionCommitServiceTests and TodoAssigneeOwnerReconciliationJobTests.
/// </summary>
public class PersonalToDoAssigneeTests
{
    private readonly Guid _todo = Guid.NewGuid();
    private readonly Guid _contact = Guid.NewGuid();
    private readonly Mock<IGenericEntityService> _entities = new();
    private readonly AssignedAccessTestDoubles.FakeAssignedAccessStore _links = new();
    private readonly InMemoryContactIdentityStore _identities = new();

    public PersonalToDoAssigneeTests()
    {
        // A personal To Do (filed under nothing) whose Assigned To is the contact.
        var todo = new Entity(PersonalToDoAssignee.Table, _todo)
        {
            ["sprk_assignedto"] = new EntityReference("contact", _contact),
        };
        _entities
            .Setup(e => e.RetrieveAsync(PersonalToDoAssignee.Table, _todo, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(todo);
    }

    private Task<PersonalToDoAssignee.Answer> Classify()
        => PersonalToDoAssignee.ClassifyAsync(
            _entities.Object, AssignedAccessTestDoubles.Representation(_links, _identities), _todo, CancellationToken.None);

    private static AssignedLinkCandidate User(Guid id, Guid? linkedTo, Guid? oid = null)
        => new(id, linkedTo, oid ?? Guid.NewGuid(), IsDisabled: false, AccessMode: 0, ApplicationId: null, IsExternal: false);

    [Fact]
    public async Task ClassifyAsync_AssignedToAnInactiveContactWithOneLinkedUser_HasNoSingleUser()
    {
        _identities.AddContact(_contact, stateCode: 1);
        _links.UsersByLink[_contact] = new() { User(Guid.NewGuid(), _contact) };

        var answer = await Classify();

        answer.UserId.Should().BeNull();
        answer.Outcome.Should().Be(PersonalToDoAssignee.Outcome.NoLinkedUser);
    }

    [Fact]
    public async Task ClassifyAsync_AssignedToAContactWhoseOidBindingNamesOneUnlinkedUser_AnswersThatUser()
    {
        var oid = Guid.NewGuid();
        var user = Guid.NewGuid();
        _identities.AddContact(_contact, oid: oid.ToString());
        _links.UsersByOid[oid] = new() { User(user, linkedTo: null, oid) };

        var answer = await Classify();

        answer.UserId.Should().Be(user);
        answer.Outcome.Should().Be(PersonalToDoAssignee.Outcome.SingleUser);
    }

    [Fact]
    public async Task ClassifyAsync_AssignedToAContactWhoseOidAnotherContactAlsoCarries_HasNoSingleUser()
    {
        var oid = Guid.NewGuid();
        _identities.AddContact(_contact, oid: oid.ToString());
        _identities.AddContact(Guid.NewGuid(), oid: oid.ToString()); // the read path resolves neither contact on this oid
        _links.UsersByOid[oid] = new() { User(Guid.NewGuid(), linkedTo: null, oid) };

        var answer = await Classify();

        answer.UserId.Should().BeNull();
        answer.Outcome.Should().Be(PersonalToDoAssignee.Outcome.NoLinkedUser);
    }

    [Fact]
    public async Task ClassifyAsync_WhenTheContactReadFails_Throws()
    {
        _identities.AddContact(_contact);
        _links.UsersByLink[_contact] = new() { User(Guid.NewGuid(), _contact) };
        _identities.FailGetContact = true;

        var act = () => Classify();

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
