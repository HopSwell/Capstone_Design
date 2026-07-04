using System.Collections;
using UnityEngine;
using TMPro;
using UnityEngine.InputSystem;

public class DialogueUIManager : MonoBehaviour
{
    public static DialogueUIManager Instance;

    [Header("대화 UI")]
    public GameObject dialoguePanel;
    public TMP_Text nameText;
    public TMP_Text dialogueText;

    [Header("Player Lock")]
    public PlayerMove playerMove;

    public bool isDialogueActive = false;

    private Coroutine typingCoroutine;

    void Awake() // 단일 객체 초기화 및 시작 시 대화창 숨김
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        if (playerMove == null)
            playerMove = FindFirstObjectByType<PlayerMove>();

        dialoguePanel.SetActive(false);
    }

    public void ShowDialogue(string npcName, string content) // 대화창 팝업 후 플레이어 고정
    {
        isDialogueActive = true;

        if (playerMove != null)
            playerMove.SetMovementLocked(true);

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        dialoguePanel.SetActive(true);
        nameText.text = npcName;

        if (typingCoroutine != null)
            StopCoroutine(typingCoroutine);

        typingCoroutine = StartCoroutine(TypeTextEffect(content));
    }

    void Update() // 대화하는 도중 ESC키로 대화 닫기.
    {
        if (isDialogueActive && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            HideDialogue();
        }
    }

    public void HideDialogue() // 대화창 숨김 및 플레이어 조작 잠금 해제
    {
        isDialogueActive = false;

        if (playerMove != null)
            playerMove.SetMovementLocked(false);

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        if (typingCoroutine != null)
        {
            StopCoroutine(typingCoroutine);
            typingCoroutine = null;
        }

        dialoguePanel.SetActive(false);
    }

    private IEnumerator TypeTextEffect(string textToType) // 글자를 한글씩 타이핑하는 효과를 내는 함수
    {
        dialogueText.text = "";

        foreach (char letter in textToType.ToCharArray())
        {
            dialogueText.text += letter;
            yield return new WaitForSeconds(0.04f);
        }

        typingCoroutine = null;
    }
}