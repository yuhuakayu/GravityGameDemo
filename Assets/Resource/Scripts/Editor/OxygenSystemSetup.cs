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
            var effects = GetOrAdd<JetpackEffects>(player.gameObject);
            var jetpack = GetOrAdd<PlayerJetpack>(player.gameObject);
            Reference(jetpack, "controller", player);
            Reference(jetpack, "oxygen", oxygen);
            Reference(jetpack, "body", player.GetComponent<Rigidbody2D>());
            Reference(jetpack, "effects", effects);
            BuildJetpack(player, effects);
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

        static void BuildJetpack(PlayerController player, JetpackEffects effects)
        {
            var nozzle = player.transform.Find("JetpackNozzle");
            if (nozzle == null)
            {
                var go = new GameObject("JetpackNozzle");
                Undo.RegisterCreatedObjectUndo(go, "Create jetpack nozzle");
                nozzle = go.transform;
                nozzle.SetParent(player.transform, false);
                // Existing player origin is at the feet, not at the sprite centre.
                var bounds = player.GetComponent<Collider2D>().bounds;
                nozzle.localPosition = player.transform.InverseTransformPoint(bounds.center);
                nozzle.localPosition += Vector3.right * bounds.extents.x * .75f;
            }
            var ps = nozzle.GetComponent<ParticleSystem>();
            if (ps == null)
            {
                ps = nozzle.gameObject.AddComponent<ParticleSystem>();
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = ps.main;
                main.playOnAwake = false;
                main.loop = true;
                main.startLifetime = new ParticleSystem.MinMaxCurve(.12f, .3f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(3f, 7f);
                main.startSize = new ParticleSystem.MinMaxCurve(.18f, .42f);
                main.startColor = new ParticleSystem.MinMaxGradient(new Color(.25f,.85f,1f), new Color(1f,.8f,.3f));
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.maxParticles = 100;
                var emission = ps.emission;
                emission.rateOverTime = 65;
                var shape = ps.shape;
                shape.shapeType = ParticleSystemShapeType.Cone;
                shape.angle = 14f;
                shape.radius = .08f;
                var size = ps.sizeOverLifetime;
                size.enabled = true;
                size.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0,1,1,0));
                var color = ps.colorOverLifetime;
                color.enabled = true;
                var gradient = new Gradient();
                gradient.SetKeys(new[] {new GradientColorKey(Color.white,0),new GradientColorKey(new Color(.3f,.5f,.65f),1)},
                    new[] {new GradientAlphaKey(1,0),new GradientAlphaKey(0,1)});
                color.color = gradient;
                var renderer = ps.GetComponent<ParticleSystemRenderer>();
                renderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(ArtFolder + "/JetpackParticle.mat");
                renderer.sortingOrder = 12;
            }
            var audio = GetOrAdd<AudioSource>(nozzle.gameObject);
            audio.playOnAwake = false;
            audio.spatialBlend = 0;
            var trail = nozzle.GetComponent<TrailRenderer>();
            if (trail == null)
            {
                trail = nozzle.gameObject.AddComponent<TrailRenderer>();
                trail.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(ArtFolder + "/JetpackParticle.mat");
                trail.time = .15f;
                trail.startWidth = .35f;
                trail.endWidth = 0;
                trail.startColor = new Color(.2f,.8f,1f,.5f);
                trail.endColor = new Color(.2f,.8f,1f,0);
                trail.sortingOrder = 11;
                trail.emitting = false;
            }
            Reference(effects,"nozzle",nozzle);
            Reference(effects,"exhaustParticles",ps);
            Reference(effects,"jetpackAudio",audio);
            Reference(effects,"dashTrail",trail);
            var settings = new SerializedObject(effects);
            var playerBounds = player.GetComponent<Collider2D>().bounds;
            settings.FindProperty("nozzleLocalOffset").vector2Value = player.transform.InverseTransformPoint(playerBounds.center);
            settings.FindProperty("nozzleDistance").floatValue = playerBounds.extents.x * .9f;
            settings.ApplyModifiedPropertiesWithoutUndo();
        }

        static void BuildHUD(PlayerOxygen oxygen)
        {
            var existing = Object.FindFirstObjectByType<OxygenHUD>();
            if (existing != null) { Reference(existing,"oxygen",oxygen); return; }
            var hud = Object.FindFirstObjectByType<GameHUD>();
            var canvasTransform = hud != null ? hud.transform.Find("HUDCanvas (Auto)") : null;
            if (canvasTransform == null)
            {
                var go = new GameObject("OxygenCanvas",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler));
                Undo.RegisterCreatedObjectUndo(go,"Create oxygen HUD");
                go.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
                go.GetComponent<Canvas>().sortingOrder = 10;
                var scaler = go.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920,1080);
                scaler.matchWidthOrHeight = .5f;
                canvasTransform = go.transform;
            }
            var panel = UIObject("OxygenHUD",canvasTransform,new Vector2(32,32),new Vector2(290,82));
            var bg = panel.gameObject.AddComponent<Image>();
            bg.color = new Color(.025f,.06f,.085f,.92f); bg.raycastTarget = false;
            var label = UIObject("OxygenValue",panel,new Vector2(16,43),new Vector2(258,30)).gameObject.AddComponent<Text>();
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = 21; label.text = "O2  100 / 100"; label.color = new Color(.7f,.93f,1);
            label.raycastTarget = false;
            var track = UIObject("Track",panel,new Vector2(16,17),new Vector2(258,19));
            var trackImage = track.gameObject.AddComponent<Image>();
            trackImage.color = new Color(.1f,.19f,.24f); trackImage.raycastTarget = false;
            var fill = UIObject("Fill",track,Vector2.zero,new Vector2(258,19)).gameObject.AddComponent<Image>();
            fill.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(ArtFolder+"/OxygenFill.png");
            fill.type = Image.Type.Filled; fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillOrigin = (int)Image.OriginHorizontal.Left; fill.fillAmount = 1;
            fill.color = new Color(.2f,.85f,1); fill.raycastTarget = false;
            var ui = panel.gameObject.AddComponent<OxygenHUD>();
            Reference(ui,"oxygen",oxygen); Reference(ui,"fillImage",fill); Reference(ui,"valueText",label);
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
            MakeTexture("JetpackParticle",32,32,(x,y)=>new Color(1,1,1,Mathf.Pow(Mathf.Clamp01(1-Vector2.Distance(new Vector2(x,y),new Vector2(15.5f,15.5f))/16),2)),32);
            MakeTexture("OxygenTank",24,40,(x,y)=>
            {
                if (y >= 33 && y <= 37 && x >= 8 && x <= 15) return new Color(.65f,.8f,.82f);
                if (y < 3 || y > 32 || x < 4 || x > 19) return Color.clear;
                if ((y < 5 || y > 30) && (x < 6 || x > 17)) return Color.clear;
                if (x == 4 || x == 19 || y == 3 || y == 32) return new Color(.05f,.2f,.27f);
                if (y >= 14 && y <= 22) return new Color(.86f,.98f,1);
                return x < 8 ? new Color(.55f,.97f,1) : new Color(.1f,.64f,.77f);
            },24);
            string materialPath = ArtFolder+"/JetpackParticle.mat";
            if (AssetDatabase.LoadAssetAtPath<Material>(materialPath) == null)
            {
                // Existing player dust uses this same built-in shader in this URP project.
                var shader = Shader.Find("Sprites/Default");
                if (shader == null) throw new System.InvalidOperationException("Sprites/Default shader unavailable.");
                var material = new Material(shader);
                material.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(ArtFolder+"/JetpackParticle.png");
                AssetDatabase.CreateAsset(material,materialPath);
            }
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
            importer.filterMode = name == "JetpackParticle" ? FilterMode.Bilinear : FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.alphaIsTransparency = true; importer.mipmapEnabled = false;
            importer.SaveAndReimport();
        }
    }
}
