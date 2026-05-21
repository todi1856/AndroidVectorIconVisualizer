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
    TextField m_PathField;
    FileSystemWatcher m_Watcher;
    volatile bool m_NeedsRefresh;

    public void CreateGUI()
    {
        VisualElement root = rootVisualElement;

        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Center;

        m_PathField = new TextField();
        m_PathField.style.flexGrow = 1;
        m_PathField.RegisterValueChangedCallback(evt =>
        {
            SetupWatcher(evt.newValue);
            Refresh(evt.newValue);
        });
        row.Add(m_PathField);

        var openButton = new Button(() =>
        {
            var path = EditorUtility.OpenFilePanel("Select Android Vector Drawable", "", "xml");
            if (!string.IsNullOrEmpty(path))
                m_PathField.value = path;
        });
        openButton.text = "Open";
        openButton.style.flexShrink = 0;
        row.Add(openButton);

        root.Add(row);
    }

    void Update()
    {
        if (m_NeedsRefresh)
        {
            m_NeedsRefresh = false;
            Refresh(m_PathField.value);
        }
    }

    void OnDestroy()
    {
        DisposeWatcher();
    }

    void SetupWatcher(string path)
    {
        DisposeWatcher();
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return;

        m_Watcher = new FileSystemWatcher(Path.GetDirectoryName(path), Path.GetFileName(path));
        m_Watcher.NotifyFilter = NotifyFilters.LastWrite;
        m_Watcher.Changed += (_, __) => m_NeedsRefresh = true;
        m_Watcher.EnableRaisingEvents = true;
    }

    void DisposeWatcher()
    {
        if (m_Watcher != null)
        {
            m_Watcher.Dispose();
            m_Watcher = null;
        }
    }

    void Refresh(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return;
        if (m_Icon != null)
        {
            rootVisualElement.Remove(m_Icon);
            m_Icon = null;
        }
        try
        {
            m_Icon = new AndroidVectorDrawableElement(File.ReadAllText(path));
            m_Icon.style.width = 300;
            m_Icon.style.height = 300;
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Failed to load Android Vector Drawable: {ex}");
            return;
        }

        rootVisualElement.Add(m_Icon);
    }
}
