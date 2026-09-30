using UnityEngine;
using UnityEngine.Events; // 必须引入这个命名空间

public class AppQuitHandler : MonoBehaviour
{
    private const string TAG = "[AppQuitHandler] ";
    
    [Header("Events Triggered Before Quit")]
    public UnityEvent onBeforeQuit;

    public void QuitApplication()
    {
        Logger.LogApp(TAG + "QuitApplication triggered.");
        try
        {
            onBeforeQuit?.Invoke();
        }
        catch (System.Exception e)
        {
            Logger.LogApp(TAG + e.Message, LogType.Error);
        }

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#elif UNITY_ANDROID
        try
        {
            using (AndroidJavaClass processClass = new AndroidJavaClass("android.os.Process"))
            {
                int pid = processClass.CallStatic<int>("myPid");
                processClass.CallStatic("killProcess", pid);
            }
        }
        catch (System.Exception e)
        {
            Logger.LogApp(TAG + e.Message, LogType.Error);
            Application.Quit();
        }
#else
        Application.Quit();
#endif
    }
}