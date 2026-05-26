using System.IO;
using System.Xml.Linq;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;
using UnityEngine.UIElements;

[ScriptedImporter(1, "android_xml")]
public class AndroidVectorDrawableImporter : ScriptedImporter
{
    public override void OnImportAsset(AssetImportContext ctx)
    {
        string xml = File.ReadAllText(ctx.assetPath);

        bool isVectorDrawable = false;
        try
        {
            var doc = XDocument.Parse(xml);
            isVectorDrawable = doc.Root?.Name.LocalName == "vector";
        }
        catch
        {
            // Not valid XML
        }

        if (isVectorDrawable)
        {
            var asset = ScriptableObject.CreateInstance<AndroidVectorDrawableAsset>();
            asset.xmlContent = xml;
            ctx.AddObjectToAsset("main", asset);
            ctx.SetMainObject(asset);
        }
        else
        {
            var textAsset = new TextAsset(xml);
            ctx.AddObjectToAsset("main", textAsset);
            ctx.SetMainObject(textAsset);
        }
    }
}

[CustomEditor(typeof(AndroidVectorDrawableImporter))]
public class AndroidVectorDrawableImporterEditor : ScriptedImporterEditor
{
    protected override bool needsApplyRevert => false;

    public override VisualElement CreateInspectorGUI()
    {
        var root = new VisualElement();

        var importer = (AndroidVectorDrawableImporter)target;
        string path = importer.assetPath;

        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return root;

        string xml;
        try
        {
            xml = File.ReadAllText(path);
            var doc = XDocument.Parse(xml);
            if (doc.Root?.Name.LocalName != "vector")
                return root;
        }
        catch
        {
            return root;
        }

        var container = new VisualElement();
        container.style.alignItems = Align.Center;
        container.style.paddingTop = 8;
        container.style.paddingBottom = 8;

        var preview = new AndroidVectorDrawableElement(xml);
        preview.style.width = 256;
        preview.style.height = 256;
        container.Add(preview);

        root.Add(container);

        var nameLabel = new Label(Path.GetFileNameWithoutExtension(path));
        nameLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
        nameLabel.style.marginTop = 4;
        nameLabel.style.marginBottom = 8;
        root.Add(nameLabel);

        var foldout = new Foldout { text = "XML Source", value = false };
        var scrollView = new ScrollView();
        scrollView.style.maxHeight = 300;
        var xmlLabel = new Label(xml);
        xmlLabel.style.whiteSpace = WhiteSpace.Pre;
        xmlLabel.style.fontSize = 11;
        scrollView.Add(xmlLabel);
        foldout.Add(scrollView);
        root.Add(foldout);

        return root;
    }
}
