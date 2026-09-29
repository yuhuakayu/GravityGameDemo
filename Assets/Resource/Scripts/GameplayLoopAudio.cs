using UnityEngine;
using UnityEngine.SceneManagement;

namespace Resource.Scripts
{
    /// <summary>关卡循环音效；距离以玩家为中心，暂停/预览停止，死亡/过场淡出。</summary>
    [DisallowMultipleComponent]
    public sealed class GameplayLoopAudio : MonoBehaviour
    {
        private SfxManager _manager;
        private PlayerController _player;
        private Rigidbody2D _playerBody;
        private AudioSource _ambient, _fall;
        private AudioClip _doorClip, _laserClip;
        private GoalDoor[] _doors = new GoalDoor[0];
        private LaserEmitter[] _lasers = new LaserEmitter[0];
        private AudioSource[] _doorSources = new AudioSource[0];
        private AudioSource[] _laserSources = new AudioSource[0];
        private float _fallVolume;

        public void Configure(AudioClip ambientClip, AudioClip fallClip, AudioClip doorClip, AudioClip laserClip)
        {
            _manager = GetComponent<SfxManager>();
            _ambient = CreateLoop(ambientClip);
            _fall = CreateLoop(fallClip);
            _doorClip = doorClip;
            _laserClip = laserClip;
            CacheSceneObjects();
        }

        private void OnEnable() => SceneManager.sceneLoaded += OnSceneLoaded;

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            StopAll();
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => CacheSceneObjects();

        private void CacheSceneObjects()
        {
            StopAll();
            foreach (var source in _doorSources) if (source != null) Destroy(source);
            foreach (var source in _laserSources) if (source != null) Destroy(source);
            _player = FindFirstObjectByType<PlayerController>();
            _playerBody = _player != null ? _player.GetComponent<Rigidbody2D>() : null;
            _doors = FindObjectsByType<GoalDoor>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            _lasers = FindObjectsByType<LaserEmitter>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            _doorSources = new AudioSource[_doors.Length];
            _laserSources = new AudioSource[_lasers.Length];
            for (int i = 0; i < _doors.Length; i++) _doorSources[i] = CreateLoop(_doorClip);
            for (int i = 0; i < _lasers.Length; i++) _laserSources[i] = CreateLoop(_laserClip);
        }

        private AudioSource CreateLoop(AudioClip clip)
        {
            var source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = true;
            source.spatialBlend = 0f;
            source.volume = 0f;
            source.clip = clip;
            return source;
        }

        private void LateUpdate()
        {
            if (_manager == null || !_manager.isActiveAndEnabled || !_manager.sfxEnabled ||
                _manager.masterVolume <= 0f || _player == null || Time.timeScale <= 0f)
            {
                StopAll();
                return;
            }

            bool ending = _player.IsDead || SceneTransition.Instance.IsTransitioning;
            foreach (var door in _doors)
                if (door != null && door.IsOpening) ending = true;
            if (ending)
            {
                FadeOut(_ambient, 0.25f);
                FadeOut(_fall, 0.45f);
                for (int i = 0; i < _doors.Length; i++)
                    if (_doors[i] == null || _doors[i].IsOpening) SetVolume(_doorSources[i], 0f);
                    else FadeOut(_doorSources[i], 1f);
                for (int i = 0; i < _lasers.Length; i++)
                    if (_lasers[i] == null || !_lasers[i].IsBeamActive) SetVolume(_laserSources[i], 0f);
                    else FadeOut(_laserSources[i], 0.3f);
                return;
            }

            if (!_player.IsGameplayActive)
            {
                StopAll();
                return;
            }

            float master = _manager.masterVolume;
            SetVolume(_ambient, 0.25f * master);
            float fallSpeed = Mathf.Max(0f, -_playerBody.linearVelocity.y);
            // 玩家没有终端速度，沿用落地力度的快速下落参考速度。
            float fall01 = Mathf.Clamp01(fallSpeed / Mathf.Max(0.01f, _player.maxLandImpactSpeed));
            _fallVolume = _player.IsGrounded
                ? Mathf.MoveTowards(_fallVolume, 0f, 0.45f * Time.unscaledDeltaTime / 0.1f)
                : 0.45f * fall01 * fall01;
            SetVolume(_fall, _fallVolume * master);
            for (int i = 0; i < _doors.Length; i++)
                SetVolume(_doorSources[i], _doors[i] != null && _doors[i].isActiveAndEnabled
                    ? DistanceVolume(_doors[i].transform, 12f) * master : 0f);
            for (int i = 0; i < _lasers.Length; i++)
                SetVolume(_laserSources[i], _lasers[i] != null && _lasers[i].IsBeamActive
                    ? DistanceVolume(_lasers[i].transform, 10f) * 0.3f * master : 0f);
        }

        private float DistanceVolume(Transform target, float range)
        {
            // MazeSceneBuilder 的一格是 2 个世界单位；仅计算 XY 距离。
            return Mathf.Clamp01(1f - Vector2.Distance(_player.transform.position, target.position) / range);
        }

        private void FadeOut(AudioSource source, float maxVolume)
        {
            SetVolume(source, Mathf.MoveTowards(source.volume, 0f,
                maxVolume * _manager.masterVolume * Time.unscaledDeltaTime / 0.15f));
        }

        private static void SetVolume(AudioSource source, float volume)
        {
            if (source == null) return;
            source.volume = volume;
            if (volume > 0f && source.clip != null)
            {
                if (!source.isPlaying) source.Play();
            }
            else if (source.isPlaying) source.Stop();
        }

        private void StopAll()
        {
            SetVolume(_ambient, 0f);
            SetVolume(_fall, 0f);
            _fallVolume = 0f;
            foreach (var source in _doorSources) SetVolume(source, 0f);
            foreach (var source in _laserSources) SetVolume(source, 0f);
        }
    }
}
