import json
import os
import random
import urllib.request
from datetime import datetime, timezone
from fastapi import FastAPI
from fastapi.middleware.cors import CORSMiddleware
from pydantic import BaseModel

app = FastAPI(title="Digital Twin AI Backend", version="1.0.0")

app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_methods=["*"],
    allow_headers=["*"],
)

# État simulé des machines de l'usine 3D
MACHINES = {
    "presse_01": {"name": "Presse Hydraulique 01", "temp": 62.0, "vibration": 1.8, "load": 74, "forced_fault": False},
    "robot_02": {"name": "Bras Robotique Soudure 02", "temp": 55.0, "vibration": 1.2, "load": 68, "forced_fault": False},
    "convoyeur_03": {"name": "Convoyeur Principal 03", "temp": 48.0, "vibration": 0.9, "load": 81, "forced_fault": False},
}


class AskRequest(BaseModel):
    question: str


def compute_status(temp: float, vibration: float) -> str:
    if temp >= 85.0 or vibration >= 4.5:
        return "CRITICAL"
    if temp >= 72.0 or vibration >= 3.0:
        return "WARNING"
    return "OK"


@app.get("/telemetry")
def get_telemetry():
    snapshot = {}
    for machine_id, data in MACHINES.items():
        if data["forced_fault"]:
            temp = round(random.uniform(88.0, 96.0), 1)
            vibration = round(random.uniform(4.8, 6.2), 2)
        else:
            temp = round(max(40.0, min(78.0, data["temp"] + random.uniform(-1.5, 1.8))), 1)
            vibration = round(max(0.5, min(3.4, data["vibration"] + random.uniform(-0.2, 0.25))), 2)

        data["temp"] = temp
        data["vibration"] = vibration
        status = compute_status(temp, vibration)

        snapshot[machine_id] = {
            "id": machine_id,
            "name": data["name"],
            "temperature_c": temp,
            "vibration_mm_s": vibration,
            "load_percent": data["load"],
            "status": status,
        }

    return {
        "timestamp": datetime.now(timezone.utc).isoformat(),
        "machines": list(snapshot.values()),
    }


@app.post("/anomaly/{machine_id}")
def toggle_anomaly(machine_id: str):
    if machine_id not in MACHINES:
        return {"error": f"Machine '{machine_id}' introuvable"}
    MACHINES[machine_id]["forced_fault"] = not MACHINES[machine_id]["forced_fault"]
    return {
        "machine_id": machine_id,
        "forced_fault": MACHINES[machine_id]["forced_fault"],
    }


def query_llm(prompt: str, telemetry: dict) -> str:
    api_key = os.getenv("GEMINI_API_KEY")
    if not api_key:
        # Fallback local immédiat sans clé API
        alerts = [m for m in telemetry["machines"] if m["status"] != "OK"]
        if not alerts:
            return "Toutes les machines fonctionnent dans les seuils nominaux. Aucune intervention requise."
        details = ", ".join(
            f"{m['name']} ({m['status']} : {m['temperature_c']}°C, {m['vibration_mm_s']} mm/s)"
            for m in alerts
        )
        return f"Anomalie détectée sur : {details}. Inspection recommandée."

    url = f"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent?key={api_key}"
    system_prompt = (
        "Tu es un assistant industriel intégré à un jumeau numérique 3D Unity. "
        "Analyse la télémétrie JSON fournie et réponds en français en 2 à 3 phrases maximum, "
        "de manière précise et actionnable.\n\n"
        f"Télémétrie actuelle :\n{json.dumps(telemetry, ensure_ascii=False)}"
    )
    payload = {
        "contents": [{"parts": [{"text": f"{system_prompt}\n\nQuestion opérateur : {prompt}"}]}]
    }
    req = urllib.request.Request(
        url,
        data=json.dumps(payload).encode("utf-8"),
        headers={"Content-Type": "application/json"},
        method="POST",
    )
    try:
        with urllib.request.urlopen(req, timeout=10) as resp:
            body = json.loads(resp.read().decode("utf-8"))
            return body["candidates"][0]["content"]["parts"][0]["text"].strip()
    except Exception as exc:
        return f"Erreur de communication avec le service IA : {exc}"


@app.post("/ask")
def ask_assistant(req: AskRequest):
    telemetry = get_telemetry()
    answer = query_llm(req.question, telemetry)
    flagged_ids = [m["id"] for m in telemetry["machines"] if m["status"] != "OK"]
    return {
        "question": req.question,
        "answer": answer,
        "flagged_machines": flagged_ids,
    }