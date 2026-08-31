using System;
using System.Collections.Generic;
using FluentAssertions;
using Sprk.Bff.Api.Services.Registration;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Registration;

/// <summary>
/// Pure domain-logic tests (ADR-038 §2 path #6 — no mocks, no DI, no I/O) for
/// <see cref="DataverseEnvironmentRecord.SelectDefault"/>. This is the selection rule that
/// <see cref="DemoExpirationService"/> (customer-provisioning-orchestration-r1 task 080) and
/// <c>RegistrationDataverseService</c> (task 081) depend on after migrating off the
/// <c>[Obsolete]</c> <c>DemoProvisioningOptions.Environments</c> + <c>DefaultEnvironment</c> pair.
///
/// Behavior protected here (would silently regress the migrated services if changed):
///  - Empty list throws instead of returning null.
///  - The single environment where <c>IsDefault == true</c> is chosen when exactly one exists.
///  - When no environment is flagged default, the first is returned (matches the original
///    <c>_options.Environments.First()</c> fallback that this migration preserves).
///  - When multiple are flagged default (data anomaly), the first flagged wins — deterministic.
/// </summary>
public class DataverseEnvironmentRecordSelectDefaultTests
{
    [Fact]
    public void SelectDefault_WhenListIsEmpty_ThrowsInvalidOperationException()
    {
        var envs = new List<DataverseEnvironmentRecord>();

        var act = () => DataverseEnvironmentRecord.SelectDefault(envs);

        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*No active Dataverse environments*");
    }

    [Fact]
    public void SelectDefault_WhenExactlyOneIsDefault_ReturnsThatOne()
    {
        var primaryId = Guid.NewGuid();
        var envs = new List<DataverseEnvironmentRecord>
        {
            new() { Id = Guid.NewGuid(), Name = "Alt-1", IsDefault = false, IsActive = true },
            new() { Id = primaryId,      Name = "Primary", IsDefault = true,  IsActive = true },
            new() { Id = Guid.NewGuid(), Name = "Alt-2", IsDefault = false, IsActive = true },
        };

        var result = DataverseEnvironmentRecord.SelectDefault(envs);

        result.Id.Should().Be(primaryId);
        result.Name.Should().Be("Primary");
        result.IsDefault.Should().BeTrue();
    }

    [Fact]
    public void SelectDefault_WhenNoneIsDefault_ReturnsFirstEntry()
    {
        var firstId = Guid.NewGuid();
        var envs = new List<DataverseEnvironmentRecord>
        {
            new() { Id = firstId,        Name = "First",  IsDefault = false, IsActive = true },
            new() { Id = Guid.NewGuid(), Name = "Second", IsDefault = false, IsActive = true },
        };

        var result = DataverseEnvironmentRecord.SelectDefault(envs);

        result.Id.Should().Be(firstId);
        result.Name.Should().Be("First");
    }

    [Fact]
    public void SelectDefault_WhenMultipleAreDefault_ReturnsFirstFlaggedDefault()
    {
        var firstDefaultId = Guid.NewGuid();
        var envs = new List<DataverseEnvironmentRecord>
        {
            new() { Id = Guid.NewGuid(),    Name = "NotDefault", IsDefault = false, IsActive = true },
            new() { Id = firstDefaultId,    Name = "DefaultA",   IsDefault = true,  IsActive = true },
            new() { Id = Guid.NewGuid(),    Name = "DefaultB",   IsDefault = true,  IsActive = true },
        };

        var result = DataverseEnvironmentRecord.SelectDefault(envs);

        result.Id.Should().Be(firstDefaultId);
        result.Name.Should().Be("DefaultA");
    }

    [Fact]
    public void SelectDefault_WhenSingleEntryNotDefault_StillReturnsIt()
    {
        var soloId = Guid.NewGuid();
        var envs = new List<DataverseEnvironmentRecord>
        {
            new() { Id = soloId, Name = "OnlyEnv", IsDefault = false, IsActive = true },
        };

        var result = DataverseEnvironmentRecord.SelectDefault(envs);

        result.Id.Should().Be(soloId);
        result.Name.Should().Be("OnlyEnv");
    }
}
