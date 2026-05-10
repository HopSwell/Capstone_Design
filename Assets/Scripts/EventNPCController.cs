using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections; // 코루틴을 위해 추가

public class EventNPCController : MonoBehaviour
{
    // [추가] 대화 진행 상태를 명확히 구분하는 상태 머신
    public enum EventState { Ready, Story, Farewell, Done }

    [Header("이벤트 NPC 설정")]
    public string npcName = "상인 에릭";

    [Header("고정 이벤트 대사")]
    [Tooltip("LLM 없이 여기서 할당한 대사만 출력합니다.")]
    public DialogueData eventDialogue;

    [Header("UI 설정")]
    public GameObject localInteractionUI;

    [Header("회전 설정")]
    public float turnSpeed = 4f;
    private Transform playerTransform;
    private Coroutine lookCoroutine;
    private Quaternion initialRotation;

    private bool isPlayerInRange = false;
    private int currentStoryIndex = 0;

    // 현재 진행 상태를 추적 (초기값은 Ready)
    private EventState currentState = EventState.Ready;

    private void Start()
    {
        // 시작 시 현재 NPC 회전값 저장
        initialRotation = transform.rotation;
    }

    void Update()
    {
        if (isPlayerInRange && Keyboard.current != null && Keyboard.current.fKey.wasPressedThisFrame)
        {
            Interact();
        }
    }

    private void Interact()
    {
        if (localInteractionUI != null) localInteractionUI.SetActive(false);

        // 상태에 따라 행동을 분기합니다.
        switch (currentState)
        {
            case EventState.Ready:
            case EventState.Story:
                // 스토리 대사 순차적 출력
                if (eventDialogue != null && eventDialogue.storyDialogues != null && currentStoryIndex < eventDialogue.storyDialogues.Length)
                {
                    DialogueUIManager.Instance.ShowDialogue(npcName, eventDialogue.storyDialogues[currentStoryIndex]);
                    currentStoryIndex++;
                    currentState = EventState.Story;
                }
                else
                {
                    // 대사가 끝나면 작별 인사 출력
                    string farewell = (eventDialogue != null && !string.IsNullOrEmpty(eventDialogue.farewellMessage)) ? eventDialogue.farewellMessage : "휴, 이제야 살겠군.";
                    DialogueUIManager.Instance.ShowDialogue(npcName, farewell);
                    currentState = EventState.Farewell;


                }
                break;

            case EventState.Farewell:
                // 작별 인사 후 F 클릭 -> 대화창 닫고 완전 완료 상태로 변경
                DialogueUIManager.Instance.HideDialogue();
                currentState = EventState.Done;
                break;

            case EventState.Done:
                // 이미 이벤트가 끝난 후 다시 말을 걸었을 때의 토글 로직
                if (DialogueUIManager.Instance.isDialogueActive)
                {
                    DialogueUIManager.Instance.HideDialogue(); // 열려있으면 닫기
                }
                else
                {
                    DialogueUIManager.Instance.ShowDialogue(npcName, "도와주셔서 정말 고맙습니다. 마을 안에서 뵙지요."); // 닫혀있으면 짧은 감사 인사 켜기
                }
                break;
        }

        // 대화 시 플레이어를 쳐다보는 회전 로직 실행
        if (lookCoroutine != null) StopCoroutine(lookCoroutine);
        lookCoroutine = StartCoroutine(SmoothTurnToPlayer());
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            isPlayerInRange = true;
            playerTransform = other.transform; // 플레이어 위치 기억

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

            // 플레이어가 도중에 멀어지면 처음부터 다시 할 수 있도록 초기화
            // (단, 이미 이벤트를 완전히 끝낸 Done 상태라면 초기화하지 않음)
            if (currentState != EventState.Done)
            {
                currentState = EventState.Ready;
                currentStoryIndex = 0;
            }

            // 플레이어가 떠나면 원래 방향으로 스르륵 돌아가는 코루틴 실행
            if (lookCoroutine != null) StopCoroutine(lookCoroutine);
            lookCoroutine = StartCoroutine(SmoothTurnToOriginal());
        }
    }

    // 플레이어를 향해 돌아보는 코루틴
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

    // NPC 기존 방향으로 돌아가는 코루틴
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