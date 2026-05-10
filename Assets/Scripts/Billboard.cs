using UnityEngine;

public class Billboard : MonoBehaviour
{
    private Transform camTransform;

    void Start()
    {
        // 메인 카메라의 위치를 찾습니다.
        camTransform = Camera.main.transform;
    }

    void LateUpdate()
    {
        // UI가 항상 카메라를 정면으로 바라보게 회전시킵니다.
        transform.LookAt(transform.position + camTransform.forward);
    }
}