using HomeShowing.Listings.Contracts;

namespace HomeShowing.Listings.Domain;

/// <summary>A tiny gazetteer for the seeded markets so "near LoDo" works without a geocoding API.</summary>
public static class Places
{
    private static readonly Dictionary<string, GeoPoint> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Denver"] = new(39.7392, -104.9903),
        ["LoDo"] = new(39.7527, -105.0003),
        ["Highlands"] = new(39.7621, -105.0118),
        ["Cherry Creek"] = new(39.7178, -104.9530),
        ["Wash Park"] = new(39.7003, -104.9732),
        ["Washington Park"] = new(39.7003, -104.9732),
        ["RiNo"] = new(39.7680, -104.9800),
        ["Boulder"] = new(40.0150, -105.2705),
        ["Austin"] = new(30.2672, -97.7431),
        ["South Congress"] = new(30.2457, -97.7500),
        ["SoCo"] = new(30.2457, -97.7500),
        ["Hyde Park"] = new(30.3060, -97.7280),
        ["Toronto"] = new(43.6532, -79.3832),
        ["Liberty Village"] = new(43.6376, -79.4210),
        ["The Annex"] = new(43.6680, -79.4040),
        ["Annex"] = new(43.6680, -79.4040),
        ["Leslieville"] = new(43.6626, -79.3322),
    };

    public static IReadOnlyCollection<string> Names => Known.Keys;

    public static bool TryResolve(string? name, out GeoPoint point)
    {
        point = new GeoPoint(0, 0);
        if (string.IsNullOrWhiteSpace(name)) return false;
        var key = name.Trim();
        if (Known.TryGetValue(key, out var p)) { point = p; return true; }
        // "LoDo, Denver" or "Denver, CO": try each part.
        foreach (var part in key.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            if (Known.TryGetValue(part, out p)) { point = p; return true; }
        return false;
    }
}
