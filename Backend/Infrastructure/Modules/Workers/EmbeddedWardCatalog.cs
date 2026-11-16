using System.Reflection;
using System.Text.Json;
using CommonService.Application.Features.Workers.Services;

namespace CommonService.Infrastructure.Modules.Workers;

/// <summary>
/// <see cref="IWardCatalog"/> over the embedded <c>vn-wards.json</c> (WORKERS-WARD-V2-01, #959): read once, kept in memory.
/// Names are compared without case and with the usual apostrophe variants treated alike (the source mixes <c>'</c> and <c>’</c>).
/// </summary>
public class EmbeddedWardCatalog : IWardCatalog
{
    private const string ResourceName = "vn-wards.json";

    private sealed record Data(Dictionary<int, WardInfo> ByCode, Dictionary<string, List<WardInfo>> ByCurrentName, Dictionary<string, List<WardInfo>> ByLegacyName);

    private readonly Lazy<Data> _data = new(Load, isThreadSafe: true);

    public WardInfo? FindByCode(int code) => _data.Value.ByCode.GetValueOrDefault(code);

    public WardInfo? ResolveName(string name)
    {
        var key = Normalize(name);
        if (key.Length == 0)
        {
            return null;
        }

        var data = _data.Value;
        if (data.ByCurrentName.TryGetValue(key, out var current))
        {
            return current.Count == 1 ? current[0] : null;
        }

        return data.ByLegacyName.TryGetValue(key, out var legacy) && legacy.Count == 1 ? legacy[0] : null;
    }

    private static string Normalize(string name) =>
        name.Trim().Replace('’', '\'').Replace('‘', '\'').ToLowerInvariant();

    private static Data Load()
    {
        using var stream = typeof(EmbeddedWardCatalog).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{ResourceName}' is missing.");
        using var doc = JsonDocument.Parse(stream);
        var root = doc.RootElement;

        var provinces = root.GetProperty("provinces").EnumerateArray()
            .ToDictionary(p => p.GetProperty("code").GetInt32(), p => p.GetProperty("name").GetString()!);

        var byCode = new Dictionary<int, WardInfo>();
        var byCurrent = new Dictionary<string, List<WardInfo>>();
        var byLegacy = new Dictionary<string, List<WardInfo>>();
        foreach (var w in root.GetProperty("wards").EnumerateArray())
        {
            var provinceCode = w.GetProperty("provinceCode").GetInt32();
            var ward = new WardInfo(w.GetProperty("code").GetInt32(), w.GetProperty("name").GetString()!, provinceCode, provinces[provinceCode]);
            byCode[ward.Code] = ward;
            Add(byCurrent, Normalize(ward.Name), ward);
            foreach (var legacy in w.GetProperty("legacy").EnumerateArray())
            {
                Add(byLegacy, Normalize(legacy.GetString()!), ward);
            }
        }

        return new Data(byCode, byCurrent, byLegacy);
    }

    private static void Add(Dictionary<string, List<WardInfo>> map, string key, WardInfo ward)
    {
        if (!map.TryGetValue(key, out var list))
        {
            map[key] = list = [];
        }

        if (!list.Contains(ward))
        {
            list.Add(ward);
        }
    }
}
