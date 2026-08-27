// -----------------------------------------------------------------------------
// H13BuildPromotedColumnsTests.cs
//
// Wave 2 pre-dispatch remediation punchlist REG-01 (2026-08-27).
//
// Pure-function tests for
// H13E2EAcceptanceGateHandler.BuildPromotedColumnsForReady — the helper that
// assembles the sprk_dataverseenvironment column set PATCHed BEFORE H13's
// Ready transition. Covers the "always sprk_provisionedon" invariant + the
// omit-when-absent contract for optional columns + InterStepState-vs-parameter
// preference for containerTypeId.
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using FluentAssertions;
using Sprk.Provisioning.ControlPlane.Handlers.E2EAcceptance;
using Sprk.Provisioning.ControlPlane.Models;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public class H13BuildPromotedColumnsTests
{
    private static readonly DateTimeOffset TestStamp =
        new(2026, 8, 27, 15, 30, 45, TimeSpan.Zero);

    [Fact]
    public void BuildPromotedColumnsForReady_Always_Sets_ProvisionedOn()
    {
        // The load-bearing REG-01 invariant — sprk_provisionedon is ALWAYS
        // written, even when the run carries NO other parameters, because
        // H0 upgrade-mode detection reads this column on the next run.
        var run = new ProvisioningRun
        {
            RunId = "run-1", CustomerId = "cust-1", EnvironmentId = Guid.NewGuid().ToString("D"),
            Parameters = new RunParameters(),
            InterStepState = new InterStepState(),
        };

        var columns = H13E2EAcceptanceGateHandler.BuildPromotedColumnsForReady(run, TestStamp);

        columns.Should().ContainKey("sprk_provisionedon");
        columns["sprk_provisionedon"].Should().Be(TestStamp,
            because: "REG-01 — sprk_provisionedon is the load-bearing column for §14A upgrade mode.");
    }

    [Fact]
    public void BuildPromotedColumnsForReady_Omits_Absent_Optional_Columns()
    {
        // Omit-when-absent rule: never overwrite an existing value with null.
        // With no NonSecret parameters and no InterStepState, only sprk_provisionedon
        // is present in the dictionary.
        var run = new ProvisioningRun
        {
            RunId = "run-1", CustomerId = "cust-1", EnvironmentId = Guid.NewGuid().ToString("D"),
            Parameters = new RunParameters(),
            InterStepState = new InterStepState(),
        };

        var columns = H13E2EAcceptanceGateHandler.BuildPromotedColumnsForReady(run, TestStamp);

        columns.Should().HaveCount(1);
        columns.Should().NotContainKey("sprk_bffversion");
        columns.Should().NotContainKey("sprk_solutionversion");
        columns.Should().NotContainKey("sprk_containertypeid");
        columns.Should().NotContainKey("sprk_azuresubscriptionid");
    }

    [Fact]
    public void BuildPromotedColumnsForReady_Populates_All_Columns_When_Available()
    {
        var run = new ProvisioningRun
        {
            RunId = "run-1", CustomerId = "cust-1", EnvironmentId = Guid.NewGuid().ToString("D"),
            Parameters = new RunParameters
            {
                NonSecret =
                {
                    ["bffVersion"] = "1.4.2",
                    ["solutionVersion"] = "2.1.0",
                    ["azureSubscriptionId"] = "11111111-1111-1111-1111-111111111111",
                    ["resourceGroupName"] = "rg-spaarke-cust1-prod",
                    ["appServiceName"] = "sprk-cust1-prod-api",
                    ["keyVaultName"] = "kv-sprk-cust1",
                    ["clientCacheBustToken"] = "abc123",
                },
            },
            InterStepState = new InterStepState
            {
                ContainerTypeId = "e2e-container-type-guid",
            },
        };

        var columns = H13E2EAcceptanceGateHandler.BuildPromotedColumnsForReady(run, TestStamp);

        columns.Should().ContainKey("sprk_provisionedon").WhoseValue.Should().Be(TestStamp);
        columns.Should().ContainKey("sprk_bffversion").WhoseValue.Should().Be("1.4.2");
        columns.Should().ContainKey("sprk_solutionversion").WhoseValue.Should().Be("2.1.0");
        columns.Should().ContainKey("sprk_azuresubscriptionid")
            .WhoseValue.Should().Be("11111111-1111-1111-1111-111111111111");
        columns.Should().ContainKey("sprk_resourcegroupname").WhoseValue.Should().Be("rg-spaarke-cust1-prod");
        columns.Should().ContainKey("sprk_appservicename").WhoseValue.Should().Be("sprk-cust1-prod-api");
        columns.Should().ContainKey("sprk_keyvaultname").WhoseValue.Should().Be("kv-sprk-cust1");
        columns.Should().ContainKey("sprk_containertypeid")
            .WhoseValue.Should().Be("e2e-container-type-guid");
        columns.Should().ContainKey("sprk_clientcachebusttoken").WhoseValue.Should().Be("abc123");
    }

    [Fact]
    public void BuildPromotedColumnsForReady_ContainerTypeId_Prefers_InterStepState_Over_NonSecret()
    {
        // InterStepState is the authoritative source per design.md §6.2 (H10 output).
        var run = new ProvisioningRun
        {
            RunId = "run-1", CustomerId = "cust-1", EnvironmentId = Guid.NewGuid().ToString("D"),
            Parameters = new RunParameters
            {
                NonSecret = { ["containerTypeId"] = "fallback-from-params" },
            },
            InterStepState = new InterStepState
            {
                ContainerTypeId = "authoritative-from-h10",
            },
        };

        var columns = H13E2EAcceptanceGateHandler.BuildPromotedColumnsForReady(run, TestStamp);

        columns["sprk_containertypeid"].Should().Be("authoritative-from-h10",
            because: "InterStepState.ContainerTypeId is H10's authoritative output (design.md §6.2).");
    }

    [Fact]
    public void BuildPromotedColumnsForReady_ContainerTypeId_FallsBack_To_NonSecret_When_InterStepState_Empty()
    {
        // Fallback for test hosts / upgrade-only runs that don't re-run H10.
        var run = new ProvisioningRun
        {
            RunId = "run-1", CustomerId = "cust-1", EnvironmentId = Guid.NewGuid().ToString("D"),
            Parameters = new RunParameters
            {
                NonSecret = { ["containerTypeId"] = "fallback-value" },
            },
            InterStepState = new InterStepState(),
        };

        var columns = H13E2EAcceptanceGateHandler.BuildPromotedColumnsForReady(run, TestStamp);

        columns["sprk_containertypeid"].Should().Be("fallback-value");
    }

    [Fact]
    public void BuildPromotedColumnsForReady_Column_Names_Are_All_Lowercase()
    {
        // REG-06 alignment — every emitted column name is a lowercase Dataverse
        // logical name. Prevents a paste-slip PascalCase name from silently
        // 400ing the PATCH.
        var run = new ProvisioningRun
        {
            RunId = "run-1", CustomerId = "cust-1", EnvironmentId = Guid.NewGuid().ToString("D"),
            Parameters = new RunParameters
            {
                NonSecret =
                {
                    ["bffVersion"] = "1.0",
                    ["solutionVersion"] = "1.0",
                    ["azureSubscriptionId"] = "sub",
                    ["resourceGroupName"] = "rg",
                    ["appServiceName"] = "app",
                    ["keyVaultName"] = "kv",
                    ["clientCacheBustToken"] = "cbt",
                },
            },
            InterStepState = new InterStepState { ContainerTypeId = "ctype" },
        };

        var columns = H13E2EAcceptanceGateHandler.BuildPromotedColumnsForReady(run, TestStamp);

        foreach (var key in columns.Keys)
        {
            key.Should().Be(key.ToLowerInvariant(),
                because: $"REG-06 — Dataverse logical names are lowercase, but '{key}' is not.");
        }
    }
}
