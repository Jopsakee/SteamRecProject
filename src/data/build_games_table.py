import json
from typing import Any, Dict, List, Optional

import pandas as pd

from .config import RAW_DIR, PROCESSED_DIR


def extract_price(raw: Dict[str, Any]):
    data = raw.get("data", {})
    po = data.get("price_overview") or {}
    return po.get("initial"), po.get("final"), po.get("discount_percent")


def extract_platforms(raw: Dict[str, Any]):
    data = raw.get("data", {})
    plats = data.get("platforms") or {}
    return plats.get("windows"), plats.get("mac"), plats.get("linux")


def extract_list_labels(raw: Dict[str, Any], key: str) -> str:
    """
    Extract ';'-separated labels from lists like genres or categories.
    """
    data = raw.get("data", {})
    items = data.get(key) or []
    labels = [
        item.get("description", "").strip()
        for item in items
        if item.get("description")
    ]
    # Remove duplicates & sort for consistency
    labels = sorted(set(labels))
    return ";".join(labels) if labels else ""


def extract_supported_languages(raw: Dict[str, Any]) -> str:
    return raw.get("data", {}).get("supported_languages", "")


def parse_record(rec: Dict[str, Any]) -> Optional[Dict[str, Any]]:
    appid = rec["appid"]
    raw = rec["raw"]

    # The 'raw' is whatever store API returned for this appid
    if not raw or not raw.get("success"):
        return None

    data = raw.get("data", {})
    app_type = data.get("type")
    if app_type not in ("game", "dlc"):
        # If you want only games, change to app_type != "game"
        return None

    price_initial, price_final, discount_percent = extract_price(raw)
    win, mac, linux = extract_platforms(raw)

    genres = extract_list_labels(raw, "genres")
    categories = extract_list_labels(raw, "categories")

    metacritic = data.get("metacritic") or {}
    achievements = data.get("achievements") or {}
    release_date = data.get("release_date") or {}

    return {
        "appid": appid,
        "name": data.get("name", ""),
        "type": app_type,
        "required_age": data.get("required_age"),
        "is_free": data.get("is_free"),
        "release_date": release_date.get("date"),
        "coming_soon": release_date.get("coming_soon"),
        "price_initial": price_initial,       # typically cents
        "price_final": price_final,
        "discount_percent": discount_percent,
        "supported_languages": extract_supported_languages(raw),
        "genres": genres,
        "categories": categories,
        "windows": win,
        "mac": mac,
        "linux": linux,
        "metacritic_score": metacritic.get("score"),
        "achievements_total": achievements.get("total"),
    }


def main():
    in_path = RAW_DIR / "appdetails_raw.jsonl"
    out_path = PROCESSED_DIR / "games.csv"

    records: List[Dict[str, Any]] = []

    print(f"Reading raw appdetails from {in_path} …")
    with in_path.open("r", encoding="utf-8") as f:
        for i, line in enumerate(f, start=1):
            try:
                rec = json.loads(line)
            except json.JSONDecodeError:
                continue

            parsed = parse_record(rec)
            if parsed:
                records.append(parsed)

            if i % 5000 == 0:
                print(f"Processed {i} raw lines… current parsed count={len(records)}")

    df = pd.DataFrame.from_records(records)
    print(f"Built table with {len(df)} games. Saving to {out_path}")
    PROCESSED_DIR.mkdir(parents=True, exist_ok=True)
    df.to_csv(out_path, index=False)


if __name__ == "__main__":
    main()
