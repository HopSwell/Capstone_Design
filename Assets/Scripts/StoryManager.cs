using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

[Serializable]
public class QuestProgressGroup
{
    public string label;
    public int requiredCount = 1;
    public int currentCount = 0;
    public List<string> objectiveIds = new List<string>();

    public bool ContainsObjective(string objectiveId) // 해당 목표가 현재 그룹에 포함되는지 확인
    {
        return objectiveIds.Contains(objectiveId);
    }

    public bool IsComplete() // 그룹 내 목표 요구 수량 달성 여부 확인
    {
        return currentCount >= requiredCount;
    }
}

[Serializable]
public class QuestInfo
{
    public int step;
    public string title;
    [TextArea(2, 4)] public string objective;
    public string location;
    public int requiredCount = 1;
    public int currentCount = 0;
    public List<QuestProgressGroup> progressGroups = new List<QuestProgressGroup>();

    public string GetProgressText() // 진행 현황 화면 출력용 텍스트 생성
    {
        if (progressGroups != null && progressGroups.Count > 0)
        {
            List<string> lines = new List<string>();

            foreach (QuestProgressGroup group in progressGroups)
            {
                if (group == null) continue;
                lines.Add($"{group.label} : {group.currentCount} / {group.requiredCount}");
            }

            return string.Join("\n", lines);
        }

        if (requiredCount <= 1)
            return "";

        return $"진행도 : {currentCount} / {requiredCount}";
    }

    public bool IsComplete() // 해당 퀘스트의 모든 목표 달성 여부 확인
    {
        if (progressGroups != null && progressGroups.Count > 0)
        {
            foreach (QuestProgressGroup group in progressGroups)
            {
                if (group != null && !group.IsComplete())
                    return false;
            }

            return true;
        }

        return currentCount >= requiredCount;
    }

    public void ResetProgress() // 임무 진행 상황 초기화
    {
        currentCount = 0;

        if (progressGroups == null) return;

        foreach (QuestProgressGroup group in progressGroups)
        {
            if (group != null)
                group.currentCount = 0;
        }
    }
}

public class StoryManager : MonoBehaviour
{
    public static StoryManager Instance;

    [Header("현재 스토리 진행 단계")]
    public int currentQuestStep = 0;

    [Header("스토리 데이터 리스트")]
    public List<StoryContextData> storyContexts;

    [Header("Quest HUD")]
    public QuestHUDController questHUD;
    public List<QuestInfo> quests = new List<QuestInfo>();
    public bool showQuestOnStart = false;

    public event Action<QuestInfo> QuestChanged;

    private readonly HashSet<string> completedObjectives = new HashSet<string>();
    private bool questVisible;
    private bool aliceRequestAccepted;

    private void Awake() // 단일 객체 초기화
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    private void Start() // 기본 임무 흐름 구성 및 시작 시 표시 여부 설정
    {
        BuildDefaultQuestFlow();

        questVisible = showQuestOnStart;

        if (questVisible)
            NotifyQuestChanged();
        else if (questHUD != null)
            questHUD.HideImmediate();
    }

    private void Update() // 키 입력을 통한 임무 단계 강제 이동
    {
        if (Keyboard.current == null) return;

        if (Keyboard.current.pageUpKey.wasPressedThisFrame)
        {
            ShowQuestHUD();
            ChangeQuestStep(currentQuestStep + 1);
        }

        if (Keyboard.current.pageDownKey.wasPressedThisFrame && currentQuestStep > 0)
        {
            ShowQuestHUD();
            ChangeQuestStep(currentQuestStep - 1);
        }
    }

    public StoryContextData GetCurrentStoryContext(string npcName) // 대상 인물 및 현재 단계와 일치하는 대본 검색
    {
        foreach (StoryContextData context in storyContexts)
        {
            if (context != null && context.questStep == currentQuestStep && context.npcName == npcName)
                return context;
        }

        return null;
    }

    public QuestInfo GetCurrentQuest() // 현재 진행 중인 임무 정보 반환
    {
        foreach (QuestInfo quest in quests)
        {
            if (quest.step == currentQuestStep)
                return quest;
        }

        return null;
    }

    public bool IsObjectiveCompleted(string objectiveId) // 특정 목표의 완료 기록 확인
    {
        if (string.IsNullOrEmpty(objectiveId)) return false;
        return completedObjectives.Contains(objectiveId);
    }

    public void NotifyNpcDialogueCompleted(string npcName) // 대화 종료 시 조건에 따른 임무 단계 진행 처리
    {
        if (currentQuestStep == 0 && npcName == "에릭")
        {
            CompleteQuestAndAdvance();
        }
        else if (currentQuestStep == 1 && npcName == "한스")
        {
            CompleteQuestAndAdvance();
        }
        else if (currentQuestStep == 2 && npcName == "앨리스")
        {
            BeginAliceInvestigation();
        }
        else if (currentQuestStep == 3 && npcName == "앨리스")
        {
            CompleteQuestAndAdvance();
        }
        else if (currentQuestStep == 4 && npcName == "마틴")
        {
            CompleteQuestAndAdvance();
        }
        else if (currentQuestStep == 5 && npcName == "헨리")
        {
            CompleteQuestAndAdvance();
        }
        else if (currentQuestStep == 6 && npcName == "헤이즐")
        {
            CompleteQuestAndAdvance();
        }
        else if (currentQuestStep == 8 && npcName == "한스")
        {
            CompleteQuestAndAdvance();
        }
    }

    private void BeginAliceInvestigation() // 단서 수집 임무 관련 초기 세팅 및 시작
    {
        if (aliceRequestAccepted) return;

        aliceRequestAccepted = true;
        completedObjectives.Clear();

        QuestInfo quest = GetCurrentQuest();
        if (quest == null) return;

        quest.title = "앨리스의 부탁";
        quest.objective = "앨리스의 텐트 근처에서 목걸이와 단서들을 조사하라.";
        quest.location = "마을 우측 캠프";
        quest.requiredCount = 4;
        quest.currentCount = 0;

        quest.progressGroups.Clear();

        quest.progressGroups.Add(new QuestProgressGroup
        {
            label = "목걸이",
            requiredCount = 1,
            currentCount = 0,
            objectiveIds = new List<string> { "alice_necklace" }
        });

        quest.progressGroups.Add(new QuestProgressGroup
        {
            label = "단서",
            requiredCount = 3,
            currentCount = 0,
            objectiveIds = new List<string>
            {
                "alice_compass",
                "alice_silver",
                "alice_scribble"
            }
        });

        NotifyQuestChanged();
    }

    public void CompleteObjective(string objectiveId) // 목표 달성 처리 및 임무 완료 여부 확인
    {
        if (string.IsNullOrEmpty(objectiveId)) return;
        if (completedObjectives.Contains(objectiveId)) return;

        QuestInfo quest = GetCurrentQuest();
        if (quest == null) return;

        completedObjectives.Add(objectiveId);

        if (EvidenceLogManager.Instance != null)
            EvidenceLogManager.Instance.AcquireEvidence(objectiveId);

        bool usedGroup = false;

        if (quest.progressGroups != null && quest.progressGroups.Count > 0)
        {
            foreach (QuestProgressGroup group in quest.progressGroups)
            {
                if (group == null) continue;

                if (group.ContainsObjective(objectiveId))
                {
                    group.currentCount = Mathf.Clamp(group.currentCount + 1, 0, group.requiredCount);
                    usedGroup = true;
                    break;
                }
            }
        }

        if (!usedGroup)
            quest.currentCount = Mathf.Clamp(quest.currentCount + 1, 0, quest.requiredCount);

        NotifyQuestChanged();

        if (quest.IsComplete())
            CompleteQuestAndAdvance();
    }

    public void NotifyBossDefeated() // 최종 괴물 처치 시 임무 완료 처리
    {
        if (currentQuestStep != 9) return;

        QuestInfo quest = GetCurrentQuest();

        if (quest != null)
        {
            quest.currentCount = 1;
            NotifyQuestChanged();
        }

        CompleteQuestAndAdvance();
    }

    public void SetCurrentQuestProgress(int currentCount, int requiredCount = -1) // 임무 현재 진행도 강제 설정
    {
        QuestInfo quest = GetCurrentQuest();
        if (quest == null) return;

        if (requiredCount > 0)
            quest.requiredCount = requiredCount;

        quest.currentCount = Mathf.Clamp(currentCount, 0, quest.requiredCount);
        NotifyQuestChanged();
    }

    public void SetCurrentQuestObjective(string objective, string location = null) // 임무 목표 내용 및 장소 텍스트 변경
    {
        QuestInfo quest = GetCurrentQuest();
        if (quest == null) return;

        quest.objective = objective;

        if (location != null)
            quest.location = location;

        NotifyQuestChanged();
    }

    public void ShowQuestHUD() // 임무 진행 상황 화면 표시
    {
        questVisible = true;
        NotifyQuestChanged();
    }

    public void HideQuestHUD() // 임무 진행 상황 화면 숨김
    {
        questVisible = false;

        if (questHUD != null)
            questHUD.Hide();

        QuestChanged?.Invoke(null);
    }

    public void CompleteQuestAndAdvance() // 현재 임무 완료 및 다음 단계로 이동
    {
        ChangeQuestStep(currentQuestStep + 1);
    }

    public void ChangeQuestStep(int step) // 지정된 임무 단계로 변경 및 기록 초기화
    {
        currentQuestStep = Mathf.Max(0, step);
        completedObjectives.Clear();

        if (currentQuestStep == 2)
            aliceRequestAccepted = false;

        QuestInfo quest = GetCurrentQuest();
        if (quest != null)
            quest.ResetProgress();

        Debug.Log($"[스토리 매니저] 현재 퀘스트 단계: {currentQuestStep}");

        if (currentQuestStep > 0)
            ShowQuestHUD();
        else
            NotifyQuestChanged();
    }

    private void NotifyQuestChanged() // 임무 변경 사항 화면 업데이트 알림 호출
    {
        QuestInfo quest = questVisible ? GetCurrentQuest() : null;

        if (questHUD != null)
            questHUD.Refresh(quest);

        QuestChanged?.Invoke(quest);
    }

    private void BuildDefaultQuestFlow() // 전체 임무 흐름 및 목표 데이터 생성
    {
        if (quests.Count > 0) return;

        quests.Add(new QuestInfo
        {
            step = 0,
            title = "상인 구출",
            objective = "에릭을 위협하는 늑대들을 처치하라.",
            location = "마을 입구",
            requiredCount = 3
        });

        quests.Add(new QuestInfo
        {
            step = 1,
            title = "촌장의 집",
            objective = "빨간 깃발이 걸린 2층 집에서 한스를 찾아라.",
            location = "빨간 깃발의 2층 집",
            requiredCount = 1
        });

        quests.Add(new QuestInfo
        {
            step = 2,
            title = "앨리스를 찾아서",
            objective = "마을 초입에 있는 앨리스와 대화하라.",
            location = "마을 초입",
            requiredCount = 1
        });

        quests.Add(new QuestInfo
        {
            step = 3,
            title = "앨리스에게 보고",
            objective = "찾아낸 물건과 단서를 앨리스에게 전하라.",
            location = "마을 초입",
            requiredCount = 1
        });

        quests.Add(new QuestInfo
        {
            step = 4,
            title = "마틴의 부탁",
            objective = "마을 안쪽에 있는 마틴과 대화하라.",
            location = "마을 안쪽",
            requiredCount = 1
        });

        quests.Add(new QuestInfo
        {
            step = 5,
            title = "수상한 배달",
            objective = "마틴의 상자를 헨리에게 전달하라.",
            location = "헨리의 집",
            requiredCount = 1
        });

        quests.Add(new QuestInfo
        {
            step = 6,
            title = "낯선 소리",
            objective = "마을 입구에서 들려오는 중얼거림의 주인을 찾아라.",
            location = "마을 입구",
            requiredCount = 1
        });

        quests.Add(new QuestInfo
        {
            step = 7,
            title = "헤이즐의 중얼거림",
            objective = "기둥 근처의 흔적과 촌장 집의 제물 목록을 조사하라.",
            location = "검은 기둥 / 촌장 집",
            requiredCount = 2
        });

        quests.Add(new QuestInfo
        {
            step = 8,
            title = "감춰진 질서",
            objective = "한스에게 의식의 진실을 추궁하라.",
            location = "촌장 집",
            requiredCount = 1
        });

        quests.Add(new QuestInfo
        {
            step = 9,
            title = "갈증의 모노리스",
            objective = "억눌린 희노애락이 뒤틀려 탄생한 갈증의 모노리스를 처치하라.",
            location = "검은 기둥",
            requiredCount = 1
        });
    }
}