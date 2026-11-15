using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Startup;
using Sprk.Bff.Api.Services.Access;
using Xunit;

namespace Sprk.Bff.Api.Tests.Infrastructure.Startup;

/// <summary>
/// uac-r2 task 197b: the host's startup validation reports a retired setting that still carries a value (one Warning, the KEY
/// only, never the value), so stale configuration is not silently ignored. The rule itself is pinned in
/// <c>ExternalTeamShareTests</c> (<see cref="ExternalTeamShareSynchronizer.ReportRetiredSettings"/>); this pins the wiring.
/// </summary>
public class StartupValidationRetiredSettingTests
{
    [Theory(DisplayName = "197b must-fire / must-not-fire: startup reports the retired unit-name setting by key only, and only when it has a value")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Startup_ReportsTheRetiredSetting_OnlyWhenSet(bool set)
    {
        const string leftover = "a-leftover-unit-name-value";
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [ExternalTeamShareSynchronizer.RetiredBusinessUnitNameConfigKey] = set ? leftover : null,
        }).Build();
        var logger = new MessageLogger();
        var sut = new StartupValidationService(
            logger, Options.Create(new GraphOptions()), Options.Create(new DataverseOptions()), Options.Create(new ServiceBusOptions()),
            Options.Create(new RedisOptions()), Options.Create(new CommunicationOptions()), configuration);

        await sut.StartAsync(CancellationToken.None);

        var warnings = logger.Messages.Where(m => m.Level == LogLevel.Warning).Select(m => m.Text).ToList();
        if (set)
        {
            warnings.Should().ContainSingle().Which.Should().Contain(ExternalTeamShareSynchronizer.RetiredBusinessUnitNameConfigKey);
            logger.Messages.Should().NotContain(m => m.Text.Contains(leftover), "the value is never logged");
        }
        else
        {
            warnings.Should().BeEmpty();
        }
    }

    private sealed class MessageLogger : ILogger<StartupValidationService>
    {
        public List<(LogLevel Level, string Text)> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Add((logLevel, formatter(state, exception)));
    }
}
