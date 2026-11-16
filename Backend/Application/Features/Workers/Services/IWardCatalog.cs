namespace CommonService.Application.Features.Workers.Services;

/// <summary>One ward, commune or special zone of the post-merger administrative units (WORKERS-WARD-V2-01, #959).</summary>
public record WardInfo(int Code, string Name, int ProvinceCode, string ProvinceName);

/// <summary>
/// The wards of Vietnam since 2025-07-01 (34 provinces/cities, no district level), from a generated list bundled with the
/// backend (<c>scripts/gen-vn-wards.mjs</c>). Nothing is read from the public API at run time.
/// </summary>
public interface IWardCatalog
{
    /// <summary>The ward with this code, or null.</summary>
    WardInfo? FindByCode(int code);

    /// <summary>
    /// The one ward a stored or typed name stands for: a current ward name that exists once in the whole country, otherwise a
    /// pre-merger (legacy) name that was merged into exactly one ward. Null when the name is unknown or ambiguous.
    /// </summary>
    WardInfo? ResolveName(string name);
}
