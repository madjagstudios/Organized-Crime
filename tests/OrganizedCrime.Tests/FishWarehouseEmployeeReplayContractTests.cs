using System.Text.Json;
using OrganizedCrime.Runtime;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class FishWarehouseEmployeeReplayContractTests
{
    private const string EmployeeGuid = "00000000-0000-0000-0000-000000000111";
    private const string HomeGuid = "00000000-0000-0000-0000-000000000222";
    private const string StationOneGuid = "00000000-0000-0000-0000-000000000333";
    private const string StationTwoGuid = "00000000-0000-0000-0000-000000000444";
    private const string StationThreeGuid = "00000000-0000-0000-0000-000000000555";

    [Fact]
    public void Measured_live_packager_raw_json_parses_the_expected_state()
    {
        var descriptor = new FishWarehouseEmployeeReplayDescriptor(
            EmployeeGuid,
            "PackagerData",
            "Packager",
            MeasuredLivePackagerRawJson);

        var parsed = descriptor.TryParseExpectedState(out var expected, out var failureReason);

        Assert.True(parsed, failureReason);
        Assert.Equal("Packager", expected.Identity);
        Assert.Equal("worker-117", expected.Id);
        Assert.Equal("oc_fishwarehouse", expected.PropertyCode);
        Assert.Equal(HomeGuid, expected.HomeGuid);
        Assert.Equal(new[] { StationOneGuid, StationTwoGuid, StationThreeGuid }, expected.StationGuids);
    }

    [Fact]
    public void Wrapped_chemist_raw_json_parses_the_expected_state_with_chemist_identity()
    {
        // OC-129: all four native types replay now, so the expected-state
        // parse must stay in agreement with the inspector map for every
        // supported type.
        var descriptor = new FishWarehouseEmployeeReplayDescriptor(
            "11111111-1111-1111-1111-111111111111",
            "ChemistData",
            "Chemist",
            ChemistWrappedEmployeeRawJson);

        Assert.True(descriptor.IsSupportedPackager);
        Assert.True(descriptor.TryParseExpectedState(out var expected, out var failureReason), failureReason);
        Assert.Equal("Chemist", expected.Identity);
        Assert.Equal("test_chemist", expected.Id);
        Assert.Equal("oc_fishwarehouse", expected.PropertyCode);
        Assert.Equal("22222222-2222-2222-2222-222222222222", expected.HomeGuid);
        Assert.Empty(expected.StationGuids);
        Assert.Null(expected.MoveItem);
        Assert.Equal(string.Empty, expected.SuppliesGuid);
    }

    [Fact]
    public void Botanist_raw_json_parses_pots_as_stations_and_supplies()
    {
        // The Botanist lists Pots instead of Stations and carries a
        // Supplies object field; both must reach the expected state.
        var descriptor = new FishWarehouseEmployeeReplayDescriptor(
            "88888888-8888-8888-8888-888888888888",
            "BotanistData",
            "Botanist",
            BotanistMeasuredRawJson);

        Assert.True(descriptor.IsSupportedPackager);
        Assert.True(descriptor.TryParseExpectedState(out var expected, out var failureReason), failureReason);
        Assert.Equal("Botanist", expected.Identity);
        Assert.Equal("botanist-1", expected.Id);
        Assert.Equal("oc_fishwarehouse", expected.PropertyCode);
        Assert.Equal("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", expected.HomeGuid);
        Assert.Equal(new[] { "cccccccc-cccc-cccc-cccc-cccccccccccc" }, expected.StationGuids);
        Assert.Equal("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb", expected.SuppliesGuid);
    }

    [Fact]
    public void Measured_live_packager_idle_move_item_defaults_are_not_expected_as_active()
    {
        var descriptor = new FishWarehouseEmployeeReplayDescriptor(
            EmployeeGuid,
            "PackagerData",
            "Packager",
            MeasuredLivePackagerRawJsonWithMoveItemData("", "", "", 0));

        var parsed = descriptor.TryParseExpectedState(out var expected, out var failureReason);

        Assert.True(parsed, failureReason);
        Assert.Null(expected.MoveItem);
    }

    [Fact]
    public void Active_packager_payload_preserves_the_native_inventory_additional_data()
    {
        const string inventoryJson = "{\"Items\":[{\"ID\":\"jar\",\"Quantity\":2}],\"SlotFilters\":[]}";
        var escapedInventoryJson = inventoryJson.Replace("\"", "\\\"", StringComparison.Ordinal);
        var rawJson = MeasuredLivePackagerRawJson.Replace(
            "  ]",
            $",\n    {{\"Name\":\"Inventory\",\"Contents\":\"{escapedInventoryJson}\"}}\n  ]",
            StringComparison.Ordinal);
        var descriptor = new FishWarehouseEmployeeReplayDescriptor(
            EmployeeGuid, "PackagerData", "Packager", rawJson);

        Assert.True(descriptor.TryParseExpectedState(out var expected, out var failureReason), failureReason);
        Assert.Equal(inventoryJson, expected.InventoryJson);
    }

    [Fact]
    public void Partially_populated_move_item_data_returns_a_named_failure()
    {
        var descriptor = new FishWarehouseEmployeeReplayDescriptor(
            EmployeeGuid,
            "PackagerData",
            "Packager",
            MeasuredLivePackagerRawJsonWithMoveItemData("source-guid", "", "", 0));

        var parsed = descriptor.TryParseExpectedState(out _, out var failureReason);

        Assert.False(parsed);
        Assert.Equal("DestinationGUID-missing", failureReason);
    }

    [Fact]
    public void Whitespace_only_move_item_data_returns_a_named_failure()
    {
        var descriptor = new FishWarehouseEmployeeReplayDescriptor(
            EmployeeGuid,
            "PackagerData",
            "Packager",
            MeasuredLivePackagerRawJsonWithMoveItemData(" ", "", "", 0));

        var parsed = descriptor.TryParseExpectedState(out _, out var failureReason);

        Assert.False(parsed);
        Assert.Equal("SourceGUID-missing", failureReason);
    }

    [Fact]
    public void Malformed_live_packager_raw_json_returns_a_named_failure()
    {
        var descriptor = new FishWarehouseEmployeeReplayDescriptor(
            EmployeeGuid,
            "PackagerData",
            "Packager",
            MeasuredLivePackagerRawJson.Replace(
                "\\\"ID\\\":\\\"worker-117\\\",",
                string.Empty,
                StringComparison.Ordinal));

        var parsed = descriptor.TryParseExpectedState(out _, out var failureReason);

        Assert.False(parsed);
        Assert.Equal("ID-missing", failureReason);
    }

    [Fact]
    public void Present_invalid_property_code_does_not_fall_back_to_assigned_property()
    {
        var descriptor = new FishWarehouseEmployeeReplayDescriptor(
            EmployeeGuid,
            "PackagerData",
            "Packager",
            MeasuredLivePackagerRawJson.Replace(
                "\\\"AssignedProperty\\\":\\\"oc_fishwarehouse\\\"",
                "\\\"PropertyCode\\\":\\\"\\\",\\\"AssignedProperty\\\":\\\"oc_fishwarehouse\\\"",
                StringComparison.Ordinal));

        var parsed = descriptor.TryParseExpectedState(out _, out var failureReason);

        Assert.False(parsed);
        Assert.Equal("PropertyCode-missing", failureReason);
    }

    [Fact]
    public void Blank_descriptor_identity_is_not_a_supported_packager()
    {
        var descriptor = new FishWarehouseEmployeeReplayDescriptor(
            EmployeeGuid,
            "PackagerData",
            "",
            MeasuredLivePackagerRawJson);

        Assert.False(descriptor.IsSupportedPackager);
    }

    [Theory]
    [InlineData("Position")]
    [InlineData("Rotation")]
    [InlineData("Bed")]
    [InlineData("Stations")]
    public void Nested_objects_must_not_be_json_strings(string propertyName)
    {
        var descriptor = new FishWarehouseEmployeeReplayDescriptor(
            EmployeeGuid,
            "PackagerData",
            "Packager",
            ObjectShapedPackagerRawJsonWithStringProperty(propertyName));

        var parsed = descriptor.TryParseExpectedState(out _, out var failureReason);

        Assert.False(parsed);
        Assert.Contains(propertyName, failureReason, StringComparison.Ordinal);
    }

    private static string ObjectShapedPackagerRawJsonWithStringProperty(string propertyName)
    {
        var objectJson = propertyName switch
        {
            "Position" => "{\"x\":10.25,\"y\":1.5,\"z\":-4.75}",
            "Rotation" => "{\"x\":0,\"y\":0.7071,\"z\":0,\"w\":0.7071}",
            "Bed" => "{\"ObjectGUID\":\"00000000-0000-0000-0000-000000000222\"}",
            "Stations" => "{\"ObjectGUIDs\":[\"00000000-0000-0000-0000-000000000333\",\"00000000-0000-0000-0000-000000000444\",\"00000000-0000-0000-0000-000000000555\"]}",
            _ => throw new ArgumentOutOfRangeException(nameof(propertyName), propertyName, null)
        };
        var escapedJson = objectJson.Replace("\"", "\\\"", StringComparison.Ordinal);

        return ObjectShapedPackagerRawJson.Replace(
            $"\"{propertyName}\":{objectJson}",
            $"\"{propertyName}\":\"{escapedJson}\"",
            StringComparison.Ordinal);
    }

    [Fact]
    public void Packager_configured_for_logistics_with_fewer_than_three_stations_parses()
    {
        // OC-7: an employee set up for a dock→interior route may keep fewer than
        // three packaging stations; it must still replay (not be lost on restart).
        var descriptor = new FishWarehouseEmployeeReplayDescriptor(
            EmployeeGuid, "PackagerData", "Packager", PackagerRawJsonWithStations(StationOneGuid));

        var parsed = descriptor.TryParseExpectedState(out var expected, out var failureReason);

        Assert.True(parsed, failureReason);
        Assert.Equal(new[] { StationOneGuid }, expected.StationGuids);
    }

    [Fact]
    public void Packager_with_no_stations_parses()
    {
        var descriptor = new FishWarehouseEmployeeReplayDescriptor(
            EmployeeGuid, "PackagerData", "Packager", PackagerRawJsonWithStations());

        var parsed = descriptor.TryParseExpectedState(out var expected, out var failureReason);

        Assert.True(parsed, failureReason);
        Assert.Empty(expected.StationGuids);
    }

    [Fact]
    public void Packager_with_an_explicitly_empty_home_parses_as_unassigned()
    {
        var rawJson = MeasuredLivePackagerRawJson.Replace(
            "\\\"ObjectGUID\\\":\\\"00000000-0000-0000-0000-000000000222\\\"",
            "\\\"ObjectGUID\\\":\\\"\\\"",
            StringComparison.Ordinal);
        var descriptor = new FishWarehouseEmployeeReplayDescriptor(
            EmployeeGuid, "PackagerData", "Packager", rawJson);

        var parsed = descriptor.TryParseExpectedState(out var expected, out var failureReason);

        Assert.True(parsed, failureReason);
        Assert.Empty(expected.HomeGuid);
    }

    [Fact]
    public void Packager_with_more_than_three_stations_returns_a_named_failure()
    {
        var descriptor = new FishWarehouseEmployeeReplayDescriptor(
            EmployeeGuid, "PackagerData", "Packager",
            PackagerRawJsonWithStations(
                StationOneGuid, StationTwoGuid, StationThreeGuid, "00000000-0000-0000-0000-000000000666"));

        var parsed = descriptor.TryParseExpectedState(out _, out var failureReason);

        Assert.False(parsed);
        Assert.Equal("station-count-exceeds-max", failureReason);
    }

    [Fact]
    public void Ten_employee_payloads_preserve_independent_home_station_and_route_configuration()
    {
        var expectedEmployees = Enumerable.Range(0, 10)
            .Select(index =>
            {
                var employeeGuid = GuidFor(0x111 + index);
                var homeGuid = GuidFor(0x211 + index);
                var stationGuid = GuidFor(0x311 + index);
                var routeSourceGuid = GuidFor(0x411 + index);
                var routeDestinationGuid = GuidFor(0x511 + index);
                var rawJson = MeasuredLivePackagerRawJsonWithMoveItemData(
                        routeSourceGuid, routeDestinationGuid, "{\"item\":\"fish\"}", index + 1)
                    .Replace(
                        "\\\"00000000-0000-0000-0000-000000000333\\\",\\\"00000000-0000-0000-0000-000000000444\\\",\\\"00000000-0000-0000-0000-000000000555\\\"",
                        $"\\\"{stationGuid}\\\"",
                        StringComparison.Ordinal)
                    .Replace(EmployeeGuid, employeeGuid, StringComparison.Ordinal)
                    .Replace(HomeGuid, homeGuid, StringComparison.Ordinal);

                var descriptor = new FishWarehouseEmployeeReplayDescriptor(
                    employeeGuid, "PackagerData", "Packager", rawJson);
                Assert.True(descriptor.TryParseExpectedState(out var expected, out var failureReason), failureReason);
                return (index, expected);
            })
            .ToArray();

        Assert.Equal(Enumerable.Range(0, 10), expectedEmployees.Select(employee => employee.index));
        Assert.Equal(10, expectedEmployees.Select(employee => employee.expected.Guid).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(10, expectedEmployees.Select(employee => employee.expected.HomeGuid).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(10, expectedEmployees.Select(employee => employee.expected.StationGuids.Single()).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(10, expectedEmployees.Select(employee => employee.expected.MoveItem!.SourceGuid).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(10, expectedEmployees.Select(employee => employee.expected.MoveItem!.DestinationGuid).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Packager_without_a_bed_section_parses_as_unassigned()
    {
        var descriptor = new FishWarehouseEmployeeReplayDescriptor(
            EmployeeGuid,
            "PackagerData",
            "Packager",
            ObjectShapedPackagerRawJson.Replace(
                "\"Bed\":{\"ObjectGUID\":\"00000000-0000-0000-0000-000000000222\"},",
                string.Empty,
                StringComparison.Ordinal));

        var parsed = descriptor.TryParseExpectedState(out var expected, out var failureReason);

        Assert.True(parsed, failureReason);
        Assert.Empty(expected.HomeGuid);
    }

    [Fact]
    public void Packager_without_a_configuration_entry_parses_as_unassigned()
    {
        var descriptor = new FishWarehouseEmployeeReplayDescriptor(
            EmployeeGuid,
            "PackagerData",
            "Packager",
            ObjectShapedPackagerRawJson.Replace(
                "\"Name\":\"Configuration\"",
                "\"Name\":\"Relationship\"",
                StringComparison.Ordinal));

        var parsed = descriptor.TryParseExpectedState(out var expected, out var failureReason);

        Assert.True(parsed, failureReason);
        Assert.Empty(expected.HomeGuid);
        Assert.Empty(expected.StationGuids);
    }

    [Fact]
    public void Packager_without_a_stations_section_parses_with_no_stations()
    {
        var descriptor = new FishWarehouseEmployeeReplayDescriptor(
            EmployeeGuid,
            "PackagerData",
            "Packager",
            ObjectShapedPackagerRawJson.Replace(
                ",\"Stations\":{\"ObjectGUIDs\":[\"00000000-0000-0000-0000-000000000333\",\"00000000-0000-0000-0000-000000000444\",\"00000000-0000-0000-0000-000000000555\"]}",
                string.Empty,
                StringComparison.Ordinal));

        var parsed = descriptor.TryParseExpectedState(out var expected, out var failureReason);

        Assert.True(parsed, failureReason);
        Assert.Empty(expected.StationGuids);
        Assert.Equal("00000000-0000-0000-0000-000000000222", expected.HomeGuid);
    }

    private static string GuidFor(int value) => $"00000000-0000-0000-0000-{value:000000000000}";

    private static string PackagerRawJsonWithStations(params string[] stationGuids)
    {
        string escaped = string.Join(",", stationGuids.Select(guid => $"\\\"{guid}\\\""));
        return MeasuredLivePackagerRawJson.Replace(
            "\\\"00000000-0000-0000-0000-000000000333\\\",\\\"00000000-0000-0000-0000-000000000444\\\",\\\"00000000-0000-0000-0000-000000000555\\\"",
            escaped,
            StringComparison.Ordinal);
    }

    private static string MeasuredLivePackagerRawJsonWithMoveItemData(
        string sourceGuid,
        string destinationGuid,
        string templateItemJson,
        int grabbedItemQuantity)
    {
        string serializedTemplateItemJson = JsonSerializer.Serialize(templateItemJson);
        var moveItemData =
            $"{{\"SourceGUID\":\"{sourceGuid}\",\"DestinationGUID\":\"{destinationGuid}\",\"TemplateItemJSON\":{serializedTemplateItemJson},\"GrabbedItemQuantity\":{grabbedItemQuantity}}}";
        var escapedMoveItemData = moveItemData
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);

        return MeasuredLivePackagerRawJson.Replace(
            "\\\"PaidForToday\\\":true}",
            $"\\\"PaidForToday\\\":true,\\\"MoveItemData\\\":{escapedMoveItemData}}}",
            StringComparison.Ordinal);
    }

    private const string ObjectShapedPackagerRawJson = """
        {
          "DataType":"PackagerData",
          "BaseData":{
            "GUID":"00000000-0000-0000-0000-000000000111",
            "ID":"worker-117",
            "FirstName":"Mika",
            "LastName":"Madsen",
            "IsMale":false,
            "AppearanceIndex":7,
            "Position":{"x":10.25,"y":1.5,"z":-4.75},
            "Rotation":{"x":0,"y":0.7071,"z":0,"w":0.7071},
            "AssignedProperty":"oc_fishwarehouse",
            "PaidForToday":true
          },
          "AdditionalDatas":[
            {"Name":"Configuration","Contents":{"Bed":{"ObjectGUID":"00000000-0000-0000-0000-000000000222"},"Stations":{"ObjectGUIDs":["00000000-0000-0000-0000-000000000333","00000000-0000-0000-0000-000000000444","00000000-0000-0000-0000-000000000555"]}}}
          ]
        }
        """;

    private const string MeasuredLivePackagerRawJson = """
        {
          "DataType":"PackagerData",
          "BaseData":"{\"GUID\":\"00000000-0000-0000-0000-000000000111\",\"ID\":\"worker-117\",\"FirstName\":\"Mika\",\"LastName\":\"Madsen\",\"IsMale\":false,\"AppearanceIndex\":7,\"Position\":{\"x\":10.25,\"y\":1.5,\"z\":-4.75},\"Rotation\":{\"x\":0,\"y\":0.7071,\"z\":0,\"w\":0.7071},\"AssignedProperty\":\"oc_fishwarehouse\",\"PaidForToday\":true}",
          "AdditionalDatas":[
            {"Name":"Configuration","Contents":"{\"Bed\":{\"ObjectGUID\":\"00000000-0000-0000-0000-000000000222\"},\"Stations\":{\"ObjectGUIDs\":[\"00000000-0000-0000-0000-000000000333\",\"00000000-0000-0000-0000-000000000444\",\"00000000-0000-0000-0000-000000000555\"]}}"}
          ]
        }
        """;

    // OC-129: a BotanistData record in the measured shape (Pots plus a
    // Supplies object field); also used by the replay host tests.
    private const string BotanistMeasuredRawJson = """
        {
          "DataType":"BotanistData",
          "BaseData":{
            "GUID":"88888888-8888-8888-8888-888888888888",
            "ID":"botanist-1",
            "FirstName":"Fern",
            "LastName":"Foliage",
            "IsMale":false,
            "AppearanceIndex":3,
            "Position":{"x":1,"y":2,"z":3},
            "Rotation":{"x":0,"y":0,"z":0,"w":1},
            "AssignedProperty":"oc_fishwarehouse",
            "PaidForToday":true,
            "BedGUID":"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"
          },
          "AdditionalDatas":[
            {"Name":"Configuration","Contents":{"Bed":{"ObjectGUID":"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"},"Supplies":{"ObjectGUID":"bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"},"Pots":{"ObjectGUIDs":["cccccccc-cccc-cccc-cccc-cccccccccccc"]}}}
          ]
        }
        """;

    // The ChemistData employee record from the sanitized OC-128 preserved
    // file: 0.4.7 wrapped BaseData with no Identity key.
    private const string ChemistWrappedEmployeeRawJson = """
        {"DataType":"ChemistData","DataVersion":0,"GameVersion":"0.4.7f6","BaseData":"{\"DataType\":\"ChemistData\",\"DataVersion\":0,\"GameVersion\":\"0.4.7f6\",\"ID\":\"test_chemist\",\"AssignedProperty\":\"oc_fishwarehouse\",\"FirstName\":\"Test\",\"LastName\":\"Chemist\",\"IsMale\":true,\"AppearanceIndex\":6,\"Position\":{\"x\":1,\"y\":2,\"z\":3},\"Rotation\":{\"x\":0,\"y\":0,\"z\":0,\"w\":1},\"GUID\":\"11111111-1111-1111-1111-111111111111\",\"PaidForToday\":false,\"MoveItemData\":{\"TemplateItemJSON\":\"\",\"GrabbedItemQuantity\":0,\"SourceGUID\":\"\",\"DestinationGUID\":\"\"}}","AdditionalDatas":[{"Name":"Relationship","Contents":"{\"RelationDelta\":0}"},{"Name":"Configuration","Contents":"{\"DataType\":\"ChemistConfigurationData\",\"Bed\":{\"ObjectGUID\":\"22222222-2222-2222-2222-222222222222\"},\"Stations\":{\"ObjectGUIDs\":[]}}"}]}
        """;
}
