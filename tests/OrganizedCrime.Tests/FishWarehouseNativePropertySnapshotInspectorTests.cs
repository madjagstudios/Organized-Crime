using System.Text;
using OrganizedCrime.Persistence;
using Xunit;

namespace OrganizedCrime.Tests;

public sealed class FishWarehouseNativePropertySnapshotInspectorTests
{
    [Fact]
    public void Inspector_returns_a_capture_failure_for_valid_json_missing_a_required_field()
    {
        var inspector = new FishWarehouseNativePropertySnapshotInspector();
        var incompleteRequiredFieldJson = """
            {
              "PropertyCode": "oc_fishwarehouse",
              "IsOwned": true,
              "Objects": []
            }
            """;

        var inspected = inspector.TryInspect(
            Encoding.UTF8.GetBytes(incompleteRequiredFieldJson),
            out var snapshot,
            out var failureReason,
            out var failureDetail);

        Assert.False(inspected);
        Assert.Null(snapshot);
        Assert.Contains("inspection failed", failureReason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Employees", failureReason, StringComparison.Ordinal);
        Assert.Contains("JsonException", failureDetail, StringComparison.Ordinal);
        Assert.Contains("TryInspect", failureDetail, StringComparison.Ordinal);
    }

    [Fact]
    public void Inspector_reads_wrapped_native_property_records_with_json_string_base_data()
    {
        var inspector = new FishWarehouseNativePropertySnapshotInspector();

        Assert.True(inspector.TryInspect(Encoding.UTF8.GetBytes(WrappedNativePropertyJson), out var snapshot, out var failureReason, out _), failureReason);

        Assert.Equal("locker-object-guid", snapshot.Objects[0].Guid);
        Assert.Equal("fish-warehouse-grid-guid", snapshot.Objects[0].GridGuid);
        Assert.Equal(2, snapshot.Objects[0].LoadOrder);
        Assert.Equal("locker", snapshot.Objects[0].ItemId);
        Assert.True(snapshot.Objects[0].IsSavedHome);
        Assert.Equal(
            """{"DataType":"ObjectData","DataVersion":1,"GameVersion":"0.4.10f1","BaseData":"{\"GUID\":\"locker-object-guid\",\"GridGUID\":\"fish-warehouse-grid-guid\",\"LoadOrder\":2,\"ItemString\":\"{\\\"ID\\\":\\\"locker\\\"}\"}","AdditionalDatas":[]}""",
            snapshot.Objects[0].RawJson);

        Assert.Equal("packager-employee-guid", snapshot.Employees[0].Guid);
        Assert.Equal("PackagerData", snapshot.Employees[0].DataType);
        Assert.Equal("Packager", snapshot.Employees[0].Identity);
        Assert.False(snapshot.Employees[0].PaidForToday);
        Assert.Equal("locker-object-guid", snapshot.Employees[0].BedGuid);
        Assert.Equal(new[] { "packaging-station-object-guid" }, snapshot.Employees[0].StationGuids);
        Assert.Equal(
            """{"DataType":"PackagerData","DataVersion":1,"GameVersion":"0.4.10f1","BaseData":"{\"GUID\":\"packager-employee-guid\",\"Identity\":\"Packager\",\"PaidForToday\":false,\"BedGUID\":\"locker-object-guid\",\"Position\":{\"x\":1,\"y\":2,\"z\":3},\"Rotation\":{\"x\":0,\"y\":0,\"z\":0,\"w\":1}}","AdditionalDatas":[{"Name":"Configuration","Contents":"{\"StationGUIDs\":[\"packaging-station-object-guid\"]}"}]}""",
            snapshot.Employees[0].RawJson);
    }

    [Fact]
    public void Inspector_preserves_the_measured_native_property_records_without_reserializing()
    {
        var inspector = new FishWarehouseNativePropertySnapshotInspector();

        Assert.True(inspector.TryInspect(Encoding.UTF8.GetBytes(MeasuredNativePropertyJson), out var snapshot, out var failureReason, out _), failureReason);

        Assert.Equal("oc_fishwarehouse", snapshot.PropertyCode);
        Assert.True(snapshot.IsOwned);
        Assert.Equal(2, snapshot.Objects.Count);
        Assert.Single(snapshot.Employees);

        var locker = snapshot.Objects[0];
        Assert.Equal("locker-object-guid", locker.Guid);
        Assert.Equal("fish-warehouse-grid-guid", locker.GridGuid);
        Assert.Equal(2, locker.LoadOrder);
        Assert.Equal("locker", locker.ItemId);
        Assert.True(locker.IsSavedHome);
        Assert.Equal("{\"GUID\":\"locker-object-guid\",\"GridGUID\":\"fish-warehouse-grid-guid\",\"LoadOrder\":2,\"ItemString\":{\"ID\":\"locker\"}}", locker.RawJson);

        var packagingStation = snapshot.Objects[1];
        Assert.Equal("packaging-station-object-guid", packagingStation.Guid);
        Assert.Equal("fish-warehouse-grid-guid", packagingStation.GridGuid);
        Assert.Equal(5, packagingStation.LoadOrder);
        Assert.Equal("packagingstation", packagingStation.ItemId);
        Assert.False(packagingStation.IsSavedHome);
        Assert.Equal("{\"GUID\":\"packaging-station-object-guid\",\"GridGUID\":\"fish-warehouse-grid-guid\",\"LoadOrder\":5,\"ItemString\":{\"ID\":\"packagingstation\"}}", packagingStation.RawJson);

        var employee = snapshot.Employees[0];
        Assert.Equal("packager-employee-guid", employee.Guid);
        Assert.Equal("PackagerData", employee.DataType);
        Assert.Equal("Packager", employee.Identity);
        Assert.False(employee.PaidForToday);
        Assert.Equal("locker-object-guid", employee.BedGuid);
        Assert.Equal(new[] { "packaging-station-object-guid", "packaging-station-backup-guid" }, employee.StationGuids);
        Assert.Equal("{\"DataType\":\"PackagerData\",\"BaseData\":{\"GUID\":\"packager-employee-guid\",\"Identity\":\"Packager\",\"PaidForToday\":false,\"BedGUID\":\"locker-object-guid\"},\"AdditionalDatas\":[{\"Name\":\"Configuration\",\"Contents\":{\"StationGUIDs\":[\"packaging-station-object-guid\",\"packaging-station-backup-guid\"]}}]}", employee.RawJson);
    }

    [Fact]
    public void Preserved_chemist_file_inspects_with_one_locker_and_one_chemist()
    {
        // Sanitized transcription of the OC-128 owner run: a 0.4.7 wrapped
        // ChemistData record with no Identity key plus its locker. This file
        // failed the whole warehouse before the identity map.
        var inspector = new FishWarehouseNativePropertySnapshotInspector();

        Assert.True(inspector.TryInspect(Encoding.UTF8.GetBytes(ChemistWrappedNativePropertyJson), out var snapshot, out var failureReason, out _), failureReason);

        var locker = Assert.Single(snapshot.Objects);
        Assert.Equal("22222222-2222-2222-2222-222222222222", locker.Guid);
        Assert.Equal("33333333-3333-3333-3333-333333333333", locker.GridGuid);
        Assert.Equal(0, locker.LoadOrder);
        Assert.Equal("locker", locker.ItemId);
        Assert.True(locker.IsSavedHome);

        var chemist = Assert.Single(snapshot.Employees);
        Assert.Equal("11111111-1111-1111-1111-111111111111", chemist.Guid);
        Assert.Equal("ChemistData", chemist.DataType);
        Assert.Equal("Chemist", chemist.Identity);
        Assert.False(chemist.PaidForToday);
        Assert.Equal("22222222-2222-2222-2222-222222222222", chemist.BedGuid);
        Assert.Empty(chemist.StationGuids);
    }

    [Theory]
    [InlineData("BotanistData", "Botanist")]
    [InlineData("ChemistData", "Chemist")]
    [InlineData("CleanerData", "Cleaner")]
    [InlineData("PackagerData", "Packager")]
    public void Known_employee_data_type_maps_to_its_identity(string dataType, string identity)
    {
        var inspector = new FishWarehouseNativePropertySnapshotInspector();
        var json = NativePropertyJsonWithEmployees(EmployeeRecordJson(dataType, "55555555-5555-5555-5555-555555555555"));

        Assert.True(inspector.TryInspect(Encoding.UTF8.GetBytes(json), out var snapshot, out var failureReason, out _), failureReason);

        var employee = Assert.Single(snapshot.Employees);
        Assert.Equal(dataType, employee.DataType);
        Assert.Equal(identity, employee.Identity);
    }

    [Fact]
    public void Unknown_employee_data_type_loads_with_itself_as_identity_and_keeps_the_rest()
    {
        // HandlerData: EEmployeeType names a Handler but the game ships no
        // HandlerData, so OC treats it as unknown: loadable, kept with its
        // raw JSON, and preserved as unsupported downstream.
        var inspector = new FishWarehouseNativePropertySnapshotInspector();
        var json = NativePropertyJsonWithEmployees(
            EmployeeRecordJson("PackagerData", "66666666-6666-6666-6666-666666666666"),
            EmployeeRecordJson("HandlerData", "77777777-7777-7777-7777-777777777777"));

        Assert.True(inspector.TryInspect(Encoding.UTF8.GetBytes(json), out var snapshot, out var failureReason, out _), failureReason);

        Assert.Equal(2, snapshot.Employees.Count);
        Assert.Equal("Packager", snapshot.Employees[0].Identity);
        Assert.Equal("HandlerData", snapshot.Employees[1].DataType);
        Assert.Equal("HandlerData", snapshot.Employees[1].Identity);
        Assert.Contains("77777777-7777-7777-7777-777777777777", snapshot.Employees[1].RawJson, StringComparison.Ordinal);
    }

    [Fact]
    public void Inspector_reads_live_packager_schema_with_configuration_fallbacks_without_changing_raw_bytes()
    {
        var inspector = new FishWarehouseNativePropertySnapshotInspector();

        Assert.True(inspector.TryInspect(Encoding.UTF8.GetBytes(LiveNativePropertyJson), out var snapshot, out var failureReason, out _), failureReason);

        var employee = Assert.Single(snapshot.Employees);
        Assert.Equal("packager-employee-guid", employee.Guid);
        Assert.Equal("PackagerData", employee.DataType);
        Assert.Equal("Packager", employee.Identity);
        Assert.False(employee.PaidForToday);
        Assert.Equal("locker-object-guid", employee.BedGuid);
        Assert.Equal(new[]
        {
            "packaging-station-object-guid",
            "packaging-station-backup-guid",
            "packaging-station-third-guid"
        }, employee.StationGuids);
        Assert.Equal(LiveEmployeeRawJson, employee.RawJson);
    }

    [Fact]
    public void Inspector_rejects_present_invalid_identity_instead_of_using_packager_fallback()
    {
        var inspector = new FishWarehouseNativePropertySnapshotInspector();
        var invalidIdentityJson = LiveNativePropertyJson.Replace(
            "\\\"AssignedProperty\\\":\\\"oc_fishwarehouse\\\"",
            "\\\"Identity\\\":\\\"\\\",\\\"AssignedProperty\\\":\\\"oc_fishwarehouse\\\"",
            StringComparison.Ordinal);

        var inspected = inspector.TryInspect(
            Encoding.UTF8.GetBytes(invalidIdentityJson),
            out _,
            out var failureReason,
            out _);

        Assert.False(inspected);
        Assert.Contains("Identity", failureReason, StringComparison.Ordinal);
    }

    [Fact]
    public void Inspector_accepts_an_explicitly_empty_configuration_bed_as_no_home()
    {
        var inspector = new FishWarehouseNativePropertySnapshotInspector();
        var noHomeJson = LiveNativePropertyJson.Replace(
            "\\\"ObjectGUID\\\":\\\"locker-object-guid\\\"",
            "\\\"ObjectGUID\\\":\\\"\\\"",
            StringComparison.Ordinal);

        var inspected = inspector.TryInspect(
            Encoding.UTF8.GetBytes(noHomeJson),
            out var snapshot,
            out var failureReason,
            out _);

        Assert.True(inspected, failureReason);
        Assert.Empty(snapshot.Employees[0].BedGuid);
    }

    [Fact]
    public void Employee_without_a_bed_section_inspects_as_unassigned()
    {
        var json = GaryPropertyJson.Replace(
            "\"Bed\":{\"ObjectGUID\":\"df416556-9780-4c63-9de2-4e265315c5e7\"},",
            string.Empty,
            StringComparison.Ordinal);
        Assert.DoesNotContain("\"Bed\"", json, StringComparison.Ordinal);
        var inspector = new FishWarehouseNativePropertySnapshotInspector();

        var inspected = inspector.TryInspect(Encoding.UTF8.GetBytes(json), out var snapshot, out var failureReason, out _);

        Assert.True(inspected, failureReason);
        Assert.Empty(snapshot.Employees[0].BedGuid);
    }

    [Fact]
    public void Employee_without_a_configuration_entry_inspects_as_unassigned()
    {
        var json = GaryPropertyJson.Replace(
            "{\"Name\":\"Configuration\",\"Contents\":{\"Bed\":{\"ObjectGUID\":\"df416556-9780-4c63-9de2-4e265315c5e7\"},\"Stations\":{\"ObjectGUIDs\":[]}}}",
            "{\"Name\":\"Relationship\",\"Contents\":{\"RelationDelta\":0}}",
            StringComparison.Ordinal);
        Assert.DoesNotContain("\"Configuration\"", json, StringComparison.Ordinal);
        var inspector = new FishWarehouseNativePropertySnapshotInspector();

        var inspected = inspector.TryInspect(Encoding.UTF8.GetBytes(json), out var snapshot, out var failureReason, out _);

        Assert.True(inspected, failureReason);
        Assert.Empty(snapshot.Employees[0].BedGuid);
        Assert.Empty(snapshot.Employees[0].StationGuids);
    }

    [Fact]
    public void Employee_without_a_stations_section_inspects_with_no_stations()
    {
        var json = GaryPropertyJson.Replace(
            ",\"Stations\":{\"ObjectGUIDs\":[]}",
            string.Empty,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Stations", json, StringComparison.Ordinal);
        var inspector = new FishWarehouseNativePropertySnapshotInspector();

        var inspected = inspector.TryInspect(Encoding.UTF8.GetBytes(json), out var snapshot, out var failureReason, out _);

        Assert.True(inspected, failureReason);
        Assert.Empty(snapshot.Employees[0].StationGuids);
        Assert.Equal("df416556-9780-4c63-9de2-4e265315c5e7", snapshot.Employees[0].BedGuid);
    }

    [Fact]
    public void Employee_without_paid_for_today_fails_naming_the_field()
    {
        var json = GaryPropertyJson.Replace(
            ",\"PaidForToday\":false",
            string.Empty,
            StringComparison.Ordinal);
        Assert.DoesNotContain("PaidForToday", json, StringComparison.Ordinal);
        var inspector = new FishWarehouseNativePropertySnapshotInspector();

        var inspected = inspector.TryInspect(Encoding.UTF8.GetBytes(json), out _, out var failureReason, out _);

        Assert.False(inspected);
        Assert.Contains("PaidForToday", failureReason, StringComparison.Ordinal);
    }

    [Fact]
    public void Locker_without_load_order_fails_naming_the_field()
    {
        var json = GaryPropertyJson.Replace(
            "\"LoadOrder\":1,",
            string.Empty,
            StringComparison.Ordinal);
        Assert.DoesNotContain("LoadOrder", json, StringComparison.Ordinal);
        var inspector = new FishWarehouseNativePropertySnapshotInspector();

        var inspected = inspector.TryInspect(Encoding.UTF8.GetBytes(json), out _, out var failureReason, out _);

        Assert.False(inspected);
        Assert.Contains("LoadOrder", failureReason, StringComparison.Ordinal);
    }

    [Fact]
    public void Locker_without_item_id_fails_naming_the_field()
    {
        var json = GaryPropertyJson.Replace(
            "\"ItemString\":{\"ID\":\"locker\"}",
            "\"ItemString\":{}",
            StringComparison.Ordinal);
        var inspector = new FishWarehouseNativePropertySnapshotInspector();

        var inspected = inspector.TryInspect(Encoding.UTF8.GetBytes(json), out _, out var failureReason, out _);

        Assert.False(inspected);
        Assert.Contains("ID", failureReason, StringComparison.Ordinal);
    }

    // Gary Pinkman with a player-placed locker, shaped like the kept
    // SaveGame_5 post-recovery PackagerData record (string-wrapped BaseData
    // and Contents flattened to objects; the inspector accepts both).
    private const string GaryPropertyJson = """
        {
          "PropertyCode": "oc_fishwarehouse",
          "IsOwned": true,
          "Objects": [
            {"GUID":"df416556-9780-4c63-9de2-4e265315c5e7","GridGUID":"d8b7d799-90f2-413c-83d3-6adcbec07f0b","LoadOrder":1,"ItemString":{"ID":"locker"}}
          ],
          "Employees": [
            {"DataType":"PackagerData","BaseData":{"ID":"gary_pinkman","FirstName":"Gary","LastName":"Pinkman","GUID":"854a259d-5b33-4577-8ac7-ede95f89302f","AssignedProperty":"oc_fishwarehouse","PaidForToday":false},"AdditionalDatas":[{"Name":"Relationship","Contents":{"RelationDelta":0}},{"Name":"Configuration","Contents":{"Bed":{"ObjectGUID":"df416556-9780-4c63-9de2-4e265315c5e7"},"Stations":{"ObjectGUIDs":[]}}}]}
          ]
        }
        """;

    private static string NativePropertyJsonWithEmployees(params string[] records) =>
        """
        {
          "PropertyCode": "oc_fishwarehouse",
          "IsOwned": true,
          "Objects": [],
          "Employees": [
        """ + "    " + string.Join(",\n    ", records) + """
          ]
        }
        """;

    private static string EmployeeRecordJson(string dataType, string guid) =>
        $$"""{"DataType":"{{dataType}}","BaseData":{"GUID":"{{guid}}","PaidForToday":false},"AdditionalDatas":[]}""";

    // Sanitized transcription of the preserved OC-128 failing file
    // (fish-warehouse.failed-inspection.20260930-011500-630.json): the 0.4.7
    // wrapped shape, a PlaceableStorageData locker plus one ChemistData
    // employee with no Identity key. GUIDs, names and positions are
    // synthetic; the key sets and nesting match the original.
    private const string ChemistWrappedNativePropertyJson = """
        {
          "DataType": "PropertyData",
          "DataVersion": 0,
          "GameVersion": "0.4.7f6",
          "PropertyCode": "oc_fishwarehouse",
          "IsOwned": true,
          "Employees": [
            {"DataType":"ChemistData","DataVersion":0,"GameVersion":"0.4.7f6","BaseData":"{\"DataType\":\"ChemistData\",\"DataVersion\":0,\"GameVersion\":\"0.4.7f6\",\"ID\":\"test_chemist\",\"AssignedProperty\":\"oc_fishwarehouse\",\"FirstName\":\"Test\",\"LastName\":\"Chemist\",\"IsMale\":true,\"AppearanceIndex\":6,\"Position\":{\"x\":1,\"y\":2,\"z\":3},\"Rotation\":{\"x\":0,\"y\":0,\"z\":0,\"w\":1},\"GUID\":\"11111111-1111-1111-1111-111111111111\",\"PaidForToday\":false,\"MoveItemData\":{\"TemplateItemJSON\":\"\",\"GrabbedItemQuantity\":0,\"SourceGUID\":\"\",\"DestinationGUID\":\"\"}}","AdditionalDatas":[{"Name":"Relationship","Contents":"{\"RelationDelta\":0}"},{"Name":"Configuration","Contents":"{\"DataType\":\"ChemistConfigurationData\",\"Bed\":{\"ObjectGUID\":\"22222222-2222-2222-2222-222222222222\"},\"Stations\":{\"ObjectGUIDs\":[]}}"}]}
          ],
          "Objects": [
            {"DataType":"PlaceableStorageData","DataVersion":0,"GameVersion":"0.4.7f6","BaseData":"{\"DataType\":\"PlaceableStorageData\",\"DataVersion\":0,\"GameVersion\":\"0.4.7f6\",\"GUID\":\"22222222-2222-2222-2222-222222222222\",\"ItemString\":\"{\\\"ID\\\":\\\"locker\\\"}\",\"LoadOrder\":0,\"GridGUID\":\"33333333-3333-3333-3333-333333333333\"}","AdditionalDatas":[]}
          ]
        }
        """;

    private const string MeasuredNativePropertyJson = """
        {
          "PropertyCode": "oc_fishwarehouse",
          "IsOwned": true,
          "Objects": [
            {"GUID":"locker-object-guid","GridGUID":"fish-warehouse-grid-guid","LoadOrder":2,"ItemString":{"ID":"locker"}},
            {"GUID":"packaging-station-object-guid","GridGUID":"fish-warehouse-grid-guid","LoadOrder":5,"ItemString":{"ID":"packagingstation"}}
          ],
          "Employees": [
            {"DataType":"PackagerData","BaseData":{"GUID":"packager-employee-guid","Identity":"Packager","PaidForToday":false,"BedGUID":"locker-object-guid"},"AdditionalDatas":[{"Name":"Configuration","Contents":{"StationGUIDs":["packaging-station-object-guid","packaging-station-backup-guid"]}}]}
          ]
        }
        """;

    private const string WrappedNativePropertyJson = """
        {
          "PropertyCode": "oc_fishwarehouse",
          "IsOwned": true,
          "Objects": [
            {"DataType":"ObjectData","DataVersion":1,"GameVersion":"0.4.10f1","BaseData":"{\"GUID\":\"locker-object-guid\",\"GridGUID\":\"fish-warehouse-grid-guid\",\"LoadOrder\":2,\"ItemString\":\"{\\\"ID\\\":\\\"locker\\\"}\"}","AdditionalDatas":[]}
          ],
          "Employees": [
            {"DataType":"PackagerData","DataVersion":1,"GameVersion":"0.4.10f1","BaseData":"{\"GUID\":\"packager-employee-guid\",\"Identity\":\"Packager\",\"PaidForToday\":false,\"BedGUID\":\"locker-object-guid\",\"Position\":{\"x\":1,\"y\":2,\"z\":3},\"Rotation\":{\"x\":0,\"y\":0,\"z\":0,\"w\":1}}","AdditionalDatas":[{"Name":"Configuration","Contents":"{\"StationGUIDs\":[\"packaging-station-object-guid\"]}"}]}
          ]
        }
        """;

    private const string LiveNativePropertyJson = """
        {
          "PropertyCode": "oc_fishwarehouse",
          "IsOwned": true,
          "Objects": [],
          "Employees": [
            {"DataType":"PackagerData","DataVersion":1,"GameVersion":"0.4.10f1","BaseData":"{\"DataType\":\"PackagerData\",\"ID\":\"david_adams\",\"AssignedProperty\":\"oc_fishwarehouse\",\"FirstName\":\"David\",\"LastName\":\"Adams\",\"IsMale\":true,\"AppearanceIndex\":0,\"Position\":{\"x\":1,\"y\":2,\"z\":3},\"Rotation\":{\"x\":0,\"y\":0,\"z\":0,\"w\":1},\"GUID\":\"packager-employee-guid\",\"PaidForToday\":false,\"MoveItemData\":{\"SourceGUID\":\"source-guid\",\"DestinationGUID\":\"destination-guid\",\"TemplateItemJSON\":\"{\\\"item\\\":\\\"fish\\\"}\",\"GrabbedItemQuantity\":2}}","AdditionalDatas":[{"Name":"Configuration","Contents":"{\"DataType\":\"ConfigurationData\",\"Bed\":{\"ObjectGUID\":\"locker-object-guid\"},\"Stations\":{\"ObjectGUIDs\":[\"packaging-station-object-guid\",\"packaging-station-backup-guid\",\"packaging-station-third-guid\"]},\"Routes\":[]}"}]}
          ]
        }
        """;

    private const string LiveEmployeeRawJson = """{"DataType":"PackagerData","DataVersion":1,"GameVersion":"0.4.10f1","BaseData":"{\"DataType\":\"PackagerData\",\"ID\":\"david_adams\",\"AssignedProperty\":\"oc_fishwarehouse\",\"FirstName\":\"David\",\"LastName\":\"Adams\",\"IsMale\":true,\"AppearanceIndex\":0,\"Position\":{\"x\":1,\"y\":2,\"z\":3},\"Rotation\":{\"x\":0,\"y\":0,\"z\":0,\"w\":1},\"GUID\":\"packager-employee-guid\",\"PaidForToday\":false,\"MoveItemData\":{\"SourceGUID\":\"source-guid\",\"DestinationGUID\":\"destination-guid\",\"TemplateItemJSON\":\"{\\\"item\\\":\\\"fish\\\"}\",\"GrabbedItemQuantity\":2}}","AdditionalDatas":[{"Name":"Configuration","Contents":"{\"DataType\":\"ConfigurationData\",\"Bed\":{\"ObjectGUID\":\"locker-object-guid\"},\"Stations\":{\"ObjectGUIDs\":[\"packaging-station-object-guid\",\"packaging-station-backup-guid\",\"packaging-station-third-guid\"]},\"Routes\":[]}"}]}""";
}
