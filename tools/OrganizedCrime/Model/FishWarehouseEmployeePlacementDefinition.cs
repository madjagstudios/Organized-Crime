namespace OrganizedCrime.Model;

public static class FishWarehouseEmployeePlacementDefinition
{
    public static bool IsSameNativeObject(int leftInstanceId, int rightInstanceId) =>
        leftInstanceId != 0 && leftInstanceId == rightInstanceId;
}
