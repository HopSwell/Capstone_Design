using Newtonsoft.Json.Linq;
using System;
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
    public bool isDevMode = false;

    [Header("NPC 설정")]
    public string npcName;

    [Header("디폴트 대사 설정")]
    public string[] defaultOutputs = { "지금은 대화하기 좀 어렵네. | 잠시 뒤에 오게나." };

    [Header("퀘스트 제어")]
    public bool forceDefaultDialogue = false;

    [Header("참조 모듈")]
    public LLMSettings llmSettings;

    [Header("UI 설정")]
    public GameObject localInteractionUI;

    [Header("회전 설정")]
    public float turnSpeed = 4f;

    [Header("LLM Retry")]
    public int maxRetryCount = 3;
    public float retryBaseDelay = 1.5f;
    public bool useFactFallbackWhenLLMFails = true;

    public event Action<NPCController, bool> DialogueCompleted;
    public bool LastDialogueUsedStoryContext { get; private set; }

    private Transform playerTransform;
    private Coroutine lookCoroutine;
    private Quaternion initialRotation;

    private NPCState currentState = NPCState.Idle;
    private int currentStoryIndex = 0;

    private List<List<string>> defaultDialoguePools = new List<List<string>>();
    private Dictionary<int, List<string>> cachedStoryDialogues = new Dictionary<int, List<string>>();
    private List<string> activeDialoguePool = null;

    private bool isPlayerInRange = false;
    private int lastKnownQuestStep = -1;
    private bool isFetching = false;
    private int lastDefaultIndex = -1;

    private static float nextAvailableRequestTime = 0f;
    private const float API_DELAY = 1.5f;

    private void Start() // 기본 대사 초기화 및 현재 단계 대본 호출
    {
        initialRotation = transform.rotation;
        RebuildDefaultDialoguePools();

        if (StoryManager.Instance != null)
        {
            lastKnownQuestStep = StoryManager.Instance.currentQuestStep;
            StartCoroutine(FetchStoryDialogueIfMissing(lastKnownQuestStep));
        }
    }

    private void Update() // 이야기 단계 변경 감지 및 상호작용 키 입력 대기
    {
        if (StoryManager.Instance != null && StoryManager.Instance.currentQuestStep != lastKnownQuestStep)
        {
            lastKnownQuestStep = StoryManager.Instance.currentQuestStep;
            StartCoroutine(FetchStoryDialogueIfMissing(lastKnownQuestStep));
        }

        if (isPlayerInRange && Keyboard.current != null && Keyboard.current.fKey.wasPressedThisFrame)
            Interact();
    }

    public void SetForceDefaultDialogue(bool value) // 기본 대사 강제 출력 여부 설정
    {
        forceDefaultDialogue = value;
    }

    public void RefreshStoryDialogueForCurrentStep() // 현재 이야기 단계에 맞는 대본 갱신
    {
        if (StoryManager.Instance == null) return;
        StartCoroutine(FetchStoryDialogueIfMissing(StoryManager.Instance.currentQuestStep));
    }

    private void RebuildDefaultDialoguePools() // 기본 대사 문자열 분할 및 목록 저장
    {
        defaultDialoguePools.Clear();

        foreach (string output in defaultOutputs)
            defaultDialoguePools.Add(ProcessDialogueText(output));
    }

    private string GetSystemInstruction() // 외부 인공지능용 세계관 및 대사 작성 규칙 지시문 정의
    {
        return "너는 다크 판타지 조사 게임의 NPC 대사를 생성하는 한국어 내러티브 엔진이다.\n\n" +
               "[세계관]\n" +
               "황혼 마을은 슬픔, 분노, 두려움 같은 감정을 드러내는 것을 금기시한다. " +
               "주민들은 평온한 얼굴과 절제된 말투를 강요받으며, 감정은 단서와 균열로만 새어 나온다.\n\n" +
               "[목표]\n" +
               "NPC의 페르소나와 현재 퀘스트 정보를 바탕으로, 플레이어가 다음 행동을 이해할 수 있는 자연스러운 한국어 대사를 만든다.\n\n" +
               "[대사 품질 규칙]\n" +
               "1. 출력은 반드시 한국어 대사만 작성한다.\n" +
               "2. 문장은 짧고 자연스럽게 쓴다. 한 문장은 35자 안팎으로 유지한다.\n" +
               "3. 전체 출력은 3~5문장으로 제한한다.\n" +
               "4. 문장 사이에는 반드시 '|' 기호를 넣는다.\n" +
               "5. 번역투, 설명문, 문어체 보고서 말투를 쓰지 않는다.\n" +
               "6. 행동 지문, 괄호 설명, 해설, 선택지, 목록 형식은 쓰지 않는다.\n" +
               "7. 핵심 정보는 빠뜨리지 않되, NPC의 말투로 자연스럽게 풀어 말한다.\n" +
               "8. 현재 퀘스트 단계에서 알 수 없는 정보는 절대 말하지 않는다.\n" +
               "9. 감정 억압 세계관은 직접 설명보다 말끝의 망설임, 회피, 경고로 드러낸다.\n\n" +
               "10. NPC 대사는 게임 속 실제 대화처럼 자연스러워야 한다.\n" +
               "11. 주제의식을 설명문처럼 말하지 말고, 인물의 망설임, 회피, 경고, 침묵으로 드러낸다.\n" +
               "12. 출력 문장은 기존 한국어 RPG 대사처럼 짧고 선명하게 쓴다.\n" +
               "13. 각 NPC의 예시 톤을 가장 우선으로 따른다.\n" +
               "[나쁜 예]\n" +
               "이 마을은 감정을 억압하는 사회 구조를 가지고 있으며 주민들은 심리적으로 고통받고 있습니다.\n\n" +
               "[좋은 예]\n" +
               "여기서는... 무서워도 웃어야 해요. | 울면 누가 보는 것 같거든요.";
    }

    private string BuildUserPrompt(StoryContextData context) // 인물 성격 및 핵심 정보가 포함된 요청 지시문 생성
    {
        return $"[NPC 이름]\n{npcName}\n\n" +
               $"[NPC 페르소나]\n{context.basePersona}\n\n" +
               $"[현재 장면에서 반드시 포함할 핵심 정보]\n{context.currentFact}\n\n" +
               "[출력 형식]\n" +
               "짧은 대사 3~5문장. 문장 사이에는 | 사용.\n" +
               "예: 문장1 | 문장2 | 문장3";
    }

    private IEnumerator FetchStoryDialogueIfMissing(int step) // 외부 인공지능 통신을 통한 새로운 대사 생성 및 저장
    {
        if (isFetching || cachedStoryDialogues.ContainsKey(step))
            yield break;

        isFetching = true;

        if (isDevMode)
        {
            isFetching = false;
            yield break;
        }

        StoryContextData targetContext = GetContextForStep(step);
        if (targetContext == null)
        {
            isFetching = false;
            yield break;
        }

        if (llmSettings == null || string.IsNullOrWhiteSpace(llmSettings.apiKey) || string.IsNullOrWhiteSpace(llmSettings.modelName))
        {
            Debug.LogWarning($"[LLM 설정 없음] {npcName}");
            isFetching = false;
            yield break;
        }

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

        string apiKey = llmSettings.apiKey.Trim();
        string modelName = llmSettings.modelName.Trim();
        string url = $"https://generativelanguage.googleapis.com/v1beta/models/{modelName}:generateContent?key={apiKey}";

        JObject requestData = new JObject
        {
            ["systemInstruction"] = new JObject
            {
                ["parts"] = new JArray { new JObject { ["text"] = GetSystemInstruction() } }
            },
            ["contents"] = new JArray
            {
                new JObject
                {
                    ["parts"] = new JArray { new JObject { ["text"] = BuildUserPrompt(targetContext) } }
                }
            },
            ["generationConfig"] = new JObject { ["temperature"] = 0.6 }
        };

        bool success = false;
        string lastError = "";

        for (int attempt = 1; attempt <= maxRetryCount; attempt++)
        {
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
                        string rawText = responseJson["candidates"]?[0]?["content"]?["parts"]?[0]?["text"]?.ToString();
                        List<string> parsedDialogue = ProcessDialogueText(rawText ?? "");

                        if (parsedDialogue.Count > 0)
                        {
                            cachedStoryDialogues[step] = parsedDialogue;
                            Debug.Log($"[LLM 로딩 완료] {npcName} / step {step}");
                            success = true;
                            break;
                        }

                        lastError = "응답 텍스트가 비어 있음";
                    }
                    catch (Exception e)
                    {
                        lastError = e.Message;
                        Debug.LogWarning($"[응답 파싱 실패] {npcName} / step {step} / {attempt}/{maxRetryCount} - {e.Message}");
                    }
                }
                else
                {
                    lastError = request.error;
                    Debug.LogWarning($"[통신 실패] {npcName} / step {step} / {attempt}/{maxRetryCount} - {request.error}");
                }
            }

            if (!success && attempt < maxRetryCount)
            {
                float delay = retryBaseDelay * attempt;
                yield return new WaitForSeconds(delay);
            }
        }

        if (!success && useFactFallbackWhenLLMFails)
        {
            string fallbackText = ConvertFactToFallbackDialogue(targetContext.currentFact);
            List<string> fallbackDialogue = ProcessDialogueText(fallbackText);

            if (fallbackDialogue.Count > 0)
            {
                cachedStoryDialogues[step] = fallbackDialogue;
                Debug.LogWarning($"[LLM fallback] {npcName} / step {step} - API 실패로 Current Fact를 사용합니다. 마지막 오류: {lastError}");
            }
            else
            {
                Debug.LogWarning($"[LLM 최종 실패] {npcName} / step {step} - fallback도 비어 있습니다. 마지막 오류: {lastError}");
            }
        }

        isFetching = false;
    }

    private string ConvertFactToFallbackDialogue(string fact) // 통신 실패 시 핵심 정보를 대체 대사로 변환
    {
        if (string.IsNullOrWhiteSpace(fact))
            return "";

        string clean = fact
            .Replace("\r\n\r\n", "|")
            .Replace("\n\n", "|")
            .Replace("\r\n", " ")
            .Replace("\n", " ")
            .Replace("\"", "")
            .Trim();

        return clean;
    }

    private void Interact() // 상호작용 시 적절한 대사 목록 선택 및 출력
    {
        if (localInteractionUI != null)
            localInteractionUI.SetActive(false);

        if (currentState == NPCState.Idle)
        {
            int currentStep = StoryManager.Instance != null ? StoryManager.Instance.currentQuestStep : 0;
            LastDialogueUsedStoryContext = false;

            if (!forceDefaultDialogue && cachedStoryDialogues.ContainsKey(currentStep))
            {
                activeDialoguePool = cachedStoryDialogues[currentStep];
                LastDialogueUsedStoryContext = true;
            }
            else if (defaultDialoguePools.Count > 0)
            {
                int randomIndex = lastDefaultIndex;

                if (defaultDialoguePools.Count > 1)
                {
                    while (randomIndex == lastDefaultIndex)
                        randomIndex = UnityEngine.Random.Range(0, defaultDialoguePools.Count);
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

        if (lookCoroutine != null)
            StopCoroutine(lookCoroutine);

        lookCoroutine = StartCoroutine(SmoothTurnToPlayer());
    }

    private StoryContextData GetContextForStep(int step) // 현재 단계에 맞는 대본 데이터 탐색
    {
        if (StoryManager.Instance == null) return null;

        foreach (StoryContextData ctx in StoryManager.Instance.storyContexts)
        {
            if (ctx != null && ctx.npcName == npcName && ctx.questStep == step)
                return ctx;
        }

        return null;
    }

    private List<string> ProcessDialogueText(string rawText) // 구분자를 기준으로 문자열을 분할하여 대사 배열로 변환
    {
        List<string> result = new List<string>();
        string cleanText = (rawText ?? "").Replace("\\|", "|").Replace("｜", "|");
        string[] splitLines = cleanText.Split('|');

        foreach (string line in splitLines)
        {
            string trimmed = line.Trim();
            if (!string.IsNullOrWhiteSpace(trimmed))
                result.Add(trimmed);
        }

        return result;
    }

    private void ShowFarewell() // 대화 종료 처리 및 완료 알림 발생
    {
        DialogueUIManager.Instance.HideDialogue();

        bool usedStoryDialogue = LastDialogueUsedStoryContext;
        DialogueCompleted?.Invoke(this, usedStoryDialogue);

        if (usedStoryDialogue && StoryManager.Instance != null)
            StoryManager.Instance.NotifyNpcDialogueCompleted(npcName);

        ResetDialogueState();
    }

    private void ResetDialogueState() // 대화 상태 초기화 및 상호작용 대기
    {
        currentState = NPCState.Idle;
        currentStoryIndex = 0;
        activeDialoguePool = null;
        LastDialogueUsedStoryContext = false;

        if (isPlayerInRange && localInteractionUI != null)
            StartCoroutine(ReactivateUIAfterDelay(1.5f));
    }

    private IEnumerator ReactivateUIAfterDelay(float delay) // 지연 시간 후 상호작용 화면 표시 재활성화
    {
        yield return new WaitForSeconds(delay);

        if (isPlayerInRange && currentState == NPCState.Idle && localInteractionUI != null)
            localInteractionUI.SetActive(true);
    }

    private void OnTriggerEnter(Collider other) // 플레이어 접근 감지 및 상호작용 표시 활성화
    {
        if (other.CompareTag("Player"))
        {
            isPlayerInRange = true;
            playerTransform = other.transform;

            if (currentState == NPCState.Idle && localInteractionUI != null)
                localInteractionUI.SetActive(true);
        }
    }

    private void OnTriggerExit(Collider other) // 플레이어 이탈 감지 및 대화창 숨김, 시선 원상복구
    {
        if (other.CompareTag("Player"))
        {
            isPlayerInRange = false;

            if (localInteractionUI != null)
                localInteractionUI.SetActive(false);

            DialogueUIManager.Instance.HideDialogue();
            ResetDialogueState();

            if (lookCoroutine != null)
                StopCoroutine(lookCoroutine);

            lookCoroutine = StartCoroutine(SmoothTurnToOriginal());
        }
    }

    private IEnumerator SmoothTurnToPlayer() // 플레이어 방향으로 부드럽게 회전
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

    private IEnumerator SmoothTurnToOriginal() // 원래 방향으로 부드럽게 회전 복구
    {
        while (Quaternion.Angle(transform.rotation, initialRotation) > 0.1f)
        {
            transform.rotation = Quaternion.Slerp(transform.rotation, initialRotation, turnSpeed * Time.deltaTime);
            yield return null;
        }

        transform.rotation = initialRotation;
    }
}