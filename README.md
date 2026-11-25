# Jumeau Numérique Interactif & Assistant IA (Unity URP + FastAPI)

Prototype de jumeau numérique industriel développé dans le cadre du Master 2 **Sciences et Technologies Métavers** (UPHF-INSA Valenciennes). L'application connecte un environnement d'usine 3D sous **Unity (URP)** à un microservice **Python / FastAPI** diffusant des données capteurs en temps réel et intégrant un assistant IA de diagnostic.

## Architecture

```text
[Scène Usine 3D - Unity URP / C#]
       │  GET /telemetry (polling 2s)
       │  POST /ask (requête opérateur)
       ▼
[Backend FastAPI - Python] ──► [Flux capteurs simulés & seuils d'alerte]
       │
       └──► [LLM API] ──► Diagnostic en langage naturel + surbrillance 3Dv