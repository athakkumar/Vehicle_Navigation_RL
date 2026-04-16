using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;

public class LiveConsole : MonoBehaviour
{
    public Transform contentParent;
    public GameObject logTextPrefab;
    public ScrollRect scrollRect;

    private Queue<GameObject> logs = new Queue<GameObject>();
    public int maxLogs = 100;

    void OnEnable()
    {
        Application.logMessageReceived += HandleLog;
    }

    void OnDisable()
    {
        Application.logMessageReceived -= HandleLog;
    }

    void HandleLog(string logString, string stackTrace, LogType type)
{
    GameObject newLog = Instantiate(logTextPrefab, contentParent);
    TMP_Text txt = newLog.GetComponent<TMP_Text>();

    switch(type)
    {
        case LogType.Error:
        case LogType.Exception:
            txt.color = Color.red;
            break;
        case LogType.Warning:
            txt.color = Color.yellow;
            break;
        default:
            txt.color = Color.green;
            break;
    }

    txt.text = logString;

    logs.Enqueue(newLog);

    if (logs.Count > maxLogs)
        Destroy(logs.Dequeue());

    StartCoroutine(ScrollToBottom());
}

IEnumerator ScrollToBottom()
{
    yield return null;

    Canvas.ForceUpdateCanvases();
    scrollRect.verticalNormalizedPosition = 0f;
}

}
