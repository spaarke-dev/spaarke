// R3 Part 1C — Task 053 (2026-06-21): Integration tests for the 3 migrated notification
// playbooks (tasks 050 / 051 / 052). Each test covers BOTH directions of the AC contract:
//
//   Happy path        — seeded test user IS a matter-team-member → notifications produced
//                       (covers AC-1C.1 + FR-1C.4 — "produces non-zero notifications").
//
//   Exclusion path    — documents/emails/events on matters where the user is NOT a member
//                       MUST be excluded from the produced notifications. This is the
//                       regression safety net for the latent tenant-wide over-disclosure
//                       defect surfaced by tasks 051 + 052 (the pre-migration emails/events
//                       playbooks had no membership filter at all). Asserts count == 0.
//
// See MigratedPlaybookFixture.cs for design rationale on orchestrator entry point + stub HTTP
// handler. Each test runs against an isolated fixture (no IClassFixture sharing) so the
// strict-mocked IMembershipResolverService can be set up per scenario without cross-test bleed.

using FluentAssertions;
using Xunit;

namespace Sprk.Bff.Api.IntegrationTests.Playbooks;

[Trait("Category", "Integration")]
[Trait("Phase", "P6")]
[Trait("Coverage", "AC-1C.1,AC-1C.2,FR-1C.4")]
public sealed class MigratedPlaybookTests
{
    // ─────────────────────────────────────────────────────────────────────────────
    // notification-new-documents.json (R3 task 050 migration)
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task NotificationNewDocumentsPlaybook_ProducesNonZeroNotifications_ForMatterMember()
    {
        // Arrange — test user IS a member of 2 matters; each has 3 new documents.
        var fixture = new MigratedPlaybookFixture();
        var (memberMatters, _) = fixture.SeedMatters(memberMatterCount: 2, nonMemberMatterCount: 0);
        foreach (var matterId in memberMatters)
        {
            fixture.SeedRegardingRecords("sprk_document", matterId, count: 3, regardingAttribute: "sprk_matter");
        }

        // Act
        var notificationCount = await fixture.ExecutePlaybookAsync("notification-new-documents.json");

        // Assert — AC-1C.1: non-zero notifications. iterateItems=true on the
        // CreateNotification node → one POST per upstream query item (2 matters × 3 docs = 6).
        notificationCount.Should().BeGreaterThan(0,
            "AC-1C.1 / FR-1C.4 — migrated notification-new-documents playbook MUST produce notifications " +
            "for documents on the seeded test user's matters");
        notificationCount.Should().Be(6,
            "iterateItems=true emits one notification per upstream query item; we seeded 2 matters × 3 docs");
    }

    [Fact]
    public async Task NotificationNewDocumentsPlaybook_ExcludesDocumentsOnNonMemberMatters()
    {
        // Arrange — test user is a member of 0 matters but 4 OTHER matters in the tenant have
        // 5 documents each. The PRE-migration broken FetchXML join would have leaked these
        // documents into the user's notifications (FR-1C.1 / A1 defect).
        var fixture = new MigratedPlaybookFixture();
        var (_, nonMemberMatters) = fixture.SeedMatters(memberMatterCount: 0, nonMemberMatterCount: 4);
        foreach (var matterId in nonMemberMatters)
        {
            fixture.SeedRegardingRecords("sprk_document", matterId, count: 5, regardingAttribute: "sprk_matter");
        }

        // Act
        var notificationCount = await fixture.ExecutePlaybookAsync("notification-new-documents.json");

        // Assert — A1 over-disclosure regression check: ZERO notifications must be produced
        // when the executing user has no matter memberships, even when 20 documents exist
        // on other tenant matters. {{joinIds myMatters.ids}} on an empty membership list
        // produces an empty 'in' clause, which the StubDataverseHandler correctly returns
        // as zero rows (mirroring real FetchXML semantics).
        notificationCount.Should().Be(0,
            "AC-1C.2 — the migration MUST prevent tenant-wide over-disclosure: a user with " +
            "no matter memberships MUST receive zero notifications, even when documents exist " +
            "on other matters in the tenant. This guards against the broken pre-migration " +
            "join pattern (R3 FR-1C.1 / A1 defect).");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // notification-new-emails.json (R3 task 051 migration — added membership filter where
    // none existed; this is the highest-stakes over-disclosure safety net.)
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task NotificationNewEmailsPlaybook_ProducesNonZeroNotifications_ForMatterMember()
    {
        // Arrange — test user IS a member of 3 matters; each has 2 inbound emails regarding it.
        var fixture = new MigratedPlaybookFixture();
        var (memberMatters, _) = fixture.SeedMatters(memberMatterCount: 3, nonMemberMatterCount: 0);
        foreach (var matterId in memberMatters)
        {
            fixture.SeedRegardingRecords("email", matterId, count: 2, regardingAttribute: "regardingobjectid");
        }

        // Act
        var notificationCount = await fixture.ExecutePlaybookAsync("notification-new-emails.json");

        // Assert — AC-1C.1: non-zero. 3 matters × 2 emails = 6 expected POSTs.
        notificationCount.Should().BeGreaterThan(0,
            "AC-1C.1 / FR-1C.4 — migrated notification-new-emails playbook MUST produce notifications " +
            "for inbound emails regarding the seeded test user's matters");
        notificationCount.Should().Be(6,
            "iterateItems=true emits one notification per email; we seeded 3 matters × 2 emails");
    }

    [Fact]
    public async Task NotificationNewEmailsPlaybook_ExcludesEmailsOnNonMemberMatters()
    {
        // Arrange — test user is a member of 0 matters but 5 OTHER matters in the tenant have
        // 4 inbound emails each. PRE-task-051 the playbook had NO membership filter at all —
        // it would have leaked every inbound email on every matter in the tenant. The added
        // {{joinIds myMatters.ids}} 'in' filter is the safety net under test.
        var fixture = new MigratedPlaybookFixture();
        var (_, nonMemberMatters) = fixture.SeedMatters(memberMatterCount: 0, nonMemberMatterCount: 5);
        foreach (var matterId in nonMemberMatters)
        {
            fixture.SeedRegardingRecords("email", matterId, count: 4, regardingAttribute: "regardingobjectid");
        }

        // Act
        var notificationCount = await fixture.ExecutePlaybookAsync("notification-new-emails.json");

        // Assert — task 051's latent over-disclosure regression check: ZERO notifications when
        // the executing user has no memberships, even though 20 emails exist on other matters.
        // This is binding-direct evidence the migration closed the leak.
        notificationCount.Should().Be(0,
            "AC-1C.2 — pre-migration notification-new-emails.json had NO membership filter and " +
            "would have notified the user about every inbound email on every matter in the tenant. " +
            "The migration adds {{joinIds myMatters.ids}} 'in' filter — this test is the safety net " +
            "that proves the leak is closed (R3 FR-1C.2 / latent A1 defect surfaced by task 051).");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // notification-new-events.json (R3 task 052 migration — same latent over-disclosure as
    // emails; same migration pattern.)
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task NotificationNewEventsPlaybook_ProducesNonZeroNotifications_ForMatterMember()
    {
        // Arrange — test user IS a member of 2 matters; each has 4 appointments regarding it.
        var fixture = new MigratedPlaybookFixture();
        var (memberMatters, _) = fixture.SeedMatters(memberMatterCount: 2, nonMemberMatterCount: 0);
        foreach (var matterId in memberMatters)
        {
            fixture.SeedRegardingRecords("appointment", matterId, count: 4, regardingAttribute: "regardingobjectid");
        }

        // Act
        var notificationCount = await fixture.ExecutePlaybookAsync("notification-new-events.json");

        // Assert — AC-1C.1: non-zero. 2 matters × 4 appointments = 8 expected POSTs.
        notificationCount.Should().BeGreaterThan(0,
            "AC-1C.1 / FR-1C.4 — migrated notification-new-events playbook MUST produce notifications " +
            "for appointments regarding the seeded test user's matters");
        notificationCount.Should().Be(8,
            "iterateItems=true emits one notification per appointment; we seeded 2 matters × 4 appts");
    }

    [Fact]
    public async Task NotificationNewEventsPlaybook_ExcludesEventsOnNonMemberMatters()
    {
        // Arrange — test user is a member of 0 matters but 3 OTHER matters in the tenant have
        // 5 appointments each. Same latent over-disclosure defect as emails (task 052).
        var fixture = new MigratedPlaybookFixture();
        var (_, nonMemberMatters) = fixture.SeedMatters(memberMatterCount: 0, nonMemberMatterCount: 3);
        foreach (var matterId in nonMemberMatters)
        {
            fixture.SeedRegardingRecords("appointment", matterId, count: 5, regardingAttribute: "regardingobjectid");
        }

        // Act
        var notificationCount = await fixture.ExecutePlaybookAsync("notification-new-events.json");

        // Assert — task 052's latent over-disclosure regression check.
        notificationCount.Should().Be(0,
            "AC-1C.2 — pre-migration notification-new-events.json had NO membership filter and " +
            "would have notified the user about every appointment on every matter in the tenant. " +
            "The migration adds {{joinIds myMatters.ids}} 'in' filter — this test is the safety net " +
            "that proves the leak is closed (R3 FR-1C.3 / latent A1 defect surfaced by task 052).");
    }
}
