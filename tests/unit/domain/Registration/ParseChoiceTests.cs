using FluentAssertions;
using Sprk.Bff.Api.Api;
using Sprk.Bff.Api.Services.Registration;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Registration;

/// <summary>
/// Pure domain-logic tests (ADR-038 §2 path #6 — no mocks, no DI, no I/O) for
/// <see cref="RegistrationEndpoints.ParseUseCase"/> and
/// <see cref="RegistrationEndpoints.ParseReferralSource"/>, the rules that turn what
/// the website sends into the <c>sprk_usecase</c> and <c>sprk_referralsource</c> choices.
///
/// These exist because the use case was silently discarded on every registration request
/// ever created. The old helper listed separator variants by hand — "documentmanagement",
/// "document-management", "document_management" — and the space was missed. The website
/// sends its picker labels, so "Document Management" matched nothing, fell through to
/// Enum.TryParse, failed there too, and returned null. Callers read null as "not supplied",
/// the write was skipped, and neither side reported a problem.
///
/// Behaviour pinned here:
///  - Separators do not decide whether a value parses. Space, hyphen, underscore and
///    none of the above all reach the same choice.
///  - The labels the website actually shows parse, including "General Evaluation",
///    whose choice is named General and so needs the longer form handled explicitly.
///  - Case is irrelevant.
///  - Genuine nonsense still returns null, because the endpoint depends on that to
///    refuse the request rather than write a record without the field.
/// </summary>
public class ParseChoiceTests
{
    // The exact strings on the website's picker. If these stop parsing, the field is
    // silently empty again, which is the whole failure this guards.
    [Theory]
    [InlineData("Document Management", UseCaseOption.DocumentManagement)]
    [InlineData("AI Analysis", UseCaseOption.AiAnalysis)]
    [InlineData("Financial Intelligence", UseCaseOption.FinancialIntelligence)]
    [InlineData("General Evaluation", UseCaseOption.General)]
    public void ParseUseCase_AcceptsTheLabelsTheWebsiteShows(string value, UseCaseOption expected)
    {
        RegistrationEndpoints.ParseUseCase(value).Should().Be(expected);
    }

    // The wire values the website sends today.
    [Theory]
    [InlineData("DocumentManagement", UseCaseOption.DocumentManagement)]
    [InlineData("AiAnalysis", UseCaseOption.AiAnalysis)]
    [InlineData("FinancialIntelligence", UseCaseOption.FinancialIntelligence)]
    [InlineData("General", UseCaseOption.General)]
    public void ParseUseCase_AcceptsTheEnumNames(string value, UseCaseOption expected)
    {
        RegistrationEndpoints.ParseUseCase(value).Should().Be(expected);
    }

    [Theory]
    [InlineData("document management")]
    [InlineData("document-management")]
    [InlineData("document_management")]
    [InlineData("DOCUMENT MANAGEMENT")]
    [InlineData("  Document   Management  ")]
    [InlineData("Document.Management")]
    public void ParseUseCase_IgnoresSeparatorsAndCase(string value)
    {
        RegistrationEndpoints.ParseUseCase(value).Should().Be(UseCaseOption.DocumentManagement);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Litigation Support")]
    [InlineData("something else entirely")]
    public void ParseUseCase_ReturnsNullForAbsentOrUnknown(string? value)
    {
        // The endpoint turns this null into a 400. It must stay null rather than
        // guessing a choice, or an unreadable value becomes a wrong value.
        RegistrationEndpoints.ParseUseCase(value).Should().BeNull();
    }

    [Theory]
    [InlineData("Website", ReferralSourceOption.Website)]
    [InlineData("website", ReferralSourceOption.Website)]
    [InlineData("Conference", ReferralSourceOption.Conference)]
    [InlineData("Referral", ReferralSourceOption.Referral)]
    [InlineData("Search", ReferralSourceOption.Search)]
    [InlineData("Other", ReferralSourceOption.Other)]
    public void ParseReferralSource_AcceptsTheLabelsTheWebsiteShows(string value, ReferralSourceOption expected)
    {
        RegistrationEndpoints.ParseReferralSource(value).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("word of mouth")]
    public void ParseReferralSource_ReturnsNullForAbsentOrUnknown(string? value)
    {
        RegistrationEndpoints.ParseReferralSource(value).Should().BeNull();
    }

    [Theory]
    [InlineData("Document Management", "documentmanagement")]
    [InlineData("AI-Analysis", "aianalysis")]
    [InlineData("  Financial_Intelligence  ", "financialintelligence")]
    [InlineData("General", "general")]
    [InlineData("!!!", "")]
    public void NormalizeChoice_StripsEverythingThatIsNotALetterOrDigit(string value, string expected)
    {
        RegistrationEndpoints.NormalizeChoice(value).Should().Be(expected);
    }
}
