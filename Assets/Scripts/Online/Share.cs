using UnityEngine;

// Opens the phone's share sheet with the invite. Elsewhere (Editor, desktop, and iOS until a native
// share plugin is added) the text goes to the clipboard instead.
public static class Share
{
    // True when the share sheet opened, false when the text was copied.
    public static bool Text(string text, string title)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        using (var intentClass = new AndroidJavaClass("android.content.Intent"))
        using (var intent = new AndroidJavaObject("android.content.Intent"))
        using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
        using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
        {
            intent.Call<AndroidJavaObject>("setAction", intentClass.GetStatic<string>("ACTION_SEND"));
            intent.Call<AndroidJavaObject>("setType", "text/plain");
            intent.Call<AndroidJavaObject>("putExtra", intentClass.GetStatic<string>("EXTRA_TEXT"), text);
            using (var chooser = intentClass.CallStatic<AndroidJavaObject>("createChooser", intent, title))
                activity.Call("startActivity", chooser);
        }
        return true;
#else
        Copy(text);
        return false;
#endif
    }

    public static void Copy(string text) => GUIUtility.systemCopyBuffer = text;
}
