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
    [Tooltip("체크 시 API 통신을 생략하고 가짜 텍스트를 출력합니다. (토큰 소모 0)")]
    public bool isDevMode = false;

    [Header("NPC 설정")]
    public string npcName;
    [TextArea(3, 5)]
    public string defaultOutput = "지금은 대화하기 좀 어렵네. | 잠시 뒤에 오게나.";

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
    private List<string> defaultDialoguePool = new List<string>();
    private Dictionary<int, List<string>> cachedStoryDialogues = new Dictionary<int, List<string>>();
    private List<string> activeDialoguePool = null;

    private bool isPlayerInRange = false;
    private int lastKnownQuestStep = -1;
    private bool isFetching = false;

    private void Start()
    {
        initialRotation = transform.rotation;
        defaultDialoguePool = ProcessDialogueText(defaultOutput);

        // 씬 시작 시 현재 퀘스트 스텝에 대해서만 로딩 (Plan B 적용)
        if (StoryManager.Instance != null)
        {
            lastKnownQuestStep = StoryManager.Instance.currentQuestStep;
            StartCoroutine(FetchStoryDialogueIfMissing(lastKnownQuestStep));
        }
    }

    void Update()
    {
        // Page Up/Down 등으로 퀘스트 스텝이 변경된 것을 감지하면 해당 스텝 대본 추가 로딩
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

    private IEnumerator FetchStoryDialogueIfMissing(int step)
    {
        // 1. 이미 캐싱되어 있다면 아무것도 하지 않음 (중복 토큰 소모 방지)
        if (cachedStoryDialogues.ContainsKey(step)) yield break;

        // 2. 동시 다발적 중복 통신 방지 락(Lock)
        while (isFetching) yield return null;
        isFetching = true;

        // 3. [개발자 모드] 켜져 있으면 API 통신 생략 후 기본 대사(defaultOutput) 연결
        if (isDevMode)
        {
            Debug.Log($"<color=yellow>[개발자 모드]</color> {npcName} - 스텝 {step} 기본 대사로 대체 (API 호출 0회)");
            // 미리 파싱해 둔 디폴트 대사 풀을 그대로 참조하여 캐싱합니다.
            cachedStoryDialogues[step] = defaultDialoguePool;
            isFetching = false;
            yield break;
        }
        // 4. 이 NPC에게 해당 스텝의 스토리 데이터가 존재하는지 확인
        StoryContextData targetContext = GetContextForStep(step);
        if (targetContext == null)
        {
            // 스텝에 맞는 데이터가 없다면 통신하지 않음 (자연스럽게 defaultOutput으로 폴백)
            isFetching = false;
            yield break;
        }

        // 5. 실제 API 통신 진행
        Debug.Log($"<color=cyan>[API 요청]</color> {npcName} - 스텝 {step} 데이터 로딩 중...");

        string apiKey = llmSettings.apiKey.Trim();
        string modelName = llmSettings.modelName.Trim();
        string url = $"https://generativelanguage.googleapis.com/v1beta/models/{modelName}:generateContent?key={apiKey}";

        JObject requestData = new JObject
        {
            ["contents"] = new JArray { new JObject { ["parts"] = new JArray { new JObject { ["text"] = BuildPrompt(targetContext) } } } }
        };

        using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(requestData.ToString());
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.timeout = 10;

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
                        cachedStoryDialogues[step] = parsedDialogue; // 딕셔너리에 영구 캐싱
                        Debug.Log($"<color=green>[로딩 완료]</color> {npcName} - 스텝 {step} 캐싱 성공.");
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"<color=red>[응답 파싱 오류]</color> {npcName} 스텝 {step} - {e.Message}");
                }
            }
            else
            {
                Debug.LogWarning($"<color=orange>[통신 실패]</color> {npcName} 스텝 {step} - {request.error}");
            }
        }

        isFetching = false;
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

    private string BuildPrompt(StoryContextData context)
    {
        return $"{context.basePersona}\n\n" +
               $"[현재 상황]\n{context.currentFact}\n\n" +
               $"[제약 조건]\n{context.negativeConstraints}\n\n" +
               $"[시스템 필수 명령]\n" +
               $"너는 게임 속 NPC다. 2~3문장으로 답변하되, 문장 사이에 반드시 '|' 기호를 넣어라.\n" +
               $"절대 '버전', '대사:' 같은 부가 설명을 적지 말고 아래 양식만 지켜라.\n" +
               $"양식: 문장1 | 문장2 | 문장3";
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

    private void Interact()
    {
        if (localInteractionUI != null) localInteractionUI.SetActive(false);

        if (currentState == NPCState.Idle)
        {
            int currentStep = StoryManager.Instance != null ? StoryManager.Instance.currentQuestStep : 0;

            // 현재 스텝의 대사가 캐싱되어 있다면 가져오고, 없으면 디폴트 대사 사용
            if (cachedStoryDialogues.ContainsKey(currentStep))
            {
                activeDialoguePool = cachedStoryDialogues[currentStep];
            }
            else
            {
                activeDialoguePool = defaultDialoguePool;
            }

            currentState = NPCState.Story;
            currentStoryIndex = 0;
        }

        switch (currentState)
        {
            case NPCState.Story:
                if (activeDialoguePool != null && currentStoryIndex < activeDialoguePool.Count)
                {
                    DialogueUIManager.Instance.ShowDialogue(npcName, activeDialoguePool[currentStoryIndex]);
                    currentStoryIndex++;
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
        DialogueUIManager.Instance.HideDialogue();
        currentState = NPCState.Farewell;
    }

    private void ResetDialogueState()
    {
        currentState = NPCState.Idle;
        currentStoryIndex = 0;
        activeDialoguePool = null;

        if (isPlayerInRange && localInteractionUI != null)
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

            if (currentState == NPCState.Idle && localInteractionUI != null)
            {
                localInteractionUI.SetActive(true);
            }
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