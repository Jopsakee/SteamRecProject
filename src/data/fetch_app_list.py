import json
import time
import csv
from typing import List, Dict, Any

import requests

from .config import RAW_DIR, STEAM_API_KEY

# IStoreService GetAppList endpoint
APP_LIST_URL = "https://api.steampowered.com/IStoreService/GetAppList/v1/"

# ---------- SETTINGS YOU CAN TUNE ----------
MAX_RESULTS_PER_PAGE = 50000  # Steam docs: max 50k
SLEEP_BETWEEN_PAGES = 1.0     # seconds, just to be polite
MAX_APPS_TOTAL = None         # e.g. 20000 to limit; None for all
INCLUDE_DLC = False           # flip to True if you also want DLC
# ------------------------------------------


def fetch_app_list() -> List[Dict[str, Any]]:
    """
    Fetch the app list using IStoreService/GetAppList with pagination.
    Requires STEAM_API_KEY.
    """
    if not STEAM_API_KEY:
        raise RuntimeError(
            "STEAM_API_KEY is not set. "
            "Add it to your .env file at project root."
        )

    apps: List[Dict[str, Any]] = []
    last_appid = 0

    while True:
        params = {
            "key": STEAM_API_KEY,
            "last_appid": last_appid,
            "max_results": MAX_RESULTS_PER_PAGE,
            # Filters – adjust if you want DLC, software, etc.
            "include_games": True,
            "include_dlc": INCLUDE_DLC,
            "include_software": False,
            "include_videos": False,
            "include_hardware": False,
        }

        print(
            f"Requesting app list page… last_appid={last_appid}, "
            f"have {len(apps)} apps so far"
        )
        resp = requests.get(APP_LIST_URL, params=params, timeout=60)
        resp.raise_for_status()
        data = resp.json()

        # Expected shape: {"response": {"apps": [ { "appid": ..., ... }, ... ]}}
        batch = data.get("response", {}).get("apps", [])
        if not batch:
            print("No more apps returned; stopping.")
            break

        apps.extend(batch)
        last_appid = batch[-1]["appid"]

        # Optional total cap
        if MAX_APPS_TOTAL is not None and len(apps) >= MAX_APPS_TOTAL:
            apps = apps[:MAX_APPS_TOTAL]
            print(f"Reached MAX_APPS_TOTAL={MAX_APPS_TOTAL}; stopping.")
            break

        # If fewer than max requested, we reached the end
        if len(batch) < MAX_RESULTS_PER_PAGE:
            print("Last page returned fewer than max_results; stopping.")
            break

        time.sleep(SLEEP_BETWEEN_PAGES)

    return apps


def main():
    apps = fetch_app_list()
    out_json = RAW_DIR / "app_list.json"
    out_csv = RAW_DIR / "app_list.csv"

    print(f"Fetched {len(apps)} apps total.")
    print(f"Saving JSON to {out_json} …")
    with out_json.open("w", encoding="utf-8") as f:
        json.dump(apps, f, ensure_ascii=False, indent=2)

    print(f"Saving CSV to {out_csv} …")
    with out_csv.open("w", encoding="utf-8", newline="") as f:
        writer = csv.writer(f)
        writer.writerow(["appid", "name"])
        for app in apps:
            # Some entries might not have a name; default to empty string
            writer.writerow([app.get("appid"), app.get("name", "")])

    print("Done.")


if __name__ == "__main__":
    main()
