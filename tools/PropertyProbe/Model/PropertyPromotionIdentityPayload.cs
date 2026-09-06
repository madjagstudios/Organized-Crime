using System.Text.Json;

namespace OrganizedCrime.PropertyProbe.Model;

public static class PropertyPromotionIdentityPayload
{
    public const string PropertyCode = "oc_fishwarehouse";
    public const string PropertyName = "Fish Warehouse";

    public static string Json => JsonSerializer.Serialize(new
    {
        propertyCode = PropertyCode,
        propertyName = PropertyName
    });
}
