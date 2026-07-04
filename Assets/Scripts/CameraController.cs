using UnityEngine;
using UnityEngine.InputSystem;

public class CameraController : MonoBehaviour
{
    [Header("카메라 설정")]
    public Transform target;
    public float distance = 4f;
    public float mouseSensitivity = 0.3f;

    private float currentX = 0f;
    private float currentY = 15f;

    void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void LateUpdate()
    {
        // 대화 중이면 카메라 회전 중단
        if (DialogueUIManager.Instance != null && DialogueUIManager.Instance.isDialogueActive) return;

        if (target == null || Mouse.current == null) return;

        Vector2 mouseDelta = Mouse.current.delta.ReadValue();

        currentX += mouseDelta.x * mouseSensitivity;
        currentY -= mouseDelta.y * mouseSensitivity;

        currentY = Mathf.Clamp(currentY, -10f, 60f);

        Vector3 direction = new Vector3(0, 0, -distance);
        Quaternion rotation = Quaternion.Euler(currentY, currentX, 0);

        Vector3 targetPosition = target.position + Vector3.up * 1.5f;

        transform.position = targetPosition + rotation * direction;
        transform.LookAt(targetPosition);
    }
}