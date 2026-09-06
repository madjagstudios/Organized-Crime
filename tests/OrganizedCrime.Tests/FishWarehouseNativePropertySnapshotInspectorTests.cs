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
            out var failureReason);

        Assert.False(inspected);
        Assert.Null(snapshot);
        Assert.Contains("inspection failed", failureReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Inspector_reads_wrapped_native_property_records_with_json_string_base_data()
    {
        var inspector = new FishWarehouseNativePropertySnapshotInspector();

        Assert.True(inspector.TryInspect(Encoding.UTF8.GetBytes(WrappedNativePropertyJson), out var snapshot, out var failureReason), failureReason);

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

        Assert.True(inspector.TryInspect(Encoding.UTF8.GetBytes(MeasuredNativePropertyJson), out var snapshot, out var failureReason), failureReason);

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
    public void Inspector_reads_live_packager_schema_with_configuration_fallbacks_without_changing_raw_bytes()
    {
        var inspector = new FishWarehouseNativePropertySnapshotInspector();

        Assert.True(inspector.TryInspect(Encoding.UTF8.GetBytes(LiveNativePropertyJson), out var snapshot, out var failureReason), failureReason);

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
            out var failureReason);

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
            out var failureReason);

        Assert.True(inspected, failureReason);
        Assert.Empty(snapshot.Employees[0].BedGuid);
    }

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
