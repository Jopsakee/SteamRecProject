using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
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
    }

    private class OwnedGamesResponse
    {
        public OwnedGamesInnerResponse? response { get; set; }
    }

    public async Task<List<OwnedGame>> GetOwnedGamesAsync(string steamId)
    {
        if (string.IsNullOrWhiteSpace(steamId))
            return new List<OwnedGame>();

        var apiKey = _configuration["Steam:ApiKey"]
                     ?? Environment.GetEnvironmentVariable("STEAM_WEB_API_KEY");

        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("Steam Web API key not configured. Set Steam:ApiKey or STEAM_WEB_API_KEY.");

        var url =
            $"https://api.steampowered.com/IPlayerService/GetOwnedGames/v1/?" +
            $"key={Uri.EscapeDataString(apiKey)}&steamid={Uri.EscapeDataString(steamId)}" +
            "&include_appinfo=1&include_played_free_games=1";

        var resp = await _httpClient.GetAsync(url);
        resp.EnsureSuccessStatusCode();

        var json = await resp.Content.ReadAsStringAsync();

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var data = JsonSerializer.Deserialize<OwnedGamesResponse>(json, options);

        return data?.response?.games ?? new List<OwnedGame>();
    }
}
