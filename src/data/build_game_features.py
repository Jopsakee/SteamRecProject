from typing import List, Dict, Any

import numpy as np
import pandas as pd
from sklearn.preprocessing import MultiLabelBinarizer, StandardScaler

from .config import PROCESSED_DIR


def split_semicolon(s: Any) -> List[str]:
    if pd.isna(s) or not s:
        return []
    return [t.strip() for t in str(s).split(";") if t.strip()]


def main():
    games_path = PROCESSED_DIR / "games.csv"
    print(f"Loading games from {games_path} …")
    games = pd.read_csv(games_path)

    # Filter to real games only
    games = games[games["type"] == "game"].copy()
    games = games.dropna(subset=["name"])

    # Parse release year
    games["release_date_parsed"] = pd.to_datetime(
        games["release_date"], errors="coerce"
    )
    games["release_year"] = games["release_date_parsed"].dt.year

    # Price in EUR-like units (if it's in cents)
    games["price_eur"] = games["price_final"] / 100.0
    # For free games, ensure price_eur is 0
    games.loc[games["is_free"] == True, "price_eur"] = 0.0

    # Binary flags as int
    for col in ["is_free", "windows", "mac", "linux", "coming_soon"]:
        if col in games.columns:
            games[col] = games[col].fillna(False).astype(int)

    # Select numeric feature columns
    numeric_cols = [
        "price_eur",
        "discount_percent",
        "metacritic_score",
        "achievements_total",
        "required_age",
        "release_year",
        "is_free",
        "windows",
        "mac",
        "linux",
    ]

    # Some numeric fields may be missing; fill with sensible defaults
    games[numeric_cols] = games[numeric_cols].fillna(0)

    # Multi-label encode genres and categories
    games["genres_list"] = games["genres"].apply(split_semicolon)
    games["categories_list"] = games["categories"].apply(split_semicolon)

    # MultiLabelBinarizer for genres
    mlb_genres = MultiLabelBinarizer()
    genres_matrix = mlb_genres.fit_transform(games["genres_list"])
    genre_feature_names = [f"genre_{g}" for g in mlb_genres.classes_]

    # MultiLabelBinarizer for categories
    mlb_cats = MultiLabelBinarizer()
    cats_matrix = mlb_cats.fit_transform(games["categories_list"])
    cat_feature_names = [f"cat_{c}" for c in mlb_cats.classes_]

    # Scale numeric features
    scaler = StandardScaler()
    numeric_matrix = scaler.fit_transform(games[numeric_cols].values)

    numeric_feature_names = numeric_cols  # already descriptive

    # Concatenate all features horizontally
    feature_matrix = np.hstack([numeric_matrix, genres_matrix, cats_matrix])
    feature_names = numeric_feature_names + genre_feature_names + cat_feature_names

    print(f"Feature matrix shape: {feature_matrix.shape}")
    print(f"# numeric features: {len(numeric_feature_names)}")
    print(f"# genre features:   {len(genre_feature_names)}")
    print(f"# category features:{len(cat_feature_names)}")

    # Save a cleaned games table (for general use, C# side, etc.)
    clean_games_path = PROCESSED_DIR / "games_clean.csv"
    cols_to_keep = [
        "appid",
        "name",
        "release_date",
        "release_year",
        "price_eur",
        "discount_percent",
        "metacritic_score",
        "achievements_total",
        "required_age",
        "is_free",
        "windows",
        "mac",
        "linux",
        "genres",
        "categories",
    ]
    # Only keep existing columns
    cols_to_keep = [c for c in cols_to_keep if c in games.columns]
    games_clean = games[cols_to_keep].copy()
    games_clean.to_csv(clean_games_path, index=False)
    print(f"Wrote cleaned games table to {clean_games_path}")

    # Save feature matrix & metadata as NumPy npz
    features_path = PROCESSED_DIR / "game_features.npz"
    np.savez_compressed(
        features_path,
        X=feature_matrix,
        appid=games["appid"].values,
        feature_names=np.array(feature_names),
    )
    print(f"Wrote feature matrix + metadata to {features_path}")


if __name__ == "__main__":
    main()
