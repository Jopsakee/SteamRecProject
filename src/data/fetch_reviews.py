import json
import time
from typing import Dict, Any, List, Set

import requests
import pandas as pd

from .config import PROCESSED_DIR, RAW_DIR

# Steam storefront appreviews endpoint (unofficial but widely used)
APPREVIEWS_URL_TEMPLATE = "https://store.steampowered.com/appreviews/{appid}"

# ---------- SETTINGS YOU CAN TUNE ----------
REQUESTS_PER_MINUTE = 90     # lower if you hit rate limits
MAX_APPS_REVIEWS = None      # e.g. 10000 to cap, None for all games in games.csv
SLEEP_BETWEEN_REQUESTS = 60.0 / REQUESTS_PER_MINUTE
# ------------------------------------------


def load_target_appids() -> List[int]:
    """
    Load appids for which we want review summaries, from games.csv.

    We only take rows where type == "game", so it's aligned with your feature set.
    """
    games_path = PROCESSED_DIR / "games.csv"
    if not games_path.exists():
        # fallback: try raw games table
        games_path = PROCESSED_DIR / "games.csv"

    print(f"Loading appids from {games_path} …")
    games = pd.read_csv(games_path)

    if "type" in games.columns:
        games = games[games["type"] == "game"].copy()

    appids = games["appid"].astype(int).tolist()
    if MAX_APPS_REVIEWS is not None:
        appids = appids[:MAX_APPS_REVIEWS]

    print(f"Will fetch review summaries for {len(appids)} appids.")
    return appids


def load_existing_reviews() -> Dict[int, Dict[str, Any]]:
    """
    If reviews_summary.csv already exists, load it so we can skip already-fetched appids.
    """
    out_path = PROCESSED_DIR / "reviews_summary.csv"
    if not out_path.exists():
        return {}

    df = pd.read_csv(out_path)
    existing: Dict[int, Dict[str, Any]] = {}
    for _, row in df.iterrows():
        appid = int(row["appid"])
        existing[appid] = {
            "appid": appid,
            "review_positive": int(row.get("review_positive", 0)),
            "review_negative": int(row.get("review_negative", 0)),
            "review_total": int(row.get("review_total", 0)),
            "review_ratio": float(row.get("review_ratio", 0.0)),
        }
    print(f"Loaded existing summaries for {len(existing)} appids.")
    return existing


def fetch_review_summary(appid: int) -> Dict[str, Any]:
    """
    Call appreviews endpoint and extract total positive/negative/total.
    """
    url = APPREVIEWS_URL_TEMPLATE.format(appid=appid)
    params = {
        "json": 1,
        "language": "all",
        "purchase_type": "all",
        "filter": "all",
        "num_per_page": 0,
    }
    resp = requests.get(url, params=params, timeout=30)
    resp.raise_for_status()
    data = resp.json()

    if not data.get("success"):
        return {
            "appid": appid,
            "review_positive": 0,
            "review_negative": 0,
            "review_total": 0,
            "review_ratio": 0.0,
        }

    summary = data.get("query_summary", {})
    total_pos = summary.get("total_positive", 0)
    total_neg = summary.get("total_negative", 0)
    total_reviews = summary.get("total_reviews", 0)

    if total_reviews > 0:
        ratio = total_pos / float(total_reviews)
    else:
        ratio = 0.0  # no data; treat as neutral/unknown later if needed

    return {
        "appid": appid,
        "review_positive": int(total_pos),
        "review_negative": int(total_neg),
        "review_total": int(total_reviews),
        "review_ratio": float(ratio),
    }


def main():
    PROCESSED_DIR.mkdir(parents=True, exist_ok=True)
    existing = load_existing_reviews()
    existing_appids: Set[int] = set(existing.keys())

    appids = load_target_appids()

    new_rows: List[Dict[str, Any]] = []
    for idx, appid in enumerate(appids, start=1):
        if appid in existing_appids:
            continue

        try:
            summary = fetch_review_summary(appid)
        except Exception as e:
            print(f"[{idx}] Error fetching reviews for appid={appid}: {e}")
            time.sleep(5)
            continue

        new_rows.append(summary)

        if idx % 50 == 0:
            print(f"Processed {idx} appids so far… last={appid}")

        time.sleep(SLEEP_BETWEEN_REQUESTS)

    # Combine old + new
    all_rows = list(existing.values()) + new_rows
    if not all_rows:
        print("No review summaries to write.")
        return

    df = pd.DataFrame(all_rows).drop_duplicates(subset=["appid"])
    out_path = PROCESSED_DIR / "reviews_summary.csv"
    df.to_csv(out_path, index=False)
    print(f"Wrote {len(df)} review summary rows to {out_path}")


if __name__ == "__main__":
    main()
