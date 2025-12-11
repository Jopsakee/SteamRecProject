import csv
import json
import time
from typing import Dict, Iterable, Set

import requests

from .config import RAW_DIR

STORE_APPDETAILS_URL = "https://store.steampowered.com/api/appdetails"

# ---------- SETTINGS YOU CAN TUNE ----------
MAX_APPS = 200         # None for all apps; start small (e.g. 2000) to test
REQUESTS_PER_MINUTE = 60  # lower if you see rate limit / 429 errors
COUNTRY = "us"            # region for prices
LANG = "en"               # language for text
# ------------------------------------------

SLEEP_BETWEEN_REQUESTS = 60.0 / REQUESTS_PER_MINUTE


def load_appids_from_csv() -> Iterable[int]:
    """
    Load appids from data/raw/app_list.csv.
    That file is produced by fetch_app_list.py.
    """
    csv_path = RAW_DIR / "app_list.csv"
    with csv_path.open("r", encoding="utf-8") as f:
        reader = csv.DictReader(f)
        for i, row in enumerate(reader):
            if MAX_APPS is not None and i >= MAX_APPS:
                break
            appid = row.get("appid")
            if appid:
                try:
                    yield int(appid)
                except ValueError:
                    continue


def load_already_fetched() -> Set[int]:
    """
    Read appids that are already in appdetails_raw.jsonl so we can resume.
    """
    path = RAW_DIR / "appdetails_raw.jsonl"
    if not path.exists():
        return set()

    fetched: Set[int] = set()
    with path.open("r", encoding="utf-8") as f:
        for line in f:
            try:
                obj = json.loads(line)
                fetched.add(int(obj["appid"]))
            except Exception:
                continue
    return fetched


def fetch_appdetails_for_appid(session: requests.Session, appid: int) -> Dict:
    params = {
        "appids": appid,
        "cc": COUNTRY,
        "l": LANG,
    }
    resp = session.get(STORE_APPDETAILS_URL, params=params, timeout=30)
    resp.raise_for_status()
    data = resp.json()
    # Response is keyed by stringified appid
    return data.get(str(appid), {})


def main():
    out_path = RAW_DIR / "appdetails_raw.jsonl"
    already = load_already_fetched()
    print(f"Already have details for {len(already)} apps.")

    session = requests.Session()

    with out_path.open("a", encoding="utf-8") as out_file:
        for idx, appid in enumerate(load_appids_from_csv(), start=1):
            if appid in already:
                continue

            try:
                info = fetch_appdetails_for_appid(session, appid)
            except Exception as e:
                print(f"[{idx}] Error fetching {appid}: {e}")
                time.sleep(5)
                continue

            record = {
                "appid": appid,
                "raw": info,  # full object from appdetails for this appid
            }
            out_file.write(json.dumps(record, ensure_ascii=False) + "\n")

            if idx % 50 == 0:
                print(f"Fetched {idx} apps so far… last appid={appid}")

            time.sleep(SLEEP_BETWEEN_REQUESTS)

    print("Done fetching appdetails.")


if __name__ == "__main__":
    main()
