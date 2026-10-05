using UnityEngine;

namespace Resource.Scripts.SceneFX
{
    /// <summary>
    /// 所有摆动物体共用的每帧数据：世界旋转角速度、旋转中心（玩家）、玩家速度、落地事件、风的时间。
    /// 由第一个调用 Tick() 的组件在每帧更新一次。
    /// </summary>
    public static class SwayWorld
    {
        public static float Omega;           // 世界角速度（弧度/秒，逆时针为正，和 Unity 的 z 角一致）
        public static Vector2 Pivot;         // 旋转中心 = 玩家脚底（世界坐标）
        public static Vector2 PlayerFeet;    // 玩家脚底（世界坐标）
        public static Vector2 PlayerVel;     // 玩家速度（世界单位/秒，按位置差估算）
        public static bool PlayerActive;     // 正在游玩（非预览、未死亡）
        public static float LandingImpact;   // 只在玩家落地的那一帧 > 0：落地前的下落速度
        public static float WindTime;        // 风的时间（跟 Time.deltaTime 走，暂停时停止）

        private static int _frame = -1;
        private static Transform _world;
        private static PlayerController _player;
        private static float _lastAngle;
        private static bool _hasAngle;
        private static Vector2 _lastPos;
        private static bool _hasPos;
        private static bool _wasGrounded;
        private static float _prevFall;

        public static void Tick()
        {
            if (_frame == Time.frameCount) return;
            _frame = Time.frameCount;
            float dt = Time.deltaTime;
            WindTime += dt;

            if (_world == null)
            {
                var rotator = Object.FindFirstObjectByType<WorldRotator>();
                _world = rotator != null ? rotator.transform : null;
                _hasAngle = false;
            }
            if (_player == null)
            {
                _player = Object.FindFirstObjectByType<PlayerController>();
                _hasPos = false;
            }

            Omega = 0f;
            if (_world != null)
            {
                float angle = _world.eulerAngles.z;
                if (_hasAngle && dt > 0f) Omega = Mathf.DeltaAngle(_lastAngle, angle) * Mathf.Deg2Rad / dt;
                _lastAngle = angle;
                _hasAngle = true;
            }

            LandingImpact = 0f;
            PlayerActive = false;
            if (_player == null) return;
            Vector2 feet = _player.transform.position;
            PlayerVel = _hasPos && dt > 0f ? (feet - _lastPos) / dt : Vector2.zero;
            _lastPos = feet;
            _hasPos = true;
            PlayerFeet = feet;
            Pivot = feet;
            PlayerActive = _player.IsGameplayActive;

            bool grounded = _player.IsGrounded;
            if (PlayerActive && grounded && !_wasGrounded && _prevFall > 2.5f) LandingImpact = _prevFall;
            _wasGrounded = grounded;
            _prevFall = Mathf.Max(0f, -PlayerVel.y);   // 屏幕向下 = 世界 -Y
        }
    }
}
