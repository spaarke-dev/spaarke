using System.Reflection;
using FluentAssertions;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.RecordCreation;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Dataverse.RecordCreation;

/// <summary>
/// field-mapping-server-write-path-r1 task 017 (ebilling-r1 reconcile, F2/F4): how the invoice-family check's answer
/// (<see cref="OwnedChildWrite.InvoiceFamilyStep"/>) becomes a <see cref="PreWriteRefusal"/>
/// (<see cref="RecordTableRules.InvoiceFamilyPreWrite"/>), in the child-record route's contract
/// (<c>ChildRecordEndpoints.InvoiceFamilyRefused</c>). Pure — <c>tests/unit/domain/**</c> (ADR-038 §2 path 6). The rendered
/// status of each kind is pinned end to end by <c>RecordCreationServiceInvoiceFamilyRuleSeamTests</c>.
/// </summary>
public class RecordTableRulesInvoiceFamilyRuleTests
{
    private static OwnedChildWrite.InvoiceFamilyStep Refused(string code, string? reason = "The guard's reason.") =>
        new(InvoiceFamilyCoreRecord.Refused("sprk_invoice", code, reason!), null, false);

    // ── F2: read_failed and unknown_entity are undecided (500), never 400 and never the 404 ───────────────────────────

    [Theory]
    [InlineData(InvoiceFamilyCoreRecordGuard.ReadFailedCode)]
    [InlineData(InvoiceFamilyCoreRecordGuard.UnknownEntityCode)]
    public void ACheckThatCouldNotDecide_IsUndecided_WithTheGuardsCodeAndReason(string code)
    {
        var refusal = RecordTableRules.InvoiceFamilyPreWrite(Refused(code, "What this invoice is filed under could not be read."))!;

        refusal.IsUndecided.Should().BeTrue();
        refusal.Code.Should().Be(code);
        refusal.Detail.Should().Be("What this invoice is filed under could not be read.");
        refusal.ParentUnavailable.Should().BeFalse("only a record the caller cannot see is the uniform not-found");
        refusal.ClientFailure.Should().BeNull();
    }

    // ── The uniform not-found: NotFound() is the only way, and record_not_found maps to it ───────────────────────────

    [Fact]
    public void TheGuardsRecordNotFound_IsNotFound()
    {
        var refusal = RecordTableRules.InvoiceFamilyPreWrite(Refused(InvoiceFamilyCoreRecordGuard.RecordNotFoundCode))!;

        refusal.ParentUnavailable.Should().BeTrue();
        refusal.Code.Should().BeNull("the uniform not-found names nothing");
        refusal.IsUndecided.Should().BeFalse();
    }

    [Fact]
    public void ARecordTheCallerCannotSee_IsNotFound()
    {
        var refusal = RecordTableRules.InvoiceFamilyPreWrite(new OwnedChildWrite.InvoiceFamilyStep(null, null, true))!;

        refusal.ParentUnavailable.Should().BeTrue();
        refusal.Code.Should().BeNull();
    }

    [Fact]
    public void EveryOtherGuardCode_IsNeverTheNotFound()
    {
        var codes = typeof(InvoiceFamilyCoreRecordGuard)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.Name.EndsWith("Code", StringComparison.Ordinal))
            .Select(f => (string)f.GetRawConstantValue()!)
            .Where(c => c != InvoiceFamilyCoreRecordGuard.RecordNotFoundCode)
            .ToList();
        codes.Should().Contain([InvoiceFamilyCoreRecordGuard.ReadFailedCode, InvoiceFamilyCoreRecordGuard.SupersedesCoreRecordMismatchCode]);

        foreach (var code in codes)
        {
            var refusal = RecordTableRules.InvoiceFamilyPreWrite(Refused(code))!;
            refusal.ParentUnavailable.Should().BeFalse(code);
            refusal.Code.Should().Be(code, "the guard's own stable code is kept");
        }
    }

    // ── F4: a supersedes refusal keeps its Code and Reason ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData(InvoiceFamilyCoreRecordGuard.SupersedesCoreRecordMismatchCode)]
    [InlineData(InvoiceFamilyCoreRecordGuard.MultipleCoreRecordsCode)]
    [InlineData(InvoiceFamilyCoreRecordGuard.NoCoreRecordCode)]
    public void WhatTheRowNamesWrong_IsRefused_KeepingTheGuardsCodeAndReason(string code)
    {
        const string Reason = "sprk_invoice regards sprk_matter(1) but the invoice it supersedes regards sprk_matter(2).";

        var refusal = RecordTableRules.InvoiceFamilyPreWrite(Refused(code, Reason))!;

        refusal.Code.Should().Be(code);
        refusal.Detail.Should().Be(Reason);
        refusal.IsUndecided.Should().BeFalse();
        refusal.ParentUnavailable.Should().BeFalse();
    }

    [Fact]
    public void ARefusalWithNoReason_TakesTheGuardsCodeOnlyMessage_NeverThrows()
    {
        var refusal = RecordTableRules.InvoiceFamilyPreWrite(Refused(InvoiceFamilyCoreRecordGuard.MultipleCoreRecordsCode, reason: null))!;

        refusal.Detail.Should().Be(InvoiceFamilyCoreRecordGuard.CodeOnlyRefusalMessage(InvoiceFamilyCoreRecordGuard.MultipleCoreRecordsCode));
    }

    // ── The caller's own failure is carried ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheCallersOwnFailure_IsCarried()
    {
        var failure = DataverseUserResponse.Fail(429, DataverseUserClientErrorCodes.ServiceError, "Too many requests.");

        var refusal = RecordTableRules.InvoiceFamilyPreWrite(new OwnedChildWrite.InvoiceFamilyStep(null, failure, false))!;

        refusal.ClientFailure.Should().BeSameAs(failure);
        refusal.ParentUnavailable.Should().BeFalse();
    }

    // ── Must-not-fire, and fail closed ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AnAdmittedRow_IsNoRefusal()
    {
        RecordTableRules.InvoiceFamilyPreWrite(null).Should().BeNull("the guard admitted the row: the create proceeds");
    }

    [Fact]
    public void AStepThatNamesNoRefusal_IsUndecided_NeverProceed()
    {
        var decidedNotRefused = new OwnedChildWrite.InvoiceFamilyStep(
            new InvoiceFamilyCoreRecord { EntityLogicalName = "sprk_invoice" }, null, false);

        foreach (var step in new[] { new OwnedChildWrite.InvoiceFamilyStep(null, null, false), decidedNotRefused })
        {
            var refusal = RecordTableRules.InvoiceFamilyPreWrite(step);
            refusal.Should().NotBeNull("a step is returned only to refuse; one that names nothing fails closed");
            refusal!.IsUndecided.Should().BeTrue();
            refusal.Code.Should().Be(InvoiceFamilyCoreRecordGuard.ReadFailedCode);
        }
    }

    // ── The factory ────────────────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("", "detail")]
    [InlineData("invoice_family_read_failed", " ")]
    public void PreWriteRefusal_UndecidedWithoutACodeOrADetail_IsRefused(string code, string detail)
    {
        var act = () => PreWriteRefusal.Undecided(code, detail);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TheRule_IsNamedWithARationale()
    {
        RecordTableRules.InvoiceFamilyRule.Rule.Name.Should().Be("InvoiceFamilyCoreRecord");
        RecordTableRules.InvoiceFamilyRule.Rule.Rationale.Should().Contain("FR-05");
    }
}
