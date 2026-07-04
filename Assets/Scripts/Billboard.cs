using UnityEngine;

public class Billboard : MonoBehaviour
{
    private Transform camTransform;

    void Start() // 메인 카메라 위치 저장
    {
        camTransform = Camera.main.transform;
    }

    void LateUpdate() // 화면 표시가 항상 카메라를 정면으로 바라보도록 회전
    {
        transform.LookAt(transform.position + camTransform.forward);
    }
}