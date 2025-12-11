# SteamRecProject

SteamRecProject is an AI-driven recommendation system designed to suggest new Steam games to players based on their existing library and playtime behavior. The system leverages both content-based and collaborative filtering techniques to provide personalized recommendations.

## Data Collection

To populate the local game catalog, we use a custom scraper to fetch data from Steam. Use the following commands to execute the scraping pipeline:

1. **Fetch the list of all apps:**
   ```bash
   python -m src.data.fetch_app_list

   python -m src>data>fetch_appdetails
---

### 2. REQUIREMENTS.md

```markdown
# Project Requirements: SteamRecProject

## 1. Project Summary
Build a Steam game recommendation system that suggests new games to a player based on their existing Steam library and play behavior. The system starts as a local application using CSV data and evolves into a cloud-hosted Azure solution.

## 2. Target User
* **PC Gamer:** Has a valid Steam account with a Steam ID.
* **Profile:** Owns at least a few games on Steam.

## 3. Inputs & Outputs

### Inputs
* **Steam User ID (Primary):** Used to query the Steam Web API for owned games and playtime.
* **User Filters (UI):** Preferred genres, price range, minimum review score.
* **Manual Selection:** Option for cold-start users (private profiles) to select favorite games manually.

### Outputs
* **Ranked List:** Top-N (e.g., top-10) recommended games the user does not currently own.
* **Metadata:** Title, genre, price, review score for each recommendation.
* **Explanation:** A short justification (e.g., "Similar to Game X in your library").

## 4. Architecture & Technology

### Phase 1 – Local Application
* **Data Source:** Local CSV file with scraped Steam game data.
* **Application:** C# application (Console/GUI) running locally in VSCode.
* **Integration:** C# client to call Steam Web API for user data.
* **AI/ML:** Recommendation models implemented in C# using **ML.NET**.
* **Python (Optional):** Used for EDA, prototyping, and scraping.

### Phase 2 – Azure Deployment
* **Backend:** C# Web API (ASP.NET Core) hosted on Azure App Service.
* **Database:** Migration from CSV to Azure SQL, Cosmos DB, or MongoDB.
* **Frontend:** Simple Web UI (Blazor/Razor/JS) to input Steam ID and view results.

## 5. AI & Machine Learning Strategy

### Techniques
The system must implement at least two AI techniques:
1.  **Content-Based Filtering:**
    * Features: Genres, tags, review scores, price.
    * Methods: Cosine similarity or K-Nearest-Neighbours in C#.
2.  **Collaborative Filtering:**
    * Data: User-game interaction dataset (owned games/playtime).
    * Methods: Matrix Factorization using ML.NET to predict preference scores.

### Implementation & Analysis
* **Primary Language:** C# using ML.NET for the final system.
* **Comparison:** Compare a manually implemented algorithm (e.g., pure cosine similarity) against an ML.NET model.
* **Metrics:** Evaluate using Precision@K, Recall@K, and runtime performance.

## 6. ML Pipeline & Forecasting

### Pipeline Steps
1.  **Preprocessing:** Cleaning data, filtering rare games, and scaling features.
2.  **Splitting:** Train/Validation/Test splits (e.g., 70/15/15).
3.  **Training:** Use ML.NET pipelines for feature engineering and model fitting.
4.  **Hyperparameter Optimization:** Tuning parameters like rank and regularization.

### Evaluation
* Use metrics like NDCG@K, HitRate@K, and RMSE (if predicting ratings).
* Compare baselines (popularity) vs. personalized models.

### Forecasting (Optional)
* Explore predicting future popularity or seasonal trends to adjust recommendation scores.

## 7. Security & Robustness

### Objectives
* Identify sensitive assets (Steam IDs, API keys, model data).
* Analyze potential threats: Data poisoning, adversarial attacks, and API abuse.

### Deliverables
* **Security Report:** Document vulnerabilities, attack scenarios, and proposed mitigations (e.g., input validation, rate limiting).
* **Incident Response:** Define procedures for detecting abnormal usage or model poisoning.