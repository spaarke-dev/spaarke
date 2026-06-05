using FluentAssertions;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Services.Insights.LiveFacts;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Insights.LiveFacts;

/// <summary>
/// Unit tests for <see cref="SubjectParser"/> (r2 Wave D5 task 034). Covers the §2.4 error
/// matrix from design-a6-multi-entity.md + the default catalog fallback.
/// </summary>
public class SubjectParserTests
{
    private static readonly Guid SampleId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    /// <summary>Create a parser using the built-in default catalog (matter + project + invoice).</summary>
    private static SubjectParser CreateDefaultParser()
        => new(Options.Create(new SubjectSchemeCatalogOptions()));

    /// <summary>Create a parser with a custom catalog (used to test the config-driven path).</summary>
    private static SubjectParser CreateParser(params (string name, string entity, string key)[] schemes)
    {
        var options = new SubjectSchemeCatalogOptions
        {
            Schemes = schemes.Select(s => new SubjectSchemeOptions
            {
                Name = s.name,
                DataverseEntity = s.entity,
                ResolverKey = s.key
            }).ToList()
        };
        return new SubjectParser(Options.Create(options));
    }

    [Theory]
    [InlineData("matter")]
    [InlineData("project")]
    [InlineData("invoice")]
    public void DefaultCatalog_RecognizesPhase15Schemes(string scheme)
    {
        var parser = CreateDefaultParser();
        var subject = $"{scheme}:{SampleId}";

        var parsed = parser.Parse(subject);

        parsed.EntityType.Should().Be(scheme);
        parsed.EntityId.Should().Be(SampleId);
    }

    [Fact]
    public void TryParse_ValidMatterSubject_Succeeds()
    {
        var parser = CreateDefaultParser();
        var subject = $"matter:{SampleId}";

        var success = parser.TryParse(subject, out var parsed, out var error);

        success.Should().BeTrue();
        error.Should().BeEmpty();
        parsed.EntityType.Should().Be("matter");
        parsed.EntityId.Should().Be(SampleId);
    }

    [Fact]
    public void TryParse_CaseInsensitiveScheme()
    {
        var parser = CreateDefaultParser();
        var subject = $"MATTER:{SampleId}";

        var success = parser.TryParse(subject, out var parsed, out _);

        success.Should().BeTrue();
        parsed.EntityType.Should().Be("matter", "schemes normalize to lower-case");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void TryParse_EmptyOrWhitespace_Fails(string subject)
    {
        var parser = CreateDefaultParser();
        var success = parser.TryParse(subject, out _, out var error);

        success.Should().BeFalse();
        error.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("matterM-1234")]                                        // no colon
    [InlineData("11111111-1111-1111-1111-111111111111")]                // missing scheme
    [InlineData(":")]                                                   // both empty
    [InlineData(":11111111-1111-1111-1111-111111111111")]               // empty scheme
    [InlineData("matter:")]                                             // empty id
    public void TryParse_StructurallyInvalid_Fails(string subject)
    {
        var parser = CreateDefaultParser();
        var success = parser.TryParse(subject, out _, out var error);

        success.Should().BeFalse();
        error.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("matter:not-a-guid")]
    [InlineData("matter:11111111-1111-1111-1111")]                      // truncated guid
    [InlineData("matter:00000000-0000-0000-0000-000000000000")]         // empty guid sentinel
    public void TryParse_InvalidGuid_Fails(string subject)
    {
        var parser = CreateDefaultParser();
        var success = parser.TryParse(subject, out _, out var error);

        success.Should().BeFalse();
        error.Should().Contain("GUID");
    }

    [Theory]
    [InlineData("document")]
    [InlineData("client")]
    [InlineData("contract")]
    public void TryParse_UnregisteredScheme_Fails(string scheme)
    {
        // Schemes reserved for Phase 2+ are not in the default catalog; the parser MUST
        // reject them with a clear error so playbook authoring mistakes surface immediately.
        var parser = CreateDefaultParser();
        var subject = $"{scheme}:{SampleId}";

        var success = parser.TryParse(subject, out _, out var error);

        success.Should().BeFalse();
        error.Should().Contain(scheme);
        error.Should().Contain("not registered");
    }

    [Fact]
    public void Parse_UnregisteredScheme_ThrowsUnknownSubjectSchemeException()
    {
        var parser = CreateDefaultParser();
        var subject = $"client:{SampleId}";

        Action act = () => parser.Parse(subject);

        act.Should().Throw<UnknownSubjectSchemeException>()
            .Where(ex => ex.Scheme == "client");
    }

    [Fact]
    public void Parse_InvalidFormat_ThrowsInvalidSubjectFormatException()
    {
        var parser = CreateDefaultParser();

        Action act = () => parser.Parse("matter:not-a-guid");

        act.Should().Throw<InvalidSubjectFormatException>();
    }

    [Fact]
    public void Parse_EmptySubject_ThrowsInvalidSubjectFormatException()
    {
        var parser = CreateDefaultParser();

        Action act = () => parser.Parse("");

        act.Should().Throw<InvalidSubjectFormatException>();
    }

    [Fact]
    public void ConfigDrivenCatalog_OverridesDefaults()
    {
        // Custom catalog: register only 'matter' + a new 'client' scheme. The 'project'
        // and 'invoice' schemes should NOT be recognized when a custom catalog is bound.
        var parser = CreateParser(
            ("matter", "sprk_matter", "matter"),
            ("client", "account", "client"));

        parser.TryParse($"matter:{SampleId}", out _, out _).Should().BeTrue();
        parser.TryParse($"client:{SampleId}", out _, out _).Should().BeTrue();
        parser.TryParse($"project:{SampleId}", out _, out var projectError).Should().BeFalse();
        projectError.Should().Contain("project");
    }

    [Fact]
    public void ParsedSubject_ToSubjectString_RoundTrips()
    {
        var parsed = new ParsedSubject("matter", SampleId);
        parsed.ToSubjectString().Should().Be($"matter:{SampleId}");
    }

    [Fact]
    public void Constructor_NullOptions_Throws()
    {
        Action act = () => new SubjectParser(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void DefaultSchemes_ListsMatterProjectInvoice()
    {
        var defaults = SubjectSchemeCatalogOptions.DefaultSchemes;
        defaults.Should().HaveCount(3);
        defaults.Select(s => s.Name).Should().BeEquivalentTo(new[] { "matter", "project", "invoice" });
    }
}
