using System;
using System.IO;
using System.Linq;
using Resource.Scripts.Gyro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Resource.Scripts.Editor
{
    /// <summary>Five bounded Play visits and the requested Maze_03 checks; no input or window automation.</summary>
    [InitializeOnLoad]
    public static class MazeSceneValidation
    {
        private const string Key = "GravityGame.MazeValidation.";
        private static PendulumValidationLoopBridge _loop;
        private static PlayerController _player;
        private static WorldRotator _world;
        private static LaserEmitter[] _lasers;
        private static float[] _lengths;
        private static Vector2[] _directions;
        private static float _startAngle, _target;
        private static int _phase, _fixedSteps;

        public static bool IsRunning => SessionState.GetBool(Key + "Active", false);
        public static string Status => SessionState.GetString(Key + "Status", "Not started.");

        static MazeSceneValidation()
        {
            EditorApplication.playModeStateChanged += PlayStateChanged;
            EditorApplication.update += Watchdog;
            AssemblyReloadEvents.beforeAssemblyReload += RestoreBackground;
            EditorApplication.quitting += RestoreBackground;
        }

        [MenuItem("Tools/Gravity Game/Levels/Validate Five Maze Scenes")]
        public static void Begin()
        {
            if (IsRunning || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play and finish any existing validation first.");
            if (SceneManager.sceneCount != 1 || SceneManager.GetActiveScene().isDirty || string.IsNullOrEmpty(SceneManager.GetActiveScene().path))
                throw new InvalidOperationException("Validation requires one saved, unmodified scene.");
            for (int i = 1; i <= 5; i++)
                if (!File.Exists(ScenePath(i))) throw new FileNotFoundException(ScenePath(i));

            SessionState.SetString(Key + "OriginalScene", SceneManager.GetActiveScene().path);
            SessionState.SetBool(Key + "OriginalBackground", Application.runInBackground);
            SessionState.SetBool(Key + "RestorePending", true);
            SessionState.SetString(Key + "Status", "Running: five Play visits; 30-second limit per scene.");
            SessionState.SetInt(Key + "Index", 1);
            SessionState.SetBool(Key + "Active", true);
            OpenNext();
        }

        public static void Stop() { Fail("Cancelled."); }

        private static string ScenePath(int index) => "Assets/Scenes/Maze_" + index.ToString("00") + ".unity";

        private static void OpenNext()
        {
            if (!IsRunning) return;
            try
            {
                SessionState.SetBool(Key + "OpenPending", false);
                SessionState.SetBool(Key + "ExpectedExit", false);
                SessionState.SetString(Key + "Deadline", DateTime.UtcNow.AddSeconds(30).Ticks.ToString());
                EditorSceneManager.OpenScene(ScenePath(SessionState.GetInt(Key + "Index", 1)), OpenSceneMode.Single);
                EditorApplication.isPlaying = true;
            }
            catch (Exception ex) { Fail(ex.Message); }
        }

        private static void PlayStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingPlayMode) RestoreBackground();
            if (state != PlayModeStateChange.EnteredEditMode) return;
            _loop = null;
            RestoreBackground();
            if (!IsRunning) { RestoreScene(); return; }
            if (!SessionState.GetBool(Key + "ExpectedExit", false)) { Fail("Play was interrupted."); return; }
            int next = SessionState.GetInt(Key + "Index", 1) + 1;
            if (next > 5)
            {
                SessionState.SetBool(Key + "Active", false);
                Append("PASS: five screenshots and Maze_03 laser checks completed.");
                RestoreScene();
            }
            else
            {
                SessionState.SetInt(Key + "Index", next);
                SessionState.SetString(Key + "Deadline", DateTime.UtcNow.AddSeconds(30).Ticks.ToString());
                SessionState.SetBool(Key + "OpenPending", true);
            }
        }

        private static void Watchdog()
        {
            if (!IsRunning) return;
            long deadline;
            if (long.TryParse(SessionState.GetString(Key + "Deadline", "0"), out deadline) && DateTime.UtcNow.Ticks > deadline)
            { Fail("30-second scene timeout."); return; }
            // delayCall can remain queued while the editor is unfocused. The editor update
            // also runs in that state; consume the flag before requesting another Play transition.
            if (SessionState.GetBool(Key + "OpenPending", false) && !EditorApplication.isPlayingOrWillChangePlaymode)
            { OpenNext(); return; }
            if (!EditorApplication.isPlaying || SessionState.GetBool(Key + "ExpectedExit", false)) return;
            try
            {
                Application.runInBackground = true;
                if (_loop == null)
                {
                    _phase = _fixedSteps = 0;
                    _loop = new GameObject("Maze Validation (Temporary)").AddComponent<PendulumValidationLoopBridge>();
                    _loop.Bind(Tick, FixedTick, LoopDestroyed);
                }
                EditorApplication.QueuePlayerLoopUpdate();
            }
            catch (Exception ex) { Fail(ex.Message); }
        }

        private static void Tick()
        {
            if (!IsRunning || SessionState.GetBool(Key + "ExpectedExit", false)) return;
            try
            {
                if (_phase == 0)
                {
                    var intro = Object.FindFirstObjectByType<LevelIntroUI>();
                    if (intro == null || !intro.IsPreviewing) return;
                    int index = SessionState.GetInt(Key + "Index", 1);
                    Capture(Camera.main, "Logs/MazeValidation/Maze_" + index.ToString("00") + ".png");
                    Append("Maze_" + index.ToString("00") + ": preview screenshot saved.");
                    if (index != 3) { FinishScene(); return; }
                    _player = Object.FindFirstObjectByType<PlayerController>();
                    _world = Object.FindFirstObjectByType<WorldRotator>();
                    _lasers = Object.FindObjectsByType<LaserEmitter>(FindObjectsSortMode.None).OrderBy(l => l.name).ToArray();
                    Require(_player != null && _world != null && _lasers.Length == 2, "Maze_03 fixture is incomplete.");
                    _lengths = new float[2]; _directions = new Vector2[2];
                    intro.BeginGameplayFromPreview();
                    Require(_player.IsGameplayActive, "Preview did not start gameplay.");
                    // All these values belong to the disposable Play scene, never saved assets/settings.
                    _player.maxMoveSpeed = 0f;
                    _player.autoMoveMode = _player.jumpEnabled = false;
                    _player.SimulatedGravityScale = 0f;
                    _player.GetComponent<Rigidbody2D>().simulated = false;
                    if (_world.RotationInput != null) _world.RotationInput.enabled = false;
                    if (GyroRuntime.Current != null) GyroRuntime.Current.enabled = false;
                    _startAngle = _world.GetComponent<Rigidbody2D>().rotation;
                    _target = _world.ClockwiseAngleReadout + 30f;
                    CheckBeams(true);
                    _fixedSteps = 0;
                    _phase = 1;
                }
                else if (_phase == 1 && Mathf.Abs(Mathf.DeltaAngle(_startAngle - 30f, _world.GetComponent<Rigidbody2D>().rotation)) < 0.05f)
                {
                    _fixedSteps = 0;
                    _phase = 2;
                }
                else if (_phase == 2 && _fixedSteps >= 3)
                {
                    CheckBeams(false);
                    float angle = Mathf.DeltaAngle(_startAngle, _world.GetComponent<Rigidbody2D>().rotation);
                    Append("Maze_03: world rotation=" + angle.ToString("F3") + " deg; both beams followed.");
                    var beam = _lasers[0].transform.Find("LaserBeam").GetComponent<BoxCollider2D>();
                    Vector2 center = PhysicalPoint(_lasers[0], beam.transform.TransformPoint(beam.offset));
                    var body = _player.GetComponent<Rigidbody2D>();
                    body.simulated = true;
                    Physics2D.SyncTransforms();
                    Vector2 offset = (Vector2)_player.GetComponent<Collider2D>().bounds.center - body.position;
                    body.position = center - offset;
                    _player.SetIntendedVelocity(Vector2.zero);
                    Physics2D.SyncTransforms();
                    _fixedSteps = 0;
                    _phase = 3;
                }
                else if (_phase == 3)
                {
                    if (_player.IsDead)
                    {
                        Require(_fixedSteps > 0, "Death occurred without a natural physics step.");
                        Append("Maze_03: beam trigger killed player via real physics contact (" + _fixedSteps + " steps).");
                        FinishScene();
                    }
                    else Require(_fixedSteps <= 10, "Beam contact did not kill player within 10 physics steps.");
                }
            }
            catch (Exception ex) { Fail(ex.Message); }
        }

        private static void FixedTick()
        {
            if (!IsRunning || _phase == 0 || SessionState.GetBool(Key + "ExpectedExit", false)) return;
            try
            {
                ++_fixedSteps;
                if (_player.IsDead) return;
                // WorldRotator calls Evaluate directly even on a disabled input component.
                // Run last: this target replaces any input-issued MoveRotation for this real physics step.
                _world.SetTargetAngle(_target);
                _world.StepPhysics(Time.fixedDeltaTime);
                Require(_fixedSteps <= 100, "Rotation did not finish within 100 physics steps.");
            }
            catch (Exception ex) { Fail(ex.Message); }
        }

        private static Vector2 PhysicalPoint(LaserEmitter laser, Vector2 renderedPoint)
        {
            var body = laser.GetComponentInParent<Rigidbody2D>();
            Quaternion rotation = Quaternion.Euler(0f, 0f, body.rotation) * Quaternion.Inverse(body.transform.rotation);
            return body.position + (Vector2)(rotation * (renderedPoint - (Vector2)body.transform.position));
        }

        private static void CheckBeams(bool initial)
        {
            for (int i = 0; i < _lasers.Length; i++)
            {
                LaserEmitter laser = _lasers[i];
                laser.RefreshBeam();
                var line = laser.transform.Find("LaserBeam/Core").GetComponent<LineRenderer>();
                Vector2 start = PhysicalPoint(laser, line.transform.TransformPoint(line.GetPosition(0)));
                Vector2 end = PhysicalPoint(laser, line.transform.TransformPoint(line.GetPosition(1)));
                Vector2 direction = (end - start).normalized;
                Require(laser.IsBeamActive && laser.BeamLength > 0.1f, "Beam is absent or self-blocked.");
                var wall = Physics2D.OverlapPoint(end + direction * 0.02f, LayerMask.GetMask("Wall", "Box"));
                Require(wall != null && !wall.isTrigger && wall != laser.GetComponent<Collider2D>(), "Beam endpoint is not blocked by a wall.");
                float distance = Vector2.Distance(end, wall.ClosestPoint(end));
                Require(distance < 0.025f, "Beam ends away from wall: " + distance);
                if (initial) { _lengths[i] = laser.BeamLength; _directions[i] = direction; }
                else
                {
                    Require(Mathf.Abs(_lengths[i] - laser.BeamLength) < 0.03f, "Rotation changed beam length.");
                    Require(Mathf.Abs(Mathf.DeltaAngle(-30f, Vector2.SignedAngle(_directions[i], direction))) < 0.1f, "Beam did not rotate with the world.");
                }
                Append(laser.name + (initial ? " before" : " after") + ": length=" + laser.BeamLength.ToString("F4") + ", wall gap=" + distance.ToString("F5"));
            }
        }

        private static void Capture(Camera camera, string path)
        {
            Require(camera != null, "Main Camera is missing.");
            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;
            var target = new RenderTexture(1280, 720, 24);
            var texture = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            try
            {
                target.Create();
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                texture.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
                texture.Apply();
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, texture.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                target.Release();
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(texture);
            }
        }

        private static void FinishScene()
        {
            SessionState.SetBool(Key + "ExpectedExit", true);
            SessionState.SetString(Key + "Deadline", DateTime.UtcNow.AddSeconds(30).Ticks.ToString());
            RestoreBackground();
            EditorApplication.isPlaying = false;
        }

        private static void LoopDestroyed()
        {
            if (IsRunning && !SessionState.GetBool(Key + "ExpectedExit", false)) Fail("Validation scene was unloaded unexpectedly.");
        }

        private static void Fail(string message)
        {
            if (!IsRunning) return;
            SessionState.SetBool(Key + "Active", false);
            Append("FAIL: " + message);
            RestoreBackground();
            if (EditorApplication.isPlayingOrWillChangePlaymode) EditorApplication.isPlaying = false;
            else RestoreScene();
        }

        private static void RestoreBackground()
        {
            if (SessionState.GetBool(Key + "RestorePending", false))
                Application.runInBackground = SessionState.GetBool(Key + "OriginalBackground", false);
        }

        private static void RestoreScene()
        {
            if (!SessionState.GetBool(Key + "RestorePending", false)) return;
            SessionState.SetBool(Key + "RestorePending", false);
            string path = SessionState.GetString(Key + "OriginalScene", "");
            if (!string.IsNullOrEmpty(path)) EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
        }

        private static void Append(string message) { SessionState.SetString(Key + "Status", Status + "\n" + message); }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
