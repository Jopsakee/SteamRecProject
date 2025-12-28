using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace SteamRec.Web.Services;

public class SteamProfileService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;

    public SteamProfileService(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _configuration = configuration;
    }

    public class OwnedGame
    {
        public int appid { get; set; }
        public int playtime_forever { get; set; }

        public int playtime_2weeks { get; set; }
    }

    private class OwnedGamesInnerResponse
    {
        public List<OwnedGame>? games { get; set; }
        public int game_count { get; set; }
    }

    private class OwnedGamesResponse
    {
        public OwnedGamesInnerResponse? response { get; set; }
    }

    private class PlayerSummary
    {
        public int communityvisibilitystate { get; set; }
    }

    private class PlayerSummariesInnerResponse
    {
        public List<PlayerSummary>? players { get; set; }
    }

    private class PlayerSummariesResponse
    {
        public PlayerSummariesInnerResponse? response { get; set; }
    }

    private class VanityResponse
    {
        public VanityInnerResponse? response { get; set; }
    }

    private class VanityInnerResponse
    {
        public string? steamid { get; set; }
        public int success { get; set; }
    }

    public class PrivateProfileException : Exception
    {
        public PrivateProfileException(string message) : base(message) { }
    }

    public class InvalidSteamIdException : Exception
    {
        public InvalidSteamIdException(string message) : base(message) { }
    }

    public async Task<string> ResolveSteamIdAsync(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return "";
        }

        var trimmed = input.Trim();
        if (IsSteamId64(trimmed))
        {
            return trimmed;
        }

        if (TryParseProfileUrl(trimmed, out var steamId, out var vanity))
        {
            if (!string.IsNullOrWhiteSpace(steamId))
            {
                return steamId!;
            }

            if (!string.IsNullOrWhiteSpace(vanity))
            {
                return await ResolveVanityUrlAsync(vanity!);
            }
        }

        if (LooksLikeVanityName(trimmed))
        {
            return await ResolveVanityUrlAsync(trimmed);
        }

        throw new InvalidSteamIdException("Enter a SteamID64 or a steamcommunity.com profile URL.");
    }

    public async Task<List<OwnedGame>> GetOwnedGamesAsync(string steamId)
    {
        if (string.IsNullOrWhiteSpace(steamId))
            return new List<OwnedGame>();

        var apiKey = GetApiKey();

        await EnsureProfileIsPublicAsync(apiKey, steamId);
        var url =
            $"https://api.steampowered.com/IPlayerService/GetOwnedGames/v1/?" +
            $"key={Uri.EscapeDataString(apiKey)}&steamid={Uri.EscapeDataString(steamId)}" +
            "&include_appinfo=1&include_played_free_games=1";

        var resp = await _httpClient.GetAsync(url);
        if (resp.StatusCode == HttpStatusCode.Forbidden)
        {
            throw new PrivateProfileException("This Steam profile is private; game library cannot be read. Refer to the guide below to make it public.");
        }
        resp.EnsureSuccessStatusCode();

        var json = await resp.Content.ReadAsStringAsync();

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var data = JsonSerializer.Deserialize<OwnedGamesResponse>(json, options);

        if (data?.response?.games == null || data.response.game_count == 0)
        {
            throw new PrivateProfileException("This Steam profile's game details are private or could not be loaded. Refer to the guide below to make it public.");
        }

        var games = data.response.games;

        // If everything came back with zero playtime, treat it as private/friends-only game details.
        var hasAnyPlaytime = games.Any(g => g.playtime_forever > 0 || g.playtime_2weeks > 0);
        if (!hasAnyPlaytime)
        {
            throw new PrivateProfileException("This Steam profile's game details are private or could not be loaded. Refer to the guide below to make it public.");
        }

        return games;
    }

    private async Task EnsureProfileIsPublicAsync(string apiKey, string steamId)
    {
        var url =
            $"https://api.steampowered.com/ISteamUser/GetPlayerSummaries/v0002/?" +
            $"key={Uri.EscapeDataString(apiKey)}&steamids={Uri.EscapeDataString(steamId)}";

        var resp = await _httpClient.GetAsync(url);

        if (resp.StatusCode == HttpStatusCode.Forbidden)
        {
            throw new PrivateProfileException("This Steam profile is private; game library cannot be read. Refer to the guide below to make it public.");
        }

        resp.EnsureSuccessStatusCode();

        var json = await resp.Content.ReadAsStringAsync();
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var data = JsonSerializer.Deserialize<PlayerSummariesResponse>(json, options);

        var visibility = data?.response?.players?.FirstOrDefault()?.communityvisibilitystate ?? 0;

        // Steam visibility: 1 = Private, 2 = Friends Only, 3 = Public
        if (visibility != 3)
            throw new PrivateProfileException("This Steam profile is private; game library cannot be read. Refer to the guide below to make it public.");
    }

    private string GetApiKey()
    {
        var apiKey = _configuration["Steam:ApiKey"]
                     ?? Environment.GetEnvironmentVariable("STEAM_WEB_API_KEY");

        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("Steam Web API key not configured. Set Steam:ApiKey or STEAM_WEB_API_KEY.");

        return apiKey;
    }

    private async Task<string> ResolveVanityUrlAsync(string vanity)
    {
        var apiKey = GetApiKey();
        var url =
            "https://api.steampowered.com/ISteamUser/ResolveVanityURL/v0001/?" +
            $"key={Uri.EscapeDataString(apiKey)}&vanityurl={Uri.EscapeDataString(vanity)}";

        var resp = await _httpClient.GetAsync(url);
        resp.EnsureSuccessStatusCode();

        var json = await resp.Content.ReadAsStringAsync();
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var data = JsonSerializer.Deserialize<VanityResponse>(json, options);

        if (data?.response?.success != 1 || string.IsNullOrWhiteSpace(data.response.steamid))
        {
            throw new InvalidSteamIdException("Could not resolve that Steam profile. Check the SteamID64 or profile URL and try again.");
        }

        return data.response.steamid!;
    }

    private static bool IsSteamId64(string value)
    {
        return Regex.IsMatch(value, @"^\d{17}$");
    }

    private static bool LooksLikeVanityName(string value)
    {
        return Regex.IsMatch(value, @"^[a-zA-Z0-9_-]{2,64}$");
    }

    private static bool TryParseProfileUrl(string input, out string? steamId, out string? vanity)
    {
        steamId = null;
        vanity = null;

        if (!TryCreateProfileUri(input, out var uri))
        {
            return false;
        }

        if (!uri.Host.Contains("steamcommunity.com", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var segments = uri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (segments.Length < 2)
        {
            return false;
        }

        if (segments[0].Equals("profiles", StringComparison.OrdinalIgnoreCase) && IsSteamId64(segments[1]))
        {
            steamId = segments[1];
            return true;
        }

        if (segments[0].Equals("id", StringComparison.OrdinalIgnoreCase))
        {
            vanity = segments[1];
            return true;
        }

        return false;
    }

    private static bool TryCreateProfileUri(string input, out Uri uri)
    {
        if (Uri.TryCreate(input, UriKind.Absolute, out uri))
        {
            return true;
        }

        if (Uri.TryCreate($"https://{input}", UriKind.Absolute, out uri))
        {
            return true;
        }

        uri = null!;
        return false;
    }
}
