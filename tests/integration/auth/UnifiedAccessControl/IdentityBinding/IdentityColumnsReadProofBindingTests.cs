using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Xunit;
using static Sprk.Bff.Api.Tests.AccessControl.IdentityBinding.IdentityBindingTestKit;

namespace Sprk.Bff.Api.Tests.AccessControl.IdentityBinding;

/// <summary>
/// The identity columns (the binding, its plane and the five collision-flag columns) are field-secured, so an EMPTY read
/// of one of them is trusted only while the BFF's read of all of them is proven (<see cref="SecuredColumnReadProof"/>).
/// Without proof a contact whose plane read empty is <see cref="BindingKind.Unreadable"/>: the bind is refused and the
/// contact flagged, and the masking probe reads Masked. With proof the binder decides exactly as before.
/// </summary>
/// <remarks>
/// The proof outcome comes from the REAL <see cref="SecuredColumnReadProof"/> over a fake permission source; the marking
/// and the probe verdict are the Dataverse store's own (<see cref="InMemoryContactIdentityStore.IdentityColumnsProof"/>);
/// the decision is the real binder's.
/// </remarks>
public class IdentityColumnsReadProofBindingTests
{
    private static readonly Guid CiamOid = Guid.Parse("dddddddd-4444-4444-8444-0000000000f1");
    private static readonly Guid ContactA = Guid.Parse("eeeeeeee-5555-4555-8555-0000000000f1");
    private const string Email = "counsel@firm.example";

    private readonly InMemoryContactIdentityStore _store = new();

    private string InvitedContactA()
    {
        var invite = InviteCode.Create(ContactA);
        _store.AddContact(ContactA, email: Email, invitedOn: Now, inviteCodeHash: invite.StoredValue);
        _store.StampInvite(ContactA, Email);
        return invite.Code;
    }

    /// <summary>
    /// The store's two proofs (the binding columns, the collision columns) made by the REAL proof over a source granting
    /// Read on <paramref name="grantedColumns"/>, as the Dataverse store makes them: one per column set.
    /// </summary>
    private async Task ProveOver(params string[] grantedColumns)
    {
        _store.BindingColumnsProof = await ProofOf(DataverseContactIdentityStore.BindingColumns, new GrantedColumns(grantedColumns));
        _store.CollisionColumnsProof = await ProofOf(DataverseContactIdentityStore.CollisionColumns, new GrantedColumns(grantedColumns));
    }

    private static Task<ColumnReadProofOutcome> ProofOf(IReadOnlyCollection<string> columns, IColumnReadPermissionSource source)
        => new SecuredColumnReadProof(source, DataverseContactIdentityStore.ContactTable, columns, NullLogger.Instance)
            .ProveAsync(CancellationToken.None);

    private static string[] EveryIdentityColumnBut(string column)
        => DataverseContactIdentityStore.IdentityColumns.Where(c => c != column).ToArray();

    // ── must fire ───────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AnEmptyPlaneRead_WithoutProof_IsNoBind_TheContactIsRefusedAndFlagged()
    {
        // The contact's employee binding is hidden from this identity: oid and plane both read empty, so the row looks unbound.
        var code = InvitedContactA();
        var employee = Guid.NewGuid();
        _store.Contacts[ContactA].Oid = employee.ToString("D");
        _store.Contacts[ContactA].Plane = (int)IdentityPlaneMarker.Workforce;
        _store.MaskBindingColumn = true;
        _store.MaskIdentityColumns = true;
        await ProveOver(EveryIdentityColumnBut(ContactBindingDecision.IdentityPlaneColumn));

        var result = await Binder(_store).ResolveCiamCallerAsync(CiamOid.ToString(), Email, code, CancellationToken.None);

        _store.BindingColumnsProof.Should().Be(ColumnReadProofOutcome.Unproven);
        _store.CollisionColumnsProof.Should().Be(ColumnReadProofOutcome.Proven, "the flag's read is proven, so it may be written");
        result.IsResolved.Should().BeFalse("an empty plane read without proof may hide a binding: no bind");
        _store.Writes.Should().NotContain(w => w.Op == "bind" || w.Op == "create");
        _store.Contacts[ContactA].Oid.Should().Be(employee.ToString("D"), "the hidden binding stands");
        _store.Writes.Should().Contain(w => w.Op == "flag" && w.RowId == ContactA
            && w.Detail.StartsWith(nameof(IdentityCollisionReason.BindingUnreadable)), "the refusal is flagged for an operator");
    }

    [Fact]
    public async Task AnEmptyCollisionRead_WithoutProof_RefusesTheBind_AndWritesNoFlag()
    {
        // Truly unbound and invited: only the collision columns' read is not proven. The bind is refused (the masking probe
        // needs every identity column proven), and no flag is written: a write sets every collision column, and a flag
        // read without proof may be hidden.
        var code = InvitedContactA();
        await ProveOver(EveryIdentityColumnBut(DataverseContactIdentityStore.FlagPartiesColumn));

        var result = await Binder(_store).ResolveCiamCallerAsync(CiamOid.ToString(), Email, code, CancellationToken.None);

        _store.BindingColumnsProof.Should().Be(ColumnReadProofOutcome.Proven);
        _store.CollisionColumnsProof.Should().Be(ColumnReadProofOutcome.Unproven);
        result.IsResolved.Should().BeFalse("an identity column read without proof refuses the bind");
        result.DenyCode.Should().Be(ContactBindingDecision.DenyBindingColumnMasked);
        _store.Contacts[ContactA].Oid.Should().BeNull();
        _store.Writes.Should().NotContain(w => w.Op == "bind");
        _store.Writes.Should().NotContain(w => w.Op == "flag", "a collision flag read without proof is never written over");
    }

    [Fact]
    public async Task ACollisionOnABoundContact_WhoseFlagReadIsNotProven_WritesNoFlag_AndKeepsTheHiddenParties()
    {
        // The plane is set and read (a value), but the collision columns' read is not proven: the flag on the contact reads
        // empty although it records a party. Writing the new collision would replace every collision column.
        var log = new BindingLogCapture<ContactIdentityBinder>();
        var code = InvitedContactA();
        var employee = Guid.NewGuid();
        var recorded = new CollisionParty(Guid.NewGuid(), IdentityPlaneMarker.Workforce, IdentityCollisionReason.EmailAmbiguous, Now);
        _store.Contacts[ContactA].Oid = employee.ToString("D");
        _store.Contacts[ContactA].Plane = (int)IdentityPlaneMarker.Workforce;
        _store.Contacts[ContactA].Flag = CollisionFlag.FromParties(new[] { recorded });
        _store.MaskCollisionColumns = true;
        _store.CollisionColumnsProof = ColumnReadProofOutcome.Unproven;

        var result = await Binder(_store, log).ResolveCiamCallerAsync(CiamOid.ToString(), Email, code, CancellationToken.None);

        result.IsResolved.Should().BeFalse("the contact is bound to another identity: the deny stands");
        result.Action.Should().Be(BindingAction.Collision);
        _store.Writes.Should().NotContain(w => w.Op == "flag", "a flag read without proof is never written over");
        _store.Contacts[ContactA].Flag!.Parties.Should().Equal(recorded);
        var line = log.Entries.Should().ContainSingle(e => e.Level == LogLevel.Error && e.Message.Contains("NOT flagged")).Subject.Message;
        line.Should().Contain(ContactA.ToString()).And.Contain(result.Reason!.Value.ToString());
        line.Should().NotContain(Email).And.NotContain(CiamOid.ToString()).And.NotContain(employee.ToString())
            .And.NotContain(recorded.Oid!.Value.ToString(), "the operator line names the contact and the reason only");
    }

    [Fact]
    public async Task WithoutProof_TheMaskingProbeIsMasked_EvenWithNoMarkedRows_SoNoBindIsWritten()
    {
        // A hidden plane reads as no plane: the probe would otherwise find no marked rows ("nothing to protect").
        _store.AddContact(ContactA, email: Email);
        await ProveOver(EveryIdentityColumnBut(ContactBindingDecision.IdentityPlaneColumn));

        var write = await Binder(_store).BindInvitedContactAsync(ContactA, etag: null, CiamOid, consumeInviteCode: false, CancellationToken.None);

        write.Status.Should().Be(StoreWriteStatus.Failed);
        write.Error.Should().Be(ContactBindingDecision.DenyBindingColumnMasked);
        _store.Contacts[ContactA].Oid.Should().BeNull();
    }

    [Fact]
    public async Task AProofThatFaulted_DeniesLikeAnUnprovenOne()
    {
        // The empty plane is Unreadable (the binding is refused as unreadable, not merely by the masking probe), and with
        // the collision columns' read not proven either, no flag is written over a flag that may be hidden.
        var code = InvitedContactA();
        _store.BindingColumnsProof = await ProofOf(DataverseContactIdentityStore.BindingColumns, new GrantedColumns(faults: true));
        _store.CollisionColumnsProof = await ProofOf(DataverseContactIdentityStore.CollisionColumns, new GrantedColumns(faults: true));

        var result = await Binder(_store).ResolveCiamCallerAsync(CiamOid.ToString(), Email, code, CancellationToken.None);

        _store.IdentityColumnsProof.Should().Be(ColumnReadProofOutcome.Faulted);
        result.IsResolved.Should().BeFalse();
        result.DenyCode.Should().Be(ContactBindingDecision.DenyContactBindingUnreadable);
        result.Reason.Should().Be(IdentityCollisionReason.BindingUnreadable);
        _store.Contacts[ContactA].Oid.Should().BeNull();
        _store.Writes.Should().NotContain(w => w.Op == "bind" || w.Op == "flag");
    }

    // ── must not fire ───────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AnEmptyRead_WithProof_BindsAsToday()
    {
        var code = InvitedContactA();
        await ProveOver(DataverseContactIdentityStore.IdentityColumns.ToArray());

        var result = await Binder(_store).ResolveCiamCallerAsync(CiamOid.ToString(), Email, code, CancellationToken.None);

        _store.IdentityColumnsProof.Should().Be(ColumnReadProofOutcome.Proven);
        result.ContactId.Should().Be(ContactA);
        _store.Contacts[ContactA].Oid.Should().Be(CiamOid.ToString("D"));
        _store.Contacts[ContactA].Plane.Should().Be((int)IdentityPlaneMarker.External);
        _store.Writes.Should().NotContain(w => w.Op == "flag");
    }

    [Fact]
    public async Task ACollisionOnABoundContact_WithBothProofs_FlagsTheContactAsToday()
    {
        // The same collision as above with every identity column's read proven: the party is appended to the readable flag.
        var code = InvitedContactA();
        var recorded = new CollisionParty(Guid.NewGuid(), IdentityPlaneMarker.Workforce, IdentityCollisionReason.EmailAmbiguous, Now);
        _store.Contacts[ContactA].Oid = Guid.NewGuid().ToString("D");
        _store.Contacts[ContactA].Plane = (int)IdentityPlaneMarker.Workforce;
        _store.Contacts[ContactA].Flag = CollisionFlag.FromParties(new[] { recorded });
        await ProveOver(DataverseContactIdentityStore.IdentityColumns.ToArray());

        var result = await Binder(_store).ResolveCiamCallerAsync(CiamOid.ToString(), Email, code, CancellationToken.None);

        _store.IdentityColumnsProof.Should().Be(ColumnReadProofOutcome.Proven);
        result.IsResolved.Should().BeFalse();
        result.Action.Should().Be(BindingAction.Collision);
        _store.Writes.Should().ContainSingle(w => w.Op == "flag" && w.RowId == ContactA);
        _store.Contacts[ContactA].Flag!.Parties.Should().HaveCount(2).And.Contain(recorded);
    }

    [Fact]
    public async Task AReadOfAPlaneThatIsSet_IsDecidedOnWithoutProof()
    {
        // A value read is not empty: the proof is for empty reads only. A CIAM binding resolves by its oid as before.
        _store.AddContact(ContactA, email: Email, oid: CiamOid.ToString("D"), plane: IdentityPlaneMarker.External);
        _store.IdentityColumnsProof = ColumnReadProofOutcome.Unproven;

        var result = await Binder(_store).ResolveCiamCallerAsync(CiamOid.ToString(), Email, inviteCode: null, CancellationToken.None);

        result.ContactId.Should().Be(ContactA);
        _store.Writes.Should().BeEmpty();
    }

    /// <summary>A permission source granting Read on the listed columns through the BFF user's own profile.</summary>
    private sealed class GrantedColumns(IReadOnlyCollection<string>? granted = null, bool faults = false) : IColumnReadPermissionSource
    {
        private static readonly Guid Me = Guid.Parse("aaaaaaaa-0000-0000-0000-0000000000f1");

        public Task<Guid?> ReadOwnUserIdAsync(CancellationToken ct)
            => faults ? throw new HttpRequestException("throttled") : Task.FromResult<Guid?>(Me);

        public Task<bool> HoldsSystemAdministratorAsync(Guid user, CancellationToken ct) => Task.FromResult(false);

        public Task<IReadOnlyCollection<string>> ColumnsGrantingReadAsync(
            Guid user, string table, IReadOnlyCollection<string> columns, bool throughTeam, CancellationToken ct)
            => Task.FromResult<IReadOnlyCollection<string>>(throughTeam
                ? Array.Empty<string>()
                : columns.Where(c => (granted ?? Array.Empty<string>()).Contains(c)).ToArray());
    }
}
