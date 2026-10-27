using FluentAssertions;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Access;

/// <summary>
/// unified-access-control-r2 task 149 — the two tables the secure-child share synchronizer decides from: which tables hold
/// a secure record's children (and how they are filed), and what a root's share carries onto a child.
/// </summary>
public class SecureChildLineageTests
{
    /// <summary>
    /// Every child table the Secure Record Owner role covers (config/secure-record-owner-role.json — the ONE codified set,
    /// task 145/146) is a table the synchronizer mirrors, and nothing else is. A child table added to the role set without
    /// a lineage entry would be owned by the Secure team and shared with nobody.
    /// </summary>
    [Fact]
    public void TheLineageMapCoversExactlyTheCodifiedChildTables()
    {
        var codified = SecureRecordOwnerRoleSet.Embedded.Tables
            .Where(t => t.Kind == "child")
            .Select(t => t.LogicalName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        SecureChildLineage.Children.Keys.Should().BeEquivalentTo(codified);
        SecureRecordOwnerRoleSet.Embedded.Tables.Where(t => t.Kind == "root").Select(t => t.LogicalName)
            .Should().BeEquivalentTo(SecureChildLineage.Roots);
    }

    /// <summary>Every lookup files a row under a root or another mirrored child — never an identity or a service request.</summary>
    [Fact]
    public void EveryLookupTargetsARootOrAMirroredChild()
    {
        foreach (var table in SecureChildLineage.Children.Values)
        {
            table.Lookups.Should().NotBeEmpty($"{table.LogicalName} must be fileable under something");
            foreach (var (column, target) in table.Lookups)
            {
                (SecureChildLineage.IsRoot(target) || SecureChildLineage.IsChild(target))
                    .Should().BeTrue($"{table.LogicalName}.{column} targets {target}");
                target.Should().NotBe("sprk_servicerequest",
                    "the ownership resolver never looks through a service request, so nothing under one is secure");
            }
        }
    }

    /// <summary>
    /// Task 182 (contract C4): the Decision Record is filed under its subject like a Signal. Its row carries the same nine (eight before ebilling-r1 task 026)
    /// subject lookups as the Signal row (never the service request's) and keeps the three typed core lookups: they are
    /// the decision context and a lineage parent, kept for good (ontology task 144 K1 / 145), not retired after a backfill.
    /// </summary>
    [Fact]
    public void TheDecisionRecordRow_HoldsTheSubjectLookups_AndTheTypedCores_ButNoServiceRequest()
    {
        var decisionRecord = SecureChildLineage.Children["sprk_decisionrecord"].Lookups;
        var signalSubjects = SecureChildLineage.Children["sprk_signal"].Lookups
            .Where(l => l.Key.StartsWith("sprk_regarding", StringComparison.Ordinal))
            .ToDictionary(l => l.Key, l => l.Value);

        // Nine since ebilling-r1 task 026 (FR-15, D-94): + sprk_regardinginvoiceline -> sprk_billingevent.
        signalSubjects.Should().HaveCount(9);
        signalSubjects.Should().Contain("sprk_regardinginvoiceline", "sprk_billingevent");
        decisionRecord.Where(l => l.Key.StartsWith("sprk_regarding", StringComparison.Ordinal))
            .ToDictionary(l => l.Key, l => l.Value)
            .Should().BeEquivalentTo(signalSubjects);
        decisionRecord.Should().Contain(new Dictionary<string, string>
        {
            ["sprk_matter"] = "sprk_matter",
            ["sprk_project"] = "sprk_project",
            ["sprk_workassignment"] = "sprk_workassignment",
        });
        decisionRecord.Should().HaveCount(12);
        decisionRecord.Keys.Should().NotContain("sprk_regardingservicerequest");
    }

    /// <summary>
    /// ebilling-r1 task 013 (FR-05): every core-record lookup of the invoice family is a lineage parent — a dropped one
    /// could leave out the root with the fewest sharees and widen the child's share intersection. The line's invoice stays
    /// a parent too (an extra lookup only narrows), but the line's CORE record is its own lookup (D-75).
    /// </summary>
    [Theory]
    [InlineData("sprk_invoice", "sprk_matter", "sprk_project", "sprk_regardingworkassignment")]
    [InlineData("sprk_billingevent", "sprk_matter", "sprk_project", "sprk_regardingworkassignment")]
    [InlineData("sprk_invoicesubmission", "sprk_regardingmatter", "sprk_regardingproject", "sprk_regardingworkassignment")]
    [InlineData("sprk_invoiceapproval", "sprk_regardingmatter", "sprk_regardingproject", "sprk_regardingworkassignment")]
    [InlineData("sprk_externalidentifier", "sprk_regardingmatter", "sprk_regardingproject", "sprk_regardingworkassignment")]
    [InlineData("sprk_invoicecorrection", "sprk_regardingmatter", "sprk_regardingproject", "sprk_regardingworkassignment")]
    public void TheInvoiceFamily_FilesUnderEveryCoreRecordLookup(string table, string matter, string project, string workAssignment)
    {
        var lookups = SecureChildLineage.Children[table].Lookups;

        lookups.Should().Contain(new Dictionary<string, string>
        {
            [matter] = "sprk_matter",
            [project] = "sprk_project",
            [workAssignment] = "sprk_workassignment",
        });
        InvoiceFamilyCoreRecordGuard.CoreRecordLookups[table].Select(l => l.Column)
            .Should().BeSubsetOf(lookups.Keys, "the guard's core lookups and the lineage's parents are the same columns");
    }

    /// <summary>ebilling-r1 task 013: the family rows that name an invoice, a line or a document keep that parent too.</summary>
    [Fact]
    public void TheInvoiceFamily_KeepsItsInvoiceLineAndDocumentParents()
    {
        SecureChildLineage.Children["sprk_invoicesubmission"].Lookups.Should().Contain(new Dictionary<string, string>
        {
            ["sprk_invoice"] = "sprk_invoice",
            ["sprk_document"] = "sprk_document",
            ["sprk_reportdocument"] = "sprk_document",
            ["sprk_resubmissionof"] = "sprk_invoicesubmission",
        });
        SecureChildLineage.Children["sprk_invoiceapproval"].Lookups.Should().Contain("sprk_invoice", "sprk_invoice");
        SecureChildLineage.Children["sprk_invoiceapproval"].Lookups.Keys.Should().NotContain("sprk_decisionrecord",
            "a Decision Record is stamped outside the access taxonomy; nothing but a Signal is filed under one");
        SecureChildLineage.Children["sprk_externalidentifier"].Lookups.Should().Contain("sprk_regardinginvoice", "sprk_invoice");
        SecureChildLineage.Children["sprk_invoicecorrection"].Lookups.Should().Contain(new Dictionary<string, string>
        {
            ["sprk_invoice"] = "sprk_invoice",
            ["sprk_invoiceline"] = "sprk_billingevent",
        });
    }

    /// <summary>
    /// The mirror follows the ownership resolver: every table it treats as a reparentable child is mirrored, except
    /// <c>sprk_servicerequest</c>, which the resolver treats as a root of its own.
    /// </summary>
    [Fact]
    public void EveryReparentableChildOfTheResolverIsMirrored()
    {
        foreach (var table in RecordOwnershipResolver.OwnershipParentEntities.Where(RecordOwnershipResolver.IsReparentableChild))
            SecureChildLineage.IsChild(table).Should().BeTrue($"{table} is owned by the Secure team under a secure parent");
    }

    /// <summary>
    /// #2084 (owner round 102 item 4): a rule is filed under its matter, a version under its rule (solution XML:
    /// sprk_policy_matter, sprk_policyversion_policy). The rule is a reparentable child of the resolver; the version is a lineage
    /// child only, because Signals and Decision Records name the version they cite.
    /// </summary>
    [Fact]
    public void ARuleIsFiledUnderItsMatter_AndAVersionUnderItsRule()
    {
        var rule = SecureChildLineage.Children["sprk_policy"];
        rule.EntitySet.Should().Be("sprk_policies");
        rule.Lookups.Should().BeEquivalentTo(
            new Dictionary<string, string> { ["sprk_matter"] = "sprk_matter", ["sprk_ruleset"] = "sprk_ruleset" },
            "sprk_currentversion names the rule's own version and can add no root; a record rule is filed under its set (task 089)");

        var version = SecureChildLineage.Children["sprk_policyversion"];
        version.EntitySet.Should().Be("sprk_policyversions");
        version.Lookups.Should().BeEquivalentTo(new Dictionary<string, string> { ["sprk_policy"] = "sprk_policy" });

        RecordOwnershipResolver.IsReparentableChild("sprk_policy").Should().BeTrue();
        RecordOwnershipResolver.IsOwnershipParent("sprk_policyversion").Should().BeFalse(
            "a Signal or Decision Record citing a version is not filed under it");
    }

    /// <summary>
    /// ebilling-r1 task 089 (EB-D-148, uac-r2 F1-a): an assignment / override row and a record-scoped rule set are filed under their core
    /// record; a record rule under its set. An assignment's Procedure, pinned version and excluded rule are citations (must fire: dropped
    /// from its parents); its core lookup is its parent (must not fire).
    /// </summary>
    [Fact]
    public void AnAssignmentAndARecordRuleSetAreFiledUnderTheirCoreRecord_AndTheAssignmentsSubjectsAreCitations()
    {
        var core = new Dictionary<string, string>
        {
            ["sprk_matter"] = "sprk_matter", ["sprk_project"] = "sprk_project", ["sprk_workassignment"] = "sprk_workassignment",
        };
        var assignment = SecureChildLineage.Children["sprk_procedureassignment"];
        assignment.EntitySet.Should().Be("sprk_procedureassignments");
        assignment.Lookups.Should().BeEquivalentTo(core, "the Procedure, pinned version, excluded rule and firm are not filing parents");

        var set = SecureChildLineage.Children["sprk_ruleset"];
        set.EntitySet.Should().Be("sprk_rulesets");
        set.Lookups.Should().BeEquivalentTo(core, "a client, firm-wide or Procedure set has no core lookup value and is never reached");

        RecordOwnershipResolver.IsReparentableChild("sprk_procedureassignment").Should().BeTrue();
        RecordOwnershipResolver.IsReparentableChild("sprk_ruleset").Should().BeTrue();

        var (matter, procedure, version, policy, firm) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var row = new Microsoft.Xrm.Sdk.Entity("sprk_procedureassignment")
        {
            ["sprk_matter"] = new Microsoft.Xrm.Sdk.EntityReference("sprk_matter", matter),
            ["sprk_procedure"] = new Microsoft.Xrm.Sdk.EntityReference("sprk_procedure", procedure),
            ["sprk_pinnedversion"] = new Microsoft.Xrm.Sdk.EntityReference("sprk_procedureversion", version),
            ["sprk_excludedpolicy"] = new Microsoft.Xrm.Sdk.EntityReference("sprk_policy", policy),
            ["sprk_firm"] = new Microsoft.Xrm.Sdk.EntityReference("sprk_organization", firm),
        };
        RecordOwnershipContext.ForChild(row).Parents.Should().Equal(new RecordOwnershipParent("sprk_matter", matter));
        RecordOwnershipResolver.IsCitation("sprk_procedureassignment", "sprk_excludedpolicy").Should().BeTrue();
        RecordOwnershipResolver.IsCitation("sprk_procedureassignment", "sprk_matter").Should().BeFalse();

        var setId = Guid.NewGuid();
        var recordRule = new Microsoft.Xrm.Sdk.Entity("sprk_policy")
        {
            ["sprk_ruleset"] = new Microsoft.Xrm.Sdk.EntityReference("sprk_ruleset", setId),
        };
        RecordOwnershipContext.ForChild(recordRule).Parents.Should().Equal(new RecordOwnershipParent("sprk_ruleset", setId));
    }

    /// <summary>
    /// #2084: a Signal CITES the rule that raised it; it is not filed under it. Reading an existing row's parents drops the
    /// citation (must fire), and the same lookup on a version still files the version under its rule (must not fire).
    /// </summary>
    [Fact]
    public void ASignalsRuleIsACitation_NotAParent_WhileAVersionsRuleIsItsParent()
    {
        var (rule, matter) = (Guid.NewGuid(), Guid.NewGuid());
        var signal = new Microsoft.Xrm.Sdk.Entity("sprk_signal", Guid.NewGuid())
        {
            ["sprk_policy"] = new Microsoft.Xrm.Sdk.EntityReference("sprk_policy", rule),
            ["sprk_matter"] = new Microsoft.Xrm.Sdk.EntityReference("sprk_matter", matter),
        };
        var version = new Microsoft.Xrm.Sdk.Entity("sprk_policyversion", Guid.NewGuid())
        {
            ["sprk_policy"] = new Microsoft.Xrm.Sdk.EntityReference("sprk_policy", rule),
        };

        RecordOwnershipContext.ParentsOf(signal).Should().Equal(new RecordOwnershipParent("sprk_matter", matter));
        RecordOwnershipContext.ParentsOf(version).Should().Equal(new RecordOwnershipParent("sprk_policy", rule));
        RecordOwnershipResolver.IsCitation("SPRK_SIGNAL", "Sprk_Policy").Should().BeTrue("logical names compare case-insensitively");
        RecordOwnershipResolver.IsCitation("sprk_policyversion", "sprk_policy").Should().BeFalse();
    }

    /// <summary>
    /// #2084 round 2: ONE predicate for every parent derivation of a row being created — the attribute form keyed by its table,
    /// <see cref="RecordOwnershipContext.ForChild"/>, and the create tool's <see cref="OwnedChildWrite.ParentsOf"/>. A Signal created
    /// naming its rule is filed under its matter only (must fire); a version naming its rule is filed under it (must not fire).
    /// </summary>
    [Fact]
    public void ACreatedSignalsRule_IsACitationOnEveryCreatePath_WhileAVersionsRuleIsItsParent()
    {
        var (rule, matter) = (Guid.NewGuid(), Guid.NewGuid());
        var signal = new Microsoft.Xrm.Sdk.Entity("sprk_signal")
        {
            ["sprk_policy"] = new Microsoft.Xrm.Sdk.EntityReference("sprk_policy", rule),
            ["sprk_matter"] = new Microsoft.Xrm.Sdk.EntityReference("sprk_matter", matter),
        };

        RecordOwnershipContext.ParentsOf(signal.Attributes, "sprk_signal").Should().Equal(new RecordOwnershipParent("sprk_matter", matter));
        var context = RecordOwnershipContext.ForChild(signal);
        context.Parents.Should().Equal(new RecordOwnershipParent("sprk_matter", matter));
        context.TargetEntityLogicalName.Should().Be("sprk_matter");

        var item = new DataverseWriteItemMapper.MappedItem("{}", [])
        {
            Lookups =
            [
                new DataverseWriteItemMapper.MappedLookup("sprk_policy", "sprk_policy", "sprk_policies", rule),
                new DataverseWriteItemMapper.MappedLookup("sprk_matter", "sprk_matter", "sprk_matters", matter),
            ],
        };
        OwnedChildWrite.ParentsOf("sprk_signal", item).Should().Equal(new RecordOwnershipParent("sprk_matter", matter));
        OwnedChildWrite.ParentsOf("sprk_policyversion", item).Should().Equal(
            new RecordOwnershipParent("sprk_policy", rule), new RecordOwnershipParent("sprk_matter", matter));
        RecordOwnershipContext.ForChild(new Microsoft.Xrm.Sdk.Entity("sprk_policyversion")
        {
            ["sprk_policy"] = new Microsoft.Xrm.Sdk.EntityReference("sprk_policy", rule),
        }).Parents.Should().Equal(new RecordOwnershipParent("sprk_policy", rule));
    }

    // ── What a child receives (Dataverse AccessRights values as literals) ────────────────────────────────────────────

    // Owner round 91 (2026-10-10, task 179): Share is carried where the root share holds it; never Assign or Create.
    [Theory]
    [InlineData(1, 1)]                    // View Only
    [InlineData(262167, 262167)]          // Collaborate → Share kept (round 91)
    [InlineData(327703, 327703)]          // Full Access → Delete and Share kept
    [InlineData(23, 23)]                  // legacy Collaborate: no Share on the root, none on the child
    [InlineData(1 | 524288, 1)]           // a UI share carrying Assign → no Assign
    [InlineData(1 | 32, 1)]               // Create means nothing on a share
    [InlineData(262167 | 524288, 262167)] // Collaborate + Assign → Assign dropped, Share kept
    public void ChildMirrorMask_KeepsTheRootsRights_ExceptAssignAndCreate(int rootMask, int childMask)
        => RecordShareLevels.ChildMirrorMask(rootMask).Should().Be(childMask);

    [Fact]
    public void ChildMirrorRights_WritesTheCanonicalLiteral_AndRefusesAssign()
    {
        RecordShareLevels.ChildMirrorRights(65559).AccessRightsCsv
            .Should().Be("ReadAccess,WriteAccess,AppendAccess,AppendToAccess,DeleteAccess");
        RecordShareLevels.ChildMirrorRights(262167).AccessRightsCsv
            .Should().Be("ReadAccess,WriteAccess,AppendAccess,AppendToAccess,ShareAccess");
        RecordShareLevels.ChildMirrorRights(1).AccessRightsCsv.Should().Be("ReadAccess");

        var withAssign = () => RecordShareLevels.ChildMirrorRights(1 | 524288);
        withAssign.Should().Throw<ArgumentOutOfRangeException>("a child mirror never carries Assign");
    }
}
