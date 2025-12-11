from pathlib import Path
import os
from dotenv import load_dotenv

# Project root = two levels up from this file (src/data/ -> project root)
PROJECT_ROOT = Path(__file__).resolve().parents[2]
DATA_DIR = PROJECT_ROOT / "data"
RAW_DIR = DATA_DIR / "raw"
PROCESSED_DIR = DATA_DIR / "processed"

# Ensure directories exist
RAW_DIR.mkdir(parents=True, exist_ok=True)
PROCESSED_DIR.mkdir(parents=True, exist_ok=True)

# Load .env
load_dotenv(PROJECT_ROOT / ".env")

# Steam Web API key (only needed for user library step)
STEAM_API_KEY = os.getenv("STEAM_API_KEY")
