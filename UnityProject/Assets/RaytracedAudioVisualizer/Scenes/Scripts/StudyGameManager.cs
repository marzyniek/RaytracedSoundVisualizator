using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using RaytracedAudioVisualizerPlugin;

[System.Serializable]
public class StudyRoute
{
    public string routeName;
    public Transform playerSpawnPoint;
    public StudyNode targetNode; 
}

public class StudyGameManager : MonoBehaviour
{
    public static StudyGameManager Instance { get; private set; }

    public enum StudyMode { SoundOnly, VisualOnly, SoundAndVisual }

    [Header("Study Info")]
    [SerializeField] private string participantID = "P01";
    
    [SerializeField] 
    private int latinSquareGroup = 0; 

    [Header("Prefabs & Routes")] 
    [SerializeField] private GameObject playerPrefab;
    [SerializeField] private GameObject npcPrefab;

    [SerializeField] private List<StudyRoute> predefinedRoutes;
    [SerializeField] private List<StudyRoute> introRoutes;

    [Header("UI Elements")]
    [SerializeField] private GameObject startPanel;
    [SerializeField] private GameObject gamePanel;
    [SerializeField] private GameObject endPanel;
    [SerializeField] private GameObject countdownPanel;
    [SerializeField] private GameObject questionnairePanel;
    [SerializeField] private GameObject pausePanel;
    [SerializeField] private Slider sensitivitySlider;
    [SerializeField] private TextMeshProUGUI trialCounterText;
    [SerializeField] private TextMeshProUGUI countdownText;
    [SerializeField] private TextMeshProUGUI conditionText;
    [SerializeField] private TextMeshProUGUI volumeText;

    [Header("Audio Settings")]
    [SerializeField] private float volumeStep = 0.05f;

    [Header("Mouse Settings")]
    [SerializeField] private float mouseSensitivity = 2f;
    public float MouseSensitivity => mouseSensitivity;
    
    private GameObject currentPlayer;
    private GameObject currentNPC;
    private int currentTrialGlobal = 0; 
    private bool isTrialActive;
    public bool IsPaused { get; private set; }
    private Vector3 lastPlayerPosition;

    private string trialsLogPath;
    private string nodesLogPath;
    private float totalDistanceWalked;
    private float trialTimer;

    private List<StudyMode> conditionOrder = new();
    private List<StudyRoute> currentConditionRoutes = new();
    private StudyMode currentState;
    private StudyRoute currentRoute;
    private int conditionIndex = 0;
    private int routeIndexWithinCondition = 0;
    
    private bool isInIntro = true;
    private int introRouteIndex = 0;

    private StudyNode currentNode;
    private float nodeEnterTime;

    private enum StudyState { WaitingToStart, CountingDown, Playing, Finished, WaitingForQuestionnaire }
    private StudyState currentStudyState = StudyState.WaitingToStart;
    
    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    private void Start()
    {
        startPanel.SetActive(true);
        gamePanel.SetActive(false);
        endPanel.SetActive(false);
        countdownPanel.SetActive(false);
        if (questionnairePanel != null) questionnairePanel.SetActive(false);
        if (pausePanel != null) pausePanel.SetActive(false);
        
        isInIntro = introRoutes != null && introRoutes.Count > 0;
        
        AudioListener.volume = 0.5f;
        
        // Load mouse sensitivity
        mouseSensitivity = PlayerPrefs.GetFloat("MouseSensitivity", 2f);
        if (sensitivitySlider != null)
        {
            sensitivitySlider.value = mouseSensitivity;
            sensitivitySlider.onValueChanged.AddListener(OnSensitivitySliderChanged);
        }

        SetupLogging();
        SetupLatinSquare();
        PrepareNextCondition();
    }

    public void OnSensitivitySliderChanged(float value)
    {
        SetMouseSensitivity(value);
    }

    public void SetMouseSensitivity(float value)
    {
        mouseSensitivity = value;
        PlayerPrefs.SetFloat("MouseSensitivity", mouseSensitivity);
        PlayerPrefs.Save();
    }

    private void SetupLogging()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        // --- WEBGL: Print Headers to Console ---
        Debug.Log("--- WEBGL LOGGING INITIALIZED ---");
        Debug.Log("Trials CSV Header: Participant,Mode,Route,GlobalTrial,Time(s),DistanceWalked(m)");
        Debug.Log("Nodes CSV Header: Participant,Mode,Route,GlobalTrial,NodeID,IsDecisionNode,EnterTime(s),LeaveTime(s),TimeSpentInNode(s)");
#else
        // --- DESKTOP/EDITOR: Create Physical Files ---
        trialsLogPath = Application.dataPath + $"/{participantID}_Results_Trials.csv";
        nodesLogPath = Application.dataPath + $"/{participantID}_Results_Nodes.csv";

        if (!File.Exists(trialsLogPath)) 
            File.AppendAllText(trialsLogPath, "Participant,Mode,Route,GlobalTrial,Time(s),DistanceWalked(m)\n");

        if (!File.Exists(nodesLogPath)) 
            File.AppendAllText(nodesLogPath, "Participant,Mode,Route,GlobalTrial,NodeID,IsDecisionNode,EnterTime(s),LeaveTime(s),TimeSpentInNode(s)\n");
#endif
    }

    private void SetupLatinSquare()
    {
        if (latinSquareGroup == 0) conditionOrder = new List<StudyMode> { StudyMode.SoundOnly, StudyMode.VisualOnly, StudyMode.SoundAndVisual };
        else if (latinSquareGroup == 1) conditionOrder = new List<StudyMode> { StudyMode.VisualOnly, StudyMode.SoundAndVisual, StudyMode.SoundOnly };
        else conditionOrder = new List<StudyMode> { StudyMode.SoundAndVisual, StudyMode.SoundOnly, StudyMode.VisualOnly };
    }

    private void PrepareNextCondition()
    {
        if (conditionIndex >= conditionOrder.Count)
        {
            EndStudy();
            return;
        }

        currentState = conditionOrder[conditionIndex];
        routeIndexWithinCondition = 0;

        currentConditionRoutes = new List<StudyRoute>(predefinedRoutes);
        for (int i = 0; i < currentConditionRoutes.Count; i++)
        {
            StudyRoute temp = currentConditionRoutes[i];
            int randomIndex = Random.Range(i, currentConditionRoutes.Count);
            currentConditionRoutes[i] = currentConditionRoutes[randomIndex];
            currentConditionRoutes[randomIndex] = temp;
        }
    }

    private void Update()
    {
        if (volumeText != null)
        {
            volumeText.text = $"Volume: {AudioListener.volume:P0}";
        }

        if (Input.GetKeyDown(KeyCode.Tab))
        {
            TogglePause();
        }

        if (IsPaused) return;

        HandleVolumeControl();

        if (currentStudyState == StudyState.WaitingToStart && Input.GetKeyDown(KeyCode.Space))
        {
            startPanel.SetActive(false);
            StartCoroutine(RunCountdownThenStart());
        }

        if (currentStudyState == StudyState.WaitingForQuestionnaire && Input.GetKeyDown(KeyCode.Space))
        {
            if (questionnairePanel != null) questionnairePanel.SetActive(false);
            
            conditionIndex++;
            PrepareNextCondition();

            if (currentStudyState != StudyState.Finished)
            {
                currentStudyState = StudyState.WaitingToStart;
                startPanel.SetActive(true);
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        if (currentStudyState == StudyState.Playing && isTrialActive && currentPlayer != null)
        {
            trialTimer += Time.deltaTime;
            float distanceThisFrame = Vector3.Distance(currentPlayer.transform.position, lastPlayerPosition);
            totalDistanceWalked += distanceThisFrame;
            lastPlayerPosition = currentPlayer.transform.position;
        }
    }

    private void HandleVolumeControl()
    {
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > 0.01f && currentState != StudyMode.VisualOnly )
        {
            // Only allow volume changes if we are NOT in VisualOnly mode, 
            // OR if we are in VisualOnly mode but want to allow the user to override (usually study constraints prefer 0 here).
            // For now, we allow adjustment but note that StartNextTrial sets it back to 0 if it's VisualOnly.
            
            float newVolume = AudioListener.volume + (scroll > 0 ? volumeStep : -volumeStep);
            AudioListener.volume = Mathf.Clamp01(newVolume);
            
            Debug.Log($"[StudyGameManager] Volume adjusted: {AudioListener.volume:P0}");
        }
    }

    public void TogglePause()
    {
        if (currentStudyState == StudyState.Finished) return;

        IsPaused = !IsPaused;
        Time.timeScale = IsPaused ? 0f : 1f;
        AudioListener.pause = IsPaused;

        Debug.Log($"[StudyGameManager] TogglePause: IsPaused={IsPaused}, TimeScale={Time.timeScale}");

        if (pausePanel != null) pausePanel.SetActive(IsPaused);
        if (gamePanel != null) gamePanel.SetActive(!IsPaused);

        if (IsPaused)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            Debug.Log("[StudyGameManager] Cursor Unlocked");
        }
        else
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            Debug.Log("[StudyGameManager] Cursor Locked");
        }
    }

    public void SetStudyMode(int modeIndex)
    {
        Debug.Log($"[StudyGameManager] SetStudyMode called with index: {modeIndex}");
        currentState = (StudyMode)modeIndex;
        if (IsPaused) TogglePause();
        RestartTrial();
    }

    public void ContinueFromQuestionnaire()
    {
        if (currentStudyState != StudyState.WaitingForQuestionnaire) return;

        if (questionnairePanel != null) questionnairePanel.SetActive(false);
    
        conditionIndex++;
        PrepareNextCondition();

        if (currentStudyState != StudyState.Finished)
        {
            currentStudyState = StudyState.WaitingToStart;
            startPanel.SetActive(true);
     
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    public void RestartTrial()
    {
        Debug.Log("[StudyGameManager] Restarting Trial...");
        if (currentPlayer != null) Destroy(currentPlayer);
        if (currentNPC != null) Destroy(currentNPC);
        isTrialActive = false;
        StopAllCoroutines();
        StartCoroutine(RunCountdownThenStart());
    }

    private void StartNextTrial()
    {
        trialTimer = 0f;
        totalDistanceWalked = 0f;
        currentNode = null;
        currentStudyState = StudyState.Playing;
        
        if (isInIntro)
        {
            currentRoute = introRoutes[introRouteIndex];
            currentState = StudyMode.SoundAndVisual;
            trialCounterText.text = $"Intro: {introRouteIndex + 1} / {introRoutes.Count}";
        }
        else
        {
            currentRoute = currentConditionRoutes[routeIndexWithinCondition];
            trialCounterText.text = $"Trial: {currentTrialGlobal + 1} / {conditionOrder.Count * predefinedRoutes.Count}";
        }

        if(conditionText != null) conditionText.text = $"Mode: {currentState}";

        if (currentPlayer != null) Destroy(currentPlayer);
        currentPlayer = Instantiate(playerPrefab, currentRoute.playerSpawnPoint.position + new Vector3(0, 0.5f, 0), currentRoute.playerSpawnPoint.rotation);
        lastPlayerPosition = currentPlayer.transform.position;

        // Apply Mode Settings
        var visualizer = currentPlayer.GetComponentInChildren<RaytracedAudioVisualizer>();
        if (visualizer != null)
        {
            visualizer.enabled = (currentState == StudyMode.VisualOnly || currentState == StudyMode.SoundAndVisual);
        }

        // Only mute if we are in VisualOnly mode. 
        // Otherwise, keep the current volume (which defaults to 0.5f and can be adjusted by the user).
        if (currentState == StudyMode.VisualOnly)
        {
            AudioListener.volume = 0.0f;
        }
        else if (AudioListener.volume <= 0.001f)
        {
            // If it was muted (e.g. from a previous VisualOnly trial), reset it to a default audible level.
            AudioListener.volume = 0.5f;
        }

        if (currentNPC != null) Destroy(currentNPC);
        if (npcPrefab != null && currentRoute.targetNode != null)
        {
            currentNPC = Instantiate(npcPrefab, currentRoute.targetNode.transform.position + new Vector3(0, 3.70f, 0), Quaternion.identity);
            currentNPC.transform.rotation = currentRoute.targetNode.transform.rotation;
        }
        
        isTrialActive = true;
    }
    public void LogNodeEnter(StudyNode node)
    {
        if (!isTrialActive) return;

        currentNode = node;
        nodeEnterTime = trialTimer;

        if (node == currentRoute.targetNode)
        {
            EndTrial();
        }
    }

    public void LogNodeExit(StudyNode node)
    {
        if (!isTrialActive || currentNode != node) return;

        float leaveTime = trialTimer;
        float timeSpent = leaveTime - nodeEnterTime;

        if (!isInIntro)
        {
            string nodeData = $"{participantID},{currentState},{currentRoute.routeName},{currentTrialGlobal},{node.nodeID},{node.isDecisionNode},{nodeEnterTime.ToString("F2", CultureInfo.InvariantCulture)},{leaveTime.ToString("F2", CultureInfo.InvariantCulture)},{timeSpent.ToString("F2", CultureInfo.InvariantCulture)}\n";
        
#if UNITY_WEBGL && !UNITY_EDITOR
            // TrimEnd removes the extra newline so the console output looks cleaner
            Debug.Log("[NODE DATA] " + nodeData.TrimEnd('\n')); 
#else
            File.AppendAllText(nodesLogPath, nodeData);
#endif
        }
        
        currentNode = null;
    }

    private void EndTrial()
    {
        isTrialActive = false;

        if (!isInIntro)
        {
            string trialData = $"{participantID},{currentState},{currentRoute.routeName},{currentTrialGlobal},{trialTimer.ToString("F2", CultureInfo.InvariantCulture)},{totalDistanceWalked.ToString("F2", CultureInfo.InvariantCulture)}\n";
        
#if UNITY_WEBGL && !UNITY_EDITOR
            // TrimEnd removes the extra newline so the console output looks cleaner
            Debug.Log("[TRIAL DATA] " + trialData.TrimEnd('\n'));
#else
            File.AppendAllText(trialsLogPath, trialData);
#endif
        }

        if (currentPlayer != null) Destroy(currentPlayer);
        if (currentNPC != null) Destroy(currentNPC);

        if (isInIntro)
        {
            introRouteIndex++;
            if (introRouteIndex >= introRoutes.Count)
            {
                isInIntro = false;
                // Ensure the state is set correctly for the first real condition
                if (conditionIndex < conditionOrder.Count)
                {
                    currentState = conditionOrder[conditionIndex];
                }
                
                currentStudyState = StudyState.WaitingToStart;
                startPanel.SetActive(true);
                gamePanel.SetActive(false);
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            else
            {
                StartCoroutine(RunCountdownThenStart());
            }
            return;
        }

        currentTrialGlobal++;
        routeIndexWithinCondition++;

        if (routeIndexWithinCondition >= currentConditionRoutes.Count)
        {
            ShowQuestionnaire();
            return;
        }

        if (currentStudyState != StudyState.Finished)
        {
            StartCoroutine(RunCountdownThenStart());
        }
    }

    private void ShowQuestionnaire()
    {
        currentStudyState = StudyState.WaitingForQuestionnaire;
        if (questionnairePanel != null) questionnairePanel.SetActive(true);
        startPanel.SetActive(false);
        gamePanel.SetActive(false);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void EndStudy()
    {
        Debug.Log($"Study Complete for {participantID}!");
        currentStudyState = StudyState.Finished;
        gamePanel.SetActive(false);
        endPanel.SetActive(true);
        AudioListener.volume = 1.0f;
        
        // Unlock cursor for the end screen
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }
    
    private IEnumerator RunCountdownThenStart()
    {
        currentStudyState = StudyState.CountingDown;
        gamePanel.SetActive(false);
        countdownPanel.SetActive(true);

        for (int i = 3; i > 0; i--)
        {
            countdownText.text = i.ToString();
            yield return new WaitForSeconds(1f);
        }

        countdownPanel.SetActive(false);
        gamePanel.SetActive(true);
        StartNextTrial();
    }
}