using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;

public class DigitalTwinController : MonoBehaviour
{
    [Serializable]
    public class MachineBinding
    {
        public string machineId;       // Ex: "presse_01", "robot_02", "convoyeur_03"
        public Renderer targetRenderer;  // MeshRenderer de la machine dans la scène
        public TMP_Text worldLabel;      // Texte World Space au-dessus de la machine
    }

    [Serializable]
    public class MachineTelemetry
    {
        public string id;
        public string name;
        public float temperature_c;
        public float vibration_mm_s;
        public int load_percent;
        public string status;
    }

    [Serializable]
    public class TelemetryResponse
    {
        public string timestamp;
        public List<MachineTelemetry> machines;
    }

    [Serializable]
    public class AskPayload
    {
        public string question;
    }

    [Serializable]
    public class AskResponse
    {
        public string question;
        public string answer;
        public List<string> flagged_machines;
    }

    [Header("Configuration API")]
    [SerializeField] private string baseUrl = "http://127.0.0.1:8000";
    [SerializeField] private float pollingIntervalSeconds = 2.0f;

    [Header("Machines 3D")]
    [SerializeField] private List<MachineBinding> machines = new List<MachineBinding>();

    [Header("UI Assistant IA (World Space / HUD)")]
    [SerializeField] private TMP_InputField questionInput;
    [SerializeField] private TMP_Text assistantOutputText;

    private readonly Dictionary<string, MachineBinding> _bindingsById = new Dictionary<string, MachineBinding>();

    private void Awake()
    {
        foreach (var binding in machines)
        {
            if (!string.IsNullOrEmpty(binding.machineId))
            {
                _bindingsById[binding.machineId] = binding;
            }
        }
    }

    private void Start()
    {
        StartCoroutine(PollTelemetryLoop());
    }

    private IEnumerator PollTelemetryLoop()
    {
        while (true)
        {
            yield return FetchTelemetry();
            yield return new WaitForSeconds(pollingIntervalSeconds);
        }
    }

    private IEnumerator FetchTelemetry()
    {
        using (UnityWebRequest request = UnityWebRequest.Get($"{baseUrl}/telemetry"))
        {
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[DigitalTwin] Erreur télémétrie : {request.error}");
                yield break;
            }

            TelemetryResponse data = JsonUtility.FromJson<TelemetryResponse>(request.downloadHandler.text);
            if (data?.machines == null) yield break;

            foreach (var machine in data.machines)
            {
                UpdateMachineVisuals(machine);
            }
        }
    }

    private void UpdateMachineVisuals(MachineTelemetry data)
    {
        if (!_bindingsById.TryGetValue(data.id, out MachineBinding binding)) return;

        Color statusColor = data.status switch
        {
            "CRITICAL" => new Color(0.90f, 0.18f, 0.18f),
            "WARNING"  => new Color(0.95f, 0.60f, 0.10f),
            _          => new Color(0.18f, 0.78f, 0.35f)
        };

        if (binding.targetRenderer != null)
        {
            binding.targetRenderer.material.color = statusColor;
        }

        if (binding.worldLabel != null)
        {
            binding.worldLabel.text =
                $"<b>{data.name}</b>\n" +
                $"Temp : {data.temperature_c:F1} °C | Vib : {data.vibration_mm_s:F2} mm/s\n" +
                $"Charge : {data.load_percent}% [{data.status}]";
        }
    }

    // À relier au bouton "Envoyer" de ton Canvas Unity
    public void OnAskButtonClicked()
    {
        string question = questionInput != null && !string.IsNullOrWhiteSpace(questionInput.text)
            ? questionInput.text
            : "Quelle est la situation actuelle des équipements de l'atelier ?";

        StartCoroutine(SendQuestionToAssistant(question));
    }

    // Raccourci pratique en démo : simuler une surchauffe sur presse_01 (touche F1)
    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.F1))
        {
            StartCoroutine(TriggerDemoAnomaly("presse_01"));
        }
    }

    private IEnumerator TriggerDemoAnomaly(string machineId)
    {
        using (UnityWebRequest req = UnityWebRequest.PostWwwForm($"{baseUrl}/anomaly/{machineId}", ""))
        {
            yield return req.SendWebRequest();
        }
    }

    private IEnumerator SendQuestionToAssistant(string question)
    {
        if (assistantOutputText != null)
        {
            assistantOutputText.text = "Analyse de la télémétrie en cours...";
        }

        string jsonBody = JsonUtility.ToJson(new AskPayload { question = question });
        byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);

        using (UnityWebRequest request = new UnityWebRequest($"{baseUrl}/ask", "POST"))
        {
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                if (assistantOutputText != null)
                    assistantOutputText.text = $"Erreur API : {request.error}";
                yield break;
            }

            AskResponse response = JsonUtility.FromJson<AskResponse>(request.downloadHandler.text);
            if (assistantOutputText != null && response != null)
            {
                assistantOutputText.text = response.answer;
            }
        }
    }
}