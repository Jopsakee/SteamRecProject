from typing import List, Optional, Set

import numpy as np
import pandas as pd
from sklearn.neighbors import NearestNeighbors

from src.data.config import PROCESSED_DIR


class ContentBasedRecommender:
    def __init__(self, n_neighbors: int = 200):
        """
        Content-based recommender using cosine similarity on game feature vectors.

        n_neighbors: how many nearest neighbours to query internally
                     (larger = more candidates to filter from).
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
        self.appid_to_index = {
            int(a): i for i, a in enumerate(self.appids)
        }

        # appid -> set of genres (for filtering)
        self.appid_to_genres = {}
        for _, row in self.games.iterrows():
            appid = int(row["appid"])
            self.appid_to_genres[appid] = self._split_semicolon(row.get("genres", ""))

        # Fit k-NN model
        self.model = NearestNeighbors(
            metric="cosine",
            algorithm="brute",
            n_neighbors=self.n_neighbors,
        )
        self.model.fit(self.X)

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

    # ---------- Public methods ----------

    def recommend_similar_by_appid(
        self,
        appid: int,
        top_n: int = 10,
        exclude_self: bool = True,
    ) -> pd.DataFrame:
        """
        Recommend games similar to a single game (by appid).
        Returns a DataFrame with: appid, name, distance, similarity.
        Applies a genre filter: recommendations must share at least one genre
        with the reference game (if it has any genres).
        """
        idx = self._get_index_for_appid(appid)
        if idx is None:
            raise ValueError(f"Unknown appid: {appid}")

        ref_genres = self.appid_to_genres.get(int(appid), set())

        query_vec = self.X[idx : idx + 1]
        distances, indices = self.model.kneighbors(query_vec, n_neighbors=self.n_neighbors)

        distances = distances[0]
        indices = indices[0]

        recs = []
        for dist, i in zip(distances, indices):
            rec_appid = int(self.appids[i])

            # Skip the same game if requested
            if exclude_self and rec_appid == appid:
                continue

            cand_genres = self.appid_to_genres.get(rec_appid, set())

            # If reference has genres, require at least one in common
            if ref_genres and not (ref_genres & cand_genres):
                continue

            similarity = 1.0 - float(dist)
            recs.append(
                {
                    "appid": rec_appid,
                    "name": self._lookup_name(rec_appid),
                    "distance": float(dist),
                    "similarity": similarity,
                }
            )
            if len(recs) >= top_n:
                break

        return pd.DataFrame(recs)

    def recommend_for_liked_appids(
        self,
        liked_appids: List[int],
        top_n: int = 10,
    ) -> pd.DataFrame:
        """
        Recommend games similar to a list of liked appids.

        Strategy:
        - Average their feature vectors.
        - Compute k-NN.
        - Filter out games that share no genre with ANY liked game (if there are genres).
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
        distances, indices_knn = self.model.kneighbors(user_vec, n_neighbors=self.n_neighbors)

        distances = distances[0]
        indices_knn = indices_knn[0]

        liked_set = {int(a) for a in liked_appids}

        recs = []
        for dist, idx in zip(distances, indices_knn):
            rec_appid = int(self.appids[idx])

            # Don't recommend games the user already 'likes'
            if rec_appid in liked_set:
                continue

            cand_genres = self.appid_to_genres.get(rec_appid, set())

            # If we know genres for liked games, require at least one overlap
            if liked_genres_union and not (liked_genres_union & cand_genres):
                continue

            similarity = 1.0 - float(dist)
            recs.append(
                {
                    "appid": rec_appid,
                    "name": self._lookup_name(rec_appid),
                    "distance": float(dist),
                    "similarity": similarity,
                }
            )
            if len(recs) >= top_n:
                break

        return pd.DataFrame(recs)
