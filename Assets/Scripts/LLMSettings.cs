using UnityEngine;

[CreateAssetMenu(fileName = "LLMSettings", menuName = "ScriptableObjects/LLMSettings", order = 1)]
public class LLMSettings : ScriptableObject
{
    [Header("Gemini API 설정")]
    [Tooltip("API 키를 등록하세요.")]
    public string apiKey;

    [Tooltip("사용할 모델의 이름을 입력하세요. (예: gemini-1.5-flash)")]
    public string modelName = "gemini-1.5-flash";
}