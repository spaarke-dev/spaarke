using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Infrastructure.DI;
using Sprk.Bff.Api.Services.Todo;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Todo;

/// <summary>
/// Unit tests for <see cref="DeepLinkBuilder"/> (smart-todo-decoupling-r3 task 060, FR-25).
/// Covers:
/// <list type="bullet">
///   <item>Happy path: valid config + valid GUID → correct Modern UCI URL.</item>
///   <item>Missing / blank / malformed OrgUrl → fail-fast in ctor with clear message.</item>
///   <item>Missing / blank / non-GUID AppId → fail-fast in ctor with clear message.</item>
///   <item><see cref="Guid.Empty"/> as todoId → <see cref="ArgumentException"/> at call site.</item>
///   <item>DI integration: <see cref="TodoSyncModule.AddTodoSync"/> resolves the singleton
///   when config is supplied; fails on first resolution when config is missing.</item>
/// </list>
/// </summary>
public sealed class DeepLinkBuilderTests
{
    private const string ValidOrgUrl = "https://spaarkedev1.crm.dynamics.com";
    private const string ValidAppId = "0a1b2c3d-4e5f-6789-abcd-ef0123456789";

    // ───────────────────────────────────────────────────────────────────────────────
    // Happy path
    // ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void BuildTodoUrl_ReturnsModernUciUrl_GivenValidConfigAndGuid()
    {
        var sut = CreateSut(ValidOrgUrl, ValidAppId);
        var todoId = new Guid("11111111-2222-3333-4444-555555555555");

        var uri = sut.BuildTodoUrl(todoId);

        uri.Should().NotBeNull();
        uri.AbsoluteUri.Should().Be(
            "https://spaarkedev1.crm.dynamics.com/apps/0a1b2c3d-4e5f-6789-abcd-ef0123456789/r/sprk_todo/11111111-2222-3333-4444-555555555555");
    }

    [Fact]
    public void BuildTodoUrl_UsesDashedGuidFormat_NotNFormat()
    {
        // The implementation uses .NET "D" specifier (dashed). Verify by ensuring
        // the rendered URL contains the dashes for both the appId and the todoId.
        var sut = CreateSut(ValidOrgUrl, ValidAppId);
        var todoId = new Guid("11111111-2222-3333-4444-555555555555");

        var uri = sut.BuildTodoUrl(todoId);

        uri.AbsoluteUri.Should().Contain("0a1b2c3d-4e5f-6789-abcd-ef0123456789");
        uri.AbsoluteUri.Should().Contain("11111111-2222-3333-4444-555555555555");
        // Negative: ensure no concatenated 32-char form
        uri.AbsoluteUri.Should().NotContain("0a1b2c3d4e5f6789abcdef0123456789");
        uri.AbsoluteUri.Should().NotContain("11111111222233334444555555555555");
    }

    [Fact]
    public void BuildTodoUrl_NormalisesTrailingSlashInOrgUrl()
    {
        // OrgUrl with a trailing slash must NOT produce a double-slash in the segment join.
        var sut = CreateSut(ValidOrgUrl + "/", ValidAppId);
        var todoId = new Guid("11111111-2222-3333-4444-555555555555");

        var uri = sut.BuildTodoUrl(todoId);

        uri.AbsoluteUri.Should().NotContain("//apps");
        uri.AbsoluteUri.Should().Contain("/apps/0a1b2c3d-4e5f-6789-abcd-ef0123456789/r/sprk_todo/");
    }

    // ───────────────────────────────────────────────────────────────────────────────
    // OrgUrl validation
    // ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Ctor_Throws_WhenOrgUrlMissing()
    {
        Action act = () => CreateSut(orgUrl: null, appId: ValidAppId);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Spaarke:Environment:OrgUrl*missing or blank*");
    }

    [Fact]
    public void Ctor_Throws_WhenOrgUrlBlank()
    {
        Action act = () => CreateSut(orgUrl: "   ", appId: ValidAppId);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Spaarke:Environment:OrgUrl*missing or blank*");
    }

    [Theory]
    [InlineData("not-a-url")]
    [InlineData("ftp://spaarkedev1.crm.dynamics.com")] // wrong scheme
    [InlineData("//spaarkedev1.crm.dynamics.com")]     // relative
    [InlineData("crm.dynamics.com/path")]              // missing scheme
    public void Ctor_Throws_WhenOrgUrlMalformed(string malformed)
    {
        Action act = () => CreateSut(orgUrl: malformed, appId: ValidAppId);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Spaarke:Environment:OrgUrl*not a valid absolute http(s) URL*");
    }

    // ───────────────────────────────────────────────────────────────────────────────
    // AppId validation
    // ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Ctor_Throws_WhenAppIdMissing()
    {
        Action act = () => CreateSut(orgUrl: ValidOrgUrl, appId: null);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Spaarke:ModelDrivenApps:DefaultAppId*missing or blank*");
    }

    [Fact]
    public void Ctor_Throws_WhenAppIdBlank()
    {
        Action act = () => CreateSut(orgUrl: ValidOrgUrl, appId: "   ");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Spaarke:ModelDrivenApps:DefaultAppId*missing or blank*");
    }

    [Fact]
    public void Ctor_Throws_WhenAppIdNotAGuid()
    {
        Action act = () => CreateSut(orgUrl: ValidOrgUrl, appId: "not-a-guid");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Spaarke:ModelDrivenApps:DefaultAppId*not a valid (non-empty) GUID*");
    }

    [Fact]
    public void Ctor_Throws_WhenAppIdEmptyGuid()
    {
        // "00000000-0000-0000-0000-000000000000" parses but equals Guid.Empty.
        Action act = () => CreateSut(orgUrl: ValidOrgUrl, appId: Guid.Empty.ToString());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Spaarke:ModelDrivenApps:DefaultAppId*not a valid (non-empty) GUID*");
    }

    // ───────────────────────────────────────────────────────────────────────────────
    // todoId validation (per-call)
    // ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void BuildTodoUrl_Throws_WhenTodoIdIsEmpty()
    {
        var sut = CreateSut(ValidOrgUrl, ValidAppId);

        Action act = () => sut.BuildTodoUrl(Guid.Empty);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("todoId");
    }

    // ───────────────────────────────────────────────────────────────────────────────
    // DI integration via TodoSyncModule
    // ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TodoSyncModule_RegistersIDeepLinkBuilder_AsSingleton()
    {
        var sp = BuildProvider(orgUrl: ValidOrgUrl, appId: ValidAppId);

        var instance1 = sp.GetRequiredService<IDeepLinkBuilder>();
        var instance2 = sp.GetRequiredService<IDeepLinkBuilder>();

        instance1.Should().BeOfType<DeepLinkBuilder>();
        instance2.Should().BeSameAs(instance1, because: "DeepLinkBuilder is registered as a singleton");
    }

    [Fact]
    public void TodoSyncModule_ResolvedDeepLinkBuilder_BuildsCorrectUrl()
    {
        var sp = BuildProvider(orgUrl: ValidOrgUrl, appId: ValidAppId);
        var builder = sp.GetRequiredService<IDeepLinkBuilder>();
        var todoId = new Guid("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

        var uri = builder.BuildTodoUrl(todoId);

        uri.AbsoluteUri.Should().Be(
            $"{ValidOrgUrl}/apps/{ValidAppId}/r/sprk_todo/{todoId:D}");
    }

    [Fact]
    public void TodoSyncModule_ResolvingIDeepLinkBuilder_FailsFast_WhenOrgUrlMissing()
    {
        // Empty config + AddTodoSync → DI registration succeeds; resolution throws because
        // the singleton ctor validates options on first activation. This is the canonical
        // fail-fast shape for product-portability config.
        var sp = BuildProvider(orgUrl: null, appId: ValidAppId);

        Action act = () => sp.GetRequiredService<IDeepLinkBuilder>();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Spaarke:Environment:OrgUrl*");
    }

    [Fact]
    public void TodoSyncModule_ResolvingIDeepLinkBuilder_FailsFast_WhenAppIdMissing()
    {
        var sp = BuildProvider(orgUrl: ValidOrgUrl, appId: null);

        Action act = () => sp.GetRequiredService<IDeepLinkBuilder>();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Spaarke:ModelDrivenApps:DefaultAppId*");
    }

    // ───────────────────────────────────────────────────────────────────────────────
    // Helpers
    // ───────────────────────────────────────────────────────────────────────────────

    private static DeepLinkBuilder CreateSut(string? orgUrl, string? appId)
    {
        var opts = Options.Create(new DeepLinkBuilderOptions
        {
            OrgUrl = orgUrl ?? string.Empty,
            AppId = appId ?? string.Empty,
        });
        return new DeepLinkBuilder(opts);
    }

    private static ServiceProvider BuildProvider(string? orgUrl, string? appId)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        var dict = new Dictionary<string, string?>();
        if (orgUrl is not null) dict[DeepLinkBuilderOptions.OrgUrlConfigKey] = orgUrl;
        if (appId is not null) dict[DeepLinkBuilderOptions.AppIdConfigKey] = appId;

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(dict)
            .Build();

        services.AddSingleton<IConfiguration>(configuration);
        services.AddTodoSync(configuration);
        return services.BuildServiceProvider();
    }
}
