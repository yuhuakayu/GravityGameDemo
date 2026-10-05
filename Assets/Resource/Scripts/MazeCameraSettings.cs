using System;
using UnityEngine;

namespace Resource.Scripts
{
    [CreateAssetMenu(menuName = "Gravity Game/Maze Camera Settings")]
    public class MazeCameraSettings : ScriptableObject
    {
        [Serializable]
        private struct SceneSize
        {
            public string sceneName;
            public float size;

            public SceneSize(string name)
            {
                sceneName = name;
                size = 0f;
            }
        }

        [SerializeField] private SceneSize[] sceneSizes =
        {
            new SceneSize("Maze_01"),
            new SceneSize("Maze_02"),
            new SceneSize("Maze_03"),
            new SceneSize("Maze_04"),
            new SceneSize("Maze_05")
        };

        public bool TryGetSize(string sceneName, out float size)
        {
            int index = FindSceneIndex(sceneName);
            size = index >= 0 ? sceneSizes[index].size : 0f;
            return size > 0f;
        }

        public void SetSize(string sceneName, float size)
        {
            int index = FindSceneIndex(sceneName);
            if (index >= 0) sceneSizes[index].size = Mathf.Clamp(size, 4f, 20f);
        }

        public void ClearSize(string sceneName)
        {
            int index = FindSceneIndex(sceneName);
            if (index >= 0) sceneSizes[index].size = 0f;
        }

        private int FindSceneIndex(string sceneName)
        {
            for (int i = 0; i < sceneSizes.Length; i++)
                if (sceneSizes[i].sceneName == sceneName) return i;
            return -1;
        }
    }
}
