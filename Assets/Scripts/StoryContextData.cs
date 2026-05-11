using UnityEngine;

[CreateAssetMenu(fileName = "NewStoryContext", menuName = "Story/Story Context Data", order = 0)]
public class StoryContextData : ScriptableObject
{
    public string npcName; // 이 대본을 사용할 NPC의 이름

    [Header("진행 상태")]
    [Tooltip("이 대본이 사용될 퀘스트의 단계 (예: 0 = 시작 전, 1 = 촌장 만남 이후)")]
    public int questStep;

    [Header("1. NPC 페르소나 (기본 성격)")]
    [TextArea(3, 5)]
    [Tooltip("예: 당신은 황혼 마을의 무뚝뚝한 대장장이입니다. 플레이어에게 경계심을 가집니다.")]
    public string basePersona;

    [Header("2. 전달해야 할 핵심 정보 (Fact / Intent)")]
    [TextArea(3, 5)]
    [Tooltip("NPC가 대화 중 반드시 유저에게 전달해야 하는 정보")]
    public string currentFact;

    [Header("3. 절대 하면 안 되는 행동 (Negative Constraints)")]
    [TextArea(3, 5)]
    [Tooltip("탈옥, 환각 방지용 제약 조건")]
    public string negativeConstraints = "1. 플레이어에게 임의로 아이템, 금화 등의 보상을 약속하지 마시오.\n2. 내가 알려주지 않은 지명이나 인물을 지어내어 말하지 마시오.\n3. 대답은 3문장 이내로 짧고 간결하게 하시오.";
}