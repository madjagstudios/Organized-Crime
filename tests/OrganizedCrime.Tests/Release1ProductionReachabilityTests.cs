using System.Security.Cryptography;
using OrganizedCrime.Runtime;
using S1API.PhoneCalls;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Release1ProductionReachabilityTests
{
    // Recorded by SHA-256 straight off tools/OrganizedCrime/Runtime/Release1WrongAddressStagingHarness.cs
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

    // Recorded by SHA-256 straight off tools/OrganizedCrime/Runtime/Release1ShortNoticeQuantityHarness.cs
    // at commit 739a88b (its last owning commit, "widen the slot decrement and prove it with a
    // quantity harness"). Task 7 never touches this file; this guard follows the same byte-identity
    // pattern the Wrong Address staging harness guard above already establishes.
    private const string ShortNoticeQuantityHarness739a88bSha256 =
        "F4C86B448536DBB5AACAC7B83C58FF3343975E8DCDFACCD9FEA150AA721F6523";

    // Recorded by SHA-256 straight off tools/OrganizedCrime/Mod.cs once OC-10 landed. OC-10 routes
    // every discarded Release1StoryRuntimeService lifecycle result (OnPreLoad, OnLoadComplete,
    // OnSaveStart, OnSaveComplete) through Release1StoryLifecycleLogging.LogIfRejected so a
    // quarantine is logged instead of silently dropping the story sidecar. Nothing else in the
    // shell moved, so the file is pinned again here. This guard follows the same byte-identity
    // pattern the harness guards above already establish.
    //
    // Re-recorded for the OC-69 merge onto the release 1 arc: OC-65 (KeyCode.Home, the production
    // gate) and OC-69 (KeyCode.F3, F4 and F5, the field contact report, spawn/despawn and provoke
    // keys, plus the unconditional HandleLoadComplete to
    // S1ApiRelease1FieldContactRuntime.ReconcileAfterLoad() call the OC-69 final review's C1 fix
    // added) both re-pinned this constant to their own branch content; landing both on the same file
    // changes Mod.cs's bytes again, so the constant is re-pinned once more here to the merged
    // content, following the same re-pin-on-intentional-change pattern every other byte-identity
    // guard in this file already uses.
    //
    // Re-recorded again for the OC-65/OC-69 final review fix: the HandleLoadComplete field contact
    // reconcile call is rerouted from the native runtime (S1ApiRelease1FieldContactRuntime.
    // ReconcileAfterLoad, a raw mutation with no world boundary member and no HasAuthority guard)
    // through Release1ArthurFieldContactHarness.TryReconcileAfterLoad(_release1SmallCourtesyWorld),
    // the same TryReadFieldContact/TryDespawnFieldContact member family the F4 despawn press already
    // uses, so the load reconcile carries the same HasAuthority guard every other field contact seam
    // has. Mod.cs's bytes change again, so the constant is re-pinned once more, same convention.
    //
    // Re-recorded once more for the live-defect fix (2026-09-05): the one-shot TryReconcileAfterLoad
    // call at HandleLoadComplete is replaced by arming Release1ArthurLoadReconcile there and pumping
    // it from OnUpdate every pass, unconditionally, so a load-complete read fault gets retried across
    // update passes instead of giving up for the rest of the session. Mod.cs's bytes change again, so
    // the constant is re-pinned once more, same convention.
    //
    // Re-recorded once more for the wall-clock retry fix (2026-09-05): the bounded-passes retry above
    // burned all thirty of its passes in under half a second, the wrong unit for how long the native
    // load time sweep needs before a freshly registered contact reads back cleanly. The reconcile
    // field's construction now passes a realtimeSinceStartup delegate, and OnUpdate's own translation
    // of a reconcile step into a log line grew a case for the new throttled-once read-failure report.
    // Mod.cs's bytes change again, so the constant is re-pinned once more, same convention.
    //
    // Re-recorded once more for the F4 presence fix (2026-09-05): live evidence showed F4's own
    // alternation flag defaulted to "spawn next" regardless of whether a contact already existed, so
    // a load constructed contact still present at the first press hit TrySpawn's own already-spawned
    // refusal instead of ever being despawned. The F4 handler now calls
    // Release1ArthurFieldContactHarness.TryToggle(_release1SmallCourtesyWorld, ref
    // _arthurFieldContactSpawnedByHarness), which reads presence first and decides from that read, and
    // the renamed field tracks only whether the harness itself put the current contact there, for the
    // despawn's own wording, not which action to take. Mod.cs's bytes change again, so the constant is
    // re-pinned once more, same convention.
    //
    // Re-recorded once more for the spawn-retry and despawn-by-handle fix (2026-09-05): live evidence
    // showed a freshly spawned contact has the same not-ready read window a load constructed one
    // already has, so every read taken right after a spawn or a provoke could fault, including
    // TryToggle's own decision read; a decision read that faulted right after this harness's own spawn
    // wrongly read absent and sent the next F4 press into TrySpawn's own already-spawned refusal,
    // stranding the contact with no keyboard path to remove it. Mod.cs now owns three
    // Release1ArthurFieldContactReadyPump fields (spawn follow-up, provoke, and the F4 decide retry),
    // F4 despawns by handle straight away when the harness already holds the contact, and F5 retries
    // the read before provoking instead of provoking blind. Mod.cs's bytes change again, so the
    // constant is re-pinned once more, same convention.
    //
    // Re-recorded once more for the native-resolve-and-force-visible fix (2026-09-05), precedent cited
    // by name only (a third party mob mod; see docs/research/2026-09-05-oc-69-mob-mod-npc-spawn-seams.md):
    // the on demand F4 spawn no longer positions, activates, or forces the contact visible
    // synchronously. The spawn-follow-up field (a Release1ArthurFieldContactReadyPump) is replaced by
    // _arthurSpawnResolvePump (a Release1ArthurFieldContactSpawnResolvePump), which retries the native
    // resolve and completion on the same wall clock window and despawns by handle if the native NPC
    // never resolves; the F4 handler cancels it instead of the old field on a despawn press, and
    // HandleToggleDecision arms it with no continuation of its own. Mod.cs's bytes change again, so the
    // constant is re-pinned once more, same convention.
    //
    // Re-recorded once more for the parked lifecycle change (2026-09-05, a seventh spec review round):
    // on-demand construction is abandoned; the load reconcile now parks the load constructed contact
    // instead of despawning it, F4 toggles park versus unpark on the renamed _arthurFieldContactParked
    // flag instead of spawn versus despawn, F5 refuses to provoke while parked, and _arthurSpawnResolvePump
    // and its logger are deleted along with the on-demand spawn path they served. Mod.cs's bytes change
    // again, so the constant is re-pinned once more, same convention.
    //
    // Re-recorded once more for OC-73: two new owner QA keys land, End for the lockdown gate harness
    // (Release1LockdownGatePatch.TryEngage/TryRelease) and Insert for the read only NPC registry dump
    // (Release1NpcRegistryDumpHarness.TryDump), plus a Release1LockdownGatePatch.Reset() call in
    // OnDeinitializeMelon so a mod reload can never leave a lockdown stuck engaged. Neither key is an
    // F key, so every existing F key count in this file stays unchanged. Mod.cs's bytes change again,
    // so the constant is re-pinned once more, same convention.
    //
    // Re-recorded once more for OC-70: the OC-69 Arthur spike (the field contact runtime construction,
    // its disposal, the per-frame reconcile pump, and the F3/F4/F5 key handlers) is wrapped in
    // #if OC_OWNER_SPIKES so a Release build excludes it entirely. Mod.cs's bytes change again, so the
    // constant is re-pinned once more, same convention.
    //
    // Re-recorded again for OC-70 Task 3: the HQ door observer and the HQ native storage boundary each
    // now receive the shared _timing seam (Timing = _timing) so their retained-identity resolves show up
    // in the morning log. Mod.cs's bytes change again, so the constant is re-pinned once more, same
    // convention.
    //
    // Re-recorded once more for OC-70 Task 4: the MelonInfo version stamp moves from 0.1.0 to
    // 1.0.0-rc1. Mod.cs's bytes change again, so the constant is re-pinned once more, same convention.
    //
    // Re-recorded once more for OC-73 Task 4: the End key handler no longer calls
    // Release1LockdownGatePatch.TryEngage/TryRelease directly; it is a second caller of the two new
    // world boundary members, S1ApiRelease1SmallCourtesyWorld.TryEngageLockdown/TryReleaseLockdown,
    // and both S1ApiRelease1SmallCourtesyWorld construction sites now pass HarmonyInstance through as
    // the new lockdownHarmony constructor argument. Mod.cs's bytes change again, so the constant is
    // re-pinned once more, same convention.
    //
    // Re-recorded once more for OC-73 Task 5: Chief Campbell's composition is wired in, as a sibling
    // of _release1ProductionComposition, right after _nativeLawResponseComposition (the Chief needs
    // its Local Pressure runtime service). A second S1ApiRelease1NativePresentation instance is built
    // for his own contact id, a new _release1ChiefComposition field is added, and it joins every
    // lifecycle handler (OnUpdate, HandleLoadComplete, HandlePreLoad, HandleSaveStart,
    // HandleSaveComplete, OnDeinitializeMelon) beside _release1ProductionComposition, each inside the
    // same _timing.Measure wrapper where one already wraps the production call, with phase names
    // ending in "/chief". He has no OnPreSceneChange call: Release1ChiefComposition exposes no such
    // method (he has no GUI decision prompt host to close on a scene change, mirroring
    // Release1TheEnvelopeComposition's own six-method lifecycle exactly), so HandlePreSceneChange is
    // unchanged. Mod.cs's bytes change again, so the constant is re-pinned once more, same convention.
    // Re-recorded for the 1.0.0 release: the MelonInfo version stamp moves from 1.0.0-rc1 to 1.0.0.
    // Mod.cs's bytes change again, so the constant is re-pinned once more, same convention.
    //
    private const string ModCsOc10Sha256 =
        "E00E9B66CAE118755591C7C1EECA9A438EE5608FF5362DBEBFBFCDF44AAE6E4E";

    // Recorded straight off the working tree at 66a60ae (the "draft the envelope spec" commit that
    // opens this branch); OC-60's tasks never touch these owner QA harness files, so their content at
    // 66a60ae and their content now must be byte-identical.
    private const string RoomWithNoNameHoldRoomHarness66a60aeSha256 =
        "099D593891029DD2DC61C77E0B7BA2CD9137B0FECEAB76AECDFC5D11F052AE54";

    private const string ShortNoticeQuantityHarness66a60aeSha256 =
        "F4C86B448536DBB5AACAC7B83C58FF3343975E8DCDFACCD9FEA150AA721F6523";

    // Recorded by SHA-256 straight off tools/OrganizedCrime/Runtime/Release1TheEnvelopeCashGateHarness.cs
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
        var roomFiles = Directory.EnumerateFiles(runtimeDir, "Release1RoomWithNoName*.cs", SearchOption.TopDirectoryOnly);
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
        var shortNoticeFiles = Directory.EnumerateFiles(runtimeDir, "Release1ShortNotice*.cs", SearchOption.TopDirectoryOnly);
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
            .Concat(Directory.EnumerateFiles(runtimeDir, "Release1QuietWindow.cs", SearchOption.TopDirectoryOnly));
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
        foreach (var file in Directory.EnumerateFiles(runtimeDir, "*.cs", SearchOption.TopDirectoryOnly))
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
        foreach (var file in Directory.EnumerateFiles(runtimeDir, "*.cs", SearchOption.TopDirectoryOnly))
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
        var bytes = ReadLineEndingNormalizedBytes(Path.Combine(root, "tools", "OrganizedCrime", "Runtime", "Release1TheEnvelopeCashGateHarness.cs"));
        Assert.Equal(EnvelopeCashGateHarness4f95ad3Sha256, Convert.ToHexString(SHA256.HashData(bytes)), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The pinned hashes are over LF content, which is what git stores for these files and what every
    /// clone outside a CRLF-converting Windows checkout sees. Normalising here keeps the pins guarding
    /// harness drift without also guarding the line-ending convention of whoever runs the tests.
    /// </summary>
    private static byte[] ReadLineEndingNormalizedBytes(string path)
    {
        var bytes = File.ReadAllBytes(path);
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
        var bytes = ReadLineEndingNormalizedBytes(Path.Combine(root, "tools", "OrganizedCrime", "Runtime", "Release1WrongAddressStagingHarness.cs"));
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        Assert.Equal(WrongAddressStagingHarnessAe43a07Sha256, hash, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Short_notice_quantity_harness_is_unchanged_since_its_last_owning_commit()
    {
        var root = FindRepositoryRoot();
        var bytes = ReadLineEndingNormalizedBytes(Path.Combine(root, "tools", "OrganizedCrime", "Runtime", "Release1ShortNoticeQuantityHarness.cs"));
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        Assert.Equal(ShortNoticeQuantityHarness739a88bSha256, hash, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Mod_is_byte_identical_to_its_oc_10_content()
    {
        var root = FindRepositoryRoot();
        var bytes = ReadLineEndingNormalizedBytes(Path.Combine(root, "tools", "OrganizedCrime", "Mod.cs"));
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        Assert.Equal(ModCsOc10Sha256, hash, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Every_owner_qa_harness_file_is_still_byte_identical_to_its_content_at_66a60ae()
    {
        var root = FindRepositoryRoot();
        var runtimeDir = Path.Combine(root, "tools", "OrganizedCrime", "Runtime");

        var wrongAddressBytes = ReadLineEndingNormalizedBytes(Path.Combine(runtimeDir, "Release1WrongAddressStagingHarness.cs"));
        Assert.Equal(
            WrongAddressStagingHarnessAe43a07Sha256,
            Convert.ToHexString(SHA256.HashData(wrongAddressBytes)),
            StringComparer.OrdinalIgnoreCase);

        var roomBytes = ReadLineEndingNormalizedBytes(Path.Combine(runtimeDir, "Release1RoomWithNoNameHoldRoomHarness.cs"));
        Assert.Equal(
            RoomWithNoNameHoldRoomHarness66a60aeSha256,
            Convert.ToHexString(SHA256.HashData(roomBytes)),
            StringComparer.OrdinalIgnoreCase);

        var shortNoticeBytes = ReadLineEndingNormalizedBytes(Path.Combine(runtimeDir, "Release1ShortNoticeQuantityHarness.cs"));
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
        var envelopeFiles = Directory.EnumerateFiles(runtimeDir, "Release1TheEnvelope*.cs", SearchOption.TopDirectoryOnly);
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

    [Fact]
    public void Mod_has_one_new_production_F_key_and_no_F12()
    {
        var root = FindRepositoryRoot();
        var text = File.ReadAllText(Path.Combine(root, "tools", "OrganizedCrime", "Mod.cs"));
        foreach (var key in new[] { "KeyCode.F1)", "KeyCode.F2)", "KeyCode.F6)", "KeyCode.F8)", "KeyCode.F9)", "KeyCode.F10)", "KeyCode.F11)" })
            Assert.Equal(1, Count(text, key));
        Assert.DoesNotContain("KeyCode.F12", text, StringComparison.Ordinal);
    }

    private static readonly string[] FieldContactMembers =
        {
            "TryParkFieldContact", "TryUnparkFieldContact", "TryDespawnFieldContact", "TryProvokeFieldContact",
            "TryReadFieldContact"
        };

    [Fact]
    public void Mod_has_three_new_owner_qa_keys_for_the_field_contact_spike_and_still_no_f7_or_f12()
    {
        var root = FindRepositoryRoot();
        var mod = File.ReadAllText(Path.Combine(root, "tools", "OrganizedCrime", "Mod.cs"));

        foreach (var n in new[] { 1, 2, 3, 4, 5, 6, 8, 9, 10, 11 })
            Assert.Equal(1, Count(mod, $"KeyCode.F{n})"));
        Assert.DoesNotContain("KeyCode.F7", mod, StringComparison.Ordinal);
        Assert.DoesNotContain("KeyCode.F12", mod, StringComparison.Ordinal);

        var onUpdateStart = mod.IndexOf("public override void OnUpdate()", StringComparison.Ordinal);
        var onUpdateEnd = mod.IndexOf("public override void OnGUI()", onUpdateStart, StringComparison.Ordinal);
        var body = mod.Substring(onUpdateStart, onUpdateEnd - onUpdateStart);
        var gateIndex = body.IndexOf("if (_ownerQaKeysEnabled)", StringComparison.Ordinal);
        Assert.True(gateIndex >= 0, "owner QA keys gate was not found in OnUpdate.");
        foreach (var n in new[] { 3, 4, 5 })
        {
            var handler = $"if (Input.GetKeyDown(KeyCode.F{n}) && _release1SmallCourtesyWorld is not null)";
            Assert.Equal(1, Count(body, handler));
            Assert.True(body.IndexOf(handler, StringComparison.Ordinal) > gateIndex, handler + " sits outside the gate.");
        }
    }

    [Fact]
    public void F5_refuses_to_provoke_while_parked_before_any_read_or_provoke_call()
    {
        // OC-69 lifecycle change (a seventh spec review round): F5 provokes only when unparked. The
        // parked check must be the very first thing the F5 handler does, ahead of the presence read and
        // the provoke pump retry, so a parked press never reads or provokes at all.
        var root = FindRepositoryRoot();
        var mod = File.ReadAllText(Path.Combine(root, "tools", "OrganizedCrime", "Mod.cs"));

        var f5Start = mod.IndexOf("if (Input.GetKeyDown(KeyCode.F5) && _release1SmallCourtesyWorld is not null)", StringComparison.Ordinal);
        Assert.True(f5Start >= 0, "F5 handler was not found.");
        var f5End = mod.IndexOf("if (Input.GetKeyDown(KeyCode.F", f5Start + 1, StringComparison.Ordinal);
        if (f5End < 0) f5End = mod.IndexOf("private static void LogF4Result(", f5Start, StringComparison.Ordinal);
        Assert.True(f5End > f5Start, "F5 handler body could not be sliced.");
        var f5Body = mod[f5Start..f5End];

        var parkedCheckIndex = f5Body.IndexOf("if (_arthurFieldContactParked)", StringComparison.Ordinal);
        var pendingCheckIndex = f5Body.IndexOf("_arthurProvokeFollowUp.IsPending", StringComparison.Ordinal);
        var readIndex = f5Body.IndexOf("TryReadFieldContact(", StringComparison.Ordinal);
        Assert.True(parkedCheckIndex >= 0 && parkedCheckIndex < pendingCheckIndex && pendingCheckIndex < readIndex,
            "the parked check must run before the pending-retry check and the presence read.");
        Assert.Contains("refused; the field contact is parked", f5Body, StringComparison.Ordinal);

        // The parked branch itself must never call TryProvoke or read the contact.
        var parkedBranchEnd = f5Body.IndexOf("else if (_arthurProvokeFollowUp.IsPending)", StringComparison.Ordinal);
        Assert.True(parkedBranchEnd > parkedCheckIndex, "the parked branch could not be sliced.");
        var parkedBranch = f5Body[parkedCheckIndex..parkedBranchEnd];
        Assert.DoesNotContain("TryProvoke", parkedBranch, StringComparison.Ordinal);
        Assert.DoesNotContain("TryReadFieldContact", parkedBranch, StringComparison.Ordinal);
    }

    [Fact]
    public void The_field_contact_harness_holds_no_unity_and_no_s1api_reference()
    {
        var root = FindRepositoryRoot();
        var harness = File.ReadAllText(
            Path.Combine(root, "tools", "OrganizedCrime", "Runtime", "Release1ArthurFieldContactHarness.cs"));

        foreach (var forbidden in new[]
                 { "UnityEngine", "S1API", "Il2Cpp", "MelonLoader", "Vector3", "HarmonyPatch", "GUI.", "RequestGameSave" })
            Assert.DoesNotContain(forbidden, harness, StringComparison.Ordinal);
    }

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
    public void Arthur_npc_uses_the_parameterless_constructor_and_declares_itself_physical()
    {
        var root = FindRepositoryRoot();
        var arthur = File.ReadAllText(Path.Combine(root, "tools", "OrganizedCrime", "Runtime", "Release1ArthurNpc.cs"));

        Assert.Contains("public sealed class Release1ArthurNpc : NPC", arthur, StringComparison.Ordinal);
        Assert.Contains("public override bool IsPhysical => true;", arthur, StringComparison.Ordinal);
        Assert.Contains("protected override void ConfigurePrefab(NPCPrefabBuilder builder)", arthur, StringComparison.Ordinal);
        Assert.Contains("Release1ArthurFieldContactHarness.ContactId", arthur, StringComparison.Ordinal);
        Assert.Contains("\"Arthur\", \"Selby\"", arthur, StringComparison.Ordinal);

        // WithSpawnPosition writes a per type static dictionary with no instance in scope, so the
        // builder can never place a contact beside the player at press time. Nell's contact identity
        // is load bearing for four shipped missions and is not reused here.
        foreach (var forbidden in new[] { "WithSpawnPosition", "Release1NellNpc", "oc_release1_nell" })
            Assert.DoesNotContain(forbidden, arthur, StringComparison.Ordinal);
    }

    [Fact]
    public void The_field_contact_runtime_never_saves_never_damages_and_never_writes_aggression_tuning()
    {
        var root = FindRepositoryRoot();
        var runtime = File.ReadAllText(
            Path.Combine(root, "tools", "OrganizedCrime", "Runtime", "S1ApiRelease1FieldContactRuntime.cs"));

        // OC-69 lifecycle change (a seventh spec review round): on-demand construction is deleted along
        // with the fix built to patch around its own two defects, the avatar donor clone and the health
        // revive call. ".Revive(" is back on this forbidden list with no exception now: the parked
        // lifecycle only ever parks and unparks a contact S1API's own load-time sweep already brought
        // up alive, so nothing in this file ever needs to revive anything again.
        foreach (var forbidden in new[]
                 {
                     "RequestGameSave", ".Damage(", ".Kill(", ".KnockOut(", ".Heal(", ".Revive(", ".RestoreHealth(",
                     "Aggressiveness =", "GiveUpRange =", "GiveUpTime =",
                     "DefaultWeaponAssetPath =", "SetCurrentWeapon", "HarmonyPatch", "GUI."
                 })
            Assert.DoesNotContain(forbidden, runtime, StringComparison.Ordinal);
    }

    [Fact]
    public void The_on_demand_construct_path_and_its_avatar_donor_and_revive_patch_are_fully_removed()
    {
        // OC-69 lifecycle change, 2026-09-05 (a seventh spec review round): on-demand construction is
        // abandoned, so every member that path or its own avatar-donor/revive patch introduced must be
        // gone from the runtime, and the donor preference file itself must no longer exist at all.
        var root = FindRepositoryRoot();
        var runtimeDir = Path.Combine(root, "tools", "OrganizedCrime", "Runtime");
        var runtime = File.ReadAllText(Path.Combine(runtimeDir, "S1ApiRelease1FieldContactRuntime.cs"));

        foreach (var removed in new[]
                 {
                     "TrySpawnFieldContact", "TryApproachFieldContact", "TryCompleteFieldContactSpawn",
                     "ApplyDonorAvatarAppearance", "ReviveContactToFullHealth", "ArthurAppearanceNpcIdPreference",
                     "TryGetAvatarSettings", "NPCHealth", "_pendingSpawnContactId", "_pendingSpawnPoint",
                     "_pendingSpawnPlayerPosition", "AvatarSettings"
                 })
            Assert.DoesNotContain(removed, runtime, StringComparison.Ordinal);

        Assert.False(File.Exists(Path.Combine(runtimeDir, "ArthurAppearanceNpcIdPreference.cs")));
        Assert.False(File.Exists(Path.Combine(runtimeDir, "Release1ArthurFieldContactSpawnResolvePump.cs")));
    }

    [Fact]
    public void The_provoke_path_uses_the_one_s1api_combat_member_and_never_the_native_one()
    {
        var root = FindRepositoryRoot();
        var runtime = File.ReadAllText(
            Path.Combine(root, "tools", "OrganizedCrime", "Runtime", "S1ApiRelease1FieldContactRuntime.cs"));

        Assert.Equal(1, Count(runtime, "SetAndAttackTarget(player)"));
        foreach (var forbidden in new[]
                 {
                     "Il2CppScheduleOne.Combat", "SetTargetAndEnable_Server", "SetTarget(",
                     ".Attack()", "EndCombat", "ReadyToAttack",
                     "MorePatrols", "PoliceStation", "Dispatch(", "LocalPressure", "WantedLevel"
                 })
            Assert.DoesNotContain(forbidden, runtime, StringComparison.Ordinal);

        Assert.DoesNotContain("the field contact provoke is not implemented yet", runtime, StringComparison.Ordinal);
    }

    [Fact]
    public void The_park_and_unpark_paths_each_force_visible_exactly_once_on_the_runtime_paths()
    {
        // OC-69 lifecycle change, 2026-09-05 (a seventh spec review round): parking forces the native
        // NPC invisible exactly once, and unparking forces it visible exactly once; the abandoned
        // on-demand spawn path used to be the only place SetVisible(true, ...) ran at all.
        var root = FindRepositoryRoot();
        var runtime = File.ReadAllText(
            Path.Combine(root, "tools", "OrganizedCrime", "Runtime", "S1ApiRelease1FieldContactRuntime.cs"));

        Assert.Equal(1, Count(runtime, "SetVisible(true, networked: true)"));
        Assert.Equal(1, Count(runtime, "SetVisible(false, networked: true)"));
        Assert.Equal(2, Count(runtime, "SetVisible("));
        Assert.Contains("GetComponent<NativeNpc>()", runtime, StringComparison.Ordinal);
        Assert.Contains("NavMesh.SamplePosition(", runtime, StringComparison.Ordinal);
        Assert.Contains("Release1ArthurFieldContactHarness.ParkingPoint", runtime, StringComparison.Ordinal);

        // TryParkFieldContact itself must never touch CanGetTo, Goto, or the navmesh: those are
        // TryUnparkFieldContact's own concerns. The slice stops at the next method.
        var parkStart = runtime.IndexOf("public Release1SmallCourtesyWorldMutationStatus TryParkFieldContact(", StringComparison.Ordinal);
        var unparkStart = runtime.IndexOf("public Release1SmallCourtesyWorldMutationStatus TryUnparkFieldContact(", StringComparison.Ordinal);
        Assert.True(parkStart >= 0 && unparkStart > parkStart, "expected TryParkFieldContact before TryUnparkFieldContact.");
        var parkBody = runtime[parkStart..unparkStart];
        foreach (var forbidden in new[] { ".CanGetTo(", ".Goto(", "SetActive(true)", "NavMesh.SamplePosition(" })
            Assert.DoesNotContain(forbidden, parkBody, StringComparison.Ordinal);

        // TryUnparkFieldContact itself must never deactivate or move the contact to the parking point:
        // those are TryParkFieldContact's own concerns. The slice runs to the next method after it.
        var despawnStart = runtime.IndexOf("public Release1SmallCourtesyWorldMutationStatus TryDespawnFieldContact(", StringComparison.Ordinal);
        Assert.True(despawnStart > unparkStart, "expected TryUnparkFieldContact before TryDespawnFieldContact.");
        var unparkBody = runtime[unparkStart..despawnStart];
        foreach (var forbidden in new[] { "SetActive(false)", "ParkingPoint" })
            Assert.DoesNotContain(forbidden, unparkBody, StringComparison.Ordinal);

        // The exact sequence the seventh review round's own owner protocol names: one navmesh snap,
        // one position set, one visible-force, and one Goto, all inside TryUnparkFieldContact.
        Assert.Equal(1, Count(unparkBody, "NavMesh.SamplePosition("));
        Assert.Equal(1, Count(unparkBody, "contact.Position ="));
        Assert.Equal(1, Count(unparkBody, "SetActive(true)"));
        Assert.Equal(1, Count(unparkBody, "SetVisible(true, networked: true)"));
        Assert.Equal(1, Count(unparkBody, ".Goto("));
    }

    [Fact]
    public void Exactly_one_production_site_constructs_the_field_contact_runtime_and_the_arthur_npc()
    {
        var root = FindRepositoryRoot();
        Assert.Equal(1, CountAcrossProductionSources(root, "new S1ApiRelease1FieldContactRuntime("));
        Assert.Equal(1, CountAcrossProductionSources(root, "new Release1ArthurNpc("));
        var mod = File.ReadAllText(Path.Combine(root, "tools", "OrganizedCrime", "Mod.cs"));
        Assert.Equal(1, Count(mod, "new S1ApiRelease1SmallCourtesyWorld(hqContext, hqStorageRuntime, fieldContactRuntime, HarmonyInstance)"));
    }

    [Fact]
    public void Field_contact_load_reconcile_pumps_every_update_pass_unconditionally_not_behind_the_owner_key_gate()
    {
        var root = FindRepositoryRoot();
        var runtime = File.ReadAllText(
            Path.Combine(root, "tools", "OrganizedCrime", "Runtime", "S1ApiRelease1FieldContactRuntime.cs"));
        // Final review correction, still true after this fix: the native runtime exposes no
        // load-reconcile entry point of its own. Reconciliation goes through the world boundary
        // instead (asserted below), so the raw runtime is never called directly from Mod.cs and
        // never needs its own HasAuthority check duplicated here.
        Assert.DoesNotContain("ReconcileAfterLoad", runtime, StringComparison.Ordinal);

        var harness = File.ReadAllText(
            Path.Combine(root, "tools", "OrganizedCrime", "Runtime", "Release1ArthurFieldContactHarness.cs"));
        // Fix for a live defect (2026-09-05): the one-shot TryReconcileAfterLoad method that used to
        // live here gave up for the rest of the session after one faulted read. It is gone; the
        // bounded, pass-retrying replacement lives in Release1ArthurLoadReconcile instead.
        Assert.DoesNotContain("TryReconcileAfterLoad", harness, StringComparison.Ordinal);

        var reconcile = File.ReadAllText(
            Path.Combine(root, "tools", "OrganizedCrime", "Runtime", "Release1ArthurLoadReconcile.cs"));
        // The reconcile path is pure world-boundary reads and mutations, the same member family the
        // F4 despawn press already uses (TryReadFieldContact, then TryDespawnFieldContact via
        // TryDespawn), each of which carries S1ApiRelease1SmallCourtesyWorld's own HasAuthority()
        // guard; it never reaches past the world boundary into the native runtime type by name, and
        // it stays free of Unity and S1API references so it stays test linkable.
        foreach (var forbidden in new[] { "UnityEngine", "S1API", "Il2Cpp", "MelonLoader", "S1ApiRelease1FieldContactRuntime" })
            Assert.DoesNotContain(forbidden, reconcile, StringComparison.Ordinal);

        var mod = File.ReadAllText(Path.Combine(root, "tools", "OrganizedCrime", "Mod.cs"));
        const string arm = "_arthurLoadReconcile.BeginAfterLoad();";
        const string pump = "_arthurLoadReconcile.Pump(_release1SmallCourtesyWorld);";
        Assert.Equal(1, Count(mod, arm));
        Assert.Equal(1, Count(mod, pump));

        var handlerStart = mod.IndexOf("private void HandleLoadComplete()", StringComparison.Ordinal);
        Assert.True(handlerStart >= 0, "HandleLoadComplete was not found.");
        var handlerEnd = mod.IndexOf("private void HandlePreSceneChange()", handlerStart, StringComparison.Ordinal);
        Assert.True(handlerEnd >= 0, "HandlePreSceneChange was not found after HandleLoadComplete.");
        var handlerBody = mod.Substring(handlerStart, handlerEnd - handlerStart);
        var armIndex = handlerBody.IndexOf(arm, StringComparison.Ordinal);
        Assert.True(armIndex >= 0, "BeginAfterLoad is not called from HandleLoadComplete.");

        // C1 fix, still true after this change: arming must run whether or not owner QA keys are
        // enabled, because S1API's own load-time sweep constructs Release1ArthurNpc regardless of
        // that preference. The gate check inside HandleLoadComplete (guarding the unrelated OC-52
        // staging dump) sits after the call, so finding the gate string at all after armIndex would
        // mean the call moved behind it.
        var loadCompleteGateIndex = handlerBody.IndexOf("if (_ownerQaKeysEnabled", armIndex, StringComparison.Ordinal);
        Assert.True(loadCompleteGateIndex < 0 || loadCompleteGateIndex > armIndex,
            "BeginAfterLoad must not sit behind the owner QA keys gate.");

        var onUpdateStart = mod.IndexOf("public override void OnUpdate()", StringComparison.Ordinal);
        Assert.True(onUpdateStart >= 0, "OnUpdate was not found.");
        var onUpdateGateIndex = mod.IndexOf("if (_ownerQaKeysEnabled)", onUpdateStart, StringComparison.Ordinal);
        Assert.True(onUpdateGateIndex >= 0, "owner QA keys gate was not found in OnUpdate.");
        var pumpIndex = mod.IndexOf(pump, onUpdateStart, StringComparison.Ordinal);
        // Every update pass must pump the reconcile, not just passes where the owner enabled the QA
        // keys, for the same reason arming must not sit behind the gate above.
        Assert.True(pumpIndex >= 0 && pumpIndex < onUpdateGateIndex,
            "Pump must run in OnUpdate before, and so outside, the owner QA keys gate.");
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
    public void Try_insert_packaged_product_has_exactly_three_production_call_sites()
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
        // exactly one call to the mission-facing interface member.
        Assert.Equal(3, sites);
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
        var root = FindRepositoryRoot();
        var mod = File.ReadAllText(Path.Combine(root, "tools", "OrganizedCrime", "Mod.cs"));

        Assert.Contains("Release1PresentationModePreference.Read()", mod, StringComparison.Ordinal);
        Assert.Contains("mode: release1PresentationMode", mod, StringComparison.Ordinal);
        Assert.Contains("native: release1Native", mod, StringComparison.Ordinal);
        Assert.Contains("release1PresentationMode == Release1PresentationMode.Native", mod, StringComparison.Ordinal);
        Assert.Contains("new S1ApiRelease1NativePresentation(", mod, StringComparison.Ordinal);
        Assert.Contains("Release1QuestPersistencePolicyPreference.Read()", mod, StringComparison.Ordinal);
        // The downgrade this test previously asserted is gone: Mod.cs no longer falls back to
        // ImguiFallback when the preference says Native.
        Assert.DoesNotContain("is not yet available; falling back", mod, StringComparison.Ordinal);
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
        var root = FindRepositoryRoot();
        var mod = File.ReadAllText(Path.Combine(root, "tools", "OrganizedCrime", "Mod.cs"));

        Assert.Contains("new Release1ProductionComposition(", mod, StringComparison.Ordinal);
        Assert.Contains("new Release1PostBenziesUnlockReader(hqContext, new S1CartelStatusSource())", mod, StringComparison.Ordinal);
        Assert.Contains("new S1ApiRelease1PhoneCallQueue()", mod, StringComparison.Ordinal);
        Assert.Equal(1, Count(mod, "new S1ApiRelease1SmallCourtesyWorld(hqContext, hqStorageRuntime, fieldContactRuntime, HarmonyInstance)"));
        Assert.Contains("_release1ProductionComposition?.OnLoadComplete();", mod, StringComparison.Ordinal);
        Assert.Contains("_release1ProductionComposition?.OnPreLoad();", mod, StringComparison.Ordinal);
        Assert.Contains("_release1ProductionComposition?.OnSaveStart();", mod, StringComparison.Ordinal);
        Assert.Contains("_release1ProductionComposition?.OnSaveComplete();", mod, StringComparison.Ordinal);
        Assert.Contains("_release1ProductionComposition?.Update();", mod, StringComparison.Ordinal);
        Assert.Contains("_release1ProductionComposition?.OnGUI();", mod, StringComparison.Ordinal);
        Assert.Contains("_release1ProductionComposition?.OnPreSceneChange();", mod, StringComparison.Ordinal);
        Assert.Contains("_release1ProductionComposition?.Dispose();", mod, StringComparison.Ordinal);
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
        var root = FindRepositoryRoot();
        var mod = File.ReadAllText(Path.Combine(root, "tools", "OrganizedCrime", "Mod.cs"));

        Assert.Contains("new Release1ChiefComposition(", mod, StringComparison.Ordinal);
        Assert.Contains("_nativeLawResponseComposition.ChiefObserver", mod, StringComparison.Ordinal);
        Assert.Contains("_release1ChiefComposition?.OnLoadComplete();", mod, StringComparison.Ordinal);
        Assert.Contains("_release1ChiefComposition?.OnPreLoad();", mod, StringComparison.Ordinal);
        Assert.Contains("_release1ChiefComposition?.OnSaveStart();", mod, StringComparison.Ordinal);
        Assert.Contains("_release1ChiefComposition?.OnSaveComplete();", mod, StringComparison.Ordinal);
        Assert.Contains("_release1ChiefComposition?.Update();", mod, StringComparison.Ordinal);
        Assert.Contains("_release1ChiefComposition?.Dispose();", mod, StringComparison.Ordinal);
        // No OnPreSceneChange call: Release1ChiefComposition exposes no such method (see the
        // Mod.cs re-pin comment above), so this is not an omission to fix.
        Assert.DoesNotContain("_release1ChiefComposition?.OnPreSceneChange();", mod, StringComparison.Ordinal);

        var composition = File.ReadAllText(Path.Combine(root, "tools", "OrganizedCrime", "Runtime", "Release1ChiefComposition.cs"));
        Assert.Contains("new Release1ChiefService(", composition, StringComparison.Ordinal);

        var lawResponse = File.ReadAllText(Path.Combine(root, "tools", "OrganizedCrime", "Runtime", "NativeLawResponseModComposition.cs"));
        Assert.Contains("new Release1ChiefTierObserver()", lawResponse, StringComparison.Ordinal);
        Assert.Contains("new LocalPressureTierTransitionFanOut(", lawResponse, StringComparison.Ordinal);
    }

    [Fact]
    public void Production_mod_gates_owner_qa_keys_on_a_preference_instead_of_a_debug_build()
    {
        var root = FindRepositoryRoot();
        var mod = File.ReadAllText(Path.Combine(root, "tools", "OrganizedCrime", "Mod.cs"));

        Assert.Contains("KeyCode.F8", mod, StringComparison.Ordinal);
        Assert.DoesNotContain("#if DEBUG", mod, StringComparison.Ordinal);
        Assert.Contains("if (_ownerQaKeysEnabled)", mod, StringComparison.Ordinal);
        Assert.Contains("OwnerQaKeysPreference.Read()", mod, StringComparison.Ordinal);

        var onUpdateStart = mod.IndexOf("public override void OnUpdate()", StringComparison.Ordinal);
        var onUpdateEnd = mod.IndexOf("public override void OnGUI()", onUpdateStart, StringComparison.Ordinal);
        Assert.True(onUpdateStart >= 0 && onUpdateEnd > onUpdateStart, "OnUpdate method body was not found.");
        var onUpdateBody = mod.Substring(onUpdateStart, onUpdateEnd - onUpdateStart);

        var gateIndex = onUpdateBody.IndexOf("if (_ownerQaKeysEnabled)", StringComparison.Ordinal);
        Assert.True(gateIndex >= 0, "owner QA keys gate was not found in OnUpdate.");
        var backslashIndex = onUpdateBody.IndexOf("KeyCode.Backslash", StringComparison.Ordinal);
        var oIndex = onUpdateBody.IndexOf("KeyCode.O)", StringComparison.Ordinal);
        var pIndex = onUpdateBody.IndexOf("KeyCode.P)", StringComparison.Ordinal);
        var f8Index = onUpdateBody.IndexOf("KeyCode.F8", StringComparison.Ordinal);
        var f9Index = onUpdateBody.IndexOf("KeyCode.F9", StringComparison.Ordinal);
        var f10Index = onUpdateBody.IndexOf("KeyCode.F10", StringComparison.Ordinal);
        Assert.True(backslashIndex > gateIndex, "Backslash handler was not found after the owner QA keys gate.");
        Assert.True(oIndex > gateIndex, "O handler was not found after the owner QA keys gate.");
        Assert.True(pIndex > gateIndex, "P handler was not found after the owner QA keys gate.");
        Assert.True(f8Index > gateIndex, "F8 handler was not found after the owner QA keys gate.");
        Assert.True(f9Index > gateIndex, "F9 handler was not found after the owner QA keys gate.");
        Assert.True(f10Index > gateIndex, "F10 handler was not found after the owner QA keys gate.");

        var initStart = mod.IndexOf("public override void OnInitializeMelon()", StringComparison.Ordinal);
        var initEnd = mod.IndexOf("public override void OnDeinitializeMelon()", initStart, StringComparison.Ordinal);
        Assert.True(initStart >= 0 && initEnd > initStart, "OnInitializeMelon method body was not found.");
        Assert.Contains("OwnerQaKeysPreference.Read()", mod.Substring(initStart, initEnd - initStart), StringComparison.Ordinal);
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
            .Concat(Directory.EnumerateFiles(runtimeDir, "Release1ConvergenceThrottle.cs", SearchOption.TopDirectoryOnly))
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

        Assert.Equal(1, Count(text, "new Release1ConvergenceThrottle()"));
        Assert.Equal(1, Count(text, "_convergence.TryBegin("));
        // Pre load, load complete, save start, save complete, and an accepted decision.
        Assert.Equal(5, Count(text, "_convergence.Clear()"));
        Assert.Equal(0, Count(text, "_lastConvergenceGameMinute"));
        Assert.Equal(0, Count(text, "_lastConvergenceRevision"));

        // OC-64 scopes The Envelope's own duplicate out. It keeps its private fields and its own
        // method; this asserts the extraction did not silently reach into it.
        var envelope = File.ReadAllText(Path.Combine(runtimeDir, "Release1TheEnvelopeMissionService.cs"));
        Assert.Equal(0, Count(envelope, "Release1ConvergenceThrottle"));
        Assert.True(Count(envelope, "_lastConvergenceGameMinute") > 0);

        // Room With No Name stays unthrottled: its Converge also runs staging and delivery, which react
        // to player deposits rather than to game minutes.
        var room = File.ReadAllText(Path.Combine(runtimeDir, "Release1RoomWithNoNameMissionService.cs"));
        Assert.Equal(0, Count(room, "Release1ConvergenceThrottle"));
        Assert.Equal(0, Count(room, "TryBeginConvergencePass"));
    }

    [Fact]
    public void The_window_engine_lift_adds_no_composition_child_and_no_presentation_change()
    {
        var root = FindRepositoryRoot();
        var runtimeDir = Path.Combine(root, "tools", "OrganizedCrime", "Runtime");

        var composition = File.ReadAllText(Path.Combine(runtimeDir, "Release1ProductionComposition.cs"));
        Assert.Equal(0, Count(composition, "Release1ConditionWindow"));
        Assert.Equal(0, Count(composition, "Release1ConvergenceThrottle"));
        Assert.Equal(0, Count(composition, "Release1WindowCopy"));

        var plan = File.ReadAllText(Path.Combine(runtimeDir, "Release1PresentationPlan.cs"));
        Assert.Equal(0, Count(plan, "Release1ConditionWindow"));
        Assert.Equal(0, Count(plan, "Release1WindowCopy"));

        // The engine has exactly three production callers: the two shims plus Keep the Lights Off's
        // own direct call (Task 4 lifts it onto the shared engine without a shim of its own).
        var callers = 0;
        foreach (var file in Directory.EnumerateFiles(runtimeDir, "*.cs", SearchOption.TopDirectoryOnly))
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

        var harness = File.ReadAllText(Path.Combine(runtimeDir, "Release1KeepTheLightsOffProductionGateHarness.cs"));
        foreach (var banned in new[] { "UnityEngine", "S1API", "Il2Cpp", "GUI.", "HarmonyPatch", "KeyCode" })
            Assert.DoesNotContain(banned, harness, StringComparison.Ordinal);
        Assert.Equal(1, Count(harness, "world.TryReadProductionActivity("));

        var contracts = File.ReadAllText(Path.Combine(runtimeDir, "Release1ProductionActivityContracts.cs"));
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

        var readers = Directory.EnumerateFiles(runtimeDir, "*.cs", SearchOption.TopDirectoryOnly)
            .Sum(file => Count(File.ReadAllText(file), "TryReadProductionActivity(out"));
        Assert.True(readers >= 3, "the boundary interface, the facade and the access layer all declare the read");

        var mod = File.ReadAllText(Path.Combine(root, "tools", "OrganizedCrime", "Mod.cs"));
        Assert.Equal(1, Count(mod, "KeyCode.Home"));
        // F3, F4 and F5 are no longer banned here: the OC-69 merge legitimately spends them on the
        // field contact seam, proven by Mod_has_three_new_owner_qa_keys_for_the_field_contact_spike_and_still_no_f7_or_f12
        // below. F7 and F12 stay banned everywhere, always.
        foreach (var banned in new[] { "KeyCode.F7", "KeyCode.F12" })
            Assert.Equal(0, Count(mod, banned));
        foreach (var kept in new[] { "KeyCode.F1)", "KeyCode.F2)", "KeyCode.F6)", "KeyCode.F8)", "KeyCode.F9)", "KeyCode.F10)", "KeyCode.F11)" })
            Assert.Equal(1, Count(mod, kept));
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
    public void Every_arthur_spike_file_is_wholly_gated_on_the_owner_spikes_symbol()
    {
        var root = FindRepositoryRoot();
        var runtimeDir = Path.Combine(root, "tools", "OrganizedCrime", "Runtime");
        foreach (var file in new[]
                 {
                     "Release1ArthurNpc.cs", "S1ApiRelease1FieldContactRuntime.cs",
                     "Release1ArthurFieldContactHarness.cs", "Release1ArthurFieldContactReadyPump.cs",
                     "Release1ArthurLoadReconcile.cs"
                 })
        {
            var text = File.ReadAllText(Path.Combine(runtimeDir, file));
            Assert.True(text.TrimStart().StartsWith("#if OC_OWNER_SPIKES", StringComparison.Ordinal),
                $"{file} must open with #if OC_OWNER_SPIKES.");
            Assert.True(text.TrimEnd().EndsWith("#endif", StringComparison.Ordinal),
                $"{file} must close with #endif.");
            Assert.Equal(1, Count(text, "#if OC_OWNER_SPIKES"));
            Assert.Equal(1, Count(text, "#endif"));
        }
    }

    [Fact]
    public void Mod_cs_gates_every_arthur_field_construction_and_key_handler_on_the_owner_spikes_symbol()
    {
        var root = FindRepositoryRoot();
        var mod = File.ReadAllText(Path.Combine(root, "tools", "OrganizedCrime", "Mod.cs"));

        // Every #if OC_OWNER_SPIKES region closes, and exactly one (the world constructor call, which
        // needs a Release-mode two-argument fallback) carries an #else. The exact number of regions is
        // not pinned here: Step 6 below lists every symbol that must be gated, and the per-symbol
        // assertions afterward are what actually enforce that, so this file can be edited without a
        // magic count going stale.
        var ifCount = Count(mod, "#if OC_OWNER_SPIKES");
        Assert.True(ifCount >= 8, $"expected at least 8 gated regions in Mod.cs, found {ifCount}.");
        Assert.Equal(ifCount, Count(mod, "#endif"));
        Assert.Equal(1, Count(mod, "#else"));

        Assert.Contains("new S1ApiRelease1SmallCourtesyWorld(hqContext, hqStorageRuntime, lockdownHarmony: HarmonyInstance)", mod, StringComparison.Ordinal);
        // The existing exact-three-argument guard (Release1ProductionReachabilityTests, above) stays
        // satisfied because that literal string is preserved verbatim inside the #if branch.

        foreach (var symbol in new[]
                 {
                     "_release1FieldContactRuntime", "_arthurFieldContactParked", "_arthurLoadReconcile",
                     "_arthurProvokeFollowUp", "_arthurToggleDecide"
                 })
        {
            var index = mod.IndexOf(symbol, StringComparison.Ordinal);
            Assert.True(index >= 0, $"{symbol} was not found.");
            var guardBefore = mod.LastIndexOf("#if OC_OWNER_SPIKES", index, StringComparison.Ordinal);
            var endifBefore = mod.LastIndexOf("#endif", index, StringComparison.Ordinal);
            Assert.True(guardBefore >= 0 && guardBefore > endifBefore,
                $"{symbol}'s first use is not inside an open #if OC_OWNER_SPIKES region.");
        }
    }

    [Fact]
    public void Mod_wires_timing_receipts_into_the_hq_door_observer_and_the_hq_storage_boundary()
    {
        var root = FindRepositoryRoot();
        var mod = File.ReadAllText(Path.Combine(root, "tools", "OrganizedCrime", "Mod.cs"));
        Assert.Contains("new SyndicateHqNativeStorageBoundary { Timing = _timing }", mod, StringComparison.Ordinal);
        // Final review fix: Mod.cs already carries four unrelated pre-existing "Timing = _timing"
        // assignments (the story runtime, the production composition, and its two mission services),
        // so a bare Count(...) >= 2 passed even with the door observer's own wiring deleted. Pin the
        // door observer's own object initializer by proximity to its own constructor call instead.
        var observerIndex = mod.IndexOf("new SyndicateHqDoorObserver(", StringComparison.Ordinal);
        Assert.True(observerIndex >= 0, "SyndicateHqDoorObserver construction was not found.");
        var observerTimingIndex = mod.IndexOf("Timing = _timing", observerIndex, StringComparison.Ordinal);
        Assert.True(observerTimingIndex >= 0 && observerTimingIndex - observerIndex < 400,
            "expected the door observer's own object initializer to set Timing = _timing.");
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
        Assert.Contains("\"1.0.0\"", attributeText, StringComparison.Ordinal);
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
