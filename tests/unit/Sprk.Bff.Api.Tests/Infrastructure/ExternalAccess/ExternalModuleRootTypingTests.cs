using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sprk.Bff.Api.Infrastructure.DI;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess;

/// <summary>
/// ebilling-r1 task 130-0 fix round 3: a module with the secure-roots rule cannot be registered when a root lookup it scopes by (or
/// declares as an extra root lookup) is not typed by <see cref="ExternalModuleDescriptor.RootLinks"/>. A comment is not enough: an
/// untyped root dimension drops out of the rule's input, and a row filed to a secure root through it would be listed. The root lookups
/// are those the lineage map files the module's table under a root table, not a second list.
/// </summary>
[Trait("category", "external-access")]
public sealed class ExternalModuleRootTypingTests
{
    // sprk_todo has the three regarding root lookups in the lineage map; RootLinks types only the work assignment one.
    private const string Entity = "sprk_todo";

    private static ExternalModuleDescriptor Module(
        string dimension, IReadOnlyList<RootLookup>? extra = null, bool secureRoots = true) => new()
    {
        Name = "todos-test",
        RecordEntity = Entity,
        RequireReadOnSecureRoots = secureRoots,
        AdditionalRootLookups = extra ?? Array.Empty<RootLookup>(),
        ScopeDimensions = new[] { new ScopeDimension { Attribute = dimension, AccessibleIds = _ => new HashSet<Guid>() } },
        PrimaryNameAttribute = "sprk_name",
        ReadableColumns = new HashSet<string> { "sprk_todoid", "sprk_name", dimension },
    };

    [Theory(DisplayName = "130-0 fix3 root typing (must fire): a secure-roots module scoped by a root lookup RootLinks does not type is refused at registration")]
    [InlineData("sprk_regardingproject")]
    [InlineData("sprk_regardingmatter")]
    public void ARootDimensionRootLinksDoesNotType_IsRefused(string dimension)
    {
        var act = () => new ExternalModuleRegistry().Register(Module(dimension));

        act.Should().Throw<InvalidOperationException>().WithMessage($"*{dimension}*does not type it*");
    }

    [Fact(DisplayName = "130-0 fix3 root typing (must fire): an extra root lookup that names a root the lineage map does not file under that root is still checked by what RootLinks returns")]
    public void AnExtraRootLookupOfTheWrongRoot_IsRefused()
    {
        // sprk_regardingmatter is a matter lookup; declaring it as a project root lookup is not a typing of it.
        var act = () => new ExternalModuleRegistry().Register(
            Module("sprk_regardingworkassignment", new[] { new RootLookup("sprk_regardingmatter", ExternalGrantRootType.Project) }));

        act.Should().Throw<InvalidOperationException>().WithMessage("*sprk_regardingmatter*");
    }

    [Theory(DisplayName = "130-0 fix3 root typing (must not fire): the work assignment dimension is typed; a declared extra root lookup types its root; a module without the rule is not checked")]
    [InlineData("sprk_regardingworkassignment", false, true)]
    [InlineData("sprk_regardingproject", true, true)]
    [InlineData("sprk_regardingproject", false, false)]
    public void ATypedRootDimension_OrOneDeclaredAsAnExtraLookup_OrNoRule_Registers(string dimension, bool declared, bool secureRoots)
    {
        var extra = declared ? new[] { new RootLookup(dimension, ExternalGrantRootType.Project) } : null;
        var registry = new ExternalModuleRegistry();

        var act = () => registry.Register(Module(dimension, extra, secureRoots));

        act.Should().NotThrow();
        registry.FindByEntity(Entity).Should().NotBeNull();
    }

    [Fact(DisplayName = "130-0 fix3 root typing (must not fire): the production modules register, and RootLinks types the invoice's work assignment once")]
    public void TheProductionRegistry_Builds_AndTypesTheInvoicesWorkAssignmentDimension()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddExternalAccess();

        var registry = services.BuildServiceProvider().GetRequiredService<ExternalModuleRegistry>();

        var invoices = registry.FindByEntity("sprk_invoice")!;
        var workAssignment = Guid.NewGuid();
        invoices.RootLinks(new Dictionary<string, object?> { ["sprk_regardingworkassignment"] = workAssignment })
            .Should().ContainSingle(link => link.Root == ExternalGrantRootType.WorkAssignment)
            .Which.LookupValue.Should().Be(workAssignment.ToString());
    }
}
