using System.Reflection;
using OrganizedCrime.Runtime;
using OrganizedCrime.Runtime.Release1.World;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class Release1WorldCapabilityTests
{
    [Theory]
    [InlineData(typeof(IGameTimeWorld), "TryReadCanonicalTotalMinutes,TryReadContext")]
    [InlineData(typeof(IProductCatalogWorld), "TryReadDeadDrops,TryReadPackaging,TryReadProducts")]
    [InlineData(typeof(IDeadDropWorld), "TryChangeSlotQuantity,TryInsertPackagedProduct,TryReadDeadDropSlots,TrySetSlotLocked,TrySubscribeDeadDropClosed")]
    [InlineData(typeof(IWalletWorld), "TryChangeCashBalance,TryDebitCashBalance,TryDebitCashBalance,TryReadCashBalance")]
    [InlineData(typeof(ILockdownWorld), "TryEngageLockdown,TryReleaseLockdown")]
    [InlineData(typeof(IDeadDropCashSlotWorld), "TryChangeDeadDropSlotCashBalance,TryReadDeadDropSlotCashBalance")]
    [InlineData(typeof(IHoldRoomWorld), "TryChangeHoldRoomSlotCashBalance,TryReadHoldRoom,TryReadHoldRoomSlotCashBalance,TrySetHoldRoomSlotLocked")]
    [InlineData(typeof(IProductionActivityWorld), "TryReadProductionActivity")]
    [InlineData(typeof(IFieldContactWorld), "TryDespawnFieldContact,TryParkFieldContact,TryProvokeFieldContact,TryReadFieldContact,TryUnparkFieldContact")]
    public void OC_154_capabilities_partition_the_existing_members(Type capability, string expected)
    {
        Assert.Equal(expected.Split(','), capability.GetMethods().Select(method => method.Name).OrderBy(name => name, StringComparer.Ordinal));
        Assert.True(capability.IsAssignableFrom(typeof(IRelease1SmallCourtesyWorld)));
    }

    [Fact]
    public void OC_154_full_world_retains_every_signature_parameter_and_nullable_out_contract()
    {
        var methods = typeof(IRelease1SmallCourtesyWorld).GetInterfaces()
            .Append(typeof(IRelease1SmallCourtesyWorld))
            .SelectMany(type => type.GetMethods()).Distinct().ToArray();
        var nullability = new NullabilityInfoContext();
        var actual = methods.Select(method => method.ReturnType.Name + " " + method.Name + "(" +
            string.Join(", ", method.GetParameters().Select(parameter =>
            {
                Assert.False(parameter.IsOptional);
                Assert.False(parameter.HasDefaultValue);
                var type = parameter.ParameterType;
                var direction = type.IsByRef ? parameter.IsOut ? "out " : "ref " : "";
                if (type.IsByRef) type = type.GetElementType()!;
                var nullable = nullability.Create(parameter).ReadState == NullabilityState.Nullable ? "?" : "";
                return direction + TypeName(type) + nullable + " " + parameter.Name;
            })) + ")").OrderBy(signature => signature, StringComparer.Ordinal);

        Assert.Equal(ExpectedSignatures.OrderBy(signature => signature, StringComparer.Ordinal), actual);
        Assert.Empty(typeof(IRelease1SmallCourtesyWorld).GetMethods());
    }

    [Theory]
    [InlineData(Release1SmallCourtesyWorldMutationStatus.Succeeded)]
    [InlineData(Release1SmallCourtesyWorldMutationStatus.Rejected)]
    [InlineData(Release1SmallCourtesyWorldMutationStatus.Unavailable)]
    [InlineData(Release1SmallCourtesyWorldMutationStatus.Ambiguous)]
    public void OC_154_bounded_default_delegates_once_with_the_exact_float_and_result(Release1SmallCourtesyWorldMutationStatus result)
    {
        var fake = new WalletDefault(result);
        IWalletWorld world = fake;
        const float amount = -123.625f;

        Assert.Equal(result, world.TryDebitCashBalance(amount, float.NaN));
        Assert.Equal(new[] { BitConverter.SingleToInt32Bits(amount) }, fake.Debits);
    }

    [Fact]
    public void OC_154_explicit_bounded_implementation_still_overrides_the_default()
    {
        var fake = new WalletOverride();
        IWalletWorld world = fake;

        Assert.Equal(Release1SmallCourtesyWorldMutationStatus.Ambiguous, world.TryDebitCashBalance(-12.25f, 44.5f));
        Assert.Equal(new[] { (-12.25f, 44.5f) }, fake.BoundedDebits);
        Assert.Empty(fake.Debits);
    }

    [Fact]
    public void OC_154_adapter_still_implements_all_members_and_overrides_both_debit_forms()
    {
        foreach (var capability in typeof(IRelease1SmallCourtesyWorld).GetInterfaces())
        {
            var map = typeof(S1ApiRelease1SmallCourtesyWorld).GetInterfaceMap(capability);
            Assert.All(map.TargetMethods, method => Assert.Equal(typeof(S1ApiRelease1SmallCourtesyWorld), method.DeclaringType));
        }
    }

    private static string TypeName(Type type) => type.IsGenericType
        ? type.Name.Split('`')[0] + "<" + string.Join(", ", type.GetGenericArguments().Select(TypeName)) + ">"
        : type == typeof(string) ? "string" : type == typeof(int) ? "int" : type == typeof(float) ? "float"
        : type == typeof(double) ? "double" : type == typeof(bool) ? "bool" : type.Name;

    private class WalletDefault : IWalletWorld
    {
        private readonly Release1SmallCourtesyWorldMutationStatus _result;
        public WalletDefault(Release1SmallCourtesyWorldMutationStatus result) => _result = result;
        public List<int> Debits { get; } = new();
        public Release1SmallCourtesyWorldReadStatus TryReadCashBalance(out float balance) => throw new NotSupportedException();
        public Release1SmallCourtesyWorldMutationStatus TryChangeCashBalance(float amount) => throw new NotSupportedException();
        public Release1SmallCourtesyWorldMutationStatus TryDebitCashBalance(float amount)
        {
            Debits.Add(BitConverter.SingleToInt32Bits(amount));
            return _result;
        }
    }

    private sealed class WalletOverride : WalletDefault, IWalletWorld
    {
        public WalletOverride() : base(Release1SmallCourtesyWorldMutationStatus.Rejected) { }
        public List<(float Amount, float Maximum)> BoundedDebits { get; } = new();
        public Release1SmallCourtesyWorldMutationStatus TryDebitCashBalance(float amount, float maximumDebit)
        {
            BoundedDebits.Add((amount, maximumDebit));
            return Release1SmallCourtesyWorldMutationStatus.Ambiguous;
        }
    }

    // Verbatim signature snapshot from the contract at 271f5c1.
    private static readonly string[] ExpectedSignatures =
    {
        "Release1SmallCourtesyWorldReadStatus TryReadContext(out Release1StoryHostContextSnapshot context)",
        "Release1SmallCourtesyWorldReadStatus TryReadCanonicalTotalMinutes(out double totalMinutes)",
        "Release1SmallCourtesyWorldReadStatus TryReadProducts(out IReadOnlyList<Release1SmallCourtesyProductCandidate> products)",
        "Release1SmallCourtesyWorldReadStatus TryReadDeadDrops(out IReadOnlyList<Release1SmallCourtesyDropCandidate> drops)",
        "Release1SmallCourtesyWorldReadStatus TryReadPackaging(Release1SmallCourtesyPackageKind kind, out Release1SmallCourtesyPackagingCandidate packaging)",
        "Release1SmallCourtesyWorldReadStatus TryReadDeadDropSlots(string deadDropGuid, out IReadOnlyList<Release1SmallCourtesySlotSnapshot> slots)",
        "Release1SmallCourtesyWorldReadStatus TryReadHoldRoom(out Release1HoldRoomSnapshot room)",
        "Release1SmallCourtesyWorldMutationStatus TrySetSlotLocked(string deadDropGuid, int slotIndex, bool locked)",
        "Release1SmallCourtesyWorldMutationStatus TryChangeSlotQuantity(string deadDropGuid, int slotIndex, int amount)",
        "Release1SmallCourtesyWorldMutationStatus TryInsertPackagedProduct(string deadDropGuid, int slotIndex, string productId, string packagingId, int quantity, out string reason)",
        "Release1SmallCourtesyWorldReadStatus TrySubscribeDeadDropClosed(string deadDropGuid, Action<string> callback, out IRelease1SmallCourtesyDropSubscription? subscription)",
        "Release1SmallCourtesyWorldReadStatus TryReadCashBalance(out float balance)",
        "Release1SmallCourtesyWorldMutationStatus TryChangeCashBalance(float amount)",
        "Release1SmallCourtesyWorldMutationStatus TryDebitCashBalance(float amount)",
        "Release1SmallCourtesyWorldMutationStatus TryDebitCashBalance(float amount, float maximumDebit)",
        "Release1SmallCourtesyWorldMutationStatus TryEngageLockdown(out string reason)",
        "Release1SmallCourtesyWorldMutationStatus TryReleaseLockdown(out string reason)",
        "Release1SmallCourtesyWorldReadStatus TryReadDeadDropSlotCashBalance(string deadDropGuid, int slotIndex, out float balance)",
        "Release1SmallCourtesyWorldMutationStatus TryChangeDeadDropSlotCashBalance(string deadDropGuid, int slotIndex, float amount)",
        "Release1SmallCourtesyWorldReadStatus TryReadHoldRoomSlotCashBalance(string closetGuid, int slotIndex, out float balance)",
        "Release1SmallCourtesyWorldMutationStatus TrySetHoldRoomSlotLocked(string closetGuid, int slotIndex, bool locked)",
        "Release1SmallCourtesyWorldMutationStatus TryChangeHoldRoomSlotCashBalance(string closetGuid, int slotIndex, float amount)",
        "Release1SmallCourtesyWorldReadStatus TryReadProductionActivity(out Release1ProductionActivitySnapshot activity)",
        "Release1SmallCourtesyWorldMutationStatus TryParkFieldContact(string contactId, out string reason)",
        "Release1SmallCourtesyWorldMutationStatus TryUnparkFieldContact(string contactId, float aheadMetres, out string reason)",
        "Release1SmallCourtesyWorldMutationStatus TryDespawnFieldContact(string contactId, out string reason)",
        "Release1SmallCourtesyWorldMutationStatus TryProvokeFieldContact(string contactId, out string reason)",
        "Release1SmallCourtesyWorldReadStatus TryReadFieldContact(string contactId, out Release1FieldContactSnapshot snapshot)",
    };
}
