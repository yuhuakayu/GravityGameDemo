using System;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Resource.Scripts.Debugging
{
    /// <summary>Build scenes only; navigation is delegated to the game's existing transition.</summary>
    public sealed class SceneTab
    {
        private string _search = string.Empty;
        private Vector2 _scroll;

        public void Draw(DebugConsole console)
        {
            Scene current = SceneManager.GetActiveScene();
            GUILayout.Label("当前场景：" + current.name + "  （序号 " + current.buildIndex + "）");
            GUILayout.Label(current.path);
            GUILayout.Space(8);

            GUILayout.BeginHorizontal();
            GUILayout.Label("搜索场景", GUILayout.Width(76));
            _search = GUILayout.TextField(_search, GUILayout.MinWidth(120));
            if (GUILayout.Button("清空", GUILayout.Width(56))) _search = string.Empty;
            GUILayout.EndHorizontal();

            var settings = console.Runtime.Settings;
            settings.skipPreview = GUILayout.Toggle(settings.skipPreview, "跳过预览直接开始");
            if (GUILayout.Button("重开当前关卡", GUILayout.Height(30)))
                console.JumpToScene(string.IsNullOrEmpty(current.path) ? current.name : current.path);

            GUILayout.Space(10);
            int sceneCount = SceneManager.sceneCountInBuildSettings;
            if (sceneCount == 0)
            {
                GUILayout.Label("（此 Tab 在当前场景不可用：构建设置中没有启用的场景）");
                return;
            }

            GUILayout.BeginHorizontal(GUI.skin.box);
            GUILayout.Label("序号", GUILayout.Width(42));
            GUILayout.Label("场景名", GUILayout.Width(145));
            GUILayout.Label("场景路径", GUILayout.MinWidth(150), GUILayout.ExpandWidth(true));
            GUILayout.Label("跳转", GUILayout.Width(65));
            GUILayout.EndHorizontal();

            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.MinHeight(150), GUILayout.MaxHeight(430));
            int visible = 0;
            for (int index = 0; index < sceneCount; index++)
            {
                string path = SceneUtility.GetScenePathByBuildIndex(index);
                string name = Path.GetFileNameWithoutExtension(path);
                if (!string.IsNullOrEmpty(_search) && name.IndexOf(_search.Trim(), StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                visible++;
                GUILayout.BeginHorizontal(GUI.skin.box);
                GUILayout.Label(index.ToString(), GUILayout.Width(42));
                GUILayout.Label(name + (index == current.buildIndex ? "  ● 当前" : string.Empty), GUILayout.Width(145));
                GUILayout.Label(path, GUILayout.MinWidth(150), GUILayout.ExpandWidth(true));
                if (GUILayout.Button("跳转", GUILayout.Width(65), GUILayout.Height(28))) console.JumpToScene(path);
                GUILayout.EndHorizontal();
            }
            if (visible == 0) GUILayout.Label("没有找到匹配的场景。");
            GUILayout.EndScrollView();
            GUILayout.Label("共 " + sceneCount + " 个已启用场景；显示 " + visible + " 个。未勾选跳过预览时，关卡保持原有预览流程。");
        }
    }
}
