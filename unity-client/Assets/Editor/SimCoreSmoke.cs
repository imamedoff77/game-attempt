using UnityEditor;
using UnityEngine;

// Usage: Unity -batchmode -nographics -quit -executeMethod SimCoreSmoke.Run
public static class SimCoreSmoke
{
    public static void Run()
    {
        bool ok = SimCoreSelfTest.Run(out string failure);
        if (!ok) Debug.LogError("SimCore self-test failed: " + failure);
        Debug.Log(ok ? "SIMCORE_SMOKE: PASS" : "SIMCORE_SMOKE: FAIL");
        EditorApplication.Exit(ok ? 0 : 1);
    }
}
