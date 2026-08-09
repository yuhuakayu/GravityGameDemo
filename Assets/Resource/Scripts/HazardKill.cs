using UnityEngine;

namespace Resource.Scripts
{
    /// <summary>
    /// 挂在尖刺等致命物体上：PlayerController.autoMoveMode 打开时，玩家撞到/触碰到这个物体
    /// 会直接死亡重开当前关卡。纯标记组件，逻辑在 PlayerController 里判断。
    /// 碰撞体用实心或触发器都可以，PlayerController 两种都会检测。
    /// </summary>
    public class HazardKill : MonoBehaviour
    {
    }
}
