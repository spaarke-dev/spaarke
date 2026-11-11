using FluentAssertions;
using Spaarke.Dataverse;
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
    /// Task 194 F1-b: a budget's buckets are its children, through the one lookup the solution XML declares
    /// (<c>sprk_BudgetBucket/Entity.xml</c>: <c>sprk_budget</c>; entity set <c>sprk_budgetbuckets</c>).
    /// </summary>
    [Fact]
    public void TheBudgetBucketRow_IsFiledUnderItsBudget()
    {
        var bucket = SecureChildLineage.Children["sprk_budgetbucket"];

        bucket.EntitySet.Should().Be("sprk_budgetbuckets");
        bucket.Lookups.Should().BeEquivalentTo(new Dictionary<string, string> { ["sprk_budget"] = "sprk_budget" });
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
    /// UAC-r2 ruling on RecordFamilyEdges (2026-10-28): a submission's core record is its ownership primary, even when the
    /// invoice it produced (and its submitted PDF) is set. A row read with the lineage's columns, in their order, names the
    /// core record first; the invoice is an output, not the submission's filing.
    /// </summary>
    [Fact]
    public void TheInvoiceSubmission_OwnershipPrimaryIsItsCoreRecord_EvenWithItsInvoiceSet()
    {
        var lineage = SecureChildLineage.Children["sprk_invoicesubmission"].Lookups;
        lineage.Keys.Take(4).Should().Equal(
            "sprk_regardingmatter", "sprk_regardingproject", "sprk_regardingworkassignment", "sprk_document");

        var matter = Guid.NewGuid();
        var values = new Dictionary<string, Microsoft.Xrm.Sdk.EntityReference>
        {
            ["sprk_regardingmatter"] = new("sprk_matter", matter),
            ["sprk_document"] = new("sprk_document", Guid.NewGuid()),
            ["sprk_invoice"] = new("sprk_invoice", Guid.NewGuid()),
            ["sprk_reportdocument"] = new("sprk_document", Guid.NewGuid()),
        };
        var row = new Microsoft.Xrm.Sdk.Entity("sprk_invoicesubmission", Guid.NewGuid());
        foreach (var column in lineage.Keys.Where(values.ContainsKey))
            row[column] = values[column]; // as a read projected by the lineage's columns returns them

        var context = RecordOwnershipContext.ForParents(RecordOwnershipContext.ParentsOf(row));

        context.TargetEntityLogicalName.Should().Be("sprk_matter");
        context.TargetRecordId.Should().Be(matter);
        context.Parents.Select(p => p.EntityLogicalName).Should().Contain("sprk_invoice", "the invoice stays a lineage parent");
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
    /// child only, because Signals and Decision Records name the version they cite. ebilling-r1 task 089a (model 5.2): a record rule
    /// is also filed under its record-scoped rule set.
    /// </summary>
    [Fact]
    public void ARuleIsFiledUnderItsMatter_AndAVersionUnderItsRule()
    {
        var rule = SecureChildLineage.Children["sprk_policy"];
        rule.EntitySet.Should().Be("sprk_policies");
        rule.Lookups.Should().BeEquivalentTo(
            new Dictionary<string, string> { ["sprk_matter"] = "sprk_matter", ["sprk_ruleset"] = "sprk_ruleset" },
            "sprk_currentversion names the rule's own version and can add no root");

        var version = SecureChildLineage.Children["sprk_policyversion"];
        version.EntitySet.Should().Be("sprk_policyversions");
        version.Lookups.Should().BeEquivalentTo(new Dictionary<string, string> { ["sprk_policy"] = "sprk_policy" });

        RecordOwnershipResolver.IsReparentableChild("sprk_policy").Should().BeTrue();
        RecordOwnershipResolver.IsOwnershipParent("sprk_policyversion").Should().BeFalse(
            "a Signal or Decision Record citing a version is not filed under it");
    }

    /// <summary>
    /// ebilling-r1 task 086 (procedure model 5.2/9.1): an invoice Stored File is a leaf child filed under its invoice ONLY. The
    /// Procedure-version, export and source lookups are not lineage lookups (a Procedure document or an export file is never reached from
    /// a secure root, and no secure root's walk reaches a Stored File through its source). The invoice is the row's PRIMARY (must fire:
    /// first in the order the writer and the reconciler ask in); the source document and communication stay in the row's question as
    /// secure-if-any parents, not citations (uac-r2 194b re-check K: a Secure source still gives the Secure team; pinned by
    /// StoredFileServiceOwnershipTests). sprk_storedfile is not an ownership parent: nothing is filed under a Stored File.
    /// </summary>
    [Fact]
    public void AnInvoiceStoredFileIsFiledUnderItsInvoiceOnly_TheInvoiceIsItsPrimary()
    {
        var file = SecureChildLineage.Children["sprk_storedfile"];
        file.EntitySet.Should().Be("sprk_storedfiles");
        file.Lookups.Should().BeEquivalentTo(
            new Dictionary<string, string> { ["sprk_invoice"] = "sprk_invoice" },
            "the Procedure-version, export and source lookups do not file a Stored File under a secure root");
        file.Lookups.Keys.First().Should().Be("sprk_invoice", "uac-r2 194b: declaration order is the primary-parent order");

        RecordOwnershipResolver.OwnershipParentEntities.Should().NotContain("sprk_storedfile");
        RecordOwnershipResolver.IsReparentableChild("sprk_storedfile").Should().BeFalse();

        var (invoice, document, communication, version, billingExport) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        // The source lookups come first in the row, so the order below is the lineage's, not the attributes'.
        var row = new Microsoft.Xrm.Sdk.Entity("sprk_storedfile", Guid.NewGuid())
        {
            ["sprk_sourcedocument"] = new Microsoft.Xrm.Sdk.EntityReference("sprk_document", document),
            ["sprk_sourcecommunication"] = new Microsoft.Xrm.Sdk.EntityReference("sprk_communication", communication),
            ["sprk_invoice"] = new Microsoft.Xrm.Sdk.EntityReference("sprk_invoice", invoice),
        };
        var ordered = RecordOwnershipContext.OrderedParentsOf(row);
        ordered.First().Should().Be(new RecordOwnershipParent("sprk_invoice", invoice), "the invoice decides an invoice file's business unit");
        ordered.Should().BeEquivalentTo(new[]
        {
            new RecordOwnershipParent("sprk_invoice", invoice),
            new RecordOwnershipParent("sprk_document", document),
            new RecordOwnershipParent("sprk_communication", communication),
        });
        RecordOwnershipContext.ForChild(row).Parents.First().Should().Be(new RecordOwnershipParent("sprk_invoice", invoice),
            "the writer's question has the reconciler's primary");
        RecordOwnershipResolver.IsCitation("sprk_storedfile", "sprk_invoice").Should().BeFalse();
        RecordOwnershipResolver.IsCitation("sprk_storedfile", "sprk_sourcedocument").Should().BeFalse("uac-r2 194b re-check K");

        // A Procedure-source or export file names no ownership parent: nothing re-owns it.
        RecordOwnershipContext.ParentsOf(new Microsoft.Xrm.Sdk.Entity("sprk_storedfile", Guid.NewGuid())
        {
            ["sprk_procedureversion"] = new Microsoft.Xrm.Sdk.EntityReference("sprk_procedureversion", version),
            ["sprk_billingexport"] = new Microsoft.Xrm.Sdk.EntityReference("sprk_billingexport", billingExport),
        }).Should().BeEmpty();
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

    /// <summary>
    /// ebilling-r1 task 089a (procedure model 5.2): a Procedure assignment and a record-scoped rule set are filed under the core
    /// record they name (live metadata 2026-11-03: sprk_matter, sprk_project, sprk_workassignment on both tables). Both are
    /// reparentable children of the resolver; a Procedure rule set (sprk_procedure only) has no lineage parent and stays outside.
    /// </summary>
    [Theory]
    [InlineData("sprk_procedureassignment", "sprk_procedureassignments")]
    [InlineData("sprk_ruleset", "sprk_rulesets")]
    public void AnAssignmentAndARecordScopedRuleSet_AreFiledUnderTheirCoreRecord(string table, string entitySet)
    {
        var row = SecureChildLineage.Children[table];

        row.EntitySet.Should().Be(entitySet);
        row.Lookups.Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["sprk_matter"] = "sprk_matter",
            ["sprk_project"] = "sprk_project",
            ["sprk_workassignment"] = "sprk_workassignment",
        });
        RecordOwnershipResolver.IsOwnershipParent(table).Should().BeTrue();
        RecordOwnershipResolver.IsReparentableChild(table).Should().BeTrue();
    }

    /// <summary>
    /// ebilling-r1 task 089a: an assignment CITES the Procedure, the pinned version and the excluded rule it names; it is filed
    /// only under its core record (must fire: the cited lookups are dropped). A Procedure rule set names no core record, so its
    /// row has no ownership parent (must not fire: it stays outside the secure family).
    /// </summary>
    [Fact]
    public void AnAssignmentsProcedureVersionAndRule_AreCitations_AndAProcedureRuleSetHasNoParent()
    {
        var (matter, procedure, version, policy) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var assignment = new Microsoft.Xrm.Sdk.Entity("sprk_procedureassignment", Guid.NewGuid())
        {
            ["sprk_matter"] = new Microsoft.Xrm.Sdk.EntityReference("sprk_matter", matter),
            ["sprk_procedure"] = new Microsoft.Xrm.Sdk.EntityReference("sprk_procedure", procedure),
            ["sprk_pinnedversion"] = new Microsoft.Xrm.Sdk.EntityReference("sprk_procedureversion", version),
            ["sprk_excludedpolicy"] = new Microsoft.Xrm.Sdk.EntityReference("sprk_policy", policy),
        };

        RecordOwnershipContext.ParentsOf(assignment).Should().Equal(new RecordOwnershipParent("sprk_matter", matter));
        RecordOwnershipContext.ForChild(assignment).TargetEntityLogicalName.Should().Be("sprk_matter");
        RecordOwnershipResolver.IsCitation("sprk_procedureassignment", "sprk_excludedpolicy").Should().BeTrue(
            "sprk_policy is an ownership parent, so without the citation an assignment would be filed under the rule it excludes");
        RecordOwnershipResolver.IsCitation("sprk_procedureassignment", "sprk_procedure").Should().BeTrue();
        RecordOwnershipResolver.IsCitation("sprk_procedureassignment", "sprk_pinnedversion").Should().BeTrue();
        RecordOwnershipResolver.IsCitation("sprk_procedureassignment", "sprk_matter").Should().BeFalse();

        var procedureSet = new Microsoft.Xrm.Sdk.Entity("sprk_ruleset", Guid.NewGuid())
        {
            ["sprk_procedure"] = new Microsoft.Xrm.Sdk.EntityReference("sprk_procedure", procedure),
        };
        RecordOwnershipContext.ParentsOf(procedureSet).Should().BeEmpty("a Procedure rule set is not a secure child (model 5.2)");

        // 089a review K7: a LEGACY uploaded set carries the document it was read from (RuleSetService). sprk_document is an
        // ownership parent, so without the citation the set would be filed under the document.
        var legacySourceDocument = Guid.NewGuid();
        var legacySet = new Microsoft.Xrm.Sdk.Entity("sprk_ruleset", Guid.NewGuid())
        {
            ["sprk_sourcedocument"] = new Microsoft.Xrm.Sdk.EntityReference("sprk_document", legacySourceDocument),
        };
        RecordOwnershipResolver.IsCitation("sprk_ruleset", "sprk_sourcedocument").Should().BeTrue();
        RecordOwnershipContext.ParentsOf(legacySet).Should().BeEmpty("a legacy set's source document is provenance, not parentage");

        var legacyRecordSet = new Microsoft.Xrm.Sdk.Entity("sprk_ruleset", Guid.NewGuid())
        {
            ["sprk_matter"] = new Microsoft.Xrm.Sdk.EntityReference("sprk_matter", matter),
            ["sprk_sourcedocument"] = new Microsoft.Xrm.Sdk.EntityReference("sprk_document", legacySourceDocument),
        };
        RecordOwnershipContext.ParentsOf(legacyRecordSet).Should().Equal(new RecordOwnershipParent("sprk_matter", matter));

        var recordRule = new Microsoft.Xrm.Sdk.Entity("sprk_policy", Guid.NewGuid())
        {
            ["sprk_ruleset"] = new Microsoft.Xrm.Sdk.EntityReference("sprk_ruleset", Guid.NewGuid()),
        };
        RecordOwnershipContext.ParentsOf(recordRule).Select(p => p.EntityLogicalName).Should().Equal("sprk_ruleset");
    }

    // ── The core record pair (a Signal's and Decision Record's core record) ─────────────────────────────────────────────

    /// <summary>The pair is read by every lineage walk on the two tables that hold one, and is never an entry of the lookup map
    /// (ownership order, filing edges and the form library read the map as an edge registry).</summary>
    [Fact]
    public void LineageColumns_AreTheLookupsPlusTheCorePair_OnlyOnSignalsAndDecisionRecords()
    {
        foreach (var table in SecureChildLineage.Children.Values)
        {
            table.Lookups.Keys.Should().NotContain(new[] { "sprk_corerecordtype", "sprk_corerecordid" }, $"{table.LogicalName}'s pair is not a lookup");
            var expected = table.LogicalName is "sprk_signal" or "sprk_decisionrecord"
                ? table.Lookups.Keys.Concat(["sprk_corerecordtype", "sprk_corerecordid"])
                : table.Lookups.Keys;
            table.LineageColumns.Should().Equal(expected, table.LogicalName);
            table.HoldsCorePair.Should().Be(RecordOwnershipContext.CorePairTables.Contains(table.LogicalName));
        }
    }

    [Theory]
    [InlineData("sprk_project", true)]
    [InlineData("sprk_matter", true)]
    [InlineData("sprk_workassignment", true)]
    [InlineData("SPRK_PROJECT", true)]
    [InlineData("sprk_servicerequest", false)] // D-36: not a root, so the pair adds nothing
    [InlineData(null, false)]                  // the catalog names no core table: the caller holds it before asking
    public void CorePairParent_IsTheCoreRecord_OnlyWhenItIsARoot(string? coreTable, bool isParent)
    {
        var core = Guid.NewGuid();

        var parent = SecureChildLineage.CorePairParent(coreTable, core);

        if (isParent)
            parent.Should().Be((coreTable!.ToLowerInvariant(), core));
        else
            parent.Should().BeNull();
    }

    [Fact]
    public void CorePairMatches_NameTheFrontiersRootsByTheirStoredId_OnlyOnTablesWithAPair()
    {
        var (project, matter) = (Guid.NewGuid(), Guid.NewGuid());
        (string, Guid)[] frontier = [("sprk_project", project), ("sprk_event", Guid.NewGuid()), ("sprk_matter", matter)];

        SecureChildLineage.CorePairMatches("sprk_signal", frontier).Should().Equal(
            new[] { "D", "B", "P", "N" }.Select(f => ("sprk_corerecordid", (object)project.ToString(f).ToLowerInvariant()))
                .Concat(new[] { "D", "B", "P", "N" }.Select(f => ("sprk_corerecordid", (object)matter.ToString(f).ToLowerInvariant()))),
            "every form Guid.TryParse reads that a writer could store: D, {B}, (P) and N");
        SecureChildLineage.CorePairMatches("SPRK_DECISIONRECORD", frontier).Should().HaveCount(8);
        SecureChildLineage.CorePairMatches("sprk_event", frontier).Should().BeEmpty("an event has no pair");
        SecureChildLineage.CorePairMatches("sprk_signal", [("sprk_event", Guid.NewGuid())]).Should().BeEmpty("a child is never a core record");
    }

    [Fact]
    public void CorePairMatches_EveryStoredForm_ParsesBackToTheRoot_AsTheUpwardReadReadsIt()
    {
        var project = Guid.NewGuid();

        foreach (var (_, value) in SecureChildLineage.CorePairMatches("sprk_signal", [("sprk_project", project)]))
        {
            var row = new Microsoft.Xrm.Sdk.Entity("sprk_signal", Guid.NewGuid())
            {
                ["sprk_corerecordtype"] = new Microsoft.Xrm.Sdk.EntityReference("sprk_recordtype_ref", Guid.NewGuid()),
                ["sprk_corerecordid"] = (string)value,
            };
            RecordOwnershipContext.CorePairOf(row).CoreId.Should().Be(project, $"{value} is a form the upward read accepts");
        }
    }

    [Fact]
    public void CorePairMatches_AServiceRequestRoot_IsMatchedOnlyForTheOrdinaryPass()
    {
        var request = Guid.NewGuid();
        (string, Guid)[] frontier = [("sprk_servicerequest", request)];

        SecureChildLineage.CorePairMatches("sprk_signal", frontier).Should().BeEmpty("D-36: a service request is no secure root");
        SecureChildLineage.CorePairMatches("sprk_signal", frontier, alsoServiceRequests: true).Select(m => m.Value)
            .Should().Equal(new[] { "D", "B", "P", "N" }.Select(f => (object)request.ToString(f).ToLowerInvariant()));
        SecureChildLineage.CorePairMatches("sprk_event", frontier, alsoServiceRequests: true).Should().BeEmpty("an event has no pair");
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
    {
        // Must-not-fire (GitHub #2560): an ordinary child table keeps the full mirror, Share included where the root holds it.
        RecordShareLevels.ChildMirrorMask("sprk_document", rootMask).Should().Be(childMask);
        RecordShareLevels.ChildMirrorMask("sprk_event", rootMask).Should().Be(childMask);
    }

    // GitHub #2560: a mirrored share on a hard-deny row carries Read only — whatever the root share carries.
    public static TheoryData<string, int, int> HardDenyMirrors()
    {
        var data = new TheoryData<string, int, int>();
        foreach (var table in new[] { "sprk_storedfile", "sprk_procedureassignment", "sprk_ruleset", "sprk_policy", "sprk_policyversion" })
        {
            data.Add(table, 327703, 1);           // Full Access → Read
            data.Add(table, 262167, 1);           // Collaborate → Read
            data.Add(table, 1, 1);                // View Only → Read
            data.Add(table, 2, 0);                // Write only: no Read on the root, nothing on the child
            data.Add(table, 2 | 65536 | 262144, 0); // Write, Delete, Share without Read → nothing
            data.Add(table, 1 | 524288, 1);       // Read + Assign → Read
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(HardDenyMirrors))]
    public void ChildMirrorMask_OnAHardDenyTable_IsReadOnly(string table, int rootMask, int childMask)
    {
        RecordShareLevels.ChildMirrorMask(table, rootMask).Should().Be(childMask);
        RecordShareLevels.ChildMirrorMask(" " + table.ToUpperInvariant() + " ", rootMask).Should().Be(childMask,
            "the hard-deny rule is by table name, as RecordOwnershipResolver.IsRestrictedContentTable reads it");
    }

    /// <summary>GitHub #2560: every hard-deny table, from the ONE list — so a table added to it later is covered too.</summary>
    [Fact]
    public void ChildMirrorMask_OnEveryHardDenyTable_CarriesReadOnly()
    {
        RecordOwnershipResolver.RestrictedContentTables.Should().NotBeEmpty();
        foreach (var table in RecordOwnershipResolver.RestrictedContentTables)
            RecordShareLevels.ChildMirrorMask(table, 327703 | 524288 | 32).Should().Be(1, $"{table} is a hard-deny table");

        // Which hard-deny tables are secure-lineage children today (the others are covered by name all the same).
        // Task 191b: ebilling 086 / 089a made the stored file, the Procedure assignment and the record-scoped rule set secure
        // children too (this pin still named only the two #2084 tables, so it failed on master).
        SecureChildLineage.Children.Keys.Where(RecordOwnershipResolver.IsRestrictedContentTable)
            .Should().BeEquivalentTo("sprk_policy", "sprk_policyversion", "sprk_procedureassignment", "sprk_ruleset", "sprk_storedfile");
    }

    [Fact]
    public void ChildMirrorMask_RefusesABlankTable()
    {
        var blank = () => RecordShareLevels.ChildMirrorMask(" ", 1);
        blank.Should().Throw<ArgumentException>("every child mirror names the child's table");
    }

    // Must-not-fire (GitHub #2560): a secure ROOT inheriting its parent's sharees (task 158) keeps the full mirror.
    [Theory]
    [InlineData(1, 1)]
    [InlineData(262167, 262167)]
    [InlineData(327703, 327703)]
    [InlineData(23, 23)]
    [InlineData(1 | 524288, 1)]
    [InlineData(1 | 32, 1)]
    [InlineData(262167 | 524288, 262167)]
    public void InheritedRootMirrorMask_KeepsTheParentsRights_ExceptAssignAndCreate(int parentMask, int rootMask)
        => RecordShareLevels.InheritedRootMirrorMask(parentMask).Should().Be(rootMask);

    // ── GitHub #2560: the reconcile's drift finding ─────────────────────────────────────────────────────────────────

    [Fact]
    public void HardDenySharesWiderThanRead_FiresForAHardDenyRowWithAWiderShare_ByPrincipalKind()
    {
        var user = DataversePrincipalRef.User(Guid.Parse("00000000-0000-4000-8000-000000000001"));
        var team = DataversePrincipalRef.Team(Guid.Parse("00000000-0000-4000-8000-000000000002"));
        var reader = DataversePrincipalRef.User(Guid.Parse("00000000-0000-4000-8000-000000000003"));
        var shares = new Dictionary<DataversePrincipalRef, int> { [user] = 1 | 2 | 65536 | 262144, [team] = 2, [reader] = 1 };

        SecureChildShareSynchronizer.HardDenySharesWiderThanRead("sprk_policy", shares)
            .Should().Equal(DataversePrincipalKind.SystemUser, DataversePrincipalKind.Team);
    }

    [Fact]
    public void HardDenySharesWiderThanRead_DoesNotFire_ForReadOnlySharesOrAnOrdinaryTable()
    {
        var user = DataversePrincipalRef.User(Guid.NewGuid());
        SecureChildShareSynchronizer.HardDenySharesWiderThanRead(
                "sprk_policyversion", new Dictionary<DataversePrincipalRef, int> { [user] = 1 })
            .Should().BeEmpty("a Read-only share on a hard-deny row is the mirror");
        SecureChildShareSynchronizer.HardDenySharesWiderThanRead(
                "sprk_document", new Dictionary<DataversePrincipalRef, int> { [user] = 327703 })
            .Should().BeEmpty("an ordinary child table carries the full mirror");
    }

    [Fact]
    public void ChildMirrorRights_WritesTheCanonicalLiteral_AndRefusesAssign()
    {
        RecordShareLevels.ChildMirrorRights("sprk_document", 65559).AccessRightsCsv
            .Should().Be("ReadAccess,WriteAccess,AppendAccess,AppendToAccess,DeleteAccess");
        RecordShareLevels.ChildMirrorRights("sprk_document", 262167).AccessRightsCsv
            .Should().Be("ReadAccess,WriteAccess,AppendAccess,AppendToAccess,ShareAccess");
        RecordShareLevels.ChildMirrorRights("sprk_document", 1).AccessRightsCsv.Should().Be("ReadAccess");

        var withAssign = () => RecordShareLevels.ChildMirrorRights("sprk_document", 1 | 524288);
        withAssign.Should().Throw<ArgumentOutOfRangeException>("a child mirror never carries Assign");
    }

    // GitHub #2560, defence in depth: the last check before a mirrored share is written refuses anything but Read on a
    // hard-deny table, whatever the caller computed.
    [Theory]
    [InlineData("sprk_policy", 1 | 2)]
    [InlineData("sprk_policy", 2)]
    [InlineData("sprk_policyversion", 262167)]
    [InlineData("sprk_storedfile", 1 | 65536)]
    [InlineData("SPRK_POLICY", 1 | 262144)]
    public void ChildMirrorRights_OnAHardDenyTable_RefusesEveryBitButRead(string table, int mask)
    {
        var write = () => RecordShareLevels.ChildMirrorRights(table, mask);
        write.Should().Throw<ArgumentOutOfRangeException>("a mirrored share on a hard-deny row carries only Read")
            .WithMessage("*hard-deny*");
    }

    [Fact]
    public void ChildMirrorRights_OnAHardDenyTable_AllowsRead_AndAnOrdinaryTableAllowsWrite()
    {
        RecordShareLevels.ChildMirrorRights("sprk_policy", 1).AccessRightsCsv.Should().Be("ReadAccess");
        RecordShareLevels.ChildMirrorRights("sprk_policyversion", 1).AccessRightsCsv.Should().Be("ReadAccess");

        // Must-not-fire: an ordinary child table takes Write (and the rest of the mirrorable set) as before.
        RecordShareLevels.ChildMirrorRights("sprk_document", 1 | 2).AccessRightsCsv.Should().Be("ReadAccess,WriteAccess");
        RecordShareLevels.ChildMirrorRights("sprk_event", 2).AccessRightsCsv.Should().Be("WriteAccess");
    }

    [Fact]
    public void ChildMirrorRights_RefusesABlankTable()
    {
        var blank = () => RecordShareLevels.ChildMirrorRights(" ", 1);
        blank.Should().Throw<ArgumentException>();
    }

    // ── unified-access-control-r2 task 191b: the external markers ────────────────────────────────────────────────────

    /// <summary>
    /// Task 191b (#2179 design v2 §3.3): every registry table's marker, pinned. A table added without a decision does not
    /// compile (the marker is a required positional argument); a marker changed by accident fails here.
    /// </summary>
    [Fact]
    public void EveryTable_DeclaresTheExpectedExternalMarker()
    {
        var expected = new Dictionary<string, ExternalMarker>(StringComparer.OrdinalIgnoreCase)
        {
            ["sprk_communication"] = ExternalMarker.Predicate(ExternalOwnRule.Communication065),
            ["sprk_communicationthread"] = ExternalMarker.Predicate(ExternalOwnRule.ThreadOpen),
            ["sprk_document"] = ExternalMarker.Predicate(ExternalOwnRule.DocumentNarrowing),
            ["sprk_invoice"] = ExternalMarker.Predicate(ExternalOwnRule.InvoiceSource),
            ["sprk_emailartifact"] = ExternalMarker.Never,
            ["sprk_attachmentartifact"] = ExternalMarker.Never,
            ["sprk_emailreviewlog"] = ExternalMarker.Never,
            ["sprk_analysis"] = ExternalMarker.DocumentText,
            ["sprk_analysisoutput"] = ExternalMarker.DocumentText,
        };

        foreach (var table in SecureChildLineage.Children.Values)
        {
            table.External.Should().NotBeNull($"{table.LogicalName} declares a marker");
            table.External.Should().Be(
                expected.TryGetValue(table.LogicalName, out var marker) ? marker : ExternalMarker.None,
                $"{table.LogicalName}'s external marker");
        }

        SecureChildLineage.OutsideLineage.Should().BeEquivalentTo(
            new Dictionary<string, ExternalMarker> { ["sprk_affinity"] = ExternalMarker.Never });
    }

    /// <summary>Task 191b: the communication-content tables (the guard's refused list) are the registry's Never markers.</summary>
    [Fact]
    public void NeverExternalTables_AreTheCommunicationContentTables()
    {
        SecureChildLineage.NeverExternalTables.Should().BeEquivalentTo(
            "sprk_emailartifact", "sprk_attachmentartifact", "sprk_emailreviewlog", "sprk_affinity");
        SecureChildLineage.ExternalMarkerOf("sprk_affinity").Should().Be(ExternalMarker.Never);
        SecureChildLineage.ExternalMarkerOf("sprk_matter").Should().BeNull("a root declares no marker: its own flags decide it");
        SecureChildLineage.ExternalMarkerOf("sprk_unknown").Should().BeNull();
    }

    [Fact]
    public void ExternalMarker_Predicate_RefusesNoRule_AndTheKindsAreDistinct()
    {
        var none = () => ExternalMarker.Predicate(ExternalOwnRule.None);
        none.Should().Throw<ArgumentOutOfRangeException>();

        ExternalMarker.None.Kind.Should().Be(ExternalMarkerKind.None);
        ExternalMarker.DocumentText.Kind.Should().Be(ExternalMarkerKind.None);
        ExternalMarker.DocumentText.CarriesDocumentText.Should().BeTrue();
        ExternalMarker.None.CarriesDocumentText.Should().BeFalse();
        ExternalMarker.Never.Kind.Should().Be(ExternalMarkerKind.Never);
        ExternalMarker.Predicate(ExternalOwnRule.ThreadOpen).Should().Be(ExternalMarker.Predicate(ExternalOwnRule.ThreadOpen));
        ExternalMarker.None.Should().NotBe(ExternalMarker.DocumentText);
    }

    /// <summary>Task 191b: the predicate's two document edges are registry lookups of the document (a renamed column would
    /// silently stop the chain bound or the chain member's link skip).</summary>
    [Fact]
    public void TheDocumentChainAndLinkColumns_AreDocumentLookups()
    {
        var document = SecureChildLineage.Children["sprk_document"];

        document.Lookups[Sprk.Bff.Api.Infrastructure.ExternalAccess.ExternalDocumentNarrowing.ParentDocumentAttribute]
            .Should().Be("sprk_document");
        document.Lookups[Sprk.Bff.Api.Infrastructure.ExternalAccess.ExternalDocumentNarrowing.RelatedCommunicationAttribute]
            .Should().Be("sprk_communication");
    }
}
