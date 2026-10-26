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
            "Maze_09", "Maze_10", "Maze_11", "Maze_12", "Maze_13", "Maze_14", "Maze_15", "Maze_16",
            "Maze_17", "Maze_18", "Maze_19", "Maze_20", "Maze_21", "Maze_22", "Mine_01", "Mine_03",
            "Mine_12", "Mine_02", "Mine_05", "Mine_06", "Mine_07", "Mine_08", "Mine_09", "Mine_10",
            "Mine_11", "Mine_13", "Mine_14", "Lava_01", "Lava_02", "Lava_03", "Lava_05", "Lava_06",
            "Lava_07", "Lava_08", "Lava_09", "Lava_10", "Lava_11", "Lava_12", "Lava_13", "Lava_14",
            "Sky_01", "Sky_02", "Sky_03", "Sky_04", "Sky_05", "Sky_06", "Sky_07", "Sky_08",
            "Sky_09", "Sky_10", "Sky_11", "Sky_12", "Sky_13", "Sky_14", "Sky_15", "Sky_16",
            "Sky_17"
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
                .Concat(previous.Where(entry => entry.path != "Assets/Scenes/MainMenu.unity" && !paths.Contains(entry.path) && System.IO.File.Exists(entry.path)))
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
