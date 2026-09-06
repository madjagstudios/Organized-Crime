namespace OrganizedCrime.PropertyProbe.Model;

public static class BuildingCensusMatcher
{
    public static bool MatchesName(string? gameObjectName, params string[] terms)
    {
        if (string.IsNullOrWhiteSpace(gameObjectName) || terms.Length == 0)
            return false;

        return terms.Any(term =>
            !string.IsNullOrWhiteSpace(term) &&
            gameObjectName.Contains(term, StringComparison.OrdinalIgnoreCase));
    }
}
