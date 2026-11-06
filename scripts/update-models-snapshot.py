"""Met à jour le catalogue de modèles embarqué dans l'API, à partir de https://models.dev/api.json.

    python3 scripts/update-models-snapshot.py

L'API lit le même format en direct (Llm:CatalogUrl) ; ce fichier sert quand models.dev est injoignable, et aux tests.
On ne garde que les fournisseurs branchés, les modèles capables d'appeler des outils, et les champs utiles.
"""
import json
import urllib.request
from pathlib import Path

PROVIDERS = ["anthropic", "openai", "google", "mistral", "groq", "deepseek", "openrouter", "xai", "opencode", "nvidia"]
OUT = Path(__file__).resolve().parents[1] / "src/AgentYattus.Api/Modules/Llm/models-snapshot.json"

# models.dev refuse les requêtes sans User-Agent explicite.
request = urllib.request.Request("https://models.dev/api.json", headers={"User-Agent": "AgentYattus"})
with urllib.request.urlopen(request, timeout=60) as response:
    catalog = json.load(response)

snapshot = {}
for pid in PROVIDERS:
    models = {}
    for mid, m in sorted(catalog[pid]["models"].items()):
        if not m.get("tool_call") or m.get("status") == "deprecated":
            continue
        cost, limit = m.get("cost") or {}, m.get("limit") or {}
        models[mid] = {
            "id": mid, "name": m.get("name", mid), "tool_call": True, "reasoning": bool(m.get("reasoning")),
            "limit": {k: limit[k] for k in ("context", "output") if k in limit},
            **({"cost": {k: cost[k] for k in ("input", "output") if k in cost}} if cost else {}),
        }
    snapshot[pid] = {"id": pid, "name": catalog[pid]["name"], "models": models}

OUT.write_text(json.dumps(snapshot, ensure_ascii=False, separators=(",", ":")) + "\n", encoding="utf-8")
print(OUT, sum(len(p["models"]) for p in snapshot.values()), "modèles")
