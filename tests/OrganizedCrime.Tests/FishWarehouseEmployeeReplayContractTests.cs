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
}
