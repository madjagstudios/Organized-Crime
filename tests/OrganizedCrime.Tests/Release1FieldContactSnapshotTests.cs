using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Release1FieldContactSnapshotTests
{
    private static Release1FieldContactSnapshot Present() => new(
        true, true, true, 4.5f, true,
        100f, 100f, true, false, false,
        90f, 100f, false, false, false, false,
        Release1FieldContactPursuitLevel.None, false, false, false,
        2, 1,
        0.5f, 20f, 8f, "Avatar/Equippables/Fists");

    [Fact]
    public void An_unavailable_snapshot_carries_every_unset_value_and_validates()
    {
        var snapshot = Release1FieldContactSnapshot.Unavailable();

        Assert.False(snapshot.Present);
        Assert.Equal(Release1FieldContactSnapshot.UnsetMeasure, snapshot.DistanceToPlayer);
        Assert.Equal(Release1FieldContactSnapshot.UnsetMeasure, snapshot.ContactHealth);
        Assert.Equal(Release1FieldContactSnapshot.UnsetMeasure, snapshot.PlayerHealth);
        Assert.Equal(Release1FieldContactSnapshot.UnsetCount, snapshot.ActiveOfficerCount);
        Assert.Equal(Release1FieldContactSnapshot.UnsetCount, snapshot.DispatchOfficerCount);
        Assert.Equal(Release1FieldContactPursuitLevel.Unknown, snapshot.PursuitLevel);
        Assert.Equal(string.Empty, snapshot.WeaponAssetPath);
        snapshot.Validate();
    }

    [Fact]
    public void An_absent_contact_keeps_the_player_and_law_fields_live_and_unsets_only_the_contact_fields()
    {
        var snapshot = Release1FieldContactSnapshot.AbsentContact(
            72f, 100f, false, false, true, false,
            Release1FieldContactPursuitLevel.Investigating, true, false, true, 3, 2);

        Assert.False(snapshot.Present);
        Assert.False(snapshot.Physical);
        Assert.False(snapshot.Visible);
        Assert.False(snapshot.Moving);
        Assert.Equal(Release1FieldContactSnapshot.UnsetMeasure, snapshot.DistanceToPlayer);
        Assert.Equal(Release1FieldContactSnapshot.UnsetMeasure, snapshot.ContactMaxHealth);
        Assert.Equal(Release1FieldContactSnapshot.UnsetMeasure, snapshot.GiveUpTime);
        Assert.Equal(string.Empty, snapshot.WeaponAssetPath);
        Assert.Equal(72f, snapshot.PlayerHealth);
        Assert.Equal(100f, snapshot.PlayerMaxHealth);
        Assert.True(snapshot.PlayerRagdolled);
        Assert.Equal(Release1FieldContactPursuitLevel.Investigating, snapshot.PursuitLevel);
        Assert.True(snapshot.Wanted);
        Assert.True(snapshot.BodySearchPending);
        Assert.Equal(3, snapshot.ActiveOfficerCount);
        Assert.Equal(2, snapshot.DispatchOfficerCount);
        snapshot.Validate();
    }

    [Fact]
    public void A_present_snapshot_round_trips_every_field_and_validates()
    {
        var snapshot = Present();

        Assert.True(snapshot.Present);
        Assert.True(snapshot.Physical);
        Assert.True(snapshot.Visible);
        Assert.Equal(4.5f, snapshot.DistanceToPlayer);
        Assert.True(snapshot.Moving);
        Assert.Equal(100f, snapshot.ContactHealth);
        Assert.Equal(100f, snapshot.ContactMaxHealth);
        Assert.True(snapshot.Conscious);
        Assert.False(snapshot.KnockedOut);
        Assert.False(snapshot.Dead);
        Assert.Equal(90f, snapshot.PlayerHealth);
        Assert.Equal(100f, snapshot.PlayerMaxHealth);
        Assert.False(snapshot.PlayerUnconscious);
        Assert.False(snapshot.PlayerArrested);
        Assert.False(snapshot.PlayerRagdolled);
        Assert.False(snapshot.PlayerTased);
        Assert.Equal(Release1FieldContactPursuitLevel.None, snapshot.PursuitLevel);
        Assert.False(snapshot.Wanted);
        Assert.False(snapshot.LethalAuthorized);
        Assert.False(snapshot.BodySearchPending);
        Assert.Equal(2, snapshot.ActiveOfficerCount);
        Assert.Equal(1, snapshot.DispatchOfficerCount);
        Assert.Equal(0.5f, snapshot.Aggressiveness);
        Assert.Equal(20f, snapshot.GiveUpRange);
        Assert.Equal(8f, snapshot.GiveUpTime);
        Assert.Equal("Avatar/Equippables/Fists", snapshot.WeaponAssetPath);
        snapshot.Validate();
    }

    [Fact]
    public void An_invalid_field_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => (Present() with { DistanceToPlayer = float.NaN }).Validate());
        Assert.Throws<ArgumentException>(() => (Present() with { PlayerHealth = float.PositiveInfinity }).Validate());
        Assert.Throws<ArgumentException>(() => (Present() with { GiveUpRange = float.NegativeInfinity }).Validate());
        Assert.Throws<ArgumentException>(() => (Present() with { PursuitLevel = (Release1FieldContactPursuitLevel)99 }).Validate());
        Assert.Throws<ArgumentNullException>(() => (Present() with { WeaponAssetPath = null! }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => (Present() with { ActiveOfficerCount = -2 }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => (Present() with { DispatchOfficerCount = -2 }).Validate());
    }

    [Fact]
    public void An_absent_contact_carrying_a_set_contact_field_is_rejected()
    {
        var absent = Release1FieldContactSnapshot.AbsentContact(
            100f, 100f, false, false, false, false,
            Release1FieldContactPursuitLevel.None, false, false, false, 0, 0);

        Assert.Throws<ArgumentException>(() => (absent with { Visible = true }).Validate());
        Assert.Throws<ArgumentException>(() => (absent with { DistanceToPlayer = 3f }).Validate());
        Assert.Throws<ArgumentException>(() => (absent with { WeaponAssetPath = "Avatar/Equippables/Fists" }).Validate());
    }

    [Fact]
    public void The_snapshot_record_carries_no_unity_and_no_s1api_type()
    {
        foreach (var property in typeof(Release1FieldContactSnapshot).GetProperties())
        {
            var name = property.PropertyType.FullName ?? string.Empty;
            Assert.DoesNotContain("UnityEngine", name, StringComparison.Ordinal);
            Assert.DoesNotContain("S1API", name, StringComparison.Ordinal);
            Assert.DoesNotContain("Il2Cpp", name, StringComparison.Ordinal);
        }
    }
}
