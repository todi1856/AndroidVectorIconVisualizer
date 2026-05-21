using System.Drawing;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

public class AndroidIconVisualizer : EditorWindow
{
    [MenuItem("Examples/Android Icon Visualizer")]
    public static void ShowExample()
    {
        AndroidIconVisualizer wnd = GetWindow<AndroidIconVisualizer>();
        wnd.titleContent = new GUIContent("Android Icon Visualizer");
    }

    AndroidVectorDrawableElement m_Icon;
    string m_LastPath;

    public void CreateGUI()
    {
        // Each editor window contains a root VisualElement object
        VisualElement root = rootVisualElement;

        // VisualElements objects can contain other VisualElement following a tree hierarchy
        Label label = new Label("Hello World!");
        root.Add(label);

        // Create button
        var button = new Button();
        button.text = "Open";
        button.clicked += () =>
        {
            var path = EditorUtility.OpenFilePanel("Select Android Vector Drawable", "", "xml");
            if (!string.IsNullOrEmpty(path))
            {
                m_LastPath = path;
                Refresh(path);
            }
        };
        root.Add(button);

        button = new Button();
        button.text = "Refresh";
        button.clicked += () =>
        {
            Refresh(m_LastPath);
        };
        root.Add(button);

    }

    private void Refresh(string path)
    {
        if (string.IsNullOrEmpty(path))
            return;
        if (!File.Exists(path))
            return;
        if (m_Icon != null)
            rootVisualElement.Remove(m_Icon);
        m_Icon = new AndroidVectorDrawableElement(File.ReadAllText(path));
        m_Icon.style.width = 300;
        m_Icon.style.height = 300;
        rootVisualElement.Add(m_Icon);
    }
}
