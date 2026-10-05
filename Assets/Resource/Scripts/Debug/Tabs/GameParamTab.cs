using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Resource.Scripts.Debugging
{
    /// <summary>Live scene tuning. References are discovered on scene changes or explicit refresh only.</summary>
    public sealed class GameParamTab
    {
        private int _sceneHandle = int.MinValue;
        private PlayerController _player;
        private PlayerOxygen _oxygen;
        private CrushGuard _crushGuard;
        private GoalDoor _door;
        private PlayerColorGrade _colorGrade;
        private MazeCameraFit _camera;
        private GUIStyle _descriptionStyle;
        private readonly List<OxygenTank> _tanks = new List<OxygenTank>();

        public void Draw(DebugConsole console)
        {
            if (_descriptionStyle == null) _descriptionStyle = new GUIStyle(GUI.skin.label) { wordWrap = true };
            Scene scene = SceneManager.GetActiveScene();
            if (_sceneHandle != scene.handle) Refresh(scene);

            GUILayout.BeginHorizontal();
            GUILayout.Label("当前场景：" + scene.name);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("刷新场景对象", GUILayout.Width(140f))) Refresh(scene);
            GUILayout.EndHorizontal();
#if UNITY_EDITOR
            GUILayout.Label("参数立即生效。镜头可用本区的保存按钮设为本关默认；其他参数仅用于当前运行，重开关卡后恢复。控制台的通用保存按钮不保存本页参数。", _descriptionStyle);
#else
            GUILayout.Label("参数立即生效，仅用于当前运行；重开关卡后恢复默认设置。控制台的保存按钮不保存本页参数。", _descriptionStyle);
#endif
            GUILayout.Space(8f);

            if (_camera != null && _camera.settings != null) DrawCamera();
            if (_player != null) DrawAntiPush();
            if (_door != null) DrawGravityLock();
            if (_colorGrade != null) DrawColorGrade();

            if (_player == null || _oxygen == null)
            {
                GUILayout.Label("（当前场景没有氧气参数）");
                GUILayout.Label("未找到玩家氧气组件。进入关卡后可调节氧气参数。", _descriptionStyle);
                return;
            }

            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label($"氧气 · {_player.name}   当前 {_oxygen.CurrentOxygen:F1} / {_oxygen.MaxOxygen:F1}");
            float maximum = ConsoleUi.Slider("最大氧气", _oxygen.MaxOxygen, 0.01f,
                Mathf.Max(500f, _oxygen.MaxOxygen), "F1");
            if (maximum != _oxygen.MaxOxygen) _oxygen.MaxOxygen = maximum;
            float drain = ConsoleUi.Slider("每秒消耗", _oxygen.DrainPerSecond, 0f,
                Mathf.Max(20f, _oxygen.DrainPerSecond), "F2", " / 秒");
            if (drain != _oxygen.DrainPerSecond) _oxygen.DrainPerSecond = drain;
            GUILayout.EndVertical();

            GUILayout.Space(8f);
            DrawTanks();
        }

        private void DrawCamera()
        {
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("镜头");
            float size = ConsoleUi.Slider("镜头大小", _camera.CurrentSize, 4f, 20f, "F2");
            if (size != _camera.CurrentSize) _camera.SetSize(size);
#if UNITY_EDITOR
            if (GUILayout.Button("保存为本关默认"))
            {
                MazeCameraSettings settings = _camera.settings;
                settings.SetSize(_camera.gameObject.scene.name, _camera.CurrentSize);
                UnityEditor.EditorUtility.SetDirty(settings);
                UnityEditor.AssetDatabase.SaveAssets();
            }
#endif
            if (GUILayout.Button("恢复默认"))
            {
                MazeCameraSettings settings = _camera.settings;
                settings.ClearSize(_camera.gameObject.scene.name);
                _camera.RestoreDefault();
#if UNITY_EDITOR
                UnityEditor.EditorUtility.SetDirty(settings);
                UnityEditor.AssetDatabase.SaveAssets();
#endif
            }
            GUILayout.EndVertical();
            GUILayout.Space(8f);
        }

        private void DrawAntiPush()
        {
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("旋转几何体 · 玩家防推动");
            _player.pauseAutoMoveWhileRotating = GUILayout.Toggle(_player.pauseAutoMoveWhileRotating,
                "世界旋转时暂停自动移动");
            GUILayout.Label($"自动移动暂停 {_player.AutoMovePausedForRotation}   自动移动横向贡献 {_player.AutoMoveVelocityContribution:F3}", _descriptionStyle);
            bool enabled = GUILayout.Toggle(_player.antiPushEnabled, "启用防推动（关闭可对比原物理推动行为）");
            if (enabled != _player.antiPushEnabled)
            {
                _player.antiPushEnabled = enabled;
                if (!enabled && _crushGuard != null) _crushGuard.ResetState();
            }
            _player.logVelocityDelta = GUILayout.Toggle(_player.logVelocityDelta, "打印每步速度变化与位置修复量");
            GUILayout.Label($"预期速度 {_player.IntendedVelocity.ToString("F3")}   物理解算速度差 {_player.LastPhysicsVelocityDelta.ToString("F3")}", _descriptionStyle);
            if (_crushGuard != null)
            {
                _crushGuard.drawDepenetrationGizmo = GUILayout.Toggle(_crushGuard.drawDepenetrationGizmo,
                    "绘制穿透修复向量（青色 / 夹死时紫色；需开启 Gizmos）");
                GUILayout.Label($"修复位移 {_crushGuard.LastCorrection.ToString("F3")}   本步修复总长 {_crushGuard.LastCorrectionDistance:F3} / {_crushGuard.maxDepenetrationPerStep:F3}", _descriptionStyle);
                GUILayout.Label($"剩余穿透 {_crushGuard.RemainingOverlapCount}   连续穿透帧 {_crushGuard.CrushFrames}   抬出帧 {_crushGuard.EscapeFrames}   临时忽略碰撞对 {_crushGuard.IgnoredCollisionCount}", _descriptionStyle);
                GUILayout.Label($"预测夹缝阻塞帧 {_crushGuard.PredictedCrushFrames}   预防抬出帧 {_crushGuard.PreventiveEscapeFrames}   本步抬出 {_crushGuard.LastPreventiveLift.ToString("F3")}", _descriptionStyle);
                if (_crushGuard.IsCrushed) GUILayout.Label("夹死保护已触发：" + _crushGuard.crushResponse);
            }
            else GUILayout.Label("当前玩家没有 CrushGuard 组件。", _descriptionStyle);
            GUILayout.EndVertical();
            GUILayout.Space(8f);
        }

        private void DrawGravityLock()
        {
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label($"重力锁出口 · 门歪 {_door.TiltAngle:F0}°   {(_door.IsUnlocked ? "解锁" : "锁住")}");
            _door.gravityLock = GUILayout.Toggle(_door.gravityLock, "门正立才能打开（关掉 = 碰到就过关）");
            float angle = ConsoleUi.Slider("解锁角度", _door.unlockAngle, 1f, 60f, "F0", "°");
            if (angle != _door.unlockAngle) _door.unlockAngle = Mathf.Round(angle);
            GUILayout.Label("门歪的角度不超过这个值就解锁。只对本次运行有效；要改默认值，改 GoalDoor.cs 里 unlockAngle 的 20。", _descriptionStyle);
            GUILayout.EndVertical();
            GUILayout.Space(8f);
        }

        private void DrawColorGrade()
        {
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("人物滤镜");
            float brightness = ConsoleUi.Slider("人物亮度", _colorGrade.Brightness, 0.6f, 1f, "F2");
            if (brightness != _colorGrade.Brightness) _colorGrade.Brightness = brightness;
            float coolness = ConsoleUi.Slider("人物偏冷", _colorGrade.Coolness, 0f, 0.15f, "F2");
            if (coolness != _colorGrade.Coolness) _colorGrade.Coolness = coolness;
            float lampIntensity = ConsoleUi.Slider("周围灯光", _colorGrade.LampIntensity, 0f, 1.2f, "F2");
            if (lampIntensity != _colorGrade.LampIntensity) _colorGrade.LampIntensity = lampIntensity;
            GUILayout.EndVertical();
            GUILayout.Space(8f);
        }

        private void DrawTanks()
        {
            // A collected tank is destroyed after its short animation. Pruning cached references
            // updates the count without rediscovering every object on every IMGUI event.
            for (int index = _tanks.Count - 1; index >= 0; --index)
                if (_tanks[index] == null || _tanks[index].gameObject.scene.handle != _sceneHandle)
                    _tanks.RemoveAt(index);

            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label($"氧气瓶 · 当前场景 {_tanks.Count} 个实例");
            if (_tanks.Count == 0)
            {
                GUILayout.Label("当前没有氧气瓶。运行时新增道具后，可点击上方“刷新场景对象”。", _descriptionStyle);
                GUILayout.EndVertical();
                return;
            }

            float minimum = _tanks[0].RestoreAmount;
            float maximum = minimum;
            foreach (OxygenTank tank in _tanks)
            {
                minimum = Mathf.Min(minimum, tank.RestoreAmount);
                maximum = Mathf.Max(maximum, tank.RestoreAmount);
            }
            bool mixed = !Mathf.Approximately(minimum, maximum);
            GUILayout.Label(mixed ? $"各实例回复量不同：{minimum:F1}–{maximum:F1}；调整滑条会统一全部实例。"
                : $"全部实例当前回复量：{minimum:F1}", _descriptionStyle);
            float previous = _tanks[0].RestoreAmount;
            float restore = ConsoleUi.Slider("统一回复量", previous, 0f, Mathf.Max(200f, maximum), "F1");
            bool apply = restore != previous;
            if (mixed && GUILayout.Button("将全部氧气瓶设为滑条当前值")) apply = true;
            if (apply)
                foreach (OxygenTank tank in _tanks) tank.RestoreAmount = restore;
            GUILayout.Label("包括当前场景中暂未激活的实例；新生成的道具需刷新后再调整。", _descriptionStyle);
            GUILayout.EndVertical();
        }

        private void Refresh(Scene scene)
        {
            _sceneHandle = scene.handle;
            _player = null;
            _oxygen = null;
            _crushGuard = null;
            _door = null;
            _colorGrade = null;
            _camera = null;
            _tanks.Clear();
            foreach (MazeCameraFit candidate in Object.FindObjectsByType<MazeCameraFit>(
                FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (candidate.gameObject.scene.handle != _sceneHandle || candidate.settings == null) continue;
                if (_camera == null) _camera = candidate;
                if (candidate.gameObject.activeInHierarchy) { _camera = candidate; break; }
            }
            foreach (PlayerController candidate in Object.FindObjectsByType<PlayerController>(
                FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (candidate.gameObject.scene.handle != _sceneHandle) continue;
                if (_player == null) _player = candidate;
                if (candidate.gameObject.activeInHierarchy) { _player = candidate; break; }
            }
            if (_player != null)
            {
                _oxygen = _player.GetComponent<PlayerOxygen>();
                _crushGuard = _player.GetComponent<CrushGuard>();
                _colorGrade = _player.GetComponent<PlayerColorGrade>();
            }
            foreach (GoalDoor door in Object.FindObjectsByType<GoalDoor>(
                FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (door.gameObject.scene.handle == _sceneHandle) { _door = door; break; }
            foreach (OxygenTank tank in Object.FindObjectsByType<OxygenTank>(
                FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (tank.gameObject.scene.handle == _sceneHandle) _tanks.Add(tank);
        }
    }
}
