using UnityEngine;

[CreateAssetMenu(fileName = "NewDialogueData", menuName = "Dialogue/NPC Dialogue Data")]
public class DialogueData : ScriptableObject
{
    [Header("스토리 대사")]
    [TextArea(3, 5)]
    [Tooltip("F를 누를 때마다 순차적으로 출력될 대사")]
    public string[] storyDialogues;

    [Header("작별 인사")]
    [TextArea(2, 3)]
    public string farewellMessage = "다음에 또 봅세";
}