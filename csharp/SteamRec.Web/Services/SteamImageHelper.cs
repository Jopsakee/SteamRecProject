namespace SteamRec.Web.Services;

public static class SteamImageHelper
{
    private const string CapsuleTemplate = "https://cdn.cloudflare.steamstatic.com/steam/apps/{0}/capsule_184x69.jpg";
    public static string BuildCapsuleUrl(int appId)
    {
        return appId > 0 ? string.Format(CapsuleTemplate, appId) : string.Empty;
    }
}