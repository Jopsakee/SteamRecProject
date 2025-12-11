from typing import List, Any, Dict

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

    # Price: assume price_final is in cents (adjust if your EDA says otherwise)
    games["price_eur"] = games["price_final"] / 100.0
    games.loc[games["is_free"] == True, "price_eur"] = 0.0

    # is_free as 0/1
    if "is_free" in games.columns:
        games["is_free"] = games["is_free"].fillna(False).astype(int)
    else:
        games["is_free"] = 0

    # ---- Merge review summaries (from fetch_reviews.py) ----
    reviews_path = PROCESSED_DIR / "reviews_summary.csv"
    if reviews_path.exists():
        print(f"Merging review summaries from {reviews_path} …")
        reviews = pd.read_csv(reviews_path)
        games = games.merge(reviews, on="appid", how="left")
    else:
        print("WARNING: reviews_summary.csv not found; filling review fields with 0.")
        games["review_positive"] = 0
        games["review_negative"] = 0
        games["review_total"] = 0
        games["review_ratio"] = 0.0

    # For games with no reviews: ratio as 0.5 (neutral-ish), volume 0
    games["review_total"] = games["review_total"].fillna(0).astype(int)
    games["review_ratio"] = games["review_ratio"].fillna(0.5)

    # Log volume to avoid huge ranges; +1 to avoid log(0)
    games["review_volume_log"] = np.log10(games["review_total"] + 1)

    # ---- Bayesian-smoothed review score (review_score_adj) ----
    mask_has_reviews = games["review_total"] > 0
    if mask_has_reviews.any():
        total_pos_global = games.loc[mask_has_reviews, "review_positive"].sum()
        total_rev_global = games.loc[mask_has_reviews, "review_total"].sum()
        global_mean_ratio = (
            total_pos_global / float(total_rev_global)
            if total_rev_global > 0
            else 0.5
        )
    else:
        global_mean_ratio = 0.5

    m = 500.0  # prior strength

    n = games["review_total"].astype(float)
    r = games["review_ratio"].astype(float)

    games["review_score_adj"] = (n / (n + m)) * r + (m / (n + m)) * global_mean_ratio

    # ---- Merge user tags from SteamSpy (from fetch_tags.py) ----
    tags_path = PROCESSED_DIR / "tags_summary.csv"
    if tags_path.exists():
        print(f"Merging user tags from {tags_path} …")
        tags_df = pd.read_csv(tags_path)
        games = games.merge(tags_df, on="appid", how="left")
    else:
        print("WARNING: tags_summary.csv not found; filling tags with ''.")
        games["tags"] = ""

    games["tags"] = games["tags"].fillna("")

    # NUMERIC FEATURES
    numeric_cols = [
        "price_eur",
        "metacritic_score",
        "release_year",
        "required_age",
        "is_free",
        "review_score_adj",
        "review_volume_log",
    ]
    for col in numeric_cols:
        if col not in games.columns:
            games[col] = 0
    games[numeric_cols] = games[numeric_cols].fillna(0)

    # MULTI-LABEL: genres, categories, user tags
    games["genres_list"] = games["genres"].apply(split_semicolon)
    games["categories_list"] = games["categories"].apply(split_semicolon)
    games["tags_list"] = games["tags"].apply(split_semicolon)

    # One-hot encode genres
    mlb_genres = MultiLabelBinarizer()
    genres_matrix = mlb_genres.fit_transform(games["genres_list"])
    genre_feature_names = [f"genre_{g}" for g in mlb_genres.classes_]

    # One-hot encode categories
    mlb_cats = MultiLabelBinarizer()
    cats_matrix = mlb_cats.fit_transform(games["categories_list"])
    cat_feature_names = [f"cat_{c}" for c in mlb_cats.classes_]

    # One-hot encode user tags
    mlb_tags = MultiLabelBinarizer()
    tags_matrix = mlb_tags.fit_transform(games["tags_list"])
    tag_feature_names = [f"tag_{t}" for t in mlb_tags.classes_]

    # Optionally drop ultra-rare tags (appear in very few games) to reduce noise
    min_tag_games = 10  # only keep tags used by at least 10 games
    tag_counts = tags_matrix.sum(axis=0)
    keep_mask = tag_counts >= min_tag_games
    tags_matrix = tags_matrix[:, keep_mask]
    tag_feature_names = [
        name for name, keep in zip(tag_feature_names, keep_mask) if keep
    ]

    # Scale numeric features
    scaler = StandardScaler()
    numeric_matrix = scaler.fit_transform(games[numeric_cols].values)
    numeric_feature_names = numeric_cols

    # ---------- PER-FEATURE WEIGHTING WITHIN NUMERIC ----------
    col_to_idx: Dict[str, int] = {
        name: idx for idx, name in enumerate(numeric_cols)
    }

    NUMERIC_BASE = 1.0
    numeric_matrix *= NUMERIC_BASE

    if "review_score_adj" in col_to_idx:
        numeric_matrix[:, col_to_idx["review_score_adj"]] *= 2.0
    if "review_volume_log" in col_to_idx:
        numeric_matrix[:, col_to_idx["review_volume_log"]] *= 1.5
    # ---------------------------------------------------------

    # ---------- BLOCK WEIGHTS ----------
    # We now have: numeric | genres | tags | categories
    # Tags capture subgenres like "Roguelike", "Bullet Hell", etc.
    GENRE_WEIGHT = 1.2
    TAG_WEIGHT = 4.0    # slightly stronger than genres for nuance
    CAT_WEIGHT = 1.2

    genres_matrix = genres_matrix * GENRE_WEIGHT
    tags_matrix = tags_matrix * TAG_WEIGHT
    cats_matrix = cats_matrix * CAT_WEIGHT
    # -----------------------------------

    # Combine all features
    X = np.hstack([numeric_matrix, genres_matrix, tags_matrix, cats_matrix])
    feature_names = (
        numeric_feature_names + genre_feature_names + tag_feature_names + cat_feature_names
    )

    print(f"Feature matrix shape: {X.shape}")
    print(f"# numeric features:   {len(numeric_feature_names)}")
    print(f"# genre features:     {len(genre_feature_names)}")
    print(f"# tag features:       {len(tag_feature_names)}")
    print(f"# category features:  {len(cat_feature_names)}")

    # Save cleaned games table (for Python + C# side)
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
        "tags",
        "review_positive",
        "review_negative",
        "review_total",
        "review_ratio",
        "review_score_adj",
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
