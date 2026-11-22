using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Communication.Access;
using Sprk.Bff.Api.Services.Communication.Membership;
using Sprk.Bff.Api.Services.Communication.Models;
using Sprk.Bff.Api.Tests.AccessControl;
using Sprk.Bff.Api.Tests.AccessControl.IdentityBinding;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Communication;

/// <summary>
/// field-mapping-server-write-path-r1 task 056b: the <see cref="PrivateMessageSendGate"/>'s Dataverse-facing seams, run for REAL
/// (the send contract tests script all six whole, so their own logic ran nowhere). The gate is the production class with no seam
/// overridden; its dependencies are doubled at their boundary, as <c>AssignTaskRecipientGateSeamTests</c> does for the same three
/// questions: the caller's and a named user's rights over a stubbed <see cref="CallerRecordAccessProbe"/> transport method, the No
/// Access wall over the REAL <see cref="SecureShareNoAccessGuard"/>, the effective Restricted flag over a stubbed
/// <see cref="ExternalParticipationService"/> flag read, and the user and column-metadata reads over a mocked
/// <see cref="IGenericEntityService"/>.
/// </summary>
public sealed class PrivateMessageSendGateSeamTests
{
    private const string ThreadTable = "sprk_communicationthread";
    private const int ThreadTypeDirect = 100000001;
    private const int ReadMask = 1;

    private static readonly Guid Sender = Guid.Parse("05605605-6b00-4000-8000-0000000000a1");
    private static readonly Guid Recipient = Guid.Parse("05605605-6b00-4000-8000-0000000000a2");
    private static readonly Guid Thread = Guid.Parse("05605605-6b00-4000-8000-0000000000a3");
    private static readonly Guid Matter = Guid.Parse("05605605-6b00-4000-8000-0000000000a4");
    private static readonly Guid UnknownMatter = Guid.Parse("05605605-6b00-4000-8000-0000000000a5");

    private readonly Mock<IGenericEntityService> _entities = new();
    private readonly Mock<IDataverseRecordShareService> _shares = new();
    private readonly Mock<CallerRecordAccessProbe> _probe = new(
        new HttpClient(), new ConfigurationBuilder().Build(), NullLogger<CallerRecordAccessProbe>.Instance, null!, null!);
    // Two flag reads: the gate's own effective Restricted read (_flags) and the guard's Secure reads (_wallFlags), kept apart so
    // each seam's answer is set on its own.
    private readonly GrantPolicyTestDoubles.FlagStubParticipationService _flags = new(defaultFlags: RootRecordFlags.None);
    private readonly GrantPolicyTestDoubles.FlagStubParticipationService _wallFlags = new(defaultFlags: RootRecordFlags.None);
    private readonly GrantPolicyTestDoubles.SeamNoAccessListReader _noAccessList = new();
    private readonly InMemoryContactIdentityStore _identities = new();
    private readonly Dictionary<Guid, bool?> _isExternal = new();
    private IReadOnlyDictionary<string, ColumnMetadata>? _communicationColumns;

    public PrivateMessageSendGateSeamTests()
    {
        // The pair's strict 1:1 Direct thread: owned by the sender, one Read share to the recipient.
        _entities
            .Setup(e => e.RetrieveAsync(ThreadTable, Thread, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity(ThreadTable, Thread)
            {
                ["sprk_threadtype"] = new OptionSetValue(ThreadTypeDirect),
                ["ownerid"] = new EntityReference("systemuser", Sender),
                ["sprk_privacystate"] = new OptionSetValue((int)ThreadPrivacyState.Open),
            });
        _shares
            .Setup(s => s.GetPrincipalAccessOrThrowAsync(ThreadTable, Thread, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DataversePrincipalAccess> { new(DataversePrincipalRef.User(Recipient), ReadMask, DateTimeOffset.UnixEpoch) });

        // The systemuser read of IsExternalAsync: a row only for a user in _isExternal, carrying its stored flag.
        _entities
            .Setup(e => e.RetrieveMultipleAsync(It.Is<QueryExpression>(q => q.EntityName == "systemuser"), It.IsAny<CancellationToken>()))
            .ReturnsAsync((QueryExpression q, CancellationToken _) =>
            {
                var id = (Guid)q.Criteria.Conditions.Single(c => c.AttributeName == "systemuserid").Values.Single();
                var rows = new List<Entity>();
                if (_isExternal.TryGetValue(id, out var flag))
                {
                    var row = new Entity("systemuser", id);
                    if (flag is { } stored)
                        row["sprk_isexternal"] = stored;
                    rows.Add(row);
                }

                return new EntityCollection(rows);
            });

        // The schema scripts have run: sprk_communication has every reference column.
        _communicationColumns = Columns(PrivateContextColumns.All);
        _entities
            .Setup(e => e.GetColumnMetadataAsync("sprk_communication", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _communicationColumns!);

        // Both users are internal, can read the matter, and are on no No Access list; the matter is neither Secure nor Restricted.
        _isExternal[Sender] = false;
        _isExternal[Recipient] = false;
        _identities.AddSystemUser(Sender, Guid.NewGuid(), "sender@customer.example");
        _identities.AddSystemUser(Recipient, Guid.NewGuid(), "recipient@customer.example");
        _flags.Flags[Matter] = new RootRecordFlags(IsSecure: false, IsRestricted: false);
        _probe
            .Setup(p => p.GetCallerRightsAsync("caller-token", "sprk_matters", Matter, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AccessRights.Read);
        _probe
            .Setup(p => p.GetPrincipalRightsAppOnlyOrUnknownAsync(Recipient, "sprk_matters", Matter, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AccessRights.Read);
    }

    private PrivateMessageSendGate Gate() =>
        new(_probe.Object,
            new SecureShareNoAccessGuard(_wallFlags, _noAccessList, _identities, AssignedAccessTestDoubles.NoFilingRows(),
                NullLogger<SecureShareNoAccessGuard>.Instance),
            _flags, _entities.Object, _shares.Object, NullLogger<PrivateMessageSendGate>.Instance);

    private Task<PrivateMessageReference> SendAbout(Guid record) =>
        Gate().CheckBeforeSendAsync(
            new SendCommunicationRequest
            {
                CommunicationType = CommunicationType.Message,
                ThreadId = Thread,
                To = Array.Empty<string>(),
                Subject = "About the matter",
                Body = "Only for you.",
                PrivateContext = new PrivateMessageContext { EntityType = "sprk_matter", Id = record },
            },
            Sender, "caller-token", "corr-056b", CancellationToken.None);

    private static IReadOnlyDictionary<string, ColumnMetadata> Columns(IEnumerable<string> names) =>
        names.Append("sprk_communicationid")
            .ToDictionary(c => c, c => new ColumnMetadata(c, "Lookup", true, true, null), StringComparer.OrdinalIgnoreCase);

    private void RecipientAskedNothing() =>
        _probe.Verify(p => p.GetPrincipalRightsAppOnlyOrUnknownAsync(Recipient, It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);

    // ── the happy path: every seam answered for real ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CheckBeforeSend_BothParticipantsInternal_WithRead_OnAnOrdinaryMatter_ReturnsTheReference()
    {
        var reference = await SendAbout(Matter);

        reference.Should().Be(new PrivateMessageReference("sprk_matter", Matter));
    }

    // ── (a) IsExternalAsync: the user's stored flag; a user with no row counts as external ─────────────────────────────────────

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("unset", false)]
    [InlineData("no row", true)]
    public async Task IsExternalAsync_ReadsTheStoredFlag_AndAUserWithNoRowIsExternal(string stored, bool expected)
    {
        var user = Guid.NewGuid();
        if (stored != "no row")
            _isExternal[user] = stored == "unset" ? null : stored == "true";

        (await Gate().IsExternalAsync(user, CancellationToken.None)).Should().Be(expected);
    }

    [Fact]
    public async Task CheckBeforeSend_ARecipientWithNoSystemUserRow_OnARestrictedMatter_IsRefusedAsTheRecipient()
    {
        _flags.Flags[Matter] = new RootRecordFlags(IsSecure: false, IsRestricted: true);
        _isExternal.Remove(Recipient);

        var act = () => SendAbout(Matter);

        var problem = (await act.Should().ThrowAsync<SdapProblemException>()).Which;
        problem.StatusCode.Should().Be(403);
        problem.Code.Should().Be(PrivateMessageSendGate.RecipientAccessCode);
    }

    [Fact]
    public async Task CheckBeforeSend_TheUserReadFaults_OnARestrictedMatter_Is503()
    {
        _flags.Flags[Matter] = new RootRecordFlags(IsSecure: false, IsRestricted: true);
        _entities
            .Setup(e => e.RetrieveMultipleAsync(It.Is<QueryExpression>(q => q.EntityName == "systemuser"), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("systemuser read faulted"));

        var act = () => SendAbout(Matter);

        (await act.Should().ThrowAsync<SdapProblemException>()).Which.Code.Should().Be(PrivateMessageSendGate.CheckUnavailableCode);
    }

    // ── (b) EffectiveRestrictedAsync: an unreadable flag ─────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("restricted", true)]
    [InlineData("standard", false)]
    [InlineData("unreadable", null)]
    [InlineData("absent", null)]
    public async Task EffectiveRestrictedAsync_ReadsTheEffectiveFlag_AndAnythingUnreadIsNull(string state, bool? expected)
    {
        switch (state)
        {
            case "restricted": _flags.Flags[Matter] = new RootRecordFlags(IsSecure: false, IsRestricted: true); break;
            case "unreadable": _flags.Flags[Matter] = RootRecordFlags.Unreadable; break;
            case "absent": _flags.Absent[Matter] = true; break;
        }

        (await Gate().EffectiveRestrictedAsync("sprk_matter", Matter, CancellationToken.None)).Should().Be(expected);
    }

    [Fact]
    public async Task CheckBeforeSend_AnUnreadableRestrictedFlag_WithAParticipantFlaggedExternal_Is503()
    {
        // Only the gate's Restricted read is unanswered; the wall's own flag read answers (not Secure).
        _flags.Flags[Matter] = RootRecordFlags.Unreadable;
        _isExternal[Recipient] = true;

        var act = () => SendAbout(Matter);

        var problem = (await act.Should().ThrowAsync<SdapProblemException>()).Which;
        problem.StatusCode.Should().Be(503);
        problem.Code.Should().Be(PrivateMessageSendGate.CheckUnavailableCode);
    }

    [Fact]
    public async Task CheckBeforeSend_AnUnreadableRestrictedFlag_WithBothParticipantsInternal_IsAllowed()
    {
        // The flag bars only a participant flagged external (InternalShareEndpoints.IsBarredOnRestricted), so for an internal pair
        // its value cannot change the answer: the gate asks each user's flag and, finding neither external, does not need it.
        _flags.Flags[Matter] = RootRecordFlags.Unreadable;

        var reference = await SendAbout(Matter);

        reference.Should().Be(new PrivateMessageReference("sprk_matter", Matter));
    }

    // ── (c) ReferenceColumnsPresentAsync: the cache, and the 503 when metadata gives no answer ──────────────────────────────────

    [Fact]
    public async Task ReferenceColumnsPresentAsync_APresentAnswer_IsReadOnceAndRemembered()
    {
        var gate = Gate();

        (await gate.ReferenceColumnsPresentAsync(CancellationToken.None)).Should().BeTrue();
        (await gate.ReferenceColumnsPresentAsync(CancellationToken.None)).Should().BeTrue();
        (await Gate().ReferenceColumnsPresentAsync(CancellationToken.None)).Should().BeTrue("the answer is kept per connection");

        _entities.Verify(e => e.GetColumnMetadataAsync("sprk_communication", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReferenceColumnsPresentAsync_AMissingColumn_IsAskedAgain_AndSeenOnceTheScriptHasRun()
    {
        _communicationColumns = Columns(PrivateContextColumns.All.Where(c => c != PrivateContextColumns.Project));
        var gate = Gate();

        (await gate.ReferenceColumnsPresentAsync(CancellationToken.None)).Should().BeFalse();
        _communicationColumns = Columns(PrivateContextColumns.All);
        (await gate.ReferenceColumnsPresentAsync(CancellationToken.None)).Should().BeTrue();

        _entities.Verify(e => e.GetColumnMetadataAsync("sprk_communication", It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task CheckBeforeSend_AMissingReferenceColumn_Is409_NamingTheSchemaScript()
    {
        _communicationColumns = Columns(PrivateContextColumns.All.Where(c => c != PrivateContextColumns.WorkAssignment));

        var act = () => SendAbout(Matter);

        var problem = (await act.Should().ThrowAsync<SdapProblemException>()).Which;
        problem.StatusCode.Should().Be(409);
        problem.Code.Should().Be(PrivateMessageSendGate.ColumnsMissingCode);
        problem.Detail.Should().Contain(PrivateMessageSendGate.SchemaScript);
    }

    [Fact]
    public async Task CheckBeforeSend_MetadataWithNoAnswer_Is503_NotA409()
    {
        _communicationColumns = null;

        var act = () => SendAbout(Matter);

        var problem = (await act.Should().ThrowAsync<SdapProblemException>()).Which;
        problem.StatusCode.Should().Be(503);
        problem.Code.Should().Be(PrivateMessageSendGate.CheckUnavailableCode);
    }

    [Fact]
    public async Task CheckBeforeSend_AMetadataReadThatFaults_Is503()
    {
        _entities
            .Setup(e => e.GetColumnMetadataAsync("sprk_communication", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("metadata read faulted"));

        var act = () => SendAbout(Matter);

        (await act.Should().ThrowAsync<SdapProblemException>()).Which.StatusCode.Should().Be(503);
    }

    // ── (d) WallAsync: the REAL guard, asked AsFlagged ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task WallAsync_AUserOnASecureMattersNoAccessList_IsWalled()
    {
        _wallFlags.Flags[Matter] = new RootRecordFlags(IsSecure: true, IsRestricted: false);
        _noAccessList.DenySystemUserOnRecord(Recipient, Matter);

        (await Gate().WallAsync("sprk_matter", Matter, Recipient, CancellationToken.None)).Should().Be(SecureShareWallOutcome.Walled);
    }

    [Fact]
    public async Task WallAsync_AsksAsFlagged_SoAnEntryOnANonSecureMatter_DoesNotWall()
    {
        // AsFlagged: the matter's own (effective) Secure flag decides whether its list applies. Asked as BeingSecured, the same
        // entry would wall the user off an ordinary matter.
        _noAccessList.DenySystemUserOnRecord(Recipient, Matter);

        (await Gate().WallAsync("sprk_matter", Matter, Recipient, CancellationToken.None)).Should().Be(SecureShareWallOutcome.NotSecure);
    }

    [Fact]
    public async Task CheckBeforeSend_AWalledRecipient_OnASecureMatter_IsTheRecipientRefusal_AfterTheSendersChecks()
    {
        _wallFlags.Flags[Matter] = new RootRecordFlags(IsSecure: true, IsRestricted: false);
        _noAccessList.DenySystemUserOnRecord(Recipient, Matter);

        var act = () => SendAbout(Matter);

        var problem = (await act.Should().ThrowAsync<SdapProblemException>()).Which;
        problem.StatusCode.Should().Be(403);
        problem.Code.Should().Be(PrivateMessageSendGate.RecipientAccessCode);
        problem.Detail.Should().Be(PrivateMessageSendGate.RecipientAccessDetail);
    }

    [Fact]
    public async Task CheckBeforeSend_AWalledSender_IsTheSenderRefusal_AndNoRecipientQuestionIsAsked()
    {
        _wallFlags.Flags[Matter] = new RootRecordFlags(IsSecure: true, IsRestricted: false);
        _noAccessList.DenySystemUserOnRecord(Sender, Matter);

        var act = () => SendAbout(Matter);

        (await act.Should().ThrowAsync<SdapProblemException>()).Which.Code.Should().Be(PrivateMessageSendGate.SenderAccessCode);
        RecipientAskedNothing();
    }

    // ── (e) an unknown record id: the sender's denial, never a 404 ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task CheckBeforeSend_AnUnknownRecordId_IsTheSendersDenial_TheSameAsADeniedRecord_AndAsksNothingAboutTheRecipient()
    {
        // The probe answers None for an id it has no record of (as Dataverse's not-found reaches the gate question), and None for
        // a record the caller may not read.
        _probe
            .Setup(p => p.GetCallerRightsAsync("caller-token", "sprk_matters", Matter, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AccessRights.None);

        var unknown = await Refusal(() => SendAbout(UnknownMatter));
        var denied = await Refusal(() => SendAbout(Matter));

        unknown.StatusCode.Should().Be(403);
        unknown.Code.Should().Be(PrivateMessageSendGate.SenderAccessCode);
        unknown.Detail.Should().Be(PrivateMessageSendGate.SenderAccessDetail);
        (unknown.StatusCode, unknown.Code, unknown.Title, unknown.Detail)
            .Should().Be((denied.StatusCode, denied.Code, denied.Title, denied.Detail), "an unknown id and a denied one look the same");
        RecipientAskedNothing();
        _flags.Reads.Should().BeEmpty("no Restricted question is asked of a record the sender cannot read");
        _wallFlags.Reads.Should().BeEmpty("no wall question is asked of a record the sender cannot read");
    }

    private static async Task<SdapProblemException> Refusal(Func<Task> send) =>
        (await send.Should().ThrowAsync<SdapProblemException>()).Which;
}
