using OrganizedCrime.QuestLifecycleProof;
using Xunit;

namespace OrganizedCrime.QuestLifecycleProof.Tests;

public sealed class QuestPresentationObservationTests
{
    [Fact]
    public void Native_objective_state_is_read_when_wrapper_entries_are_empty()
    {
        var wrapper = new FakeWrapperQuest(
            new FakeNativeQuest(
                new FakeInteropQuestEntries(
                    new FakeNativeEntry(FakeQuestState.Active))));

        var snapshot = QuestPresentationObserver.Observe(wrapper);

        Assert.Empty(wrapper.QuestEntries);
        Assert.Equal(["Active"], snapshot.ObjectiveStates);
        Assert.Equal("Active", snapshot.SoleObjectiveState);
    }

    [Fact]
    public void Empty_interop_shaped_native_collection_has_no_sole_objective()
    {
        var wrapper = new FakeWrapperQuest(
            new FakeNativeQuest(new FakeInteropQuestEntries()));

        var snapshot = QuestPresentationObserver.Observe(wrapper);

        Assert.NotNull(snapshot.ObjectiveStates);
        Assert.Empty(snapshot.ObjectiveStates);
        Assert.Null(snapshot.SoleObjectiveState);
    }

    [Fact]
    public void Multiple_interop_shaped_native_entries_have_no_sole_objective()
    {
        var wrapper = new FakeWrapperQuest(
            new FakeNativeQuest(
                new FakeInteropQuestEntries(
                    new FakeNativeEntry(FakeQuestState.Active),
                    new FakeNativeEntry(FakeQuestState.Active))));

        var snapshot = QuestPresentationObserver.Observe(wrapper);

        Assert.Equal(["Active", "Active"], snapshot.ObjectiveStates);
        Assert.Null(snapshot.SoleObjectiveState);
    }

    [Fact]
    public void Managed_enumerable_native_collection_remains_a_supported_fallback()
    {
        var wrapper = new FakeWrapperQuest(
            new FakeNativeQuest(
                new object[] { new FakeNativeEntry(FakeQuestState.Active) }));

        var snapshot = QuestPresentationObserver.Observe(wrapper);

        Assert.Equal(["Active"], snapshot.ObjectiveStates);
        Assert.Equal("Active", snapshot.SoleObjectiveState);
    }

    [Fact]
    public void Unreadable_interop_count_fails_closed()
    {
        var wrapper = new FakeWrapperQuest(
            new FakeNativeQuest(new FakeUnreadableCountCollection()));

        var snapshot = QuestPresentationObserver.Observe(wrapper);

        Assert.Null(snapshot.ObjectiveStates);
        Assert.Null(snapshot.SoleObjectiveState);
    }

    [Fact]
    public void Unreadable_interop_index_fails_closed()
    {
        var wrapper = new FakeWrapperQuest(
            new FakeNativeQuest(new FakeUnreadableItemCollection()));

        var snapshot = QuestPresentationObserver.Observe(wrapper);

        Assert.Null(snapshot.ObjectiveStates);
        Assert.Null(snapshot.SoleObjectiveState);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    public void Invalid_interop_count_fails_closed(int count)
    {
        var wrapper = new FakeWrapperQuest(
            new FakeNativeQuest(new FakeReportedCountCollection(count)));

        var snapshot = QuestPresentationObserver.Observe(wrapper);

        Assert.Null(snapshot.ObjectiveStates);
        Assert.Null(snapshot.SoleObjectiveState);
    }

    [Fact]
    public void Null_interop_entry_fails_closed()
    {
        var wrapper = new FakeWrapperQuest(
            new FakeNativeQuest(new FakeInteropQuestEntries((object?)null)));

        var snapshot = QuestPresentationObserver.Observe(wrapper);

        Assert.Null(snapshot.ObjectiveStates);
        Assert.Null(snapshot.SoleObjectiveState);
    }

    [Fact]
    public void Null_interop_entry_state_fails_closed()
    {
        var wrapper = new FakeWrapperQuest(
            new FakeNativeQuest(
                new FakeInteropQuestEntries(
                    new FakeNativeEntryWithNullableState(null))));

        var snapshot = QuestPresentationObserver.Observe(wrapper);

        Assert.Null(snapshot.ObjectiveStates);
        Assert.Null(snapshot.SoleObjectiveState);
    }

    [Fact]
    public void Unavailable_native_entry_state_fails_closed()
    {
        var wrapper = new FakeWrapperQuest(
            new FakeNativeQuest(
                new FakeInteropQuestEntries(
                    new FakeNativeEntryWithoutState())));

        var snapshot = QuestPresentationObserver.Observe(wrapper);

        Assert.Null(snapshot.ObjectiveStates);
        Assert.Null(snapshot.SoleObjectiveState);
    }

    [Fact]
    public void Enum_valued_quest_state_is_stringified()
    {
        var wrapper = new FakeWrapperQuest(
            new FakeNativeQuest(
                new FakeInteropQuestEntries(
                    new FakeNativeEntry(FakeQuestState.Active))));

        var snapshot = QuestPresentationObserver.Observe(wrapper);

        Assert.Equal("Active", snapshot.QuestState);
    }

    private enum FakeQuestState
    {
        Active
    }

    private abstract class FakeWrapperQuestBase
    {
        private readonly object S1Quest;

        protected FakeWrapperQuestBase(object nativeQuest)
        {
            S1Quest = nativeQuest;
        }
    }

    private sealed class FakeWrapperQuest : FakeWrapperQuestBase
    {
        public readonly List<object> QuestEntries = [];

        public FakeQuestState QuestState => FakeQuestState.Active;

        public FakeWrapperQuest(object nativeQuest)
            : base(nativeQuest)
        {
        }
    }

    private sealed class FakeNativeQuest
    {
        public object Entries { get; }

        public FakeNativeQuest(object entries)
        {
            Entries = entries;
        }
    }

    private sealed class FakeInteropQuestEntries
    {
        private readonly object?[] _entries;

        public FakeInteropQuestEntries(params object?[] entries)
        {
            _entries = entries;
        }

        public int Count => _entries.Length;

        public object? this[int index] => _entries[index];
    }

    private sealed class FakeUnreadableCountCollection
    {
        public int Count => throw new InvalidOperationException("Count unavailable");

        public object this[int index] => new FakeNativeEntry(FakeQuestState.Active);
    }

    private sealed class FakeUnreadableItemCollection
    {
        public int Count => 1;

        public object this[int index] => throw new InvalidOperationException("Item unavailable");
    }

    private sealed class FakeReportedCountCollection
    {
        public FakeReportedCountCollection(int count)
        {
            Count = count;
        }

        public int Count { get; }

        public object this[int index] => new FakeNativeEntry(FakeQuestState.Active);
    }

    private sealed class FakeNativeEntry
    {
        public FakeQuestState State { get; }

        public FakeNativeEntry(FakeQuestState state)
        {
            State = state;
        }
    }

    private sealed class FakeNativeEntryWithNullableState
    {
        public FakeQuestState? State { get; }

        public FakeNativeEntryWithNullableState(FakeQuestState? state)
        {
            State = state;
        }
    }

    private sealed class FakeNativeEntryWithoutState;
}
