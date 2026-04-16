using UnityEngine;
using Cinemachine;

public class CameraManager : MonoBehaviour
{
    [Header("Editor Camera (Top Down)")]
    public Camera editorCamera;

    [Header("Driving Camera (Cinemachine)")]
    public CinemachineVirtualCamera followVCam;

    void Start()
    {
        SwitchToEditor();
    }

    public void SwitchToDriving(GameObject car)
    {
        if (car == null) return;

        followVCam.Follow = car.transform;
        followVCam.LookAt = car.transform;

        followVCam.Priority = 10;

        editorCamera.enabled = false;
    }

    public void SwitchToEditor()
    {
        followVCam.Priority = 0;

        editorCamera.enabled = true;
    }
}
