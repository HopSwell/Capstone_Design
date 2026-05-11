using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class StoryManager : MonoBehaviour
{
    public static StoryManager Instance;

    [Header("현재 스토리 진행 단계")]
    public int currentQuestStep = 0;

    [Header("스토리 데이터 리스트")]
    public List<StoryContextData> storyContexts;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    public StoryContextData GetCurrentStoryContext(string npcName)
    {
        foreach (var context in storyContexts)
        {
            if (context.questStep == currentQuestStep && context.npcName == npcName)
            {
                return context;
            }
        }
        return null;
    }

    private void Update()
    {
        if (Keyboard.current != null)
        {
            // Page Up 키: 퀘스트 단계 1단계 올리기
            if (Keyboard.current.pageUpKey.wasPressedThisFrame)
            {
                ChangeQuestStep(currentQuestStep + 1);
            }

            // Page Down 키: 퀘스트 단계 1단계 내리기 (0 미만으로는 내려가지 않음)
            if (Keyboard.current.pageDownKey.wasPressedThisFrame)
            {
                if (currentQuestStep > 0)
                {
                    ChangeQuestStep(currentQuestStep - 1);
                }
            }
        }
    }

    private void ChangeQuestStep(int step)
    {
        currentQuestStep = step;
        Debug.Log($"<color=cyan>[스토리 매니저]</color> 현재 퀘스트 단계가 <b>{currentQuestStep}</b> (으)로 변경되었습니다.");
    }
}