using FluentAssertions;
using Sprk.Bff.Api.Configuration;
using Xunit;

namespace Sprk.Bff.Api.Tests.OptionsValidation;

/// <summary>
/// auth-v4 task 054 (FR-E5) — the validator must not make an API key mandatory for a path that can
/// authenticate with managed identity.
/// </summary>
/// <remarks>
/// <para>These are the forcing function for the specific way this migration could have been defeated.
/// Task 053 fixed the DI block in <c>AnalysisServicesModule.AddRagServices</c> that silently
/// un-registered six services when <c>AiSearchKey</c> went missing — but clearing the key would
/// STILL have failed startup here, at options validation, with an error naming the key rather than
/// the migration. A key-presence check in a validator is the same defect as a key-presence check in
/// a DI gate, one layer up and easier to miss.</para>
/// <para>Namespace is deliberately <c>OptionsValidation</c>, not <c>Configuration</c>: a
/// <c>Sprk.Bff.Api.Tests.Configuration</c> namespace shadows <c>Sprk.Bff.Api.Configuration</c> for
/// every test that refers to production options by the short name <c>Configuration.X</c>, which
/// broke <c>AssociationStatusMapperTests</c> when this file was first placed there.</para>
/// </remarks>
[Trait("Category", "Configuration")]
public class DocumentIntelligenceOptionsValidatorTests
{
    private static DocumentIntelligenceOptions Valid() => new()
    {
        Enabled = true,
        OpenAiEndpoint = "https://test-openai.openai.azure.com/",
        OpenAiKey = "a-key",
        MaxOutputTokens = 1000,
        Temperature = 0.2f,
        MaxConcurrentStreams = 3,
    };

    [Fact]
    public void Succeeds_WhenOpenAiKeyIsAbsent_BecauseManagedIdentityIsTheAlternative()
    {
        var options = Valid();
        options.OpenAiKey = string.Empty;

        var result = new DocumentIntelligenceOptionsValidator().Validate(null, options);

        result.Failed.Should().BeFalse(
            "OpenAiClient falls forward to the DI TokenCredential when no key is configured — " +
            "requiring the key here would fail startup before the Entra path could ever run");
    }

    [Fact]
    public void StillFails_WhenOpenAiEndpointIsAbsent()
    {
        // The endpoint stays required: unlike the credential, there is nothing to infer it from.
        // This is what keeps the relaxation above from becoming "validate nothing".
        var options = Valid();
        options.OpenAiEndpoint = string.Empty;

        var result = new DocumentIntelligenceOptionsValidator().Validate(null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("OpenAiEndpoint");
    }

    [Fact]
    public void Succeeds_WhenRecordMatchingEnabledAndAiSearchKeyIsAbsent()
    {
        // The task-053 cutover path. Every AI Search consumer now routes through
        // SearchClientFactory, where an absent key selects Entra.
        var options = Valid();
        options.RecordMatchingEnabled = true;
        options.AiSearchEndpoint = "https://test-search.search.windows.net";
        options.AiSearchIndexName = "spaarke-records-index";
        options.AiSearchKey = null;

        var result = new DocumentIntelligenceOptionsValidator().Validate(null, options);

        result.Failed.Should().BeFalse(
            "clearing AiSearch--AdminKey must not fail startup — that would re-create task 053's " +
            "defect one layer up, at options validation");
    }

    [Fact]
    public void StillFails_WhenRecordMatchingEnabledAndEndpointOrIndexIsAbsent()
    {
        var options = Valid();
        options.RecordMatchingEnabled = true;
        options.AiSearchEndpoint = null;
        options.AiSearchIndexName = string.Empty;

        var result = new DocumentIntelligenceOptionsValidator().Validate(null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("AiSearchEndpoint");
        result.FailureMessage.Should().Contain("AiSearchIndexName");
    }

    [Fact]
    public void SkipsEverything_WhenFeatureIsDisabled()
    {
        var result = new DocumentIntelligenceOptionsValidator()
            .Validate(null, new DocumentIntelligenceOptions { Enabled = false });

        result.Failed.Should().BeFalse();
    }
}
