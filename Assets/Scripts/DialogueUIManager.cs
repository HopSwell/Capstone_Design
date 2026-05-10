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

    // [추가] 현재 대화 중인지 알려주는 상태 변수
    public bool isDialogueActive = false;

    private Coroutine typingCoroutine;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        dialoguePanel.SetActive(false);
    }

    public void ShowDialogue(string npcName, string content)
    {
        isDialogueActive = true; // [추가] 대화 상태 ON

        // [추가] 마우스 커서 잠금 해제 및 표시 (대화창을 편하게 보기 위함)
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        dialoguePanel.SetActive(true);
        nameText.text = npcName;

        if (typingCoroutine != null) StopCoroutine(typingCoroutine);
        typingCoroutine = StartCoroutine(TypeTextEffect(content));
    }
    void Update()
    {
        // 대화창이 켜져 있고, ESC 키가 눌렸을 때
        if (isDialogueActive && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            HideDialogue(); // 대화 즉시 종료
        }
    }

    public void HideDialogue()
    {
        isDialogueActive = false; // [추가] 대화 상태 OFF

        // [추가] 마우스 커서 다시 화면 중앙에 잠그고 숨기기
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        dialoguePanel.SetActive(false);
    }

    private IEnumerator TypeTextEffect(string textToType)
    {
        dialogueText.text = "";
        foreach (char letter in textToType.ToCharArray())
        {
            dialogueText.text += letter;
            yield return new WaitForSeconds(0.04f);
        }
    }
}