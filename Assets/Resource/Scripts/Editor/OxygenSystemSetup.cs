using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace Resource.Scripts.Editor
{
    /// <summary>Explicit, repeatable scene setup; never runs automatically on import.</summary>
    public static class OxygenSystemSetup
    {
        public const string ArtFolder = "Assets/Resource/Art/Oxygen";
        public const string TankPath = "Assets/Resource/Prefabs/OxygenTank.prefab";

        [MenuItem("Tools/Gravity Game/Install Oxygen System in Current Scene")]
        public static void InstallCurrentScene()
        {
            if (Application.isPlaying) throw new System.InvalidOperationException("Stop Play mode before scene setup.");
            var player = Object.FindFirstObjectByType<PlayerController>();
            if (player == null) throw new System.InvalidOperationException("This scene has no PlayerController.");
            EnsureAssets();
            Undo.RegisterFullObjectHierarchyUndo(player.gameObject, "Install oxygen system");
            var oxygen = GetOrAdd<PlayerOxygen>(player.gameObject);
            BuildHUD(oxygen);
            PlaceExampleTank(player);
            EditorSceneManager.MarkSceneDirty(player.gameObject.scene);
            Debug.Log("[Oxygen] Installed player components, HUD and one WorldRoot pickup. Save scene to keep changes.");
        }

        static T GetOrAdd<T>(GameObject go) where T : Component
        {
            var component = go.GetComponent<T>();
            return component != null ? component : Undo.AddComponent<T>(go);
        }

        static void Reference(Object target, string name, Object value)
        {
            var serialized = new SerializedObject(target);
            var property = serialized.FindProperty(name);
            if (property == null) throw new System.InvalidOperationException(target.GetType().Name + "." + name);
            property.objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        static void BuildHUD(PlayerOxygen oxygen)
        {
            var existing = Object.FindFirstObjectByType<OxygenHUD>();
            if (existing != null) { Reference(existing,"oxygen",oxygen); existing.ApplyPixelStyle(); return; }
            var hud = Object.FindFirstObjectByType<GameHUD>();
            var canvasTransform = hud != null ? hud.transform.Find("HUDCanvas (Auto)") : null;
            if (canvasTransform == null)
            {
                var go = new GameObject("OxygenCanvas",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler));
                Undo.RegisterCreatedObjectUndo(go,"Create oxygen HUD");
                go.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
                go.GetComponent<Canvas>().sortingOrder = 10;
                PixelUI.ConfigureCanvas(go.GetComponent<Canvas>());
                canvasTransform = go.transform;
            }
            var panel = UIObject("OxygenHUD",canvasTransform,new Vector2(32,32),new Vector2(290,82));
            var bg = panel.gameObject.AddComponent<Image>();
            bg.color = new Color(.025f,.06f,.085f,.92f); bg.raycastTarget = false;
            var label = PixelUI.EnsureText(UIObject("OxygenValue",panel,new Vector2(18,45),new Vector2(264,27)).gameObject,
                18,false,TextAnchor.MiddleLeft,PixelUI.TextLight);
            label.text = "O2  100 / 100";
            var track = UIObject("Track",panel,new Vector2(16,17),new Vector2(258,19));
            var trackImage = track.gameObject.AddComponent<Image>();
            trackImage.color = new Color(.1f,.19f,.24f); trackImage.raycastTarget = false;
            var fill = UIObject("Fill",track,Vector2.zero,new Vector2(258,19)).gameObject.AddComponent<Image>();
            fill.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(ArtFolder+"/OxygenFill.png");
            fill.type = Image.Type.Filled; fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillOrigin = (int)Image.OriginHorizontal.Left; fill.fillAmount = 1;
            fill.color = new Color(.2f,.85f,1); fill.raycastTarget = false;
            var ui = panel.gameObject.AddComponent<OxygenHUD>();
            Reference(ui,"oxygen",oxygen); Reference(ui,"fillImage",fill); Reference(ui,"valueLabel",label);
            ui.ApplyPixelStyle();
        }

        static RectTransform UIObject(string name, Transform parent, Vector2 position, Vector2 size)
        {
            var go = new GameObject(name,typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(go,"Create oxygen UI");
            var rt = go.GetComponent<RectTransform>(); rt.SetParent(parent,false);
            rt.anchorMin = rt.anchorMax = rt.pivot = Vector2.zero;
            rt.anchoredPosition = position; rt.sizeDelta = size;
            return rt;
        }

        static void PlaceExampleTank(PlayerController player)
        {
            if (Object.FindFirstObjectByType<OxygenTank>() != null) return;
            var world = Object.FindFirstObjectByType<WorldRotator>();
            if (world == null) return;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TankPath);
            var tank = (GameObject)PrefabUtility.InstantiatePrefab(prefab,world.transform);
            Undo.RegisterCreatedObjectUndo(tank,"Place oxygen tank");
            var bounds = player.GetComponent<Collider2D>().bounds;
            tank.transform.position = bounds.center + Vector3.right * (bounds.extents.x + 2f);
            tank.name = "OxygenTank";
        }

        public static void EnsureAssets()
        {
            Directory.CreateDirectory(ArtFolder);
            Directory.CreateDirectory("Assets/Resource/Prefabs");
            MakeTexture("OxygenFill",4,4,(x,y)=>Color.white,4);
            MakeTexture("OxygenTank",24,40,(x,y)=>
            {
                if (y >= 33 && y <= 37 && x >= 8 && x <= 15) return new Color(.65f,.8f,.82f);
                if (y < 3 || y > 32 || x < 4 || x > 19) return Color.clear;
                if ((y < 5 || y > 30) && (x < 6 || x > 17)) return Color.clear;
                if (x == 4 || x == 19 || y == 3 || y == 32) return new Color(.05f,.2f,.27f);
                if (y >= 14 && y <= 22) return new Color(.86f,.98f,1);
                return x < 8 ? new Color(.55f,.97f,1) : new Color(.1f,.64f,.77f);
            },24);
            if (AssetDatabase.LoadAssetAtPath<GameObject>(TankPath) == null)
            {
                var root = new GameObject("OxygenTank",typeof(SpriteRenderer),typeof(CircleCollider2D),typeof(OxygenTank));
                var visual = new GameObject("Visual",typeof(SpriteRenderer)); visual.transform.SetParent(root.transform,false);
                var renderer = visual.GetComponent<SpriteRenderer>();
                renderer.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(ArtFolder+"/OxygenTank.png");
                renderer.sortingOrder = 10;
                var collider = root.GetComponent<CircleCollider2D>(); collider.radius = .65f; collider.isTrigger = true;
                Reference(root.GetComponent<OxygenTank>(),"visual",visual.transform);
                PrefabUtility.SaveAsPrefabAsset(root,TankPath); Object.DestroyImmediate(root);
            }
            AssetDatabase.SaveAssets();
        }

        static void MakeTexture(string name,int width,int height,System.Func<int,int,Color> pixel,float ppu)
        {
            string path = ArtFolder+"/"+name+".png";
            if (File.Exists(path)) return;
            var texture = new Texture2D(width,height,TextureFormat.RGBA32,false);
            for (int y=0;y<height;y++) for(int x=0;x<width;x++) texture.SetPixel(x,y,pixel(x,y));
            texture.Apply(); File.WriteAllBytes(path,texture.EncodeToPNG()); Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite; importer.spritePixelsPerUnit = ppu;
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.alphaIsTransparency = true; importer.mipmapEnabled = false;
            importer.SaveAndReimport();
        }
    }
}
