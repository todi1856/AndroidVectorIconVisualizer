using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

public class AndroidIconVisualizer : EditorWindow
{
    public AndroidVectorDrawableAsset m_Icon;
    [MenuItem("Examples/Android Icon Visualizer")]
    public static void ShowExample()
    {
        AndroidIconVisualizer wnd = GetWindow<AndroidIconVisualizer>();
        wnd.titleContent = new GUIContent("Android Icon Visualizer");
    }

    private void OnGUI()
    {
        m_Icon = (AndroidVectorDrawableAsset)EditorGUILayout.ObjectField("Icon", m_Icon, typeof(AndroidVectorDrawableAsset), true);
    }
}
