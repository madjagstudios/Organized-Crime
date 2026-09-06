using OrganizedCrime.Runtime;
using UnityEngine;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Release1DecisionPromptHostTests
{
    [Fact]
    public void Passive_prompt_borrows_and_restores_cursor_only_after_explicit_open()
    {
        var platform = new FakePlatform
        {
            CameraAvailable = true,
            CursorLockState = Release1CursorLockState.Locked,
            CursorVisible = false
        };
        using var host = new Release1DecisionPromptHost(() => Card(), platform);

        host.Update();

        Assert.False(host.ModalOpen);
        Assert.Empty(platform.Events);

        platform.NextInput = Release1DecisionPromptInput.Open;
        host.Update();

        Assert.True(host.ModalOpen);
        Assert.Equal(new[]
        {
            "add:OrganizedCrime.DecisionPrompt",
            "look:False",
            "free",
            "cursor:None:True"
        }, platform.Events);

        platform.Events.Clear();
        platform.NextInput = Release1DecisionPromptInput.Close;
        host.Update();

        Assert.False(host.ModalOpen);
        Assert.Equal(new[]
        {
            "remove:OrganizedCrime.DecisionPrompt",
            "look:True",
            "lock",
            "cursor:Locked:False"
        }, platform.Events);
    }

    [Theory]
    [InlineData("phone")]
    [InlineData("pause")]
    public void Vanilla_ui_handoff_removes_the_OC_registration_and_restores_look_without_relocking(string owner)
    {
        var platform = new FakePlatform
        {
            CameraAvailable = true,
            CursorLockState = Release1CursorLockState.Locked
        };
        using var host = new Release1DecisionPromptHost(() => Card(), platform);
        platform.NextInput = Release1DecisionPromptInput.Open;
        host.Update();
        platform.Events.Clear();
        platform.PhoneOpen = owner == "phone";
        platform.PauseOpen = owner == "pause";

        host.Update();

        Assert.False(host.ModalOpen);
        Assert.Equal(new[]
        {
            "remove:OrganizedCrime.DecisionPrompt",
            "look:True"
        }, platform.Events);
    }

    [Theory]
    [InlineData("phone")]
    [InlineData("pause")]
    public void Open_is_ignored_while_vanilla_ui_already_owns_the_cursor(string owner)
    {
        var platform = new FakePlatform
        {
            CameraAvailable = true,
            CursorLockState = Release1CursorLockState.None,
            CursorVisible = true,
            PhoneOpen = owner == "phone",
            PauseOpen = owner == "pause",
            NextInput = Release1DecisionPromptInput.Open
        };
        using var host = new Release1DecisionPromptHost(() => Card(), platform);

        host.Update();

        Assert.False(host.ModalOpen);
        Assert.Empty(platform.Events);
    }

    [Fact]
    public void Unity_key_mapping_uses_enter_for_primary_and_never_uses_vanilla_interact_E()
    {
        Assert.Equal(
            Release1DecisionPromptInput.Primary,
            Release1DecisionPromptUnityPlatform.MapInput(
                modalOpen: true,
                KeyCode.F7,
                key => key == KeyCode.Return));
        Assert.Equal(
            Release1DecisionPromptInput.None,
            Release1DecisionPromptUnityPlatform.MapInput(
                modalOpen: true,
                KeyCode.F7,
                key => key == KeyCode.E));
    }

    [Theory]
    [InlineData("preload")]
    [InlineData("scene")]
    [InlineData("dispose")]
    public void Lifecycle_boundaries_force_an_idempotent_full_release(string boundary)
    {
        var platform = new FakePlatform
        {
            CameraAvailable = true,
            CursorLockState = Release1CursorLockState.Locked
        };
        var host = new Release1DecisionPromptHost(() => Card(), platform);
        platform.NextInput = Release1DecisionPromptInput.Open;
        host.Update();
        platform.Events.Clear();

        switch (boundary)
        {
            case "preload": host.OnPreLoad(); host.OnPreLoad(); break;
            case "scene": host.OnPreSceneChange(); host.OnPreSceneChange(); break;
            case "dispose": host.Dispose(); host.Dispose(); break;
        }

        Assert.False(host.ModalOpen);
        Assert.Equal(1, platform.Events.Count(entry => entry.StartsWith("remove:", StringComparison.Ordinal)));
        Assert.Equal(1, platform.Events.Count(entry => entry == "lock"));
        Assert.Equal(1, platform.Events.Count(entry => entry == "cursor:Locked:False"));
    }

    [Fact]
    public void Keyboard_commands_invoke_only_explicit_bound_actions()
    {
        var primaryCalls = 0;
        var secondaryCalls = 0;
        Release1DecisionPromptCard? current = null;
        current = Card(
            primary: () => primaryCalls++,
            secondary: () => { secondaryCalls++; current = null; });
        var platform = new FakePlatform { CameraAvailable = true };
        using var host = new Release1DecisionPromptHost(() => current, platform);
        platform.NextInput = Release1DecisionPromptInput.Open;
        host.Update();

        platform.NextInput = Release1DecisionPromptInput.Primary;
        host.Update();
        Assert.Equal(1, primaryCalls);
        Assert.Equal(0, secondaryCalls);
        Assert.True(host.ModalOpen);

        platform.NextInput = Release1DecisionPromptInput.Close;
        host.Update();
        Assert.Equal(1, primaryCalls);
        Assert.Equal(0, secondaryCalls);
        Assert.False(host.ModalOpen);

        platform.NextInput = Release1DecisionPromptInput.Open;
        host.Update();
        platform.NextInput = Release1DecisionPromptInput.Secondary;
        host.Update();
        Assert.Equal(1, primaryCalls);
        Assert.Equal(1, secondaryCalls);
        Assert.False(host.ModalOpen);
    }

    [Fact]
    public void Missing_camera_degrades_to_cursor_only_and_still_restores()
    {
        var platform = new FakePlatform
        {
            CameraAvailable = false,
            CursorLockState = Release1CursorLockState.Locked,
            CursorVisible = false
        };
        using var host = new Release1DecisionPromptHost(() => Card(), platform);
        platform.NextInput = Release1DecisionPromptInput.Open;
        host.Update();

        Assert.Equal(new[] { "cursor:None:True" }, platform.Events);

        platform.Events.Clear();
        platform.NextInput = Release1DecisionPromptInput.Close;
        host.Update();

        Assert.Equal(new[] { "cursor:Locked:False" }, platform.Events);
    }

    [Fact]
    public void Losing_the_actionable_view_releases_without_invoking_a_command()
    {
        Release1DecisionPromptCard? current = Card();
        var platform = new FakePlatform
        {
            CameraAvailable = true,
            CursorLockState = Release1CursorLockState.Locked
        };
        using var host = new Release1DecisionPromptHost(() => current, platform);
        platform.NextInput = Release1DecisionPromptInput.Open;
        host.Update();
        platform.Events.Clear();
        current = null;

        host.Update();

        Assert.False(host.ModalOpen);
        Assert.Contains("remove:OrganizedCrime.DecisionPrompt", platform.Events);
        Assert.Contains("lock", platform.Events);
    }

    [Fact]
    public void Throwing_command_releases_cursor_and_is_not_retried()
    {
        var calls = 0;
        var platform = new FakePlatform
        {
            CameraAvailable = true,
            CursorLockState = Release1CursorLockState.Locked
        };
        using var host = new Release1DecisionPromptHost(
            () => Card(primary: () => { calls++; throw new InvalidOperationException("boom"); }),
            platform);
        platform.NextInput = Release1DecisionPromptInput.Open;
        host.Update();
        platform.Events.Clear();
        platform.NextInput = Release1DecisionPromptInput.Primary;

        host.Update();
        host.Update();

        Assert.Equal(1, calls);
        Assert.False(host.ModalOpen);
        Assert.Contains("remove:OrganizedCrime.DecisionPrompt", platform.Events);
        Assert.Contains("lock", platform.Events);
    }

    [Fact]
    public void Player_copy_normalization_removes_em_dashes_at_the_render_boundary()
    {
        Assert.Equal("Offer - review it", Release1PlayerCopy.Normalize("Offer \u2014 review it"));
        Assert.DoesNotContain('\u2014', Release1PlayerCopy.Normalize(Release1PayphoneBanner.Text));
        Assert.DoesNotContain('\u2014', Release1PlayerCopy.Normalize(Release1IntroPromptPresenter.PromptText));
    }

    private static Release1DecisionPromptCard Card(Action? primary = null, Action? secondary = null) => new(
        "test",
        "A decision is waiting. Press F7 to review.",
        "Test decision",
        "Choose deliberately.",
        "Accept",
        primary ?? (() => { }),
        secondary is null ? null : "Not now",
        secondary);

    private sealed class FakePlatform : IRelease1DecisionPromptPlatform
    {
        public bool CameraAvailable { get; set; }
        public bool PhoneOpen { get; set; }
        public bool PauseOpen { get; set; }
        public bool VanillaUiOwnsCursor => PhoneOpen || PauseOpen;
        public Release1CursorLockState CursorLockState { get; set; } = Release1CursorLockState.None;
        public bool CursorVisible { get; set; }
        public string OpenKeyLabel => "F7";
        public Release1DecisionPromptInput NextInput { get; set; }
        public List<string> Events { get; } = new();

        public Release1DecisionPromptInput ReadInput(bool modalOpen)
        {
            var result = NextInput;
            NextInput = Release1DecisionPromptInput.None;
            return result;
        }

        public void AddActiveUiElement(string token) => Events.Add($"add:{token}");
        public void RemoveActiveUiElement(string token) => Events.Add($"remove:{token}");
        public void SetCanLook(bool canLook) => Events.Add($"look:{canLook}");
        public void FreeMouse() => Events.Add("free");
        public void LockMouse() => Events.Add("lock");
        public void SetCursor(Release1CursorLockState lockState, bool visible)
        {
            CursorLockState = lockState;
            CursorVisible = visible;
            Events.Add($"cursor:{lockState}:{visible}");
        }
    }
}
