from typing import Dict, Any, List

import requests
import pandas as pd

from .config import RAW_DIR, PROCESSED_DIR, STEAM_API_KEY

GET_OWNED_GAMES_URL = "https://api.steampowered.com/IPlayerService/GetOwnedGames/v1/"


def fetch_owned_games(steamid: str) -> List[Dict[str, Any]]:
    if not STEAM_API_KEY:
        raise RuntimeError(
            "STEAM_API_KEY not set. Add it to your .env file as STEAM_API_KEY=..."
        )

    params = {
        "key": STEAM_API_KEY,
        "steamid": steamid,
        "include_appinfo": 0,
        "include_played_free_games": 1,
    }
    resp = requests.get(GET_OWNED_GAMES_URL, params=params, timeout=60)
    resp.raise_for_status()
    data = resp.json()
    games = data.get("response", {}).get("games", [])
    return games


def main():
    STEAM_IDS = [
        "76561198164987397",
        "76561198236701708", 
    ]

    all_rows: List[Dict[str, Any]] = []
    for sid in STEAM_IDS:
        print(f"Fetching owned games for {sid} …")
        games = fetch_owned_games(sid)
        for g in games:
            all_rows.append(
                {
                    "steamid": sid,
                    "appid": g["appid"],
                    "playtime_forever": g.get("playtime_forever", 0),
                    "playtime_2weeks": g.get("playtime_2weeks", 0),
                }
            )

    df = pd.DataFrame(all_rows)
    out_path = PROCESSED_DIR / "interactions.csv"
    df.to_csv(out_path, index=False)
    print(f"Wrote {len(df)} user-game interaction rows to {out_path}")


if __name__ == "__main__":
    main()
