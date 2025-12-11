import time
from typing import Dict, Any, List, Set

import pandas as pd
import requests

from .config import PROCESSED_DIR

STEAMSPY_URL = "https://steamspy.com/api.php"

# 1 request/second is SteamSpy's recommendation
REQUESTS_PER_SECOND = 1.0
SLEEP_BETWEEN_REQUESTS = 1.0 / REQUESTS_PER_SECOND


def load_target_appids() -> List[int]:
    """
    Load appids for which we want user tags, from games.csv.
    We only keep rows where type == 'game'.
    """
    games_path = PROCESSED_DIR / "games.csv"
    print(f"Loading appids from {games_path} …")
    games = pd.read_csv(games_path)

    if "type" in games.columns:
        games = games[games["type"] == "game"].copy()

    appids = games["appid"].astype(int).tolist()
    print(f"Will fetch tags for {len(appids)} appids.")
    return appids


def load_existing_tags() -> pd.DataFrame:
    """
    If tags_summary.csv exists, load it so we can resume and skip already-fetched appids.
    """
    out_path = PROCESSED_DIR / "tags_summary.csv"
    if not out_path.exists():
        return pd.DataFrame(columns=["appid", "tags"])

    df = pd.read_csv(out_path)
    print(f"Loaded existing tags for {len(df)} appids.")
    return df


def fetch_tags_for_appid(appid: int) -> str:
    """
    Call SteamSpy appdetails endpoint and extract tag names.
    """
    params = {
        "request": "appdetails",
        "appid": str(appid),
    }
    resp = requests.get(STEAMSPY_URL, params=params, timeout=30)
    resp.raise_for_status()
    data: Dict[str, Any] = resp.json()

    tags_dict = data.get("tags") or {}
    if not isinstance(tags_dict, dict):
        return ""

    # tags_dict: {"Roguelike": 1234, "Indie": 5678, ...}
    # We just keep tag names; votes are ignored for now.
    tag_names = sorted(tags_dict.keys(), key=str.lower)
    return ";".join(tag_names)


def main():
    PROCESSED_DIR.mkdir(parents=True, exist_ok=True)

    existing_df = load_existing_tags()
    existing_appids: Set[int] = set(existing_df["appid"].astype(int).tolist())

    appids = load_target_appids()

    new_rows: List[Dict[str, Any]] = []

    for idx, appid in enumerate(appids, start=1):
        if appid in existing_appids:
            continue

        try:
            tags_str = fetch_tags_for_appid(appid)
        except Exception as e:
            print(f"[{idx}] Error fetching tags for appid={appid}: {e}")
            tags_str = ""

        new_rows.append({"appid": appid, "tags": tags_str})

        if idx % 50 == 0:
            print(f"Processed {idx} appids so far… last={appid}")

        time.sleep(SLEEP_BETWEEN_REQUESTS)

    # Combine old + new
    if new_rows:
        new_df = pd.DataFrame(new_rows)
        combined = pd.concat([existing_df, new_df], ignore_index=True)
        combined = combined.drop_duplicates(subset=["appid"])
    else:
        combined = existing_df

    out_path = PROCESSED_DIR / "tags_summary.csv"
    combined.to_csv(out_path, index=False)
    print(f"Wrote tags for {len(combined)} appids to {out_path}")


if __name__ == "__main__":
    main()
