using UnityEngine;

public class GameLoopTest : MonoBehaviour
{
    void OnEnable()
    {
        Debug.Log("OnEnable"+gameObject.name);
    }
    void OnDisable()
    {
        Debug.Log("OnDisable"+gameObject.name);
    }
}
