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

    [Header("테스트 설정")]
    [Tooltip("체크 시 API 통신을 생략하고 가짜 텍스트를 출력합니다.")]
    public bool isDevMode = false;

    [Header("NPC 설정")]
    public string npcName;

    [Header("디폴트 대사 설정")]
    [Tooltip("퀘스트 조건이 맞지 않을 때 출력할 대사들입니다. 배열로 여러 개 등록 가능합니다.")]
    public string[] defaultOutputs = { "지금은 대화하기 좀 어렵네. | 잠시 뒤에 오게나." };

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

    // 대사 저장소 (메모리 캐싱)
    private List<List<string>> defaultDialoguePools = new List<List<string>>();
    private Dictionary<int, List<string>> cachedStoryDialogues = new Dictionary<int, List<string>>();
    private List<string> activeDialoguePool = null;

    private bool isPlayerInRange = false;
    private int lastKnownQuestStep = -1;
    private bool isFetching = false;

    // 랜덤 대사 중복 방지용 변수
    private int lastDefaultIndex = -1;

    // API 지연 제어용 정적 변수
    private static float nextAvailableRequestTime = 0f;
    private const float API_DELAY = 1.5f;

    private void Start()
    {
        initialRotation = transform.rotation;

        // 모든 디폴트 대사 미리 파싱
        foreach (string output in defaultOutputs)
        {
            defaultDialoguePools.Add(ProcessDialogueText(output));
        }

        if (StoryManager.Instance != null)
        {
            lastKnownQuestStep = StoryManager.Instance.currentQuestStep;
            StartCoroutine(FetchStoryDialogueIfMissing(lastKnownQuestStep));
        }
    }

    void Update()
    {
        if (StoryManager.Instance != null && StoryManager.Instance.currentQuestStep != lastKnownQuestStep)
        {
            lastKnownQuestStep = StoryManager.Instance.currentQuestStep;
            StartCoroutine(FetchStoryDialogueIfMissing(lastKnownQuestStep));
        }

        if (isPlayerInRange && Keyboard.current != null && Keyboard.current.fKey.wasPressedThisFrame)
        {
            Interact();
        }
    }

    private string GetSystemInstruction()
    {
        return "너는 롤플레잉 게임의 NPC 대사를 런타임에 처리하는 '내러티브 텍스트 엔진'이다.\n\n" +
               "[시스템 목적]\n" +
               "퀘스트 진행의 무결성(Integrity)을 보장하기 위해, 기획자가 의도한 핵심 정보를 왜곡 없이 플레이어에게 전달해야 한다.\n\n" +
               "[출력 규칙]\n" +
               "1. [지정된 대본]의 문장을 하나도 빠짐없이 순서대로 출력할 것.\n" +
               "2. [페르소나]의 성격에 맞게 말하되, 게임 시스템과의 충돌을 방지하기 위해 대본에 없는 감정 묘사(*웃음* 등), 행동 지문, 사족은 엄격히 차단할 것.\n" +
               "3. UI 텍스트 파싱 시스템 규격에 맞춰, 문장 사이에는 무조건 '|' 기호를 넣어 구분할 것.\n" +
               "4. 양식: 대본문장1 | 대본문장2 | 대본문장3 | 대본문장4";
    }

    private string BuildUserPrompt(StoryContextData context)
    {
        return $"[내 이름]: {npcName}\n" +
               $"[페르소나]: {context.basePersona}\n" +
               $"[지정된 대본]:\n{context.currentFact}";
    }
    private IEnumerator FetchStoryDialogueIfMissing(int step)
    {
        if (isDevMode)
        {
            Debug.Log($"<color=yellow>[개발자 모드]</color> {npcName} -  퀘스트 단계 : {step} / 디폴트 랜덤 대사 출력)");
            isFetching = false;
            yield break; 
        }
    

        StoryContextData targetContext = GetContextForStep(step);
        if (targetContext == null)
        {
            isFetching = false;
            yield break;
        }

        // 트래픽 스파이크 방지용 순차 대기열
        if (Time.time < nextAvailableRequestTime)
        {
            float waitTime = nextAvailableRequestTime - Time.time;
            nextAvailableRequestTime += API_DELAY;
            yield return new WaitForSeconds(waitTime);
        }
        else
        {
            nextAvailableRequestTime = Time.time + API_DELAY;
        }

        Debug.Log($"<color=cyan>[API 요청]</color> {npcName} - 스텝 {step} 데이터 로딩 중...");

        string apiKey = llmSettings.apiKey.Trim();
        string modelName = llmSettings.modelName.Trim();
        string url = $"https://generativelanguage.googleapis.com/v1beta/models/{modelName}:generateContent?key={apiKey}";

        JObject requestData = new JObject
        {
            ["systemInstruction"] = new JObject { ["parts"] = new JArray { new JObject { ["text"] = GetSystemInstruction() } } },
            ["contents"] = new JArray { new JObject { ["parts"] = new JArray { new JObject { ["text"] = BuildUserPrompt(targetContext) } } } },
            ["generationConfig"] = new JObject { ["temperature"] = 0.6 }
        };

        using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(requestData.ToString());
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            request.timeout = 20;

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                try
                {
                    JObject responseJson = JObject.Parse(request.downloadHandler.text);
                    string rawText = responseJson["candidates"][0]["content"]["parts"][0]["text"].ToString();
                    List<string> parsedDialogue = ProcessDialogueText(rawText);

                    if (parsedDialogue.Count > 0)
                    {
                        cachedStoryDialogues[step] = parsedDialogue;
                        Debug.Log($"<color=green>[로딩 완료]</color> {npcName} - 스텝 {step} 캐싱 성공.");
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"<color=red>[응답 파싱 오류]</color> {npcName} - {e.Message}");
                }
            }
            else
            {
                string errorBody = request.downloadHandler != null ? request.downloadHandler.text : "내용 없음";
                Debug.LogWarning($"<color=orange>[통신 실패]</color> {npcName} - {request.error}\n원인: {errorBody}");
            }
        }

        isFetching = false;
    }

    private void Interact()
    {
        if (localInteractionUI != null) localInteractionUI.SetActive(false);

        if (currentState == NPCState.Idle)
        {
            int currentStep = StoryManager.Instance != null ? StoryManager.Instance.currentQuestStep : 0;

            if (cachedStoryDialogues.ContainsKey(currentStep))
            {
                activeDialoguePool = cachedStoryDialogues[currentStep];
            }
            else if (defaultDialoguePools.Count > 0)
            {
                // 안티 리피티션 랜덤 선택 로직
                int randomIndex = lastDefaultIndex;
                if (defaultDialoguePools.Count > 1)
                {
                    while (randomIndex == lastDefaultIndex)
                    {
                        randomIndex = UnityEngine.Random.Range(0, defaultDialoguePools.Count);
                    }
                }
                else
                {
                    randomIndex = 0;
                }

                lastDefaultIndex = randomIndex;
                activeDialoguePool = defaultDialoguePools[randomIndex];
            }

            currentState = NPCState.Story;
            currentStoryIndex = 0;
        }

        if (activeDialoguePool != null && currentStoryIndex < activeDialoguePool.Count)
        {
            DialogueUIManager.Instance.ShowDialogue(npcName, activeDialoguePool[currentStoryIndex]);
            currentStoryIndex++;
        }
        else
        {
            ShowFarewell();
        }

        if (lookCoroutine != null) StopCoroutine(lookCoroutine);
        lookCoroutine = StartCoroutine(SmoothTurnToPlayer());
    }

    private StoryContextData GetContextForStep(int step)
    {
        if (StoryManager.Instance == null) return null;
        foreach (var ctx in StoryManager.Instance.storyContexts)
        {
            if (ctx.npcName == this.npcName && ctx.questStep == step) return ctx;
        }
        return null;
    }

    private List<string> ProcessDialogueText(string rawText)
    {
        List<string> result = new List<string>();
        string cleanText = rawText.Replace("\\|", "|").Replace("｜", "|");
        string[] splitLines = cleanText.Split('|');
        foreach (string line in splitLines)
        {
            string trimmed = line.Trim();
            if (!string.IsNullOrWhiteSpace(trimmed)) result.Add(trimmed);
        }
        return result;
    }

    private void ShowFarewell()
    {
        DialogueUIManager.Instance.HideDialogue();

        ResetDialogueState();
    }

    private void ResetDialogueState()
    {
        currentState = NPCState.Idle;
        currentStoryIndex = 0;
        activeDialoguePool = null;

        if (isPlayerInRange && localInteractionUI != null)
        {
            StartCoroutine(ReactivateUIAfterDelay(1.5f));
        }
    }
    private IEnumerator ReactivateUIAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);

        // 1.5초가 지난 후에도 플레이어가 여전히 범위 안에 있고, 대화 중이 아니라면 UI 켜기
        if (isPlayerInRange && currentState == NPCState.Idle && localInteractionUI != null)
        {
            localInteractionUI.SetActive(true);
        }
    }


    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            isPlayerInRange = true;
            playerTransform = other.transform;
            if (currentState == NPCState.Idle && localInteractionUI != null) localInteractionUI.SetActive(true);
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