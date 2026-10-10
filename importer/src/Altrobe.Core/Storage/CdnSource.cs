using System.Text.RegularExpressions;
using Altrobe.Core.Hosted;

namespace Altrobe.Core.Storage;

// Where a hosted bake reads a build from: Blizzard's CDN, by the build and CDN config hashes the
// version service lists for a product and region. No install folder is involved.
public sealed partial record CdnSource
{
    public CdnSource(string product, string region, string buildConfig, string cdnConfig, string version, string cdnPath = "tpr/wow")
    {
        if (!Hash().IsMatch(buildConfig)) throw new ArgumentException($"Build config \"{buildConfig}\" is not a 32-digit hex hash.", nameof(buildConfig));
        if (!Hash().IsMatch(cdnConfig)) throw new ArgumentException($"CDN config \"{cdnConfig}\" is not a 32-digit hex hash.", nameof(cdnConfig));
        (Product, Region, BuildConfig, CdnConfig, Version, CdnPath) = (product, region, buildConfig, cdnConfig, version, cdnPath);
    }

    public string Product { get; }
    public string Region { get; }
    public string BuildConfig { get; }
    public string CdnConfig { get; }
    // The build's version string ("1.60.1.70235"), as the local app names builds.
    public string Version { get; }
    // The CDN's folder for the product family; every WoW product uses tpr/wow.
    public string CdnPath { get; }

    public static CdnSource From(string product, VersionEntry entry) =>
        new(product, entry.Region, entry.BuildConfig, entry.CdnConfig, entry.VersionsName);

    [GeneratedRegex("^[0-9a-f]{32}$")]
    private static partial Regex Hash();
}
