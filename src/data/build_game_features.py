from typing import List, Any

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

    # Keep real games only
    games = games[games["type"] == "game"].copy()
    games = games.dropna(subset=["name"])

    # Parse release_year from release_date
    games["release_date_parsed"] = pd.to_datetime(
        games["release_date"], errors="coerce"
    )
    games["release_year"] = games["release_date_parsed"].dt.year

    # Price: assume price_final is in cents (your EDA should confirm).
    # If it's already in euros/dollars, change "/ 100.0" to just "= games['price_final']".
    games["price_eur"] = games["price_final"] / 100.0
    games.loc[games["is_free"] == True, "price_eur"] = 0.0

    # Make sure boolean columns are 0/1
    for col in ["is_free"]:
        if col in games.columns:
            games[col] = games[col].fillna(False).astype(int)
        else:
            games[col] = 0

    # NUMERIC FEATURES (secondary importance vs genres/categories)
    # These are things a typical gamer might care about indirectly:
    # - rough price
    # - release year (old-school vs modern)
    # - age rating
    # - critic score
    numeric_cols = [
        "price_eur",
        "metacritic_score",
        "release_year",
        "required_age",
        "is_free",
    ]
    # Ensure all exist and fill missing
    for col in numeric_cols:
        if col not in games.columns:
            games[col] = 0
    games[numeric_cols] = games[numeric_cols].fillna(0)

    # MULTI-LABEL: genres and categories (highest importance)
    games["genres_list"] = games["genres"].apply(split_semicolon)
    games["categories_list"] = games["categories"].apply(split_semicolon)

    # One-hot encode genres
    mlb_genres = MultiLabelBinarizer()
    genres_matrix = mlb_genres.fit_transform(games["genres_list"])
    genre_feature_names = [f"genre_{g}" for g in mlb_genres.classes_]

    # One-hot encode categories
    mlb_cats = MultiLabelBinarizer()
    cats_matrix = mlb_cats.fit_transform(games["categories_list"])
    cat_feature_names = [f"cat_{c}" for c in mlb_cats.classes_]

    # Scale numeric features
    scaler = StandardScaler()
    numeric_matrix = scaler.fit_transform(games[numeric_cols].values)
    numeric_feature_names = numeric_cols

    # ---------- WEIGHTING ----------
    # We want:
    #   genres  >  categories  >  numeric
    NUMERIC_WEIGHT = 1.0
    GENRE_WEIGHT = 2.0
    CAT_WEIGHT = 1.5

    numeric_matrix = numeric_matrix * NUMERIC_WEIGHT
    genres_matrix = genres_matrix * GENRE_WEIGHT
    cats_matrix = cats_matrix * CAT_WEIGHT
    # -------------------------------

    # Combine all features
    X = np.hstack([numeric_matrix, genres_matrix, cats_matrix])
    feature_names = numeric_feature_names + genre_feature_names + cat_feature_names

    print(f"Feature matrix shape: {X.shape}")
    print(f"# numeric features:   {len(numeric_feature_names)}")
    print(f"# genre features:     {len(genre_feature_names)}")
    print(f"# category features:  {len(cat_feature_names)}")

    # Save a cleaned games table for general use (and for C# side later)
    clean_cols = [
        "appid",
        "name",
        "release_date",
        "release_year",
        "price_eur",
        "metacritic_score",
        "required_age",
        "is_free",
        "genres",
        "categories",
    ]
    clean_cols = [c for c in clean_cols if c in games.columns]
    games_clean = games[clean_cols].copy()
    clean_path = PROCESSED_DIR / "games_clean.csv"
    games_clean.to_csv(clean_path, index=False)
    print(f"Wrote cleaned games table to {clean_path}")

    # Save feature matrix & metadata
    features_path = PROCESSED_DIR / "game_features.npz"
    np.savez_compressed(
        features_path,
        X=X,
        appid=games["appid"].values,
        feature_names=np.array(feature_names),
    )
    print(f"Wrote features to {features_path}")


if __name__ == "__main__":
    main()
