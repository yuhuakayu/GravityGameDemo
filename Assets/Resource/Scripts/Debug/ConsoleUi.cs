using UnityEngine;

namespace Resource.Scripts.Debugging
{
    public static class ConsoleUi
    {
        public const string Unavailable = "（此 Tab 在当前场景不可用）";
        public static float Slider(string label, float value, float min, float max, string format = "F2", string unit = "")
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(215));
            float next = GUILayout.HorizontalSlider(value, min, max, GUILayout.MinWidth(100));
            GUILayout.Label(next.ToString(format) + unit, GUILayout.Width(140));
            GUILayout.EndHorizontal();
            return next;
        }
    }
}
