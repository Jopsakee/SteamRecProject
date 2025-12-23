namespace SteamRec.Web.Services;

public static class SteamImageHelper
{
    private const string CapsuleTemplate = "https://cdn.cloudflare.steamstatic.com/steam/apps/{0}/capsule_184x69.jpg";
    private const string StorePageTemplate = "https://store.steampowered.com/app/{0}/";
    public static string BuildCapsuleUrl(int appId)
    {
        return appId > 0 ? string.Format(CapsuleTemplate, appId) : string.Empty;
    }

    public static string BuildStorePageUrl(int appId)
    {
        return appId > 0 ? string.Format(StorePageTemplate, appId) : string.Empty;
    }
}