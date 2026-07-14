using UnityEngine;

namespace Resource.Scripts
{
    /// <summary>
    /// 挂在纯视觉支架物体上（比如"静止clock"）——不参与物理，不带 Rigidbody2D，
    /// 每帧把自己的位置对齐到摆锤圆心（Circle），旋转对齐到 WorldRoot 的角度。
    ///
    /// 为什么不直接把 Clock 挂到这个支架下面：Clock 上有 Rigidbody2D，靠
    /// MovePosition/MoveRotation 驱动；如果它的父物体链条里有物体在被别的脚本
    /// 用普通 Transform 操作（RotateAround 之类）持续旋转，物理系统和 Transform
    /// 父子关系会互相打架，轻则抖动、重则直接飞出去。这个脚本用"每帧对齐"代替
    /// "父子关系"，效果一样是"跟着一起动"，但完全不碰 Rigidbody2D，没有这个坑。
    /// </summary>
    public class PivotBracketFollow : MonoBehaviour
    {
        [Tooltip("要对齐位置的目标，比如摆锤的 Circle（圆心）")]
        public Transform followPosition;
        [Tooltip("要对齐旋转角度（仅 Z 轴）的目标，比如 WorldRoot。留空则不改旋转")]
        public Transform followRotation;

        void LateUpdate()
        {
            if (followPosition != null)
                transform.position = followPosition.position;

            if (followRotation != null)
            {
                Vector3 euler = transform.eulerAngles;
                euler.z = followRotation.eulerAngles.z;
                transform.eulerAngles = euler;
            }
        }
    }
}
