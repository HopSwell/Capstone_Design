using Newtonsoft.Json.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.InputSystem;

public class NPCController : MonoBehaviour
{
    public enum NPCState { Idle, Greeting, Story, Farewell }

    [Header("NPC 설정")]
    public string npcName;
    [TextArea(3, 5)]
    public string systemPrompt;
    public string defaultOutput = "지금은 바빠 보인다, 다음에 다시 찾아오자";

    [Header("스토리 데이터")]
    public DialogueData dialogueData;

    [Header("참조 모듈")]
    public LLMSettings llmSettings;

    [Header("UI 설정")]
    public GameObject localInteractionUI;

    [Header("회전 설정")]
    public float turnSpeed = 4f;
    private Transform playerTransform;
    private Coroutine lookCoroutine;
    private Quaternion initialRotation;

    private NPCState currentState = NPCState.Idle;
    private int currentStoryIndex = 0;

    private List<string> dialoguePool = new List<string>();
    private bool isLoaded = false;
    private bool isPlayerInRange = false;

    private static float globalApiDelay = 0f;
    private static int currentKeyIndex = 0;

    private void Awake()
    {
        globalApiDelay = 0f;
    }

    private void Start()
    {
        initialRotation = transform.rotation;

        if (llmSettings != null)
        {
            Debug.Log($"{npcName}: LLM 설정 확인");
            StartCoroutine(DelayedPreload());
            globalApiDelay += 1.5f;
        }
        else
        {
            Debug.LogError($"{npcName}: LLM Settings 누락");
        }
    }

    private IEnumerator DelayedPreload()
    {
        float waitTime = globalApiDelay;
        Debug.Log($"{npcName}: {waitTime}초 후 API 요청 시작");

        if (waitTime > 0)
        {
            yield return new WaitForSeconds(waitTime);
        }
        yield return StartCoroutine(PreloadDialogue());
    }

    void Update()
    {
        if (isPlayerInRange && Keyboard.current != null && Keyboard.current.fKey.wasPressedThisFrame)
        {
            if (!DialogueUIManager.Instance.isDialogueActive && currentState != NPCState.Idle)
            {
                ResetDialogueState();
            }

            Interact();
        }
    }

    private void Interact()
    {
        if (localInteractionUI != null) localInteractionUI.SetActive(false);

        switch (currentState)
        {
            case NPCState.Idle:
                string greeting = defaultOutput;
                if (isLoaded && dialoguePool.Count > 0)
                {
                    greeting = dialoguePool[Random.Range(0, dialoguePool.Count)];
                }
                DialogueUIManager.Instance.ShowDialogue(npcName, greeting);
                currentState = NPCState.Greeting;
                break;

            case NPCState.Greeting:
            case NPCState.Story:
                if (dialogueData != null && dialogueData.storyDialogues != null && currentStoryIndex < dialogueData.storyDialogues.Length)
                {
                    DialogueUIManager.Instance.ShowDialogue(npcName, dialogueData.storyDialogues[currentStoryIndex]);
                    currentStoryIndex++;
                    currentState = NPCState.Story;
                }
                else
                {
                    ShowFarewell();
                }
                break;

            case NPCState.Farewell:
                DialogueUIManager.Instance.HideDialogue();
                ResetDialogueState();
                break;
        }

        if (lookCoroutine != null) StopCoroutine(lookCoroutine);
        lookCoroutine = StartCoroutine(SmoothTurnToPlayer());
    }

    private void ShowFarewell()
    {
        string farewell = (dialogueData != null && !string.IsNullOrEmpty(dialogueData.farewellMessage)) ? dialogueData.farewellMessage : "다음에 또 보지.";
        DialogueUIManager.Instance.ShowDialogue(npcName, farewell);
        currentState = NPCState.Farewell;

    }

    private void ResetDialogueState()
    {
        currentState = NPCState.Idle;
        currentStoryIndex = 0;
    }

    private IEnumerator PreloadDialogue()
    {
        if (llmSettings.apiKeys == null || llmSettings.apiKeys.Length == 0)
        {
            Debug.LogError($"{npcName}: API 키가 설정되지 않았습니다. LLMSettings를 확인하세요.");
            yield break;
        }

        Debug.Log($"{npcName} API 통신 시도 (사용 중인 키 인덱스: {currentKeyIndex})");

        string apiKey = llmSettings.apiKeys[currentKeyIndex].Trim();
        string modelName = llmSettings.modelName.Trim();
        string url = $"https://generativelanguage.googleapis.com/v1beta/models/{modelName}:generateContent?key={apiKey}";

        JObject requestData = new JObject
        {
            ["contents"] = new JArray { new JObject { ["parts"] = new JArray { new JObject { ["text"] = systemPrompt } } } }
        };

        string jsonPayload = requestData.ToString();

        // 통신 종료 시 자동으로 메모리 누수 방지
        using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonPayload);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.uploadHandler.contentType = "application/json";
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            // 유니티 내장 타임아웃 기능 설정 (10초)
            request.timeout = 10;

            // API 응답 대기
            yield return request.SendWebRequest();

            // 통신 결과가 '성공'이 아닌 모든 경우 (타임아웃, 429 등)
            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"<color=orange>[API 연결 실패]</color> {npcName} - 사유: {request.error}. 다음 키로 교체하여 재시도합니다.");

                // 다음 키로 인덱스 변경
                currentKeyIndex = (currentKeyIndex + 1) % llmSettings.apiKeys.Length;

                // 서버 과부하 및 유니티 프리징 방지를 위해 1초 대기 후 재귀 호출
                yield return new WaitForSeconds(1f);
                yield return StartCoroutine(PreloadDialogue());
                yield break; 
            }

            // 여기까지 도달했다면 통신 성공
            try
            {
                JObject responseJson = JObject.Parse(request.downloadHandler.text);
                string rawText = responseJson["candidates"][0]["content"]["parts"][0]["text"].ToString();

                string[] splitLines = rawText.Split('|');
                dialoguePool.Clear();

                foreach (string line in splitLines)
                {
                    string cleanLine = line.Trim();
                    if (!string.IsNullOrWhiteSpace(cleanLine))
                    {
                        dialoguePool.Add(cleanLine);
                    }
                }

                if (dialoguePool.Count > 0)
                {
                    isLoaded = true;
                    Debug.Log($"<color=green>{npcName}의 대사가 준비 완료되었습니다. (준비된 대사: {dialoguePool.Count}개)</color>");
                }
                else
                {
                    Debug.LogWarning($"{npcName}: 파싱할 수 있는 대사가 없습니다. 프롬프트를 확인하세요.");
                }
            }
            catch (System.Exception e)
            {
                // 데이터 파싱 에러는 키 교체 문제가 아니므로 재시도하지 않고 에러만 출력
                Debug.LogError($"{npcName}: JSON 해석 실패 - {e.Message}");
            }
        } // using 블록이 끝나면 request 객체는 안전하게 파괴
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            isPlayerInRange = true;
            playerTransform = other.transform;

            if (localInteractionUI != null) localInteractionUI.SetActive(true);
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            isPlayerInRange = false;

            if (localInteractionUI != null) localInteractionUI.SetActive(false);

            DialogueUIManager.Instance.HideDialogue();

            ResetDialogueState();

            if (lookCoroutine != null) StopCoroutine(lookCoroutine);
            lookCoroutine = StartCoroutine(SmoothTurnToOriginal());
        }
    }

    private IEnumerator SmoothTurnToPlayer()
    {
        if (playerTransform == null) yield break;

        Vector3 direction = (playerTransform.position - transform.position).normalized;
        direction.y = 0;
        Quaternion targetRotation = Quaternion.LookRotation(direction);

        while (Quaternion.Angle(transform.rotation, targetRotation) > 0.1f)
        {
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, turnSpeed * Time.deltaTime);
            yield return null;
        }
    }

    private IEnumerator SmoothTurnToOriginal()
    {
        while (Quaternion.Angle(transform.rotation, initialRotation) > 0.1f)
        {
            transform.rotation = Quaternion.Slerp(transform.rotation, initialRotation, turnSpeed * Time.deltaTime);
            yield return null;
        }

        transform.rotation = initialRotation;
    }
}