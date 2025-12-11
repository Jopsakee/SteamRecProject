from typing import List, Optional, Set, Dict

import numpy as np
import pandas as pd
from sklearn.neighbors import NearestNeighbors

from src.data.config import PROCESSED_DIR


class ContentBasedRecommender:
    def __init__(self, n_neighbors: int = 200):
        """
        Content-based recommender using:
          1) Cosine similarity on feature vectors (genres/categories + numeric),
          2) Genre filtering,
          3) Re-ranking by review score and review volume.

        n_neighbors: how many nearest neighbours to query internally
                     (larger = more candidates to filter and re-rank).
        """
        self.n_neighbors = n_neighbors

        # Load feature matrix & metadata
        features_path = PROCESSED_DIR / "game_features.npz"
        games_path = PROCESSED_DIR / "games_clean.csv"

        data = np.load(features_path, allow_pickle=True)
        self.X: np.ndarray = data["X"]
        self.appids: np.ndarray = data["appid"]
        self.feature_names: np.ndarray = data["feature_names"]

        self.games: pd.DataFrame = pd.read_csv(games_path)

        # appid -> index in feature matrix
        self.appid_to_index: Dict[int, int] = {
            int(a): i for i, a in enumerate(self.appids)
        }

        # appid -> set of genres (for filtering)
        self.appid_to_genres: Dict[int, Set[str]] = {}
        for _, row in self.games.iterrows():
            appid = int(row["appid"])
            self.appid_to_genres[appid] = self._split_semicolon(row.get("genres", ""))

        # appid -> review fields (for re-ranking)
        self.appid_to_review_score: Dict[int, float] = {}
        self.appid_to_review_volume_log: Dict[int, float] = {}

        # Ensure review columns exist (from build_game_features)
        if "review_score_adj" not in self.games.columns:
            self.games["review_score_adj"] = 0.5
        if "review_total" not in self.games.columns:
            self.games["review_total"] = 0

        # log volume
        self.games["review_volume_log"] = np.log10(
            self.games["review_total"].fillna(0) + 1
        )

        for _, row in self.games.iterrows():
            appid = int(row["appid"])
            self.appid_to_review_score[appid] = float(row.get("review_score_adj", 0.5))
            self.appid_to_review_volume_log[appid] = float(row.get("review_volume_log", 0.0))

        # Precompute min/max for normalization
        self.review_score_min = float(self.games["review_score_adj"].min())
        self.review_score_max = float(self.games["review_score_adj"].max())
        self.review_vol_min = float(self.games["review_volume_log"].min())
        self.review_vol_max = float(self.games["review_volume_log"].max())

        # Fit k-NN model on feature matrix
        self.model = NearestNeighbors(
            metric="cosine",
            algorithm="brute",
            n_neighbors=self.n_neighbors,
        )
        self.model.fit(self.X)

        # Re-ranking weights
        self.W_SIM = 3.0    # similarity weight
        self.W_REV = 0.8    # adjusted review score weight
        self.W_VOL = 0.9      # review volume weight

    # ---------- Internal helpers ----------

    @staticmethod
    def _split_semicolon(s) -> Set[str]:
        if pd.isna(s) or not s:
            return set()
        return {t.strip() for t in str(s).split(";") if t.strip()}

    def _get_index_for_appid(self, appid: int) -> Optional[int]:
        return self.appid_to_index.get(int(appid))

    def _lookup_name(self, appid: int) -> str:
        row = self.games.loc[self.games["appid"] == appid]
        if row.empty:
            return ""
        return str(row.iloc[0]["name"])

    def _get_review_score(self, appid: int) -> float:
        return self.appid_to_review_score.get(int(appid), 0.5)

    def _get_review_volume_log(self, appid: int) -> float:
        return self.appid_to_review_volume_log.get(int(appid), 0.0)

    def _normalize(
        self, value: float, vmin: float, vmax: float, default: float = 0.5
    ) -> float:
        if vmax <= vmin:
            return default
        return (value - vmin) / (vmax - vmin)

    def _compute_overall_score(
        self, similarity: float, appid: int
    ) -> float:
        """
        Combine:
          - similarity (0..1),
          - normalized review_score_adj (0..1),
          - normalized review_volume_log (0..1)
        into one overall score.
        """
        review_score = self._get_review_score(appid)
        review_vol = self._get_review_volume_log(appid)

        norm_rev = self._normalize(
            review_score, self.review_score_min, self.review_score_max, default=0.5
        )
        norm_vol = self._normalize(
            review_vol, self.review_vol_min, self.review_vol_max, default=0.0
        )

        return (
            self.W_SIM * similarity
            + self.W_REV * norm_rev
            + self.W_VOL * norm_vol
        )

    # ---------- Public methods ----------

    def recommend_similar_by_appid(
        self,
        appid: int,
        top_n: int = 10,
        exclude_self: bool = True,
    ) -> pd.DataFrame:
        """
        Recommend games similar to a single game (by appid).

        Pipeline:
          1) Get k-NN neighbours by cosine similarity in feature space.
          2) Filter to games that share at least one genre with the reference game
             (if the reference has any genres).
          3) For each candidate, compute an overall_score using
             similarity + review_score_adj + review_volume_log.
          4) Sort by overall_score descending and return top_n.
        """
        idx = self._get_index_for_appid(appid)
        if idx is None:
            raise ValueError(f"Unknown appid: {appid}")

        ref_genres = self.appid_to_genres.get(int(appid), set())

        query_vec = self.X[idx : idx + 1]
        distances, indices = self.model.kneighbors(
            query_vec, n_neighbors=self.n_neighbors
        )

        distances = distances[0]
        indices = indices[0]

        candidates = []
        for dist, i in zip(distances, indices):
            rec_appid = int(self.appids[i])

            if exclude_self and rec_appid == appid:
                continue

            cand_genres = self.appid_to_genres.get(rec_appid, set())
            # If reference has genres, require at least one overlap
            if ref_genres and not (ref_genres & cand_genres):
                continue

            similarity = 1.0 - float(dist)
            overall_score = self._compute_overall_score(similarity, rec_appid)

            candidates.append(
                {
                    "appid": rec_appid,
                    "name": self._lookup_name(rec_appid),
                    "distance": float(dist),
                    "similarity": similarity,
                    "review_score_adj": self._get_review_score(rec_appid),
                    "review_volume_log": self._get_review_volume_log(rec_appid),
                    "overall_score": overall_score,
                }
            )

        # Sort candidates by overall_score descending
        candidates.sort(key=lambda x: x["overall_score"], reverse=True)

        # Return top_n as DataFrame
        return pd.DataFrame(candidates[:top_n])

    def recommend_for_liked_appids(
        self,
        liked_appids: List[int],
        top_n: int = 10,
    ) -> pd.DataFrame:
        """
        Recommend games similar to a list of liked appids.

        Strategy:
          1) Average their feature vectors to represent the user's "taste vector".
          2) Get k-NN neighbours.
          3) Filter out games user already likes.
          4) Filter out games that share no genre with ANY liked game (if any genres).
          5) Re-rank using similarity + review score + review volume.
        """
        indices = [self._get_index_for_appid(a) for a in liked_appids]
        indices = [i for i in indices if i is not None]
        if not indices:
            raise ValueError("None of the liked appids were found in the feature matrix.")

        # Union of all genres from liked games
        liked_genres_union: Set[str] = set()
        for a in liked_appids:
            liked_genres_union |= self.appid_to_genres.get(int(a), set())

        user_vec = self.X[indices].mean(axis=0, keepdims=True)
        distances, indices_knn = self.model.kneighbors(
            user_vec, n_neighbors=self.n_neighbors
        )

        distances = distances[0]
        indices_knn = indices_knn[0]

        liked_set = {int(a) for a in liked_appids}

        candidates = []
        for dist, idx_neigh in zip(distances, indices_knn):
            rec_appid = int(self.appids[idx_neigh])

            if rec_appid in liked_set:
                continue

            cand_genres = self.appid_to_genres.get(rec_appid, set())
            if liked_genres_union and not (liked_genres_union & cand_genres):
                continue

            similarity = 1.0 - float(dist)
            overall_score = self._compute_overall_score(similarity, rec_appid)

            candidates.append(
                {
                    "appid": rec_appid,
                    "name": self._lookup_name(rec_appid),
                    "distance": float(dist),
                    "similarity": similarity,
                    "review_score_adj": self._get_review_score(rec_appid),
                    "review_volume_log": self._get_review_volume_log(rec_appid),
                    "overall_score": overall_score,
                }
            )

        candidates.sort(key=lambda x: x["overall_score"], reverse=True)
        return pd.DataFrame(candidates[:top_n])
