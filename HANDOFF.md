# UpSide Down（Gravity Game）项目交接文档

> 本文件分两部分。第一部分给人看，快速了解现状。第二部分给 Claude Code（另一个连了 Unity MCP 的会话）看，尽量写得详细、可执行。

---

## 第一部分：给你看的（简单版）

这是一个 Unity 2D 重力旋转解谜平台跳跃游戏，毕业设计用。核心玩法：**世界绕着玩家旋转**（不是玩家转向）。

**这次（在没连 Unity MCP 的会话里）做完的东西**：
- 一整套 UI 系统：主菜单（标题/选关/设置）、游戏内暂停菜单、设置面板（音量/分辨率/全屏/语言）
- 中/日/英三语切换
- 全套程序化合成音效（项目没有真实音频素材，代码里现场生成波形）
- 关卡氛围演出（背景、剪影、萤火虫、光照）、场景转场特效
- 火把/蜡烛装饰、通关触发器代码
- 刚加的新机制：**自动移动 + 撞墙强制转向 + 触雷即死**（用来制造紧迫感的核心玩法，代码写完了但完全没在 Unity 里跑过）

**因为没连 MCP，所有代码都是"盲写"的**——没有在 Unity Editor 里实际运行、看画面、测手感。这次要交给另一个连了 MCP 的会话，去做那些必须要看画面才能做的事：摆场景物体、调数值、实测手感、修可能存在的 bug。

**已知一个大坑**：之前发现 `WorldRotator` 组件的 `Pivot` 字段在场景文件里被手动指定成了一个叫"360TurnMain"的固定物体，导致世界旋转实际上不是以玩家为中心——虽然当时提醒你去 Inspector 里清空了，但请那边的会话务必**先确认这个字段现在是不是 None**，不然新机制里"转世界=改变撞墙方向"这套逻辑会直接不成立。

第二部分是详细的技术交接，直接把整份文件发给那边的 Claude Code 就行。

---

## 第二部分：给 Claude Code 的详细交接

### 项目基本信息

- Unity 版本：6000.1.3f1（不是 2021 LTS，之前有文档写错过）
- 渲染管线：URP 17.1.0
- 输入系统：**新 Input System**（`UnityEngine.InputSystem`），项目里禁止用旧版 `UnityEngine.Input`
- 所有脚本在 `Assets/Resource/Scripts/` 下，命名空间统一 `Resource.Scripts`
- 场景：`Assets/Scenes/Stage1.unity`（当前唯一有实际内容的关卡，原名 SampleScene，已改名）、`Assets/Scenes/Stage2.unity`（新建的，目前基本是空场景，没有确认有没有基础地面和出生点）
- 项目里**没有任何真实美术/音频素材**（除了 Super_Retro_Collection 这个第三方美术包和少量 Backgrounds 图）。所有装饰性视觉/音效都是运行时程序生成的（`Texture2D`/`AudioClip.Create` 手写波形），这是刻意的设计选择，不是偷懒——因为写代码的会话没法可视化验证手动导入的美术资源对不对。
- 玩家角色目前用占位精灵，PPT 里设计的粉发女角色立绘一直没拿到裁好的透明 PNG，等到了再做 Idle/Run 动画。

### 核心玩法机制

#### 1. 世界旋转（WorldRotator，`WorldRoot.cs`）—— 原有核心机制，务必先验证

- 挂在 `WorldRoot` 物体上，`transform.RotateAround(pivot, Vector3.forward, delta)` 旋转整个世界
- `pivot` 字段**留空**时会自动找场景里的 `PlayerController`，用玩家的实时坐标当轴心（落地用当前帧坐标，空中用 `airborneDelayFrames`帧之前的坐标，防止空中旋转过于眩晕）
- **请先去 Hierarchy 找到 `WorldRoot` 物体，检查 `WorldRotator` 组件的 `Pivot` 字段是不是 None**。之前排查到这个字段被手动指到了一个叫"360TurnMain"的固定物体（坐标 `(0, 5.46, 0)`，上面挂着调试用的 `DS5GyroReader`），导致旋转实际上不是以玩家为中心。如果又被改回去了，清空即可。
- 同一个组件上还有个 `Use Gyro If Available` 字段，之前场景里发现被打开过（代码默认是 false，因为陀螺仪手感被用户明确否决过，"手感特别诡异"）。如果还是打开状态，建议关掉，除非用户后来又要求开。
- 摇杆输入有三层滤波（死区/低通平滑/方向持续时间）防止手柄晃动误触发旋转，`DebugTuningUI`（F1 呼出）里能实时调这些参数。

#### 2. 摆锤机关（PivotPendulum.cs）—— 已修复，架构改过

- `pivot` 字段现在是一个**外部锚点**（不需要是 Clock 自己的子物体，比如可以是 WorldRoot 下的"静止clock"），每帧读取 `pivot.position` 算出摆动的世界坐标
- `gravityPoint`（场景里叫 Grivity）**必须**还是 Clock 自己的刚体子物体
- 请确认场景里这几个字段配置正确：`pivot` 指向外部锚点、`gravityPoint` 指向 Clock 自己的子物体、`worldRoot` 指向真正的 WorldRoot 物体
- `dealsImpactForce`（默认 false）控制要不要把玩家弹飞，默认是"撞了就跟墙一样挡住"，不发射玩家

#### 3. 新增：自动移动 + 撞墙强制转向（刚写完，完全没测过）

这是这次新加的"紧迫感"核心玩法，目的是让玩家有种被追着跑的焦虑感。**这部分最需要在 Unity 里实际验证手感**。

**设计**：玩家自动朝一个方向匀速滑行（不再手动控制移动，也没有跳跃，重力关掉），唯一的操作是**转动世界**——转世界会改变玩家接下来会撞上哪面墙。撞到贴了方向标的墙会被强制改成往那个方向滑；撞到致命机关直接死亡重开本关。

**涉及的文件**：
- `PlayerController.cs` 新增字段：
  - `autoMoveMode`（bool，默认 false）—— 打开后接管移动逻辑，手动移动/跳跃全部失效，`Start()` 里会把 `rb.gravityScale` 设成 0
  - `autoMoveSpeed`（float，默认 6）—— 滑行速度
  - `autoMoveStartAngle`（float，默认 0）—— 初始滑行方向角度（0=右，90=上，180=左，270=下）
  - 新方法 `HandleAutoMove()`、`HandleAutoMoveCollision(GameObject)`、`Die()`
  - `OnCollisionEnter2D`/新增的 `OnTriggerEnter2D` 在 `autoMoveMode` 打开时会检查碰到的物体上有没有 `WallRedirect` 或 `HazardKill` 组件
- `WallRedirect.cs`（新文件）—— 挂在墙上，`redirectAngle` 字段指定玩家撞上后被强制改成的方向角度。`Start()` 里会自动生成一个箭头 `SpriteRenderer` 子物体（程序画的，指向 `redirectAngle`），方便摆关卡的时候直接看清方向，不需要真美术
- `HazardKill.cs`（新文件）—— 纯标记组件，挂在尖刺等致命物体上，玩家碰到（`autoMoveMode` 下）会调用 `PlayerController.Die()`：停止移动、放死亡音效（`SfxManager.PlayPlayerDeath()`，新加的下坠音效）、通过 `SceneTransition.Instance.LoadScene(当前场景名)` 重开本关
- 碰撞体是实心还是触发器都行，`PlayerController` 两种都会检测（`OnCollisionEnter2D` + `OnTriggerEnter2D`）

**需要在 Unity 里做的事**（这几项必须要看画面才能做，之前那个会话做不了）：
1. 在 `Stage2`（或者新建一个测试场景/单独区域）里搭一小段"走廊"，两三面墙挂 `WallRedirect`（设不同角度），放一两个挂了 `HazardKill` 的尖刺物体，把玩家的 `PlayerController.Auto Move Mode` 勾上，实测手感
2. 检查 `WallRedirect` 自动生成的箭头贴图朝向对不对（应该是贴图本地 +X 方向经过 `Transform` 旋转到 `redirectAngle`）、大小合不合适（`arrowSize` 字段，默认 1 世界单位）
3. 判断撞墙转向的速度衔接是否够顺（现在是直接赋值 `rb.linearVelocity = _autoMoveDir * autoMoveSpeed`，没有任何缓冲/插值，如果觉得太生硬可以加一点 Lerp）
4. 死亡重开的节奏体感——现在死亡音效播放后立刻走 `SceneTransition`（黑幕转场），中间没有额外停顿，可能需要加一点延迟让死亡音效播完再转场（可以参考 `GoalDoor.cs` 里 `DoGoalSequence()` 的写法，用 `WaitForSecondsRealtime` 等一下）
5. 这个机制目前是**独立开关**，跟原来的手动移动关卡（Stage1）完全不冲突（`autoMoveMode` 默认 false）。用户提到的另一个"紧迫感"方向（场景随时间缩小 / 灌腐蚀液）明确说了跟撞墙强制转向"是同一个目的"，先不用做，除非用户改主意

### 已完成系统清单（这些都写完了，理论上能跑，可能需要微调）

**音频**：`SfxManager.cs` —— 单例，`AudioClip.Create` 现场生成所有音效波形，覆盖脚步/跳跃/落地/撞墙/按钮/开门/转场/齿轮咔嗒/通关音阶/死亡音效/火堆噼啪声等。`masterVolume` 字段接了 `SettingsManager` 的音量滑条。

**UI**：
- `MainMenuUI.cs` —— 主菜单（标题/关卡选择/设置），运行时全屏 Canvas 遮罩层盖在场景上，不是独立场景文件。`levels` 字段是个 `List<LevelEntry>`，默认只有当前场景一关，加新关卡直接往数组里加元素（`sceneName` 填对应场景名）。
- `GameHUD.cs` —— 游戏内 HUD（关卡名 + 齿轮图标按钮）、暂停菜单（继续/设置/重新开始/返回主菜单）、独立的游戏内设置子面板。手柄 Start 键直接呼出暂停菜单（不是设置面板，进设置要在菜单里再选一次——这是刻意设计成两层的）。
- `DebugTuningUI.cs` —— F1 呼出的调试面板，能实时调世界旋转/手柄震动/玩家移动/音效相关的参数。里面有个复用的 `DebugSliderDrag` 组件（拖拽滑条），`MainMenuUI`/`GameHUD` 的设置面板滑条也是复用这个。
- `SettingsManager.cs` —— 主音量/音乐音量/音效音量/分辨率/全屏，PlayerPrefs 持久化。
- `LocalizationManager.cs` —— 中/日/英三语切换，只覆盖"设置"相关文字（标题页 Options 按钮、两个设置面板的所有文字），标题页 Start/Quit、暂停菜单继续/重新开始/退出这些还是固定中文，没做多语言。

**关卡演出**：
- `LevelAtmosphere.cs` —— 自动搭建背景（`Resources/Backgrounds/DungeonA`）、程序生成的近景石柱剪影、萤火虫粒子、调暗环境光。背景视差系数改成了 1（完全跟摄像机锁死），之前用 0.2 会导致玩家跑远一点就露出 Unity 默认空白背景。
- `ParallaxLayer.cs` / `FollowTarget2D.cs` —— 通用视差滚动 / 平滑跟随组件，被摄像机跟随、背景跟随、萤火虫跟随复用。
- `SceneTransition.cs` —— 单例，黑幕虹膜开合式转场，`LoadScene(sceneName, onComplete)`。
- `GoalDoor.cs` —— 通关触发器，`OnTriggerEnter2D` 检测玩家，播放开门音效+通关音阶，然后走 `SceneTransition` 切到 `nextSceneName`。**这个组件目前没有挂在场景里任何物体上**，玩家现在没法真正"通关"，需要找个门/触发器物体手动加上这个组件并设置 `nextSceneName`。
- `TorchLight2D.cs` —— 挂在火把上自动加暖光 `Light2D` + 火焰噼啪音效循环。之前有个 bug：音效用了 3D 空间衰减（`spatialBlend=1`），2D 游戏摄像机 Z 轴偏移导致衰减距离几乎总是超出范围、完全听不到，已经改成 `spatialBlend=0`（非空间音效）修好了。
- `CandleSpawner.cs` —— 自动在玩家出生点附近生成一个蜡烛装饰（借用 Torch_01 预制体顶替，没有真蜡烛美术），加 `TorchLight2D`。

**状态管理**：
- `GameFlowState.cs` —— 静态字段 `HasEnteredGame`，玩家在主菜单第一次选关确认后设成 true。`MainMenuUI.Start()` 一开始检查这个标记，已经是 true 就直接销毁自己不弹菜单——解决"重新开始/切下一关会重复弹主菜单"的问题。这个标记只在内存里，不会跨 Play 会话持久化，是刻意的。

**玩家手感强化**（`PlayerController.cs` 里加的，不影响原有移动手感）：跑动扬尘粒子（注意：粒子系统必须显式指定材质，`AddComponent<ParticleSystem>()` 默认材质在 URP 下会变成粉紫色 shader 缺失色，代码里已经用 `Sprites/Default` + 程序生成的软圆点贴图修好了）、脚步声、落地/撞墙缓冲动画、手柄震动、玩家随身点光源。

**其他**：`PivotBracketFollow.cs` —— 摆锤架构重构后基本没在用了，没删，算是遗留文件。

### 代码约定 / 架构模式

- 几乎所有新系统都是**运行时自举**的 MonoBehaviour：要么是懒加载单例（`XxxManager.Instance` 第一次访问时自动 `new GameObject().AddComponent<Xxx>()`），要么是从 `PlayerController.Start()` 的一串 `FindObjectOfType<Xxx>() == null` 检查里自动创建。这是刻意选择的模式，因为写代码的会话没法手动在 Unity Editor 里拖引用/摆场景物体，所以让一切能用代码自动搭建。**如果你（连了 MCP 的会话）要新加系统，可以选择继续用这个模式，也可以直接在场景里手动摆，两种都行，不冲突。**
- 没有真实美术/音频素材的地方，一律用运行时程序生成（`Texture2D.SetPixels32` 画贴图、`AudioClip.Create` 手写波形），代码注释里都写清楚了"为什么没用真资源"。如果你有能力生成/获取真实美术资源替换，直接换掉对应的 `Resources.Load<T>()` 调用或者删掉程序生成逻辑改用真 Sprite/AudioClip 引用即可，不用保留兼容层。
- 手柄输入统一用 `Gamepad.current`（新 Input System），永远要 null check。项目主要用 DualSense 手柄，通过 Steam Input 映射。**陀螺仪功能默认关闭**，用户明确反馈过原生陀螺仪方案手感诡异，不要在没有明确要求的情况下重新启用。
- 每次多文件批量修改后，建议做一次大括号配对检查（`grep -o '{' file | wc -l` vs `}`），这边的会话因为没法编译验证，一直靠这个做最低限度的语法保险，你如果能直接在 Unity 里看编译报错就不需要这个了。

### 待办事项汇总（按优先级）

1. **验证 `WorldRotator.pivot` 字段是 None**（见上面"核心玩法机制"第 1 条，最高优先级，直接影响新旧两套机制能不能正常工作）
2. **把 `GoalDoor` 挂到 Stage1 场景里的实际终点门/触发器上**，设置 `nextSceneName`，让游戏能真正走完流程
3. **测试并调优新加的自动移动/撞墙转向/死亡机制**（见上面详细说明），这是当前最大的一块未验证内容
4. 检查 `Stage2` 场景有没有基础地面和玩家出生点
5. 用新机制（或者继续手动移动机制）实际设计出 1 个能玩的完整关卡——目前关卡内容基本是空的，只有一个摆锤机关
6. 如果拿到了角色立绘美术资源，做 Idle/Run 动画替换掉占位精灵
7. 提交前：确认调试开关（`isDebugLog` 等）默认关闭，确认打包 Build 能正常跑（不只是 Editor 里能跑）

如果有问题欢迎随时反馈给写这份文档的那个会话（没连 MCP 的那边），代码逻辑相关的问题它会更清楚为什么这么设计。
