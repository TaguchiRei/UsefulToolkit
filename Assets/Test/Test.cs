using System.Globalization;
using UnityEngine;
using UsefulToolkit.Attributes;
using UsefulToolkit.Debugging;

public class Test : MonoBehaviour
{
    [ShowOnly] public string TestString = "Test";
    private SceneViewInfoBoard info;
    
    void Start()
    {
        info = SceneViewInfoBoard.Setup(gameObject, "title", "description");
        info.ObserveInfo(() => Time.deltaTime.ToString(CultureInfo.InvariantCulture));
    }

    [MethodExecutor("test", true)]
    public void TestMethod()
    {
        Debug.Log("TestMethod");
    }
}