using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace Resource.Scripts.Editor
{
    public static class LevelOrder
    {
        public static readonly string[] Scenes =
        {
            "Maze_01", "Maze_02", "Maze_03", "Maze_04", "Maze_05", "Maze_06", "Maze_07", "Maze_08",
            "Mine_01", "Mine_02", "Mine_03", "Mine_04",
            "Lava_01", "Lava_02", "Lava_03", "Lava_04",
            "Sky_01", "Sky_02", "Sky_03", "Sky_04"
        };

        public static string Next(string sceneName)
        {
            int index = Array.IndexOf(Scenes, sceneName);
            return index >= 0 && index + 1 < Scenes.Length ? Scenes[index + 1] : "MainMenu";
        }

        public static void RegisterBuildScenes()
        {
            var paths = Scenes.Select(name => "Assets/Scenes/" + name + ".unity").ToArray();
            var previous = EditorBuildSettings.scenes;
            var scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/MainMenu.unity", true) }
                .Concat(paths.Select(path => new EditorBuildSettingsScene(path, true)))
                .Concat(previous.Where(entry => entry.path != "Assets/Scenes/MainMenu.unity" && !paths.Contains(entry.path)))
                .ToArray();
            EditorBuildSettings.scenes = scenes;
        }

        public static void UpdateLevelSelection()
        {
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity", OpenSceneMode.Single);
            var menu = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<MainMenuUI>(true)).Single();
            menu.levels = Scenes.Select((name, index) => new LevelEntry
            {
                displayName = "Level " + (index + 1),
                sceneName = name,
                unlocked = true
            }).ToList();
            EditorUtility.SetDirty(menu);
            EditorSceneManager.SaveScene(scene);
        }
    }
}
