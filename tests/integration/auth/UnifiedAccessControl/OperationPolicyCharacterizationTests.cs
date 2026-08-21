using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Spaarke.Core.Auth;
using Spaarke.Core.Auth.Rules;
using Spaarke.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// Characterization + negative suite for <see cref="OperationAccessPolicy"/> and
/// <see cref="OperationAccessRule"/> — the operation-string → required-rights table that every
/// endpoint filter consults.
///
/// Pins two confirmed findings (unified-access-control-r2 spec NFR-07):
///   A-3  — "read" is not a key in the policy, so every site passing it is an unconditional 403.
///   A-20 — three further operation strings are absent ("finance.read", "finance.confirm",
///          "entity.associate_document"), AND the access snapshot's rights ceiling is Read, which
///          defeats every policy requiring Write or above.
///
/// The Characterization_ prefix is the contract: tasks 003 and 005 grep for it and flip the pinned
/// expectation to the fixed one.
/// </summary>
public class OperationPolicyCharacterizationTests
{
    private static OperationAccessRule Rule() => new(NullLogger<OperationAccessRule>.Instance);

    private static AccessSnapshot Snapshot(AccessRights rights) => new()
    {
        UserId = "user-1",
        ResourceId = "resource-1",
        AccessRights = rights
    };

    private static AuthorizationContext Context(string operation) => new()
    {
        UserId = "user-1",
        ResourceId = "resource-1",
        Operation = operation,
        // These tests exercise OperationAccessRule directly (not AuthorizationService), so the token
        // never reaches a data source — but task 004 made UserAccessToken `required`, which forces
        // every construction site to state its intent rather than inherit app-only by omission.
        UserAccessToken = "rule-level-test-token"
    };

    // ─────────────────────────────────────────────────────────────────────────────
    // NEGATIVE — must already hold. Fail-closed on an operation the policy doesn't know.
    // ADR-003: characterization MUST NOT weaken fail-closed behavior; this pins the deny.
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task EvaluateAsync_WhenOperationAbsentFromPolicy_DeniesWithUnknownOperation()
    {
        // Arrange — an operation string that is not, and should never be, a policy key.
        var snapshot = Snapshot(AccessRights.Read | AccessRights.Write | AccessRights.Delete);

        // Act
        var result = await Rule().EvaluateAsync(Context("definitely.not.a.real.operation"), snapshot);

        // Assert — fail-closed regardless of how many rights the caller holds.
        result.Decision.Should().Be(AuthorizationDecision.Deny);
        result.ReasonCode.Should().Be("sdap.access.deny.unknown_operation");
    }

    [Fact]
    public void IsOperationSupported_ForOperationAbsentFromPolicy_ReturnsFalse()
    {
        OperationAccessPolicy.IsOperationSupported("definitely.not.a.real.operation").Should().BeFalse();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // CHARACTERIZATION — A-3 / A-20: operation strings live enforcement sites pass, which
    // are absent from the policy and therefore deny unconditionally.
    // Flipped by: task 003 (add the missing keys + a completeness test).
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ✅ FLIPPED BY TASK 003 (FR-03) — was a Characterization_ test pinning A-3/A-20.
    ///
    /// Each of these strings is passed by a live enforcement site:
    ///   "read"                       → DataverseDocumentsEndpoints.cs:443,
    ///                                  FileAccessEndpoints.cs:118 (eml-render),
    ///                                  ChatDocumentEndpoints.cs:915
    ///   "finance.read"               → FinanceEndpoints.cs:18, :51, :65
    ///   "finance.confirm"            → FinanceEndpoints.cs:23, :37
    ///   "entity.associate_document"  → EntityAccessFilter.cs:64 (OfficeEndpoints.cs:173)
    ///
    /// Before task 003 none was a policy key, so each site returned 403 for every caller regardless
    /// of rights. They now resolve. The general forcing function preventing recurrence lives in
    /// <see cref="OperationAccessPolicyCompletenessTests"/>.
    /// </summary>
    [Theory]
    [InlineData("read")]
    [InlineData("finance.read")]
    [InlineData("finance.confirm")]
    [InlineData("entity.associate_document")]
    public void LiveOperationString_ResolvesInPolicy(string operation)
    {
        OperationAccessPolicy.IsOperationSupported(operation).Should().BeTrue(
            "'{0}' is passed by a live enforcement site; if it does not resolve, that site denies " +
            "every caller unconditionally (findings A-3/A-20, closed by task 003)", operation);
    }

    /// <summary>
    /// ✅ FLIPPED BY TASK 003 (FR-03) — was the consequence half of A-3/A-20: a caller holding every
    /// right was still denied, because the denial keyed on the operation being unknown rather than on
    /// rights. The decision is now rights-based, so a fully-privileged caller is allowed.
    ///
    /// Note <see cref="AccessRights.AppendTo"/> in the rights set: task 003 assigned
    /// <c>entity.associate_document</c> → <c>AppendTo</c> (attaching a document TO the target entity),
    /// so a set that omits it does NOT satisfy that operation. The original task-001 version of this
    /// test omitted AppendTo and would have mis-reported this as still-denied.
    /// </summary>
    [Theory]
    [InlineData("read")]
    [InlineData("finance.read")]
    [InlineData("finance.confirm")]
    [InlineData("entity.associate_document")]
    public async Task LiveOperationString_WithFullRights_IsAllowed(string operation)
    {
        // Arrange — every right the model can express, AppendTo included.
        var allRights = AccessRights.Read | AccessRights.Write | AccessRights.Delete
                        | AccessRights.Create | AccessRights.Append | AccessRights.AppendTo
                        | AccessRights.Share;

        // Act
        var result = await Rule().EvaluateAsync(Context(operation), Snapshot(allRights));

        // Assert
        result.Decision.Should().Be(AuthorizationDecision.Allow);
        result.ReasonCode.Should().Be($"sdap.access.allow.operation.{operation}");
    }

    /// <summary>
    /// The other side of the flip, and the part that matters for security: now that these operations
    /// resolve, they must be decided on RIGHTS rather than waved through. A Read-only caller must
    /// still be denied the two mutating operations — with <c>insufficient_rights</c>, not
    /// <c>unknown_operation</c>.
    ///
    /// This is what makes task 003 a fix rather than a loosening: registering a key converts an
    /// unconditional deny into a real decision, and this test proves the decision still says no when
    /// it should.
    /// </summary>
    [Theory]
    [InlineData("finance.confirm")]            // requires Write
    [InlineData("entity.associate_document")]  // requires AppendTo
    public async Task MutatingOperation_WithReadOnlyRights_DeniedForInsufficientRights(string operation)
    {
        var result = await Rule().EvaluateAsync(Context(operation), Snapshot(AccessRights.Read));

        result.Decision.Should().Be(AuthorizationDecision.Deny);
        result.ReasonCode.Should().Be("sdap.access.deny.insufficient_rights",
            "the operation is now known, so the denial must be a rights decision — a lingering " +
            "unknown_operation would mean the key never registered");
    }

    /// <summary>
    /// Read-only operations ARE satisfied by a Read-only caller. Pins that task 003 did not
    /// over-restrict the two read operations while registering them.
    /// </summary>
    [Theory]
    [InlineData("read")]
    [InlineData("finance.read")]
    public async Task ReadOperation_WithReadOnlyRights_IsAllowed(string operation)
    {
        var result = await Rule().EvaluateAsync(Context(operation), Snapshot(AccessRights.Read));

        result.Decision.Should().Be(AuthorizationDecision.Allow);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // CHARACTERIZATION — A-20 Read-ceiling: the snapshot can never carry more than Read
    // (DataverseAccessDataSource.QueryUserPermissionsAsync:368-372), so every policy requiring
    // Write or above is unsatisfiable in production.
    // Flipped by: task 005 (FR-04, lift the Read ceiling).
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A-20 — CURRENT (BROKEN) BEHAVIOR. These operations ARE valid policy keys, but each requires a
    /// right above Read. Because DataverseAccessDataSource returns at most AccessRights.Read, a
    /// Read-only snapshot is what production always presents — so all of these deny in practice.
    ///
    /// FLIPPED BY: task 005 (FR-04) — once the data source returns real Write/Create/Delete/Share,
    /// a caller with those rights is allowed and this test asserts Allow for a full-rights snapshot.
    /// </summary>
    [Theory]
    [InlineData("upload_file")]        // Write | Create
    [InlineData("create_container")]   // Create | Write
    [InlineData("download_file")]      // Write  (security policy: download requires Write)
    [InlineData("share_document")]     // Share
    [InlineData("delete_file")]        // Delete
    public async Task Characterization_WritePlusOperation_DeniedUnderReadCeiling(string operation)
    {
        // Arrange — the ceiling production can actually produce today.
        var readOnlyCeiling = Snapshot(AccessRights.Read);

        // Act
        var result = await Rule().EvaluateAsync(Context(operation), readOnlyCeiling);

        // Assert — the operation IS known (so this is not the A-3 unknown-operation path) but the
        // Read ceiling cannot satisfy it.
        OperationAccessPolicy.IsOperationSupported(operation).Should().BeTrue(
            "this test isolates the Read-ceiling effect, not the unknown-operation effect");
        result.Decision.Should().Be(AuthorizationDecision.Deny);
        result.ReasonCode.Should().NotBe("sdap.access.deny.unknown_operation");
    }

    /// <summary>
    /// A-20 — the same operations DO allow once the snapshot carries the rights the policy asks for.
    /// This proves the defect is the data source's Read ceiling, not the policy table, and gives
    /// task 005 an unambiguous target: make production produce a snapshot like this one.
    /// </summary>
    [Theory]
    [InlineData("upload_file")]
    [InlineData("create_container")]
    [InlineData("download_file")]
    [InlineData("share_document")]
    [InlineData("delete_file")]
    public async Task EvaluateAsync_WhenSnapshotCarriesRequiredRights_Allows(string operation)
    {
        var allRights = AccessRights.Read | AccessRights.Write | AccessRights.Delete
                        | AccessRights.Create | AccessRights.Append | AccessRights.Share;

        var result = await Rule().EvaluateAsync(Context(operation), Snapshot(allRights));

        result.Decision.Should().Be(AuthorizationDecision.Allow);
    }
}
