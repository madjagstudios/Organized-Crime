namespace OrganizedCrime.Persistence;

// Maps native employee save-data types to the identity OC uses downstream.
// The 0.4.7 save carries no Identity key, so the DataType is the source of
// truth; older saves that do carry Identity keep it (callers check first).
//
// FACT (S1Atlas build 3871b66f218aac58be237374e57b3d4f669e3ed6792c1c2373a0b4dbfb52f241,
// index 666fa489c6eb23e0432bf52b621f8db5b4209eab4102d3e56883a9678f8ccd85): the
// EmployeeData subclasses are BotanistData, ChemistData, CleanerData and
// PackagerData, mirroring the Botanist/Chemist/Cleaner/Packager employee
// classes. EEmployeeType additionally names Handler, but there is no
// HandlerData type and no Handler class, so it stays unknown until the
// game ships one. An unknown DataType maps to itself: still unsupported
// downstream, preserved with its raw JSON, and never fatal.
internal static class FishWarehouseEmployeeIdentity
{
    internal static string FromDataType(string dataType) => TryMapDataType(dataType) ?? dataType;

    internal static bool IsSupportedEmployee(string dataType, string identity) =>
        TryMapDataType(dataType) is { } mapped && string.Equals(mapped, identity, StringComparison.Ordinal);

    internal static string? TryMapDataType(string dataType) => dataType switch
    {
        "BotanistData" => "Botanist",
        "ChemistData" => "Chemist",
        "CleanerData" => "Cleaner",
        "PackagerData" => "Packager",
        _ => null,
    };
}
