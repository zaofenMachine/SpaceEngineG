using UnityEditor;
using UnityEditorInternal;

namespace SpaceEngine.Editor
{
    /// <summary>Keep requested background flight/tests advancing when the Editor is unfocused.</summary>
    [InitializeOnLoad]
    public static class BackgroundPlayerLoop
    {
        static BackgroundPlayerLoop(){EditorApplication.update+=Pump;}
        static void Pump()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode&&!InternalEditorUtility.isApplicationActive)
            {
                EditorApplication.QueuePlayerLoopUpdate();
                InternalEditorUtility.RepaintAllViews();
            }
        }
    }
}
