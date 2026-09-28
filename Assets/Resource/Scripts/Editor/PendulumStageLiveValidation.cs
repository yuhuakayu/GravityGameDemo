using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Resource.Scripts.Editor
{
    /// <summary>Actual Unity PlayerLoop/renderer scheduling around private physics scenes. Never saves scene changes.</summary>
    public sealed class PendulumStageLiveValidation
    {
        [MenuItem("Tools/Gravity Game/Physics/Start Stage1 Real Frame Validation")]
        public static void RunMenu() { Debug.Log(Begin("rendered", 10)); }

        [MenuItem("Tools/Gravity Game/Physics/Stop Stage1 Real Frame Validation")]
        public static void StopMenu()
        {
            if (Current != null && Current._loop != null) Object.Destroy(Current._loop.gameObject);
        }

        [Serializable] public sealed class Profile
        {
            public int targetFrameRate;
            public int renderedFrames, updateFrames;
            public double elapsedRealtimeSeconds, averageRenderedFramesPerSecond;
            public bool physicsPassed, frameProfilePassed;
            public string frameProfileFailure;
            public List<PendulumStageValidation.Result> results = new List<PendulumStageValidation.Result>();
            public List<PendulumScenarioValidation.ScenarioResult> additionalScenarios = new List<PendulumScenarioValidation.ScenarioResult>();
        }
        [Serializable] public sealed class Report
        {
            public string utc;
            public string method = "Real Unity PlayerLoop Update/FixedUpdate; targetFrameRate=60 then -1, vSync=0. Rendered frames use the Time.renderedFrameCount delta per profile; Update invocations are counted separately. Each private PhysicsScene2D uses Stage1 geometry and production World/Pendulum/Player FixedUpdates, with controlled rotation targets and test pivots. Top/side cases use explicit current-player pivot; far-wall variants cover automatic airborne-history pivot and fixed initial WorldRoot origin. Ground probes and player input are absent, so this does not validate the grounded automatic-pivot or input paths. Each local scene is simulated once and CompletePhysicsStep runs immediately afterward. Source scene motion is temporarily disabled and restored. No per-turn player reset.";
            public string state;
            public bool passed, physicsPassed, frameProfilesPassed;
            public int requestedRevolutionsPerDirection;
            public List<Profile> profiles = new List<Profile>();
            public string error;
        }

        public static PendulumStageLiveValidation Current { get; private set; }
        public Report Results { get; private set; }
        private readonly List<PendulumStageValidation.Session> _sessions = new List<PendulumStageValidation.Session>();
        private readonly Dictionary<PendulumStageValidation.Session, PendulumScenarioValidation.Placement> _placements = new Dictionary<PendulumStageValidation.Session, PendulumScenarioValidation.Placement>();
        private readonly Dictionary<PendulumStageValidation.Session, PendulumScenarioValidation.ScenarioResult> _scenarioResults = new Dictionary<PendulumStageValidation.Session, PendulumScenarioValidation.ScenarioResult>();
        private readonly Dictionary<Behaviour, bool> _sourceEnabled = new Dictionary<Behaviour, bool>();
        private readonly Dictionary<Rigidbody2D, bool> _sourceSimulated = new Dictionary<Rigidbody2D, bool>();
        private WorldRotator _world;
        private PlayerController _player;
        private PivotPendulum[] _pendulums;
        private Scene _sourceScene;
        private Profile _profile;
        private int _savedTargetFrameRate, _savedVsync, _profileIndex, _startedRenderedFrameCount;
        private float _savedTimeScale;
        private double _started, _lastUpdate, _lastProgressSave;
        private bool _restored;
        private string _path;
        private PendulumValidationLoopBridge _loop;

        public static string Begin(string label = "rendered", int revolutions = 10)
        {
            if (!Application.isPlaying) throw new InvalidOperationException("Enter Play mode with Stage1 loaded first.");
            if (Current != null) throw new InvalidOperationException("A real-frame validation is already running.");
            var source = SceneManager.GetSceneByName("Stage1");
            if (!source.IsValid() || !source.isLoaded) throw new InvalidOperationException("Stage1 is not loaded.");
            var go = new GameObject("Pendulum Live Validation (Temporary)");
            // Editor assemblies cannot supply an attachable MonoBehaviour. The small
            // bridge belongs to Assembly-CSharp and forwards the real PlayerLoop only.
            var loop = go.AddComponent<PendulumValidationLoopBridge>();
            var runner = new PendulumStageLiveValidation { _loop = loop };
            loop.Bind(runner.Update, runner.FixedUpdate, runner.OnDestroy);
            Current = runner;
            runner._sourceScene = source;
            runner._savedTargetFrameRate = Application.targetFrameRate;
            runner._savedVsync = QualitySettings.vSyncCount;
            runner._savedTimeScale = Time.timeScale;
            runner.Results = new Report
            {
                utc = DateTime.UtcNow.ToString("O"), state = "running", requestedRevolutionsPerDirection = Mathf.Max(1, revolutions)
            };
            string safeLabel = new string(label.Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_').ToArray());
            runner._path = PendulumStageValidation.Folder + "/Pendulum-" + safeLabel + ".json";
            try
            {
                var roots = source.GetRootGameObjects();
                runner._world = roots.SelectMany(r => r.GetComponentsInChildren<WorldRotator>(true)).Single();
                runner._player = roots.SelectMany(r => r.GetComponentsInChildren<PlayerController>(true)).Single();
                runner._pendulums = roots.SelectMany(r => r.GetComponentsInChildren<PivotPendulum>(true)).OrderBy(p => p.name).ToArray();
                foreach (var body in roots.SelectMany(r => r.GetComponentsInChildren<Rigidbody2D>(true)))
                {
                    runner._sourceSimulated[body] = body.simulated;
                    body.simulated = false;
                }
                foreach (var behaviour in roots.SelectMany(r => r.GetComponentsInChildren<Behaviour>(true)))
                {
                    if (!(behaviour is PlayerController) && !(behaviour is PlayerOxygen) && !(behaviour is WorldRotator) &&
                        !(behaviour is PivotPendulum) && !(behaviour is LevelIntroUI)) continue;
                    runner._sourceEnabled[behaviour] = behaviour.enabled;
                    behaviour.enabled = false;
                }
                QualitySettings.vSyncCount = 0;
                Time.timeScale = 1f;
                runner.StartProfile();
                return "Started real PlayerLoop validation; progress/results: " + runner._path;
            }
            catch (Exception ex) { runner.Fail(ex); throw; }
        }

        public static string Status()
        {
            return Current != null ? JsonUtility.ToJson(Current.Results, true) : "No live validation is running; inspect the saved report.";
        }

        private void StartProfile()
        {
            Application.targetFrameRate = _profileIndex == 0 ? 60 : -1;
            _profile = new Profile { targetFrameRate = Application.targetFrameRate };
            Results.profiles.Add(_profile);
            for (int scenario = 0; scenario < 6; ++scenario)
            {
                int board = scenario < 2 ? scenario : Mathf.Min(scenario - 2, 1);
                var f = new PendulumStageValidation.Fixture(_world, _player, _pendulums, board);
                // The source scene was frozen before cloning. Private fixtures must simulate.
                foreach (var root in f.Scene.GetRootGameObjects())
                    foreach (var body in root.GetComponentsInChildren<Rigidbody2D>()) body.simulated = true;
                foreach (var pendulum in f.Pendulums) pendulum.enabled = true;
                f.World.enabled = true;
                f.Player.enabled = true;
                f.Player.BeginGameplay();
                var result = new PendulumStageValidation.Result
                {
                    name = (scenario < 2 ? _pendulums[board].name + " top" : scenario < 4 ? _pendulums[board].name + " side" : scenario == 4 ? "far wall / automatic pivot" : "far wall / fixed root-origin pivot") + " / real PlayerLoop / " + (_profileIndex == 0 ? "60 FPS cap" : "unlimited"),
                    simulatedRenderHz = 0, revolutions = Results.requestedRevolutionsPerDirection,
                    clonedWorldColliders = f.Geometry.Count
                };
                _profile.results.Add(result);
                var session = new PendulumStageValidation.Session(f, result, 1, Results.requestedRevolutionsPerDirection);
                _sessions.Add(session);
                if (scenario >= 2)
                {
                    var scenarioResult = new PendulumScenarioValidation.ScenarioResult
                    {
                        scenario = scenario < 4 ? _pendulums[board].name + " free-end side" : scenario == 4 ? "playable far wall / automatic pivot" : "playable far wall / fixed root-origin pivot", physics = result
                    };
                    _scenarioResults.Add(session, scenarioResult);
                    _profile.additionalScenarios.Add(scenarioResult);
                    _placements.Add(session, PendulumScenarioValidation.Place(f, scenario - 2, scenarioResult));
                }
            }
            _started = _lastUpdate = Time.realtimeSinceStartupAsDouble;
            _startedRenderedFrameCount = Time.renderedFrameCount;
            _lastProgressSave = _started;
            SceneManager.SetActiveScene(_sourceScene);
            Save();
        }

        private void Update()
        {
            if (Results == null || Results.state != "running") return;
            double now = Time.realtimeSinceStartupAsDouble;
            double frameTime = now - _lastUpdate;
            _lastUpdate = now;
            _profile.updateFrames++;
            _profile.renderedFrames = Math.Max(0, Time.renderedFrameCount - _startedRenderedFrameCount);
            _profile.elapsedRealtimeSeconds = now - _started;
            _profile.averageRenderedFramesPerSecond = _profile.renderedFrames / Math.Max(.0001, _profile.elapsedRealtimeSeconds);
            try
            {
                for (int index = 0; index < _sessions.Count;)
                {
                    var session = _sessions[index];
                    try
                    {
                        if (!session.Finished) session.PrepareFrame(Math.Min(frameTime, .1));
                        ++index;
                    }
                    catch (Exception ex) { FailSession(session, ex); }
                }
                if (_sessions.All(s => s.Finished))
                {
                    foreach (var session in _sessions.ToArray())
                    {
                        try
                        {
                            session.Finish();
                            if (_scenarioResults.TryGetValue(session, out var scenarioResult)) PendulumScenarioValidation.FinalizeResult(scenarioResult);
                        }
                        catch (Exception ex)
                        {
                            session.Result.passed = false;
                            session.Result.error = ex.ToString();
                        }
                        ReleaseSession(session);
                    }
                    _profile.physicsPassed = _profile.results.All(r => r.passed);
                    ValidateFrameProfile(_profile);
                    _sessions.Clear();
                    _placements.Clear(); _scenarioResults.Clear();
                    if (++_profileIndex < 2) StartProfile();
                    else
                    {
                        Results.physicsPassed = Results.profiles.Count == 2 && Results.profiles.All(p => p.physicsPassed);
                        Results.frameProfilesPassed = Results.profiles.Count == 2 && Results.profiles.All(p => p.frameProfilePassed);
                        Results.passed = Results.physicsPassed && Results.frameProfilesPassed;
                        Results.state = "complete";
                        Save(); Restore(); Object.Destroy(_loop.gameObject);
                    }
                }
                else if (now - _lastProgressSave >= 5)
                { _lastProgressSave = now; Save(); }
            }
            catch (Exception ex) { Fail(ex); }
        }

        private void FixedUpdate()
        {
            if (Results == null || Results.state != "running") return;
            try
            {
                for (int index = 0; index < _sessions.Count;)
                {
                    var session = _sessions[index];
                    try
                    {
                        if (!session.Finished)
                        {
                            session.Fixture.Physics.Simulate(Time.fixedDeltaTime);
                            session.Fixture.Player.CompletePhysicsStep(Time.fixedDeltaTime);
                            session.RecordPhysicsStep();
                            if (_placements.TryGetValue(session, out var placement))
                                PendulumScenarioValidation.Measure(placement, session.Fixture, _scenarioResults[session]);
                        }
                        ++index;
                    }
                    catch (Exception ex) { FailSession(session, ex); }
                }
            }
            catch (Exception ex) { Fail(ex); }
        }

        private void Save()
        {
            Directory.CreateDirectory(PendulumStageValidation.Folder);
            File.WriteAllText(_path, JsonUtility.ToJson(Results, true));
        }
        private void FailSession(PendulumStageValidation.Session session, Exception exception)
        {
            // Preserve the failed case and every measured counter in the profile report.
            // It is never restarted or finalized into a pass, while other cases continue.
            session.Result.passed = false;
            session.Result.error = exception.ToString();
            ReleaseSession(session);
            Save();
        }

        private void ReleaseSession(PendulumStageValidation.Session session)
        {
            _sessions.Remove(session);
            _placements.Remove(session);
            _scenarioResults.Remove(session);
            try { session.Fixture.Dispose(); }
            catch (Exception cleanupException)
            {
                session.Result.passed = false;
                session.Result.error = (session.Result.error ?? "") + "\nFixture cleanup failed: " + cleanupException;
            }
        }

        private static void ValidateFrameProfile(Profile profile)
        {
            profile.frameProfilePassed = false;
            if (profile.renderedFrames <= 0 || profile.elapsedRealtimeSeconds <= 0d)
            {
                profile.frameProfileFailure = "No rendered-frame evidence was recorded for this profile.";
                return;
            }
            if (profile.targetFrameRate == 60 &&
                (profile.averageRenderedFramesPerSecond < 55d || profile.averageRenderedFramesPerSecond > 65d))
            {
                profile.frameProfileFailure = "The 60 FPS profile requires measured rendered FPS in [55, 65]; actual=" +
                    profile.averageRenderedFramesPerSecond.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) + ".";
                return;
            }
            // Unlimited is an uncapped scheduling profile, not a promise of 240 FPS.
            // Its measured render rate remains visible even when the hardware is slower.
            profile.frameProfilePassed = true;
            profile.frameProfileFailure = "";
        }
        private void Fail(Exception ex)
        {
            Results.error = ex.ToString(); Results.state = "failed"; Results.passed = false;
            Save(); Restore(); Object.Destroy(_loop.gameObject);
        }
        private void Restore()
        {
            if (_restored) return;
            _restored = true;
            foreach (var session in _sessions) session.Fixture.Dispose();
            _sessions.Clear();
            _placements.Clear(); _scenarioResults.Clear();
            foreach (var pair in _sourceSimulated) if (pair.Key != null) pair.Key.simulated = pair.Value;
            foreach (var pair in _sourceEnabled) if (pair.Key != null) pair.Key.enabled = pair.Value;
            Time.timeScale = _savedTimeScale;
            Application.targetFrameRate = _savedTargetFrameRate;
            QualitySettings.vSyncCount = _savedVsync;
            if (_sourceScene.IsValid() && _sourceScene.isLoaded) SceneManager.SetActiveScene(_sourceScene);
            if (Current == this) Current = null;
        }
        private void OnDestroy()
        {
            if (!_restored && Results != null)
            {
                Results.state = "interrupted"; Results.passed = false;
                if (!string.IsNullOrEmpty(_path)) Save();
                Restore();
            }
        }
    }
}
