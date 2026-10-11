using System.Security.Cryptography;
using OrganizedCrime.Runtime;
using OrganizedCrime.Runtime.Composition;
using S1API.PhoneCalls;
using Xunit;

namespace OrganizedCrime.Tests;

[Collection("OC-151 composition scope")]
public sealed class Release1ProductionReachabilityTests
{
    // Recorded by SHA-256 straight off tools/OrganizedCrime/Runtime/Qa/Release1WrongAddressStagingHarness.cs
    // at commit ae43a07 (the last commit to touch it, "make the staging dump report per-drop read
    // status"). The OC-57 brief's own text names 99ec8ba, the harness's first commit, but the OC-52
    // review packet already recorded and accepted that the file moved once more after that, to
    // ae43a07, and has been untouched since; this guard follows that established, corrected baseline
    // rather than reintroducing the superseded one.
    //
    // Re-recorded for the OC-65/OC-69 final review fix: this file also declares the shared
    // Release1StagingHarnessStatus enum every owner QA harness returns through
    // Release1StagingHarnessResult, and the OC-69 field contact harness's own Ambiguous-versus-
    // Faulted mismatch (a world Ambiguous status silently printing as Faulted) can only be fixed by
    // adding a member to that shared enum, so this constant is re-pinned once more here, the same
    // re-pin-on-intentional-change convention every other byte-identity guard in this file uses.
    //
    // Re-recorded once more for OC-73: the lockdown gate harness needs to report a case that is not a
    // fault but also is not a plain read status, specifically that a host side action (enabling
    // curfew) could not safely be taken because this session is not the authoritative host. Rather
    // than invent a second, parallel result type, this adds one more member, NeedsOptionB, to the
    // same shared enum, so the constant is re-pinned once more here, same convention.
    private const string WrongAddressStagingHarnessAe43a07Sha256 =
        "7785CC20083E076419F6B3142115F2ABCFA983D3D465624F5AB4A58EE529B5E2";

    // Recorded by SHA-256 straight off tools/OrganizedCrime/Runtime/Qa/Release1ShortNoticeQuantityHarness.cs
    // at commit 739a88b (its last owning commit, "widen the slot decrement and prove it with a
    // quantity harness"). Task 7 never touches this file; this guard follows the same byte-identity
    // pattern the Wrong Address staging harness guard above already establishes.
    private const string ShortNoticeQuantityHarness739a88bSha256 =
        "F4C86B448536DBB5AACAC7B83C58FF3343975E8DCDFACCD9FEA150AA721F6523";

    // Recorded straight off the working tree at 66a60ae (the "draft the envelope spec" commit that
    // opens this branch); OC-60's tasks never touch these owner QA harness files, so their content at
    // 66a60ae and their content now must be byte-identical.
    // Re-recorded for OC-146: the harness gains using OrganizedCrime.Model after its contract types moved to Model; no logic change.
    private const string RoomWithNoNameHoldRoomHarness66a60aeSha256 =
        "24536FE600903E7FA554C5DBDD3EAC4D021D0B3FD6211179187B660CE7B67AEF";

    private const string ShortNoticeQuantityHarness66a60aeSha256 =
        "F4C86B448536DBB5AACAC7B83C58FF3343975E8DCDFACCD9FEA150AA721F6523";

    // Recorded by SHA-256 straight off tools/OrganizedCrime/Runtime/Qa/Release1TheEnvelopeCashGateHarness.cs
    // at commit 4f95ad3 (the "draft the envelope closet deposit spec" commit that opens this branch,
    // a docs-only commit that never touches this file). Task 1 keeps the OC-60 dead drop cash gate
    // harness untouched while it adds the closet-level seam beside it, so this file's content at
    // 4f95ad3 and its content now must be byte-identical, the same pattern the harness guards above
    // already establish.
    private const string EnvelopeCashGateHarness4f95ad3Sha256 =
        "47427B2AFC8CB36F9B62641824A54CDD84CAA3B0CA0F848ED77F8096CFE9B63D";

    [Fact]
    public void Production_composition_exposes_the_reviewed_reader_story_phone_and_prompt_path()
    {
        var type = typeof(Release1ProductionComposition);

        Assert.Equal(typeof(Release1TransitionPublisherService), type.GetProperty(nameof(Release1ProductionComposition.Publisher))!.PropertyType);
        Assert.Equal(typeof(Release1PhoneCallService), type.GetProperty(nameof(Release1ProductionComposition.PhoneService))!.PropertyType);
        Assert.Equal(typeof(Release1IntroPromptPresenter), type.GetProperty(nameof(Release1ProductionComposition.PromptPresenter))!.PropertyType);
        Assert.Equal(typeof(Release1PayphoneBanner), type.GetProperty(nameof(Release1ProductionComposition.Banner))!.PropertyType);
        Assert.Equal(typeof(Release1SmallCourtesyComposition), type.GetProperty(nameof(Release1ProductionComposition.SmallCourtesy))!.PropertyType);
        Assert.Equal(typeof(Release1WrongAddressComposition), type.GetProperty(nameof(Release1ProductionComposition.WrongAddress))!.PropertyType);
        Assert.Equal(typeof(Release1RoomWithNoNameComposition), type.GetProperty(nameof(Release1ProductionComposition.RoomWithNoName))!.PropertyType);
        Assert.Equal(typeof(Release1ShortNoticeComposition), type.GetProperty(nameof(Release1ProductionComposition.ShortNotice))!.PropertyType);
        Assert.Equal(typeof(Release1KeepTheLightsOffComposition), type.GetProperty(nameof(Release1ProductionComposition.KeepTheLightsOff))!.PropertyType);
        Assert.Equal(typeof(Release1DecisionPromptHost), type.GetProperty(nameof(Release1ProductionComposition.DecisionPromptHost))!.PropertyType);
    }

    [Fact]
    public void Exactly_one_production_site_constructs_the_wrong_address_composition()
    {
        var root = FindRepositoryRoot();
        var sites = CountAcrossProductionSources(root, "new Release1WrongAddressComposition(");
        Assert.Equal(1, sites);
    }

    [Fact]
    public void Exactly_one_production_site_constructs_the_room_with_no_name_composition()
    {
        var root = FindRepositoryRoot();
        var sites = CountAcrossProductionSources(root, "new Release1RoomWithNoNameComposition(");
        Assert.Equal(1, sites);
    }

    [Fact]
    public void Exactly_one_production_site_constructs_the_short_notice_composition()
    {
        var root = FindRepositoryRoot();
        var sites = CountAcrossProductionSources(root, "new Release1ShortNoticeComposition(");
        Assert.Equal(1, sites);
    }

    [Fact]
    public void Exactly_one_production_site_constructs_the_keep_the_lights_off_composition()
    {
        var root = FindRepositoryRoot();
        var sites = CountAcrossProductionSources(root, "new Release1KeepTheLightsOffComposition(");
        Assert.Equal(1, sites);
    }

    [Fact]
    public void Exactly_one_production_site_constructs_the_envelope_composition()
    {
        var root = FindRepositoryRoot();
        var sites = CountAcrossProductionSources(root, "new Release1TheEnvelopeComposition(");
        Assert.Equal(1, sites);
    }

    [Fact]
    public void Wrong_address_composition_does_not_dispose_the_shared_world_or_the_shared_phone_service()
    {
        var root = FindRepositoryRoot();
        var composition = File.ReadAllText(Path.Combine(root, "tools", "OrganizedCrime", "Runtime", "Release1WrongAddressComposition.cs"));

        Assert.DoesNotContain("world.Dispose", composition, StringComparison.Ordinal);
        Assert.DoesNotContain("phone.Dispose", composition, StringComparison.Ordinal);
        Assert.DoesNotContain("is IDisposable", composition, StringComparison.Ordinal);
        Assert.DoesNotContain("GUI.", composition, StringComparison.Ordinal);
        Assert.DoesNotContain("HarmonyPatch", composition, StringComparison.Ordinal);
    }

    [Fact]
    public void Room_with_no_name_composition_does_not_dispose_the_shared_world_or_the_shared_phone_service()
    {
        var root = FindRepositoryRoot();
        var composition = File.ReadAllText(Path.Combine(root, "tools", "OrganizedCrime", "Runtime", "Release1RoomWithNoNameComposition.cs"));

        Assert.DoesNotContain("world.Dispose", composition, StringComparison.Ordinal);
        Assert.DoesNotContain("phone.Dispose", composition, StringComparison.Ordinal);
        Assert.DoesNotContain("is IDisposable", composition, StringComparison.Ordinal);
        Assert.DoesNotContain("GUI.", composition, StringComparison.Ordinal);
        Assert.DoesNotContain("HarmonyPatch", composition, StringComparison.Ordinal);
        Assert.DoesNotContain("KeyCode.F", composition, StringComparison.Ordinal);
    }

    [Fact]
    public void No_room_with_no_name_production_file_references_GUI_or_a_harmony_patch_or_a_new_F_key()
    {
        var root = FindRepositoryRoot();
        var runtimeDir = Path.Combine(root, "tools", "OrganizedCrime", "Runtime");
        var roomFiles = Directory.EnumerateFiles(runtimeDir, "Release1RoomWithNoName*.cs", SearchOption.TopDirectoryOnly)
            .Concat(Directory.EnumerateFiles(Path.Combine(runtimeDir, "Qa"), "Release1RoomWithNoName*.cs", SearchOption.TopDirectoryOnly));
        var seen = 0;
        foreach (var file in roomFiles)
        {
            seen++;
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("GUI.", text, StringComparison.Ordinal);
            Assert.DoesNotContain("HarmonyPatch", text, StringComparison.Ordinal);
            Assert.DoesNotContain("KeyCode.F", text, StringComparison.Ordinal);
        }
        Assert.True(seen > 0, "expected at least one Release1RoomWithNoName*.cs production file");
    }

    [Fact]
    public void Short_notice_composition_does_not_dispose_the_shared_world_or_the_shared_phone_service()
    {
        var root = FindRepositoryRoot();
        var composition = File.ReadAllText(Path.Combine(root, "tools", "OrganizedCrime", "Runtime", "Release1ShortNoticeComposition.cs"));

        Assert.DoesNotContain("world.Dispose", composition, StringComparison.Ordinal);
        Assert.DoesNotContain("phone.Dispose", composition, StringComparison.Ordinal);
        Assert.DoesNotContain("is IDisposable", composition, StringComparison.Ordinal);
        Assert.DoesNotContain("GUI.", composition, StringComparison.Ordinal);
        Assert.DoesNotContain("HarmonyPatch", composition, StringComparison.Ordinal);
        Assert.DoesNotContain("KeyCode.F", composition, StringComparison.Ordinal);
    }

    [Fact]
    public void No_short_notice_production_file_references_GUI_or_a_harmony_patch_or_a_new_F_key()
    {
        var root = FindRepositoryRoot();
        var runtimeDir = Path.Combine(root, "tools", "OrganizedCrime", "Runtime");
        var shortNoticeFiles = Directory.EnumerateFiles(runtimeDir, "Release1ShortNotice*.cs", SearchOption.TopDirectoryOnly)
            .Concat(Directory.EnumerateFiles(Path.Combine(runtimeDir, "Qa"), "Release1ShortNotice*.cs", SearchOption.TopDirectoryOnly));
        var seen = 0;
        foreach (var file in shortNoticeFiles)
        {
            seen++;
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("GUI.", text, StringComparison.Ordinal);
            Assert.DoesNotContain("HarmonyPatch", text, StringComparison.Ordinal);
            Assert.DoesNotContain("KeyCode.F", text, StringComparison.Ordinal);
        }
        Assert.True(seen > 0, "expected at least one Release1ShortNotice*.cs production file");
    }

    [Fact]
    public void Try_change_slot_quantity_has_exactly_one_call_site_in_the_short_notice_mission_service()
    {
        var root = FindRepositoryRoot();
        var text = File.ReadAllText(Path.Combine(root, "tools", "OrganizedCrime", "Runtime", "Release1ShortNoticeMissionService.cs"));
        Assert.Equal(1, Count(text, ".TryChangeSlotQuantity("));
    }

    [Fact]
    public void Keep_the_lights_off_composition_does_not_dispose_the_shared_world_or_the_shared_phone_service()
    {
        var root = FindRepositoryRoot();
        var composition = File.ReadAllText(Path.Combine(root, "tools", "OrganizedCrime", "Runtime", "Release1KeepTheLightsOffComposition.cs"));

        Assert.DoesNotContain("world.Dispose", composition, StringComparison.Ordinal);
        Assert.DoesNotContain("phone.Dispose", composition, StringComparison.Ordinal);
        Assert.DoesNotContain("is IDisposable", composition, StringComparison.Ordinal);
        Assert.DoesNotContain("GUI.", composition, StringComparison.Ordinal);
        Assert.DoesNotContain("HarmonyPatch", composition, StringComparison.Ordinal);
        Assert.DoesNotContain("KeyCode.F", composition, StringComparison.Ordinal);
    }

    [Fact]
    public void No_keep_the_lights_off_production_file_references_GUI_or_a_harmony_patch_or_any_F_key()
    {
        var root = FindRepositoryRoot();
        var runtimeDir = Path.Combine(root, "tools", "OrganizedCrime", "Runtime");
        var files = Directory.EnumerateFiles(runtimeDir, "Release1KeepTheLightsOff*.cs", SearchOption.TopDirectoryOnly)
            .Concat(Directory.EnumerateFiles(runtimeDir, "Release1QuietCensusClassifier.cs", SearchOption.TopDirectoryOnly))
            .Concat(Directory.EnumerateFiles(runtimeDir, "Release1QuietWindow.cs", SearchOption.TopDirectoryOnly))
            .Concat(Directory.EnumerateFiles(Path.Combine(runtimeDir, "Qa"), "Release1KeepTheLightsOff*.cs", SearchOption.TopDirectoryOnly));
        var seen = 0;
        foreach (var file in files)
        {
            seen++;
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("GUI.", text, StringComparison.Ordinal);
            Assert.DoesNotContain("HarmonyPatch", text, StringComparison.Ordinal);
            // This mission claims no owner QA key.
            Assert.DoesNotContain("KeyCode.F", text, StringComparison.Ordinal);
            Assert.DoesNotContain(".TryInsertPackagedProduct(", text, StringComparison.Ordinal);
            Assert.DoesNotContain(".TryChangeSlotQuantity(", text, StringComparison.Ordinal);
        }
        Assert.True(seen > 0, "expected at least one Release1KeepTheLightsOff*.cs production file");
    }

    [Fact]
    public void Try_subscribe_dead_drop_closed_has_no_call_site_in_the_keep_the_lights_off_mission_service()
    {
        var root = FindRepositoryRoot();
        var text = File.ReadAllText(Path.Combine(root, "tools", "OrganizedCrime", "Runtime", "Release1KeepTheLightsOffMissionService.cs"));
        Assert.Equal(0, Count(text, ".TrySubscribeDeadDropClosed("));
    }

    [Fact]
    public void Try_change_slot_quantity_has_one_call_site_per_using_mission_service_and_none_in_the_two_newest()
    {
        var root = FindRepositoryRoot();
        var runtimeDir = Path.Combine(root, "tools", "OrganizedCrime", "Runtime");
        foreach (var missionServiceFile in new[]
                 {
                     "Release1SmallCourtesyMissionService.cs",
                     "Release1WrongAddressMissionService.cs",
                     "Release1RoomWithNoNameMissionService.cs",
                     "Release1ShortNoticeMissionService.cs"
                 })
        {
            var text = File.ReadAllText(Path.Combine(runtimeDir, missionServiceFile));
            Assert.Equal(1, Count(text, ".TryChangeSlotQuantity("));
        }

        // Neither of the two newest missions moves a dead drop slot quantity: Keep the Lights Off
        // observes drops without touching them, and The Envelope moves cash rather than cargo.
        var keepTheLightsOffText = File.ReadAllText(Path.Combine(runtimeDir, "Release1KeepTheLightsOffMissionService.cs"));
        Assert.Equal(0, Count(keepTheLightsOffText, ".TryChangeSlotQuantity("));

        var envelopeText = File.ReadAllText(Path.Combine(runtimeDir, "Release1TheEnvelopeMissionService.cs"));
        Assert.Equal(0, Count(envelopeText, ".TryChangeSlotQuantity("));
    }

    [Fact]
    public void Try_change_dead_drop_slot_cash_balance_has_no_production_call_site_outside_the_harness()
    {
        // OC-61 Task 2 moves The Envelope's assignment to the Syndicate HQ hold room, so its shipped
        // single-drop, single-slot consumption pipeline (the sole caller of this member) is deleted
        // with the dead drop destination. Task 4 wires the closet based replacement against
        // TryChangeHoldRoomSlotCashBalance instead (see
        // Try_change_hold_room_slot_cash_balance_has_one_production_call_site_outside_the_harness_and_the_facade
        // below), so this member's own call site count stays at zero for good.
        var root = FindRepositoryRoot();
        var runtimeDir = Path.Combine(root, "tools", "OrganizedCrime", "Runtime");
        var sites = 0;
        var qaDir = Path.Combine(runtimeDir, "Qa");
        var scanned = Directory.EnumerateFiles(runtimeDir, "*.cs", SearchOption.TopDirectoryOnly)
            .Concat(Directory.EnumerateFiles(qaDir, "*.cs", SearchOption.TopDirectoryOnly));
        foreach (var file in scanned)
        {
            var name = Path.GetFileName(file);
            // The F1 gate harness itself, and S1ApiRelease1SmallCourtesyWorld's own internal
            // forwarding from the boundary member to the access-layer implementation, are excluded
            // the same way TryInsertPackagedProduct's own call-site count excludes that file: neither
            // is a second mission-facing caller of the interface member.
            if (name == "Release1TheEnvelopeCashGateHarness.cs") continue;
            if (name == "S1ApiRelease1SmallCourtesyWorld.cs") continue;
            sites += Count(File.ReadAllText(file), ".TryChangeDeadDropSlotCashBalance(");
        }
        Assert.Equal(0, sites);

        var envelopeText = File.ReadAllText(Path.Combine(runtimeDir, "Release1TheEnvelopeMissionService.cs"));
        Assert.Equal(0, Count(envelopeText, "_world.TryChangeDeadDropSlotCashBalance("));
    }

    // S1ApiRelease1SmallCourtesyWorld.cs is excluded from the scan above because its own boundary
    // member forwards to the access layer, a second textual occurrence of the interface member's
    // name that is not a second mission-facing caller. That exclusion does not cover the one call
    // that actually mutates a CashInstance's balance: cash.ChangeBalance(amount), inside the access
    // implementation. This asserts that call site stays singular, so a second mutation smuggled into
    // this file (bypassing every mission service's single call site) would still be caught.
    [Fact]
    public void S1ApiRelease1SmallCourtesyWorld_has_exactly_one_CashInstance_ChangeBalance_call()
    {
        var root = FindRepositoryRoot();
        var text = File.ReadAllText(Path.Combine(root, "tools", "OrganizedCrime", "Runtime", "S1ApiRelease1SmallCourtesyWorld.cs"));
        Assert.Equal(1, Count(text, "cash.ChangeBalance("));
        Assert.Equal(1, Count(text, ".ChangeBalance("));
    }

    [Fact]
    public void Try_change_hold_room_slot_cash_balance_has_one_production_call_site_outside_the_harness_and_the_facade()
    {
        var root = FindRepositoryRoot();
        var runtimeDir = Path.Combine(root, "tools", "OrganizedCrime", "Runtime");
        var sites = 0;
        var qaDir = Path.Combine(runtimeDir, "Qa");
        var scanned = Directory.EnumerateFiles(runtimeDir, "*.cs", SearchOption.TopDirectoryOnly)
            .Concat(Directory.EnumerateFiles(qaDir, "*.cs", SearchOption.TopDirectoryOnly));
        foreach (var file in scanned)
        {
            var name = Path.GetFileName(file);
            if (name == "Release1TheEnvelopeClosetCashGateHarness.cs") continue;
            if (name == "S1ApiRelease1SmallCourtesyWorld.cs") continue;
            sites += Count(File.ReadAllText(file), ".TryChangeHoldRoomSlotCashBalance(");
        }
        // Task 4 wires the locked, multi-slot closet consumption transaction, so the seam now has
        // exactly one mission-facing caller: Release1TheEnvelopeMissionService.cs's own ConsumeEnvelope.
        Assert.Equal(1, sites);

        var envelopeText = File.ReadAllText(Path.Combine(runtimeDir, "Release1TheEnvelopeMissionService.cs"));
        Assert.Equal(1, Count(envelopeText, "_world.TryChangeHoldRoomSlotCashBalance("));
        Assert.Equal(0, Count(envelopeText, ".TryChangeDeadDropSlotCashBalance("));
        Assert.Equal(0, Count(envelopeText, ".TryChangeSlotQuantity("));
        Assert.Equal(0, Count(envelopeText, ".TryInsertPackagedProduct("));
        Assert.Equal(0, Count(envelopeText, ".TrySubscribeDeadDropClosed("));
        Assert.Equal(0, Count(envelopeText, ".TryChangeCashBalance("));
    }

    [Fact]
    public void Syndicate_hq_native_storage_boundary_has_exactly_one_CashInstance_ChangeBalance_call()
    {
        var root = FindRepositoryRoot();
        var text = File.ReadAllText(Path.Combine(root, "tools", "OrganizedCrime", "Runtime", "SyndicateHqNativeStorageBoundary.cs"));
        Assert.Equal(1, Count(text, "cash.ChangeBalance("));
        Assert.Equal(1, Count(text, ".ChangeBalance("));
    }

    [Fact]
    public void The_dead_drop_cash_gate_harness_is_unchanged_since_its_owning_commit()
    {
        var root = FindRepositoryRoot();
        var bytes = ReadLineEndingNormalizedBytes(Path.Combine(root, "tools", "OrganizedCrime", "Runtime", "Qa", "Release1TheEnvelopeCashGateHarness.cs"));
        Assert.Equal(EnvelopeCashGateHarness4f95ad3Sha256, Convert.ToHexString(SHA256.HashData(bytes)), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The pinned hashes are over LF content, which is what git stores for these files and what every
    /// clone outside a CRLF-converting Windows checkout sees. Normalising here keeps the pins guarding
    /// harness drift without also guarding the line-ending convention of whoever runs the tests.
    /// </summary>
    private static byte[] ReadLineEndingNormalizedBytes(string path)
    {
        // OC-154: invert only the approved interface/import edits before hashing.
        // The original hashes still pin every body, value, branch, and call order.
        var text = File.ReadAllText(path);
        var capability = Path.GetFileName(path) switch
        {
            "Release1WrongAddressStagingHarness.cs" => "IDeadDropInventoryWorld",
            "Release1ShortNoticeQuantityHarness.cs" => "IDeadDropInventoryWorld",
            "Release1RoomWithNoNameHoldRoomHarness.cs" => "IHoldRoomWorld",
            "Release1TheEnvelopeCashGateHarness.cs" => "IDeadDropCashWorld",
            _ => throw new InvalidOperationException("No OC-154 structural hash mapping for " + path)
        };
        const string import = "using OrganizedCrime.Runtime.Release1.World;";
        Assert.StartsWith(import, text, StringComparison.Ordinal);
        Assert.Contains(capability + " world", text, StringComparison.Ordinal);
        text = text[(text.IndexOf('\n') + 1)..]
            .Replace(capability + " world", "IRelease1SmallCourtesyWorld world", StringComparison.Ordinal);
        var bytes = System.Text.Encoding.UTF8.GetBytes(text);
        var normalized = new List<byte>(bytes.Length);
        for (var i = 0; i < bytes.Length; i++)
        {
            if (bytes[i] == (byte)'\r' && i + 1 < bytes.Length && bytes[i + 1] == (byte)'\n') continue;
            normalized.Add(bytes[i]);
        }
        return normalized.ToArray();
    }

    [Fact]
    public void Wrong_address_staging_harness_is_unchanged_since_its_last_owning_commit()
    {
        var root = FindRepositoryRoot();
        var bytes = ReadLineEndingNormalizedBytes(Path.Combine(root, "tools", "OrganizedCrime", "Runtime", "Qa", "Release1WrongAddressStagingHarness.cs"));
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        Assert.Equal(WrongAddressStagingHarnessAe43a07Sha256, hash, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Short_notice_quantity_harness_is_unchanged_since_its_last_owning_commit()
    {
        var root = FindRepositoryRoot();
        var bytes = ReadLineEndingNormalizedBytes(Path.Combine(root, "tools", "OrganizedCrime", "Runtime", "Qa", "Release1ShortNoticeQuantityHarness.cs"));
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        Assert.Equal(ShortNoticeQuantityHarness739a88bSha256, hash, StringComparer.OrdinalIgnoreCase);
    }

    // OC-151: Mod_is_byte_identical_to_its_oc_10_content was removed; the shell moved into
    // OcCompositionRoot plus setup modules, and the same no-unreviewed-shell-change guarantee
    // now lives in the exact setup/teardown trace assertions (CompositionAssertions, called by
    // OcCompositionRootTests and ModCompositionCharacterizationTests).

    [Fact]
    public void Every_owner_qa_harness_file_is_still_byte_identical_to_its_content_at_66a60ae()
    {
        var root = FindRepositoryRoot();
        var runtimeDir = Path.Combine(root, "tools", "OrganizedCrime", "Runtime");
        var qaDir = Path.Combine(runtimeDir, "Qa");

        var wrongAddressBytes = ReadLineEndingNormalizedBytes(Path.Combine(qaDir, "Release1WrongAddressStagingHarness.cs"));
        Assert.Equal(
            WrongAddressStagingHarnessAe43a07Sha256,
            Convert.ToHexString(SHA256.HashData(wrongAddressBytes)),
            StringComparer.OrdinalIgnoreCase);

        var roomBytes = ReadLineEndingNormalizedBytes(Path.Combine(qaDir, "Release1RoomWithNoNameHoldRoomHarness.cs"));
        Assert.Equal(
            RoomWithNoNameHoldRoomHarness66a60aeSha256,
            Convert.ToHexString(SHA256.HashData(roomBytes)),
            StringComparer.OrdinalIgnoreCase);

        var shortNoticeBytes = ReadLineEndingNormalizedBytes(Path.Combine(qaDir, "Release1ShortNoticeQuantityHarness.cs"));
        Assert.Equal(
            ShortNoticeQuantityHarness66a60aeSha256,
            Convert.ToHexString(SHA256.HashData(shortNoticeBytes)),
            StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Try_change_cash_balance_has_exactly_one_call_site_per_mission_service()
    {
        var root = FindRepositoryRoot();
        var runtimeDir = Path.Combine(root, "tools", "OrganizedCrime", "Runtime");
        foreach (var missionServiceFile in new[]
                 {
                     "Release1SmallCourtesyMissionService.cs",
                     "Release1WrongAddressMissionService.cs",
                     "Release1RoomWithNoNameMissionService.cs",
                     "Release1ShortNoticeMissionService.cs"
                 })
        {
            var text = File.ReadAllText(Path.Combine(runtimeDir, missionServiceFile));
            Assert.Equal(1, Count(text, ".TryChangeCashBalance("));
        }

        // The Envelope pays no cash reward: the player-wallet member is never called from its
        // mission service at all, distinct from TryChangeDeadDropSlotCashBalance above. Task 4
        // drops Keep the Lights Off's payout entirely, so it joins The Envelope at zero.
        var envelopeText = File.ReadAllText(Path.Combine(runtimeDir, "Release1TheEnvelopeMissionService.cs"));
        Assert.Equal(0, Count(envelopeText, ".TryChangeCashBalance("));

        var keepTheLightsOffText = File.ReadAllText(Path.Combine(runtimeDir, "Release1KeepTheLightsOffMissionService.cs"));
        Assert.Equal(0, Count(keepTheLightsOffText, ".TryChangeCashBalance("));
    }

    [Fact]
    public void The_envelope_composition_does_not_dispose_the_shared_world_or_the_shared_phone_service()
    {
        var root = FindRepositoryRoot();
        var composition = File.ReadAllText(Path.Combine(root, "tools", "OrganizedCrime", "Runtime", "Release1TheEnvelopeComposition.cs"));

        Assert.DoesNotContain("world.Dispose", composition, StringComparison.Ordinal);
        Assert.DoesNotContain("phone.Dispose", composition, StringComparison.Ordinal);
        Assert.DoesNotContain("is IDisposable", composition, StringComparison.Ordinal);
        Assert.DoesNotContain("GUI.", composition, StringComparison.Ordinal);
        Assert.DoesNotContain("HarmonyPatch", composition, StringComparison.Ordinal);
        Assert.DoesNotContain("KeyCode.F", composition, StringComparison.Ordinal);
    }

    [Fact]
    public void No_the_envelope_production_file_references_GUI_or_a_harmony_patch_or_a_new_F_key()
    {
        var root = FindRepositoryRoot();
        var runtimeDir = Path.Combine(root, "tools", "OrganizedCrime", "Runtime");
        var envelopeFiles = Directory.EnumerateFiles(runtimeDir, "Release1TheEnvelope*.cs", SearchOption.TopDirectoryOnly)
            .Concat(Directory.EnumerateFiles(Path.Combine(runtimeDir, "Qa"), "Release1TheEnvelope*.cs", SearchOption.TopDirectoryOnly));
        var seen = 0;
        foreach (var file in envelopeFiles)
        {
            seen++;
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("GUI.", text, StringComparison.Ordinal);
            Assert.DoesNotContain("HarmonyPatch", text, StringComparison.Ordinal);
            Assert.DoesNotContain("KeyCode.F", text, StringComparison.Ordinal);
            Assert.DoesNotContain("MorePatrols", text, StringComparison.Ordinal);
            Assert.DoesNotContain("PoliceStation", text, StringComparison.Ordinal);
            Assert.DoesNotContain("LocalPressure", text, StringComparison.Ordinal);
            Assert.DoesNotContain("WantedLevel", text, StringComparison.Ordinal);
            Assert.DoesNotContain("Dispatch", text, StringComparison.Ordinal);
        }
        Assert.True(seen > 0, "expected at least one Release1TheEnvelope*.cs production file");
    }

    [Fact]
    public void The_envelope_quest_still_creates_exactly_one_entry()
    {
        var root = FindRepositoryRoot();
        var questPath = Path.Combine(root, "tools", "OrganizedCrime", "Runtime", "Release1TheEnvelopeQuest.cs");
        var text = File.ReadAllText(questPath);

        Assert.Equal(1, Count(text, "AddEntry(placeholder, (Vector3?)null)"));
    }

    // OC-151: Mod_has_one_new_production_F_key_and_no_F12 was removed; the keys moved to
    // OwnerQaKeys, and the same every-F-key-once-and-no-F12 guarantee now lives in the exact
    // 22-key map assertion (CompositionAssertions.AssertKeyMap).

    private static readonly string[] FieldContactMembers =
        {
            "TryParkFieldContact", "TryUnparkFieldContact", "TryDespawnFieldContact", "TryProvokeFieldContact",
            "TryReadFieldContact"
        };

    [Fact]
    public void No_field_contact_member_is_reached_from_a_mission_service_or_a_convergence_pass()
    {
        var runtimeDir = Path.Combine(FindRepositoryRoot(), "tools", "OrganizedCrime", "Runtime");
        foreach (var file in Directory.EnumerateFiles(runtimeDir, "*MissionService.cs", SearchOption.TopDirectoryOnly))
        {
            var text = File.ReadAllText(file);
            foreach (var member in FieldContactMembers)
                Assert.DoesNotContain(member, text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Wrong_address_presentation_source_contains_no_GUI_reference()
    {
        var root = FindRepositoryRoot();
        var presentation = File.ReadAllText(Path.Combine(root, "tools", "OrganizedCrime", "Runtime", "Release1WrongAddressPresentation.cs"));

        Assert.DoesNotContain("GUI.", presentation, StringComparison.Ordinal);
        Assert.DoesNotContain("OnGUI", presentation, StringComparison.Ordinal);
        Assert.DoesNotContain("HarmonyPatch", presentation, StringComparison.Ordinal);
    }

    [Fact]
    public void Try_insert_packaged_product_has_exactly_five_production_call_sites()
    {
        var root = FindRepositoryRoot();
        var runtimeDir = Path.Combine(root, "tools", "OrganizedCrime");
        var sites = 0;
        foreach (var file in Directory.EnumerateFiles(runtimeDir, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                continue;
            // S1ApiRelease1SmallCourtesyWorld both declares and implements TryInsertPackagedProduct;
            // its own internal forwarding from the boundary member to the access-layer implementation
            // is plumbing inside one interface implementation, not a second caller of the mission-
            // facing interface, so it is excluded from this call-site count.
            if (Path.GetFileName(file) == "S1ApiRelease1SmallCourtesyWorld.cs") continue;
            sites += Count(File.ReadAllText(file), ".TryInsertPackagedProduct(");
        }
        // Small Courtesy, Wrong Address, and now Room With No Name each stage a consignment through
        // exactly one call to the mission-facing interface member; OC-121 adds the
        // Chapter 3 QA deliver verb as the fourth staging caller, and OC-123 the
        // Chapter 3 package-recovery staging as the fifth.
        Assert.Equal(5, sites);
    }

    private static int CountAcrossProductionSources(string root, string needle)
    {
        var runtimeDir = Path.Combine(root, "tools", "OrganizedCrime");
        var total = 0;
        foreach (var file in Directory.EnumerateFiles(runtimeDir, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                continue;
            total += Count(File.ReadAllText(file), needle);
        }
        return total;
    }

    [Fact]
    public void Production_phone_adapter_and_definition_use_the_supported_S1API_surface()
    {
        Assert.Contains(typeof(IRelease1PhoneCallQueue), typeof(S1ApiRelease1PhoneCallQueue).GetInterfaces());
        Assert.True(typeof(PhoneCallDefinition).IsAssignableFrom(typeof(OcRelease1PhoneCallDefinition)));
        Assert.Contains(typeof(IRelease1PayphoneCue), typeof(Release1PayphoneBanner).GetInterfaces());
    }

    [Fact]
    public void Production_mod_reads_the_presentation_mode_preference_and_constructs_the_native_boundary_in_native_mode()
    {
        // OC-151: the mode branch moved into Release1Module/Chapter3Module behind the engine
        // seam; Native builds both natives with no downgrade, Imgui builds neither.
        using var native = new CompositionScope(engine => engine.PresentationModeResult = Release1PresentationMode.Native);

        CompositionAssertions.AssertNativeSetupTrace(native.Engine);
        CompositionAssertions.AssertNativePrefReads(native.Engine);
        Assert.NotNull(native.Root.Production!.Projector);
        Assert.NotNull(native.Root.Chief!.Projector);
        Assert.NotNull(native.Root.Chapter3Projector);

        using var imgui = new CompositionScope();

        Assert.Null(imgui.Root.Production!.Projector);
        Assert.Null(imgui.Root.Chief!.Projector);
        Assert.Null(imgui.Root.Chapter3Projector);
        Assert.Equal(0, imgui.Engine.QuestPolicyReads);

        // The downgrade stays gone: neither setup module falls back when the mode is Native.
        var root = FindRepositoryRoot();
        var release1 = File.ReadAllText(Path.Combine(root, "tools", "OrganizedCrime", "Runtime", "Composition", "Release1Module.cs"));
        var chapter3 = File.ReadAllText(Path.Combine(root, "tools", "OrganizedCrime", "Runtime", "Composition", "Chapter3Module.cs"));
        Assert.DoesNotContain("is not yet available; falling back", release1, StringComparison.Ordinal);
        Assert.DoesNotContain("is not yet available; falling back", chapter3, StringComparison.Ordinal);

        // OC-151 Blocker 5: the production engine must read the real preferences, not only
        // the fake seam.
        var melon = File.ReadAllText(Path.Combine(root, "tools", "OrganizedCrime", "Runtime", "Composition", "MelonEngineAdapters.cs"));
        Assert.Contains("Release1PresentationModePreference.Read()", melon, StringComparison.Ordinal);
        Assert.Contains("Release1QuestPersistencePolicyPreference.Read()", melon, StringComparison.Ordinal);
    }

    [Fact]
    public void Small_courtesy_presenter_source_contains_no_GUI_reference()
    {
        var root = FindRepositoryRoot();
        var presentation = File.ReadAllText(Path.Combine(root, "tools", "OrganizedCrime", "Runtime", "Release1SmallCourtesyPresentation.cs"));
        var composition = File.ReadAllText(Path.Combine(root, "tools", "OrganizedCrime", "Runtime", "Release1SmallCourtesyComposition.cs"));

        Assert.DoesNotContain("GUI.", presentation, StringComparison.Ordinal);
        Assert.DoesNotContain("OnGUI", presentation, StringComparison.Ordinal);
        Assert.DoesNotContain("OnGUI", composition, StringComparison.Ordinal);
    }

    [Fact]
    public void Production_mod_wires_the_composition_through_load_update_gui_and_reverse_teardown()
    {
        // OC-151: the wiring moved into Release1Module plus the lifecycle tables; the engine
        // factories the module calls are the substituted S1Api constructions, in trace order.
        using var scope = new CompositionScope();
        var trace = scope.Engine.Trace;

        Assert.NotNull(scope.Root.Production);
        Assert.NotNull(scope.Root.Production!.SmallCourtesy);
        Assert.NotNull(scope.Root.Production.PhoneService);
        Assert.True(trace.IndexOf("factory:CourtesyWorld") < trace.IndexOf("factory:CartelSource"), "courtesy world before the unlock reader source");
        Assert.True(trace.IndexOf("factory:CartelSource") < trace.IndexOf("factory:PhoneQueue"), "unlock reader source before the phone queue");
        Assert.True(trace.IndexOf("factory:PhoneQueue") < trace.IndexOf("step:Release1.Production"), "factories before the composition");
        CompositionAssertions.AssertPhaseTables(LifecycleTables.ForRoot(scope.Root));
        CompositionAssertions.AssertUpdateSlotsWired(LifecycleTables.ForRoot(scope.Root).Update);
        CompositionAssertions.AssertForRootProductionLifecycleNeedles();
        scope.Root.Teardown();
        Assert.Contains("teardown:Production.Dispose", scope.Engine.Trace);

        var root = FindRepositoryRoot();
        Assert.DoesNotContain("KeyCode.F", File.ReadAllText(Path.Combine(root, "tools", "OrganizedCrime", "Runtime", "Release1SmallCourtesyComposition.cs")), StringComparison.Ordinal);
        Assert.DoesNotContain("KeyCode.F", File.ReadAllText(Path.Combine(root, "tools", "OrganizedCrime", "Runtime", "Release1SmallCourtesyPresentation.cs")), StringComparison.Ordinal);

        var promptHost = File.ReadAllText(Path.Combine(root, "tools", "OrganizedCrime", "Runtime", "Release1DecisionPromptHost.cs"));
        Assert.Contains("AddActiveUIElement", promptHost, StringComparison.Ordinal);
        Assert.Contains("FreeMouse", promptHost, StringComparison.Ordinal);
        Assert.Contains("LockMouse", promptHost, StringComparison.Ordinal);
        Assert.DoesNotContain("LeftAlt", promptHost, StringComparison.Ordinal);
        Assert.DoesNotContain("SendTextMessage", promptHost, StringComparison.Ordinal);
        Assert.DoesNotContain("MSGConversation", promptHost, StringComparison.Ordinal);
    }

    // OC-73 Task 5. Reachability sweep: Release1ChiefService, Release1ChiefComposition,
    // Release1ChiefTierObserver and LocalPressureTierTransitionFanOut are all live production types
    // this task and Task 2 introduced; this pins that every one of them is actually reached from the
    // mod shell, the same wiring guarantee Production_mod_wires_the_composition_through... already
    // gives Release1ProductionComposition, so none of the four can quietly become dead code.
    [Fact]
    public void Production_mod_wires_the_chief_composition_through_load_update_and_reverse_teardown()
    {
        // OC-151: the wiring moved into Release1Module plus the lifecycle tables. The chief
        // takes no PreSceneChange slot: the table pins exactly the three participants.
        using var scope = new CompositionScope();

        Assert.NotNull(scope.Root.Chief);
        Assert.NotNull(scope.Root.Chief!.Service);
        Assert.NotNull(scope.Root.Law!.ChiefObserver);
        var tables = LifecycleTables.ForRoot(scope.Root);
        CompositionAssertions.AssertPhaseTables(tables);
        CompositionAssertions.AssertUpdateSlotsWired(tables.Update);
        CompositionAssertions.AssertForRootChiefLifecycleNeedles();
        scope.Root.Teardown();
        Assert.Contains("teardown:Chief.Dispose", scope.Engine.Trace);

        var root = FindRepositoryRoot();
        var composition = File.ReadAllText(Path.Combine(root, "tools", "OrganizedCrime", "Runtime", "Release1ChiefComposition.cs"));
        Assert.Contains("new Release1ChiefService(", composition, StringComparison.Ordinal);

        var lawResponse = File.ReadAllText(Path.Combine(root, "tools", "OrganizedCrime", "Runtime", "NativeLawResponseModComposition.cs"));
        Assert.Contains("new Release1ChiefTierObserver()", lawResponse, StringComparison.Ordinal);
        Assert.Contains("new LocalPressureTierTransitionFanOut(", lawResponse, StringComparison.Ordinal);
    }

    [Fact]
    public void Production_mod_gates_owner_qa_keys_on_a_preference_instead_of_a_debug_build()
    {
        // OC-151: the keys moved to OwnerQaKeys and the gate into OwnerQaKeys.Poll; the
        // preference read is the first setup-trace entry. Gate mechanics (typing and the
        // disabled flag return before any key read) live in OwnerQaKeysTests.
        CompositionAssertions.AssertKeyMap(new OcCompositionRoot(new FakeOcEngineAdapters(), _ => { }, _ => { }).QaKeys.Bindings);

        var root = FindRepositoryRoot();
        var keys = File.ReadAllText(Path.Combine(root, "tools", "OrganizedCrime", "Runtime", "Qa", "OwnerQaKeys.cs"));
        Assert.DoesNotContain("#if DEBUG", keys, StringComparison.Ordinal);
        var typingGate = keys.IndexOf("if (_engine.IsPlayerTyping())", StringComparison.Ordinal);
        var enabledGate = keys.IndexOf("if (!_root.OwnerQaKeysEnabled)", StringComparison.Ordinal);
        var dispatch = keys.IndexOf("binding.Handler();", StringComparison.Ordinal);
        Assert.True(typingGate >= 0 && enabledGate > typingGate && dispatch > enabledGate,
            "Poll must return while typing and while disabled before dispatching any binding.");

        using var scope = new CompositionScope();
        Assert.Equal("read:OwnerQaKeys", scope.Engine.Trace[0]);
        Assert.Equal(1, scope.Engine.OwnerQaKeysReads);

        var melon = File.ReadAllText(Path.Combine(root, "tools", "OrganizedCrime", "Runtime", "Composition", "MelonEngineAdapters.cs"));
        Assert.Contains("OwnerQaKeysPreference.Read()", melon, StringComparison.Ordinal);
    }

    [Fact]
    public void Nell_npc_borrows_a_portrait_from_the_configured_preference_inside_a_try_block()
    {
        // Release1NellNpc.cs is S1API/MelonLoader-dependent and not linked into the test project (see
        // its own doc comment), so this reachability test reads the source directly, the same pattern
        // Release1ProductionReachabilityTests already uses for Mod.cs above. The preference default
        // itself (empty string, skip entirely) is covered by Release1NellPortraitNpcIdPreference.cs's
        // own doc comment and by the OwnerQaKeysPreference/Release1PresentationModePreference pattern
        // this file already pins for the other two MelonPreferences-backed reads.
        var root = FindRepositoryRoot();
        var nellNpc = File.ReadAllText(Path.Combine(root, "tools", "OrganizedCrime", "Runtime", "Release1NellNpc.cs"));

        Assert.Contains("Release1NellPortraitNpcIdPreference.Read()", nellNpc, StringComparison.Ordinal);
        Assert.Contains("NPC.Get(npcId)", nellNpc, StringComparison.Ordinal);
        Assert.Contains("Icon = source.Icon;", nellNpc, StringComparison.Ordinal);
        Assert.Contains("RefreshMessagingIcons();", nellNpc, StringComparison.Ordinal);
        Assert.Contains("catch (Exception exception)", nellNpc, StringComparison.Ordinal);

        var tryStart = nellNpc.IndexOf("private void TryApplyPortrait()", StringComparison.Ordinal);
        Assert.True(tryStart >= 0, "TryApplyPortrait was not found.");
        var tryBody = nellNpc.Substring(tryStart);
        var portraitReadIndex = tryBody.IndexOf("Release1NellPortraitNpcIdPreference.Read()", StringComparison.Ordinal);
        var catchIndex = tryBody.IndexOf("catch (Exception exception)", StringComparison.Ordinal);
        Assert.True(portraitReadIndex >= 0 && catchIndex > portraitReadIndex,
            "The portrait preference read and the icon borrow must both be inside TryApplyPortrait's try block.");

        // The ctor now reads "Nell", not "Eleanor": the display-name decision (spec section 1.4).
        Assert.Contains("base(Release1NativePresentationSupport.NellNpcId, \"Nell\", \"Grey\", null)", nellNpc, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Eleanor\"", nellNpc, StringComparison.Ordinal);
    }

    [Fact]
    public void Chief_portrait_borrows_an_icon_and_otherwise_keeps_the_default()
    {
        // Release1ChiefCampbellNpc.cs is S1API/MelonLoader-dependent and not linked into the test
        // project, so this reads the source, like the Nell portrait test above. The avatar-render
        // fallback is gone on purpose: 0.4.7 removed Avatar.CurrentSettings, Avatar.GetMugshot, and
        // the old MugshotGenerator, and the owner chose to drop the fallback rather than rebuild it.
        // A shipped static portrait is tracked as OC-78; until then the borrow is the only source,
        // and when the NPC has no icon the Chief silently keeps S1API's default.
        var root = FindRepositoryRoot();
        var chiefNpc = File.ReadAllText(Path.Combine(root, "tools", "OrganizedCrime", "Runtime", "Release1ChiefCampbellNpc.cs"));

        var tryStart = chiefNpc.IndexOf("private void TryApplyPortrait()", StringComparison.Ordinal);
        Assert.True(tryStart >= 0, "TryApplyPortrait was not found.");
        var tryBody = chiefNpc.Substring(tryStart);
        var borrowIndex = tryBody.IndexOf("NPC.Get(npcId)", StringComparison.Ordinal);
        var catchIndex = tryBody.IndexOf("catch (Exception exception)", StringComparison.Ordinal);
        Assert.True(borrowIndex >= 0 && catchIndex > borrowIndex,
            "the borrow must be inside TryApplyPortrait's try block.");

        Assert.Contains("Icon = source.Icon;", chiefNpc, StringComparison.Ordinal);
        Assert.Contains("Chief Campbell portrait: no icon found on '{npcId}'; keeping the default.", chiefNpc, StringComparison.Ordinal);

        Assert.DoesNotContain("GetMugshot(", chiefNpc, StringComparison.Ordinal);
        Assert.DoesNotContain("CurrentSettings", chiefNpc, StringComparison.Ordinal);
        Assert.DoesNotContain("TryRenderPortraitFromAvatar", chiefNpc, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplyRenderedPortrait", chiefNpc, StringComparison.Ordinal);
        Assert.DoesNotContain("_mugshotCallback", chiefNpc, StringComparison.Ordinal);
    }

    [Fact]
    public void Nell_portrait_defaults_to_lily_turner_and_the_preference_still_overrides_when_non_empty()
    {
        var root = FindRepositoryRoot();
        var nellNpc = File.ReadAllText(Path.Combine(root, "tools", "OrganizedCrime", "Runtime", "Release1NellNpc.cs"));

        Assert.Contains("\"lily_turner\"", nellNpc, StringComparison.Ordinal);
        // The early return that used to skip the borrow entirely on an empty preference is gone: the
        // preference is now an override on top of a fixed default, never a skip switch.
        Assert.DoesNotContain("if (string.IsNullOrEmpty(npcId)) return;", nellNpc, StringComparison.Ordinal);

        var tryStart = nellNpc.IndexOf("private void TryApplyPortrait()", StringComparison.Ordinal);
        Assert.True(tryStart >= 0, "TryApplyPortrait was not found.");
        var tryBody = nellNpc.Substring(tryStart);
        var readIndex = tryBody.IndexOf("Release1NellPortraitNpcIdPreference.Read()", StringComparison.Ordinal);
        var defaultIndex = tryBody.IndexOf("lily_turner", StringComparison.Ordinal);
        var borrowIndex = tryBody.IndexOf("NPC.Get(npcId)", StringComparison.Ordinal);
        Assert.True(readIndex >= 0 && readIndex < defaultIndex && defaultIndex < borrowIndex,
            "the preference must be read, then defaulted, then used to borrow, in that order.");
    }

    [Fact]
    public void The_condition_window_engine_encodes_no_condition_subject_matter_and_no_owner_key()
    {
        var root = FindRepositoryRoot();
        var runtimeDir = Path.Combine(root, "tools", "OrganizedCrime", "Runtime");
        var files = Directory.EnumerateFiles(runtimeDir, "Release1ConditionWindow*.cs", SearchOption.TopDirectoryOnly)
            .Concat(Directory.EnumerateFiles(Path.Combine(root, "tools", "OrganizedCrime", "Core", "Scheduling"), "GameHourGate.cs", SearchOption.TopDirectoryOnly))
            .ToList();
        Assert.NotEmpty(files);
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("GUI.", text, StringComparison.Ordinal);
            Assert.DoesNotContain("HarmonyPatch", text, StringComparison.Ordinal);
            Assert.DoesNotContain("KeyCode.F", text, StringComparison.Ordinal);
            // Decision 15: OC-65 must be able to replace a condition by swapping a classifier and a read
            // member, so no condition's subject matter may leak into the shared engine.
            foreach (var banned in new[]
                     {
                         "ProductId", "Closet", "DeadDrop", "Fingerprint", "Packag",
                         "KeepTheLightsOff", "RoomWithNoName", "TheEnvelope", "MissionKey"
                     })
                Assert.DoesNotContain(banned, text, StringComparison.Ordinal);
            // Decision 4: the window constant is never duplicated or re-validated inside the engine.
            Assert.DoesNotContain("1_440", text, StringComparison.Ordinal);
            Assert.DoesNotContain("1440", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_quiet_window_is_a_shim_that_owns_no_arithmetic_of_its_own()
    {
        var root = FindRepositoryRoot();
        var text = File.ReadAllText(Path.Combine(root, "tools", "OrganizedCrime", "Runtime", "Release1QuietWindow.cs"));

        Assert.Equal(1, Count(text, "Release1ConditionWindow.Decide("));
        // The window and grace comparisons now live in the engine alone.
        Assert.DoesNotContain(">=", text, StringComparison.Ordinal);
        Assert.DoesNotContain("double.IsFinite", text, StringComparison.Ordinal);
        // Two pinned test files compile against this constant; it must survive the lift.
        Assert.Contains("public const double BreachGraceGameMinutes = 60d;", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_hold_window_is_a_shim_that_owns_no_arithmetic_of_its_own()
    {
        var root = FindRepositoryRoot();
        var text = File.ReadAllText(Path.Combine(root, "tools", "OrganizedCrime", "Runtime", "Release1HoldWindow.cs"));

        Assert.Equal(1, Count(text, "Release1ConditionWindow.Decide("));
        Assert.DoesNotContain(">=", text, StringComparison.Ordinal);
        Assert.DoesNotContain("double.IsFinite", text, StringComparison.Ordinal);
        Assert.Contains("public const double MissingGraceGameMinutes = 60d;", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Keep_the_lights_off_owns_exactly_one_convergence_throttle_and_clears_it_at_every_boundary()
    {
        var root = FindRepositoryRoot();
        var runtimeDir = Path.Combine(root, "tools", "OrganizedCrime", "Runtime");
        var text = File.ReadAllText(Path.Combine(runtimeDir, "Release1KeepTheLightsOffMissionService.cs"));

        Assert.Equal(1, Count(text, "new GameHourGate()"));
        Assert.Equal(1, Count(text, "_convergence.TryPass("));
        // Pre load, load complete, save start, save complete, and an accepted decision.
        Assert.Equal(5, Count(text, "_convergence.Clear()"));
        Assert.Equal(0, Count(text, "_lastConvergenceGameMinute"));
        Assert.Equal(0, Count(text, "_lastConvergenceRevision"));

        // OC-64 scopes The Envelope's own duplicate out. It keeps its private fields and its own
        // method; this asserts the extraction did not silently reach into it.
        var envelope = File.ReadAllText(Path.Combine(runtimeDir, "Release1TheEnvelopeMissionService.cs"));
        Assert.Equal(0, Count(envelope, "GameHourGate"));
        Assert.True(Count(envelope, "_lastConvergenceGameMinute") > 0);

        // Room With No Name stays unthrottled: its Converge also runs staging and delivery, which react
        // to player deposits rather than to game minutes.
        var room = File.ReadAllText(Path.Combine(runtimeDir, "Release1RoomWithNoNameMissionService.cs"));
        Assert.Equal(0, Count(room, "GameHourGate"));
        Assert.Equal(0, Count(room, "TryBeginConvergencePass"));
    }

    [Fact]
    public void The_window_engine_lift_adds_no_composition_child_and_no_presentation_change()
    {
        var root = FindRepositoryRoot();
        var runtimeDir = Path.Combine(root, "tools", "OrganizedCrime", "Runtime");

        var composition = File.ReadAllText(Path.Combine(runtimeDir, "Release1ProductionComposition.cs"));
        Assert.Equal(0, Count(composition, "Release1ConditionWindow"));
        Assert.Equal(0, Count(composition, "GameHourGate"));
        Assert.Equal(0, Count(composition, "Release1WindowCopy"));

        var plan = File.ReadAllText(Path.Combine(runtimeDir, "Release1PresentationPlan.cs"));
        Assert.Equal(0, Count(plan, "Release1ConditionWindow"));
        Assert.Equal(0, Count(plan, "Release1WindowCopy"));

        // The engine has exactly three production callers: the two shims plus Keep the Lights Off's
        // own direct call (Task 4 lifts it onto the shared engine without a shim of its own).
        var callers = 0;
        var qaDir = Path.Combine(runtimeDir, "Qa");
        var scanned = Directory.EnumerateFiles(runtimeDir, "*.cs", SearchOption.TopDirectoryOnly)
            .Concat(Directory.EnumerateFiles(qaDir, "*.cs", SearchOption.TopDirectoryOnly));
        foreach (var file in scanned)
        {
            var name = Path.GetFileName(file);
            if (name == "Release1ConditionWindow.cs") continue;
            callers += Count(File.ReadAllText(file), "Release1ConditionWindow.Decide(");
        }
        Assert.Equal(3, callers); // the two shims plus this mission's direct call
    }

    [Fact]
    public void Keep_the_lights_off_reads_the_shared_engine_directly_and_owns_no_product_or_reward_seam()
    {
        var root = FindRepositoryRoot();
        var runtimeDir = Path.Combine(root, "tools", "OrganizedCrime", "Runtime");
        var service = File.ReadAllText(Path.Combine(runtimeDir, "Release1KeepTheLightsOffMissionService.cs"));

        Assert.Equal(1, Count(service, "Release1ConditionWindow.Decide("));
        Assert.Equal(0, Count(service, "Release1QuietWindow"));
        Assert.Equal(0, Count(service, "Release1QuietCensus"));
        Assert.Equal(0, Count(service, "TryChangeCashBalance"));
        Assert.Equal(0, Count(service, "Release1NativeEffectJournalEntry"));
        Assert.Equal(0, Count(service, "TryReadProducts"));
        Assert.Equal(0, Count(service, "TryReadDeadDrops"));
        Assert.Equal(1, Count(service, "TryRestoreKeepTheLightsOffAssignment("));
        // Five OC-63 boundary sites plus acceptance all arm the next pass as a baseline pass.
        Assert.Equal(6, Count(service, "_baselinePassPending = true;"));
        Assert.Equal(5, Count(service, "_convergence.Clear()"));
    }

    [Fact]
    public void The_production_census_seam_is_read_only_and_owns_exactly_one_owner_qa_key()
    {
        var root = FindRepositoryRoot();
        var runtimeDir = Path.Combine(root, "tools", "OrganizedCrime", "Runtime");
        var modelDir = Path.Combine(root, "tools", "OrganizedCrime", "Model");

        var harness = File.ReadAllText(Path.Combine(runtimeDir, "Qa", "Release1KeepTheLightsOffProductionGateHarness.cs"));
        foreach (var banned in new[] { "UnityEngine", "S1API", "Il2Cpp", "GUI.", "HarmonyPatch", "KeyCode" })
            Assert.DoesNotContain(banned, harness, StringComparison.Ordinal);
        Assert.Equal(1, Count(harness, "world.TryReadProductionActivity("));

        var contracts = File.ReadAllText(Path.Combine(modelDir, "Release1ProductionActivityContracts.cs"));
        // "MushroomBed" is deliberately not in this banned list: decision 5's own Home diagnostic list
        // (WorkBehaviourTypeNames) is required, by Release1ProductionActivityContractTests, to carry
        // MistMushroomBedBehaviour, HarvestMushroomBedBehaviour and ApplySpawnToMushroomBedBehaviour
        // verbatim, since Botanist's mushroom-bed actions are legitimate employee work, not a grow
        // container read. The actual boundary this row protects, never resolving a MushroomBed entity's
        // own growth state, is asserted below against Il2CppScheduleOne.Growing in the access layer.
        foreach (var banned in new[] { "UnityEngine", "S1API", "Il2Cpp", "Customer", "Dealer" })
            Assert.DoesNotContain(banned, contracts, StringComparison.Ordinal);

        // The IL2CPP read is the only place a property, an employee or a station is resolved, and it never
        // writes: no setter, no Mark, no Fire, no Assign anywhere in the new members, and no Growing type.
        var access = File.ReadAllText(Path.Combine(runtimeDir, "S1ApiRelease1SmallCourtesyWorld.cs"));
        Assert.Equal(1, Count(access, "private static IEnumerable<NativeProperty> OwnedProperties()"));
        foreach (var banned in new[] { "MarkIsWorking", "SetIsWorking", "AssignProperty", "Il2CppScheduleOne.Growing" })
            Assert.DoesNotContain(banned, access, StringComparison.Ordinal);

        var qaDir = Path.Combine(runtimeDir, "Qa");
        var readers = Directory.EnumerateFiles(runtimeDir, "*.cs", SearchOption.TopDirectoryOnly)
            .Concat(Directory.EnumerateFiles(qaDir, "*.cs", SearchOption.TopDirectoryOnly))
            .Sum(file => Count(File.ReadAllText(file), "TryReadProductionActivity(out"));
        Assert.True(readers >= 3, "the boundary interface, the facade and the access layer all declare the read");

        // OC-151: the keys moved to OwnerQaKeys; the exact 22-key map pins Home once,
        // every kept F-key once, and F7/F12 absent (F3/F4/F5 went with the OC-150 spike).
        CompositionAssertions.AssertKeyMap(new OcCompositionRoot(new FakeOcEngineAdapters(), _ => { }, _ => { }).QaKeys.Bindings);
    }

    [Fact]
    public void The_production_classifier_is_pure_and_names_no_native_type()
    {
        var root = FindRepositoryRoot();
        var text = File.ReadAllText(Path.Combine(root, "tools", "OrganizedCrime", "Runtime",
            "Release1ProductionActivityClassifier.cs"));

        foreach (var banned in new[] { "Il2Cpp", "UnityEngine", "S1API", "_world", "_story", "DateTime", "KeyCode" })
            Assert.DoesNotContain(banned, text, StringComparison.Ordinal);
        Assert.Equal(1, Count(text, "ToWindowObservation"));
        // The engine's own vocabulary appears in exactly one method and nowhere else in this mission.
        Assert.Equal(3, Count(text, "Release1WindowObservation."));
    }

    [Fact]
    public void The_mod_project_defines_the_spike_symbol_only_for_debug()
    {
        var root = FindRepositoryRoot();
        var csproj = File.ReadAllText(Path.Combine(root, "tools", "OrganizedCrime", "OrganizedCrime.csproj"));
        Assert.Contains(
            "<DefineConstants Condition=\"'$(Configuration)'=='Debug'\">$(DefineConstants);OC_OWNER_SPIKES</DefineConstants>",
            csproj, StringComparison.Ordinal);
        // Guards against a second, unconditional definition slipping in beside the conditional one.
        Assert.Equal(1, Count(csproj, "OC_OWNER_SPIKES"));
    }

    [Fact]
    public void The_test_project_always_defines_the_spike_symbol()
    {
        var root = FindRepositoryRoot();
        var csproj = File.ReadAllText(Path.Combine(root, "tests", "OrganizedCrime.Tests", "OrganizedCrime.Tests.csproj"));
        Assert.Contains("<DefineConstants>$(DefineConstants);OC_OWNER_SPIKES</DefineConstants>", csproj, StringComparison.Ordinal);
    }

    [Fact]
    public void Mod_wires_timing_receipts_into_the_hq_door_observer_and_the_hq_storage_boundary()
    {
        // OC-151: the wiring moved into HqModule, still construction-local (both values stay
        // locals, unreachable post-build), so the same initializer pins follow it there.
        var root = FindRepositoryRoot();
        var module = File.ReadAllText(Path.Combine(root, "tools", "OrganizedCrime", "Runtime", "Composition", "HqModule.cs"));
        Assert.Contains("new SyndicateHqNativeStorageBoundary { Timing = root.Timing }", module, StringComparison.Ordinal);
        // Final review fix (carried over): pin the door observer's own object initializer by
        // proximity to its own constructor call, so the other Timing assignments in the module
        // cannot mask a deleted observer wiring.
        var observerIndex = module.IndexOf("new SyndicateHqDoorObserver(", StringComparison.Ordinal);
        Assert.True(observerIndex >= 0, "SyndicateHqDoorObserver construction was not found.");
        var observerTimingIndex = module.IndexOf("Timing = root.Timing", observerIndex, StringComparison.Ordinal);
        Assert.True(observerTimingIndex >= 0 && observerTimingIndex - observerIndex < 400,
            "expected the door observer's own object initializer to set Timing = root.Timing.");
    }

    [Fact]
    public void MelonInfo_declares_the_release_1_version_stamp()
    {
        var root = FindRepositoryRoot();
        var mod = File.ReadAllText(Path.Combine(root, "tools", "OrganizedCrime", "Mod.cs"));
        var attributeStart = mod.IndexOf("[assembly: MelonInfo(", StringComparison.Ordinal);
        var attributeEnd = mod.IndexOf(")]", attributeStart, StringComparison.Ordinal);
        Assert.True(attributeStart >= 0 && attributeEnd > attributeStart, "MelonInfo attribute was not found.");
        var attributeText = mod[attributeStart..attributeEnd];
        Assert.Contains("\"2.0.0\"", attributeText, StringComparison.Ordinal);
        Assert.Contains("\"Organized Crime\"", attributeText, StringComparison.Ordinal);
        Assert.Contains("\"MadJag Studios\"", attributeText, StringComparison.Ordinal);
    }

    private static int Count(string text, string value)
    {
        var count = 0;
        var offset = 0;
        while ((offset = text.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += value.Length;
        }
        return count;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "tools", "OrganizedCrime", "OrganizedCrime.csproj")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("repository root not found");
    }
}
