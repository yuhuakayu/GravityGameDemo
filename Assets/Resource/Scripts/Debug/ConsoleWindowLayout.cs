using Resource.Scripts.Gyro;
using UnityEngine;

namespace Resource.Scripts.Debugging
{
    /// <summary>Screen-pixel geometry shared by the console's move, resize, and settings controls.</summary>
    public static class ConsoleWindowLayout
    {
        public const int Left = 1;
        public const int Right = 2;
        public const int Top = 4;
        public const int Bottom = 8;
        public const float Margin = 10f;
        public const float MinimumWidth = 480f;
        public const float MinimumHeight = 320f;

        public static Rect GetRect(GyroSettings settings, Vector2 screen)
        {
            screen = ValidScreen(screen);
            if (!settings.consoleWindowCustomized)
            {
                float fraction = Mathf.Clamp(FiniteOr(settings.consoleScreenFraction, 0.7f), 0.4f, 1f);
                return ClampRect(new Rect(Margin, Margin, screen.x * fraction, screen.y * fraction), screen);
            }

            Rect normalized = settings.consoleWindowNormalized;
            return ClampRect(new Rect(normalized.x * screen.x, normalized.y * screen.y,
                normalized.width * screen.x, normalized.height * screen.y), screen);
        }

        public static void StoreRect(GyroSettings settings, Rect rect, Vector2 screen)
        {
            screen = ValidScreen(screen);
            rect = ClampRect(rect, screen);
            settings.consoleWindowNormalized = new Rect(rect.x / screen.x, rect.y / screen.y,
                rect.width / screen.x, rect.height / screen.y);
            settings.consoleWindowCustomized = true;
            settings.consoleScreenFraction = Mathf.Clamp(rect.width / screen.x, 0.4f, 1f);
        }

        public static Rect ClampRect(Rect rect, Vector2 screen)
        {
            Rect available = AvailableRect(ValidScreen(screen));
            float width = Mathf.Clamp(FiniteOr(rect.width, MinimumWidth),
                Mathf.Min(MinimumWidth, available.width), available.width);
            float height = Mathf.Clamp(FiniteOr(rect.height, MinimumHeight),
                Mathf.Min(MinimumHeight, available.height), available.height);
            float x = Mathf.Clamp(FiniteOr(rect.x, available.x), available.xMin, available.xMax - width);
            float y = Mathf.Clamp(FiniteOr(rect.y, available.y), available.yMin, available.yMax - height);
            return new Rect(x, y, width, height);
        }

        public static Rect ResizeRect(Rect start, Vector2 delta, int edges, Vector2 screen)
        {
            screen = ValidScreen(screen);
            start = ClampRect(start, screen);
            Rect available = AvailableRect(screen);
            float minWidth = Mathf.Min(MinimumWidth, available.width);
            float minHeight = Mathf.Min(MinimumHeight, available.height);
            delta.x = FiniteOr(delta.x, 0f);
            delta.y = FiniteOr(delta.y, 0f);

            // Clamp the moving edge directly so the opposite edge stays anchored.
            float left = start.xMin;
            float right = start.xMax;
            float top = start.yMin;
            float bottom = start.yMax;
            if ((edges & Left) != 0)
                left = Mathf.Clamp(left + delta.x, available.xMin, right - minWidth);
            else if ((edges & Right) != 0)
                right = Mathf.Clamp(right + delta.x, left + minWidth, available.xMax);
            if ((edges & Top) != 0)
                top = Mathf.Clamp(top + delta.y, available.yMin, bottom - minHeight);
            else if ((edges & Bottom) != 0)
                bottom = Mathf.Clamp(bottom + delta.y, top + minHeight, available.yMax);

            return Rect.MinMaxRect(left, top, right, bottom);
        }

        public static void ApplyFraction(GyroSettings settings, float fraction, Vector2 screen)
        {
            screen = ValidScreen(screen);
            fraction = Mathf.Clamp(FiniteOr(fraction, 0.7f), 0.4f, 1f);
            Rect rect = GetRect(settings, screen);
            rect.width = screen.x * fraction;
            rect.height = screen.y * fraction;
            StoreRect(settings, rect, screen);
        }

        public static void Reset(GyroSettings settings)
        {
            settings.consoleWindowCustomized = false;
            settings.consoleWindowNormalized = new Rect(0f, 0f, 0.7f, 0.7f);
            settings.consoleScreenFraction = 0.7f;
        }

        private static Vector2 ValidScreen(Vector2 screen) => new Vector2(
            Mathf.Max(1f, FiniteOr(screen.x, 1f)), Mathf.Max(1f, FiniteOr(screen.y, 1f)));

        private static Rect AvailableRect(Vector2 screen)
        {
            // Extremely small/minimized views still retain a finite, positive rectangle.
            float marginX = Mathf.Min(Margin, (screen.x - 1f) * 0.5f);
            float marginY = Mathf.Min(Margin, (screen.y - 1f) * 0.5f);
            return new Rect(marginX, marginY, screen.x - marginX * 2f, screen.y - marginY * 2f);
        }

        private static float FiniteOr(float value, float fallback) =>
            float.IsNaN(value) || float.IsInfinity(value) ? fallback : value;
    }
}
