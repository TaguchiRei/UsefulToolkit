using System.Globalization;
using UnityEngine;
using UsefulToolkit.Attributes;
using UsefulToolkit.Debugging;

public class Test : MonoBehaviour
{
    private SceneViewInfoBoard info;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        info = SceneViewInfoBoard.Setup(gameObject, "title", "description");
        info.ObserveInfo(() => Time.deltaTime.ToString(CultureInfo.InvariantCulture));
    }

    // Update is called once per frame
    void Update()
    {
    }

    [MethodExecutor]
    void TestMethod()
    {
        Debug.Log("TestMethod");
    }
}