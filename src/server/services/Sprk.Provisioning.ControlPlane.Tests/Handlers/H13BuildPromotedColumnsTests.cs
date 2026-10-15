// -----------------------------------------------------------------------------
// H13BuildPromotedColumnsTests.cs
//
// Wave 2 pre-dispatch remediation punchlist REG-01 (2026-08-27).
//
// Pure-function tests for
// H13E2EAcceptanceGateHandler.BuildPromotedColumnsForReady — the helper that
// assembles the sprk_dataverseenvironment column set PATCHed BEFORE H13's
// Ready transition. Covers the "always sprk_provisionedon" invariant + the
// omit-when-absent contract for optional columns + the source of each column
// (task 245a, G25): resource group / App Service / customer Key Vault from
// InterStepState (H2a outputs); containerTypeId from the intake parameter only.
// Task 245b: sprk_bffversion ← H9's BffBuildId, sprk_solutionversion ← H6's
// SpaarkeMaster record as "SpaarkeMaster {version} ({type})" (T218b),
// sprk_clientcachebusttoken ← the run id. T257: sprk_bffappid ← H3's BffAppRegId (the Copilot agent render reads it).
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using FluentAssertions;
using Sprk.Provisioning.ControlPlane.Handlers.E2EAcceptance;
using Sprk.Provisioning.ControlPlane.Handlers.SolutionImport;
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
            RunId = "run-1", CustomerId = "cust1", EnvironmentId = Guid.NewGuid().ToString("D"),
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
        // With no NonSecret parameters and no InterStepState, only sprk_provisionedon and the
        // run-id cache-bust token (task 245b — every run has an id) are present.
        var run = new ProvisioningRun
        {
            RunId = "run-1", CustomerId = "cust1", EnvironmentId = Guid.NewGuid().ToString("D"),
            Parameters = new RunParameters(),
            InterStepState = new InterStepState(),
        };

        var columns = H13E2EAcceptanceGateHandler.BuildPromotedColumnsForReady(run, TestStamp);

        columns.Should().HaveCount(2);
        columns["sprk_clientcachebusttoken"].Should().Be("run-1");
        columns.Should().NotContainKey("sprk_bffversion");
        columns.Should().NotContainKey("sprk_solutionversion");
        columns.Should().NotContainKey("sprk_containertypeid");
        columns.Should().NotContainKey("sprk_azuresubscriptionid");
        columns.Should().NotContainKey("sprk_bffappid");
    }

    [Fact]
    public void BuildPromotedColumnsForReady_Populates_All_Columns_When_Available()
    {
        var run = new ProvisioningRun
        {
            RunId = "run-1", CustomerId = "cust1", EnvironmentId = Guid.NewGuid().ToString("D"),
            Parameters = new RunParameters
            {
                NonSecret =
                {
                    ["subscriptionId"] = "11111111-1111-1111-1111-111111111111",
                    ["containerTypeId"] = "e2e-container-type-guid",
                },
            },
            // H2a outputs (task 245a, G25); H9 + H6 outputs (task 245b).
            InterStepState = new InterStepState
            {
                ResourceGroupName = "rg-spaarke-cust1-prod",
                AppServiceName = "sprk-cust1-prod-api",
                KeyVaultName = "kv-sprk-cust1",
                BffAppRegId = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
                BffBuildId = "2026.09.30-123",
                ImportedSolutions = Solutions(("SpaarkeMaster", "1.4.2.0", true)),
            },
        };

        var columns = H13E2EAcceptanceGateHandler.BuildPromotedColumnsForReady(run, TestStamp);

        columns.Should().ContainKey("sprk_provisionedon").WhoseValue.Should().Be(TestStamp);
        columns.Should().ContainKey("sprk_bffversion").WhoseValue.Should().Be("2026.09.30-123");
        columns.Should().ContainKey("sprk_solutionversion").WhoseValue.Should()
            .Be(ImportedSolutionSet.ComputeVersion(run.InterStepState.ImportedSolutions));
        columns.Should().ContainKey("sprk_azuresubscriptionid")
            .WhoseValue.Should().Be("11111111-1111-1111-1111-111111111111");
        columns.Should().ContainKey("sprk_resourcegroupname").WhoseValue.Should().Be("rg-spaarke-cust1-prod");
        columns.Should().ContainKey("sprk_appservicename").WhoseValue.Should().Be("sprk-cust1-prod-api");
        columns.Should().ContainKey("sprk_keyvaultname").WhoseValue.Should().Be("kv-sprk-cust1");
        columns.Should().ContainKey("sprk_containertypeid")
            .WhoseValue.Should().Be("e2e-container-type-guid");
        columns.Should().ContainKey("sprk_clientcachebusttoken").WhoseValue.Should().Be("run-1");
        columns.Should().ContainKey("sprk_bffappid").WhoseValue.Should().Be("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
            "T257: the Copilot agent package is rendered from the registry row, and the BFF app id lives only in the run");
        columns.Should().HaveCount(10, "every promoted column is asserted above — a new one needs its registry column too");
    }

    [Fact]
    public void SolutionVersion_IsThePackageVersionAndType_FitsTheColumn()
    {
        // Pinned vectors — the /provision-environment Step 6a registry fallback builds the same string in PowerShell.
        ImportedSolutionSet.ComputeVersion(Solutions(("SpaarkeMaster", "1.4.2.0", true)))
            .Should().Be("SpaarkeMaster 1.4.2.0 (managed)");
        ImportedSolutionSet.ComputeVersion(Solutions(("SpaarkeMaster", " 1.4.2.0 ", false)))
            .Should().Be("SpaarkeMaster 1.4.2.0 (unmanaged)", "the package type is recorded on the registry row (owner D8)");
        ImportedSolutionSet.ComputeVersion(Solutions(("SpaarkeMaster", "65535.65535.65535.65535", false)))!
            .Length.Should().BeLessThanOrEqualTo(50, "sprk_solutionversion is String(50)");

        ImportedSolutionSet.ComputeVersion([]).Should().BeNull();
        ImportedSolutionSet.ComputeVersion(Solutions(("SpaarkeCore", "1.1.0.0", false)))
            .Should().BeNull("a record from before T218b (no SpaarkeMaster) writes nothing rather than a wrong value");
        ImportedSolutionSet.ComputeVersion(Solutions(("SpaarkeMaster", "", true))).Should().BeNull();
    }

    private static List<ImportedSolutionRecord> Solutions(params (string Name, string Version, bool IsManaged)[] solutions)
        => solutions.Select(s => new ImportedSolutionRecord(s.Name, s.Version, Guid.NewGuid().ToString("D"), s.IsManaged)).ToList();

    [Fact]
    public void BuildPromotedColumnsForReady_ContainerTypeId_Reads_Intake_Parameter_Not_InterStepState()
    {
        // Task 245a (G25): the container type pre-exists per environment and is an
        // intake value; InterStepState.ContainerTypeId has no producer, so a value
        // there (stale/hand-patched) must NOT win over the intake parameter.
        var run = new ProvisioningRun
        {
            RunId = "run-1", CustomerId = "cust1", EnvironmentId = Guid.NewGuid().ToString("D"),
            Parameters = new RunParameters
            {
                NonSecret = { ["containerTypeId"] = "ct-from-intake" },
            },
            InterStepState = new InterStepState
            {
                ContainerTypeId = "unproduced-interstep-value",
            },
        };

        var columns = H13E2EAcceptanceGateHandler.BuildPromotedColumnsForReady(run, TestStamp);

        columns["sprk_containertypeid"].Should().Be("ct-from-intake",
            because: "the intake parameter containerTypeId is the only source (InterStepState.ContainerTypeId is [NoProducer]).");
    }

    [Fact]
    public void BuildPromotedColumnsForReady_ContainerTypeId_From_Intake_Parameter_When_InterStepState_Empty()
    {
        var run = new ProvisioningRun
        {
            RunId = "run-1", CustomerId = "cust1", EnvironmentId = Guid.NewGuid().ToString("D"),
            Parameters = new RunParameters
            {
                NonSecret = { ["containerTypeId"] = "intake-value" },
            },
            InterStepState = new InterStepState(),
        };

        var columns = H13E2EAcceptanceGateHandler.BuildPromotedColumnsForReady(run, TestStamp);

        columns["sprk_containertypeid"].Should().Be("intake-value");
    }

    [Fact]
    public void BuildPromotedColumnsForReady_ContainerTypeId_Omitted_When_Only_InterStepState_Carries_It()
    {
        var run = new ProvisioningRun
        {
            RunId = "run-1", CustomerId = "cust1", EnvironmentId = Guid.NewGuid().ToString("D"),
            Parameters = new RunParameters(),
            InterStepState = new InterStepState { ContainerTypeId = "unproduced-interstep-value" },
        };

        var columns = H13E2EAcceptanceGateHandler.BuildPromotedColumnsForReady(run, TestStamp);

        columns.Should().NotContainKey("sprk_containertypeid",
            because: "InterStepState.ContainerTypeId is not a source; with no intake value the column is omitted, never guessed.");
    }

    [Fact]
    public void BuildPromotedColumnsForReady_StampNames_Read_From_InterStepState_Not_Run_Parameters()
    {
        // Task 245a (G25): resource group / App Service / customer Key Vault are H2a
        // outputs. The intake key "keyVaultName" names the PLATFORM vault and must
        // not reach sprk_keyvaultname.
        var run = new ProvisioningRun
        {
            RunId = "run-1", CustomerId = "cust1", EnvironmentId = Guid.NewGuid().ToString("D"),
            Parameters = new RunParameters
            {
                NonSecret = { ["keyVaultName"] = "platform-vault-from-intake" },
            },
            InterStepState = new InterStepState(),
        };

        var withoutInterStep = H13E2EAcceptanceGateHandler.BuildPromotedColumnsForReady(run, TestStamp);

        withoutInterStep.Should().NotContainKey("sprk_keyvaultname");

        run.InterStepState.ResourceGroupName = "rg-from-h2a";
        run.InterStepState.AppServiceName = "app-from-h2a";
        run.InterStepState.KeyVaultName = "customer-vault-from-h2a";

        var withInterStep = H13E2EAcceptanceGateHandler.BuildPromotedColumnsForReady(run, TestStamp);

        withInterStep["sprk_resourcegroupname"].Should().Be("rg-from-h2a");
        withInterStep["sprk_appservicename"].Should().Be("app-from-h2a");
        withInterStep["sprk_keyvaultname"].Should().Be("customer-vault-from-h2a");
    }

    [Fact]
    public void BuildPromotedColumnsForReady_Column_Names_Are_All_Lowercase()
    {
        // REG-06 alignment — every emitted column name is a lowercase Dataverse
        // logical name. Prevents a paste-slip PascalCase name from silently
        // 400ing the PATCH.
        var run = new ProvisioningRun
        {
            RunId = "run-1", CustomerId = "cust1", EnvironmentId = Guid.NewGuid().ToString("D"),
            Parameters = new RunParameters
            {
                NonSecret =
                {
                    ["subscriptionId"] = "sub",
                    ["containerTypeId"] = "ctype",
                },
            },
            InterStepState = new InterStepState
            {
                ResourceGroupName = "rg",
                AppServiceName = "app",
                KeyVaultName = "kv",
                BffBuildId = "2026.09.30-1",
                ImportedSolutions = Solutions(("SpaarkeMaster", "1.0.0.0", true)),
            },
        };

        // provisionedon + 2 intake + 3 H2a outputs + bffversion (H9) + solutionversion (H6) + cache-bust (run id)
        var expectedColumnCount = 9;

        var columns = H13E2EAcceptanceGateHandler.BuildPromotedColumnsForReady(run, TestStamp);

        columns.Should().HaveCount(expectedColumnCount, because: "every column must be emitted for the lowercase check to cover it");
        foreach (var key in columns.Keys)
        {
            key.Should().Be(key.ToLowerInvariant(),
                because: $"REG-06 — Dataverse logical names are lowercase, but '{key}' is not.");
        }
    }
}
