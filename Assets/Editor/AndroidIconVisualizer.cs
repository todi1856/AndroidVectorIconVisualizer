using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

public class AndroidIconVisualizer : EditorWindow
{
    [SerializeField] private AndroidVectorDrawableAsset m_Icon;

    private VisualElement m_PreviewContainer;

    [MenuItem("Android/Android Icon Visualizer")]
    public static void ShowExample()
    {
        var wnd = GetWindow<AndroidIconVisualizer>();
        wnd.titleContent = new GUIContent("Android Icon Visualizer");
    }

    public void CreateGUI()
    {
        rootVisualElement.Add(new Label("<b>Note: Had to .android_xml extension, since .xml is reserved by something else.</b>"));
        rootVisualElement.Add(new Label("<b>Select an Android Vector Drawable Asset to preview its icon:</b>"));
        var objectField = new ObjectField("Icon")
        {
            objectType = typeof(AndroidVectorDrawableAsset),
            value = m_Icon
        };
        objectField.RegisterValueChangedCallback(evt =>
        {
            m_Icon = evt.newValue as AndroidVectorDrawableAsset;
            UpdatePreview();
        });
        rootVisualElement.Add(objectField);

        m_PreviewContainer = new VisualElement();
        m_PreviewContainer.style.alignItems = Align.Center;
        m_PreviewContainer.style.paddingTop = 8;
        m_PreviewContainer.style.flexGrow = 1;
        rootVisualElement.Add(m_PreviewContainer);

        UpdatePreview();
    }

    private void UpdatePreview()
    {
        m_PreviewContainer.Clear();

        if (m_Icon == null || string.IsNullOrEmpty(m_Icon.xmlContent))
            return;

        var preview = new AndroidVectorDrawableElement(m_Icon.xmlContent);
        preview.style.width = 256;
        preview.style.height = 256;
        m_PreviewContainer.Add(preview);
    }
}
