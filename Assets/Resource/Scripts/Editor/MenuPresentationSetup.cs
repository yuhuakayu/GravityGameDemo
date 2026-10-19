using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Resource.Scripts.UI.Menu;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Resource.Scripts.Editor
{
    public static class MenuPresentationSetup
    {
        private const string ArtRoot = "Assets/Resource/Art/UI/Pixel/Menu/";
        private const string MenuArtPath = "Assets/Resource/Resources/Menu/MenuArt.asset";

        [MenuItem("Tools/Gravity Game/UI/Apply Menu Presentation")]
        public static void Apply()
        {
            var entries = new List<MenuArt.Entry>();
            foreach (string file in Directory.GetFiles(ArtRoot, "*.png", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.Ordinal))
            {
                string path = file.Replace('\\', '/');
                entries.Add(new MenuArt.Entry
                {
                    key = path.Substring(ArtRoot.Length, path.Length - ArtRoot.Length - ".png".Length),
                    frames = new[] { AssetDatabase.LoadAssetAtPath<Sprite>(path) }
                });
            }

            foreach (string world in new[] { "maze", "mine", "lava", "sky" })
            {
                string folder = world == "maze" ? "Assets/Resource/Mode/Cave Tileset/Door/"
                    : "Assets/Resource/Art/SmallLevels/" + char.ToUpperInvariant(world[0]) + world.Substring(1) + "/Door/";
                foreach (string animation in new[] { "idle", "open" })
                    entries.Add(new MenuArt.Entry
                    {
                        key = "Door/" + world + "_" + animation,
                        frames = AssetDatabase.LoadAllAssetsAtPath(folder + "door_" + animation + ".png")
                            .OfType<Sprite>().OrderBy(sprite => sprite.name, StringComparer.Ordinal).ToArray()
                    });
            }

            var art = AssetDatabase.LoadAssetAtPath<MenuArt>(MenuArtPath);
            if (art == null)
            {
                art = ScriptableObject.CreateInstance<MenuArt>();
                AssetDatabase.CreateAsset(art, MenuArtPath);
            }
            art.sprites = entries.ToArray();
            art.logoLetters = AssetDatabase.LoadAssetAtPath<TextAsset>(ArtRoot + "Title/logo_letters.json");
            art.blobs = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Resource/Resources/Menu/menu_blobs.json");
            art.blobShader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Resource/Scripts/UI/Menu/MenuBlobs.shader");
            EditorUtility.SetDirty(art);
            AssetDatabase.SaveAssetIfDirty(art);

            var scene = EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity", OpenSceneMode.Single);
            var menu = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<MainMenuUI>(true)).Single();
            var canvas = menu.transform.Find("MainMenuCanvas (Auto)");
            if (canvas != null)
                foreach (string name in new[] { "TitlePanel", "LevelSelectPanel", "PixelBackground", "Backdrop", "Background", "TorchGlow0", "TorchGlow1", "Fog0", "Fog1", "Fog2" })
                {
                    var child = canvas.Find(name);
                    if (child != null) UnityEngine.Object.DestroyImmediate(child.gameObject);
                }
            foreach (var presentation in scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<MenuPresentation>(true)).ToArray())
                UnityEngine.Object.DestroyImmediate(presentation.gameObject);
            EditorSceneManager.SaveScene(scene);
        }
    }
}
