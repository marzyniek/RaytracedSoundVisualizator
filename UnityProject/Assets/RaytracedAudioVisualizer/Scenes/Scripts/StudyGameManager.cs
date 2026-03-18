using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

public class StudyGameManager : MonoBehaviour
{
    [Header("Prefabs")] [SerializeField] private GameObject playerPrefab;

    [SerializeField] private GameObject npcPrefab;

    [Header("Spawn Locations")] [SerializeField]
    private Transform[] playerSpawnPoints;

    [SerializeField] private Transform[] npcSpawnPoints;

    [Header("Study Settings")] [SerializeField]
    private int totalTrials = 5;

    [SerializeField] private float successRadius = 2.0f; // How close player needs to get to NPC
    [SerializeField] private string participantID = "P01";
    private GameObject currentNPC;

    // Instance tracking
    private GameObject currentPlayer;

    // State tracking
    private int currentTrial;
    private bool isTrialActive;
    private Vector3 lastPlayerPosition;

    // File path for data logging
    private string logFilePath;
    private float totalDistanceWalked;

    private List<int> trialOrder = new();
    private float trialTimer;

    private void Start()
    {
        // Setup the CSV file path in the project folder
        logFilePath = Application.dataPath + "/" + participantID + "_Results.csv";

        trialOrder = Enumerable.Range(0, totalTrials).ToList();

        for (var i = 0; i < trialOrder.Count; i++)
        {
            var temp = trialOrder[i];
            var randomIndex = Random.Range(i, trialOrder.Count);
            trialOrder[i] = trialOrder[randomIndex];
            trialOrder[randomIndex] = temp;
        }

        if (!File.Exists(logFilePath)) File.AppendAllText(logFilePath, "Trial,SpawnIndex,Time(s),DistanceWalked(m)\n");

        StartNextTrial();
    }

    private void Update()
    {
        if (!isTrialActive || currentPlayer == null || currentNPC == null) return;

        // 1. Update Time
        trialTimer += Time.deltaTime;

        // 2. Track Distance Walked
        var distanceThisFrame = Vector3.Distance(currentPlayer.transform.position, lastPlayerPosition);
        totalDistanceWalked += distanceThisFrame;
        lastPlayerPosition = currentPlayer.transform.position;

        // 3. Check Win Condition (Did player reach the NPC?)
        var distanceToNPC = Vector3.Distance(currentPlayer.transform.position, currentNPC.transform.position);
        if (distanceToNPC <= successRadius) EndTrial();
    }

    private void StartNextTrial()
    {
        if (currentTrial >= totalTrials)
        {
            Debug.Log("Study Complete for " + participantID + "!");
            return;
        }

        trialTimer = 0f;
        totalDistanceWalked = 0f;

        // Pick random spawn points (or you could make this sequential)
        Debug.Log($"Trial {trialOrder[currentTrial]} Started");
        var pSpawn = playerSpawnPoints[trialOrder[currentTrial]];
        var nSpawn = npcSpawnPoints[trialOrder[currentTrial]];

        // Spawn instances
        currentPlayer = Instantiate(playerPrefab, pSpawn.position, pSpawn.rotation);
        currentNPC = Instantiate(npcPrefab, nSpawn.position, nSpawn.rotation);

        lastPlayerPosition = currentPlayer.transform.position;
        isTrialActive = true;

        Debug.Log($"--- Trial {currentTrial} Started ---");
    }

    private void EndTrial()
    {
        isTrialActive = false;

        // Log Data to CSV
        var dataRow =
            $"{currentTrial},{trialOrder[currentTrial]},{trialTimer.ToString("F2", CultureInfo.InvariantCulture)},{totalDistanceWalked.ToString("F2", CultureInfo.InvariantCulture)}\n";
        File.AppendAllText(logFilePath, dataRow);

        Debug.Log($"Trial {currentTrial} Finished! Time: {trialTimer:F2}s | Distance: {totalDistanceWalked:F2}m");

        // Clean up for the next round
        Destroy(currentPlayer);
        Destroy(currentNPC);

        // Optional: Add a short delay here before starting the next trial
        currentTrial++;
        StartNextTrial();
    }
}