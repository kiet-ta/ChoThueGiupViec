namespace CommonService.Application.Features.Workers.Services;

/// <summary>
/// The photo is sent as a URL, not as a file, so the real Variance of Laplacian cannot be measured yet (deferred). Until it is, the
/// quality check runs on a deterministic stand-in image chosen by the URL: a URL containing <c>blur</c>, <c>low_quality</c> or
/// <c>vol_45</c> is a blurry photo, any other URL is a sharp one. Before this, the stand-in was built from the URL text, so a normal URL
/// scored 3 to 60 and was rejected at random (decisions D18).
/// </summary>
public static class JobPhotoStandIn
{
    private const int Pixels = 640 * 480;

    private static readonly string[] BlurMarkers = ["blur", "low_quality", "vol_45"];

    private static readonly byte[] Sharp = BuildSharp();

    public static bool IsBlurry(string photoUrl) =>
        BlurMarkers.Any(m => photoUrl.Contains(m, StringComparison.OrdinalIgnoreCase));

    public static byte[] BytesFor(string photoUrl)
    {
        if (IsBlurry(photoUrl))
        {
            // Constant grey: variance 0.
            var flat = new byte[Pixels];
            Array.Fill(flat, (byte)128);
            return flat;
        }

        return Sharp;
    }

    // Fixed-seed noise: the same sharp image every time, with a Laplacian variance far above the threshold.
    private static byte[] BuildSharp()
    {
        var bytes = new byte[Pixels];
        new Random(20261016).NextBytes(bytes);
        return bytes;
    }
}
