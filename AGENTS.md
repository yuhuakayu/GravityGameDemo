# 项目协作指南

适用于本仓库的开发、排查和验证。先读相关代码，再修改；用户当前任务对范围和验证的明确要求优先于这里的默认约定。

## 技术与运行环境

- Unity **6000.1.3f1**，准确版本以 `ProjectSettings/ProjectVersion.txt` 为准。
- C#、Unity 2D、URP 17.1.0、Input System 1.14.2、Unity Test Framework 1.5.1；依赖以 `Packages/manifest.json` 和 `Packages/packages-lock.json` 为准。
- 普通按键使用 `UnityEngine.InputSystem`；陀螺仪通过 JoyShockLibrary 原生层接入。不要新增旧版 `UnityEngine.Input` 输入逻辑，也不要假定所有手柄数据都来自 Steam Input。
- Unity MCP 项目包固定为 v10.2.0。连接或包升级是独立工作，不要为普通玩法修改顺带升级依赖。
- 当前开发环境是 Windows / PowerShell，项目路径含空格，命令中的路径必须正确加引号。

## 目录与关键入口

| 路径 | 用途 |
| --- | --- |
| `Assets/Scenes/` | `MainMenu`、`Stage1`、`Stage2` 和 `Test_PillarCrush`；启用场景清单见 `ProjectSettings/EditorBuildSettings.asset` |
| `Assets/Resource/Scripts/` | 主要玩法脚本，常用命名空间为 `Resource.Scripts` |
| `Assets/Resource/Scripts/PlayerController.cs` | 玩家输入、手动/自动移动、自管理速度、防推动、死亡入口 |
| `Assets/Resource/Scripts/WorldRoot.cs` | 类名是 **`WorldRotator`**；世界刚体旋转、累计角度、下一物理步姿态 |
| `Assets/Resource/Scripts/PivotPendulum.cs` | 摆锤板几何、铰链对齐和摆动；`PivotBracketFollow.cs` 处理支架跟随 |
| `Assets/Resource/Scripts/CrushGuard.cs`、`Assets/Resource/Scripts/Physics/` | 穿透修复、夹死保护、运动几何限步与测试旋转器 |
| `Assets/Resource/Scripts/Gyro/Core/` | 校准、方向提取、速度/角度映射、采样积分与录制；程序集 `GravityGame.Gyro.Core` |
| `Assets/Resource/Scripts/Gyro/Native/` | JoyShockLibrary 互操作与控制器连接 |
| `Assets/Resource/Scripts/Gyro/Runtime/` | `GyroRuntime`、`WorldRotationInput`、设置持久化，连接输入与世界旋转 |
| `Assets/Resource/Scripts/Debug/` | 波浪键调试控制台、窗口拖动/缩放、场景/陀螺仪/玩法/性能分页 |
| `Assets/Resource/Scripts/Oxygen/` | 氧气、HUD、拾取粒子和氧气瓶；喷气背包已移除 |
| `Assets/Resource/Scripts/PlayerMovementAnimation.cs` | 着地呼吸、连续离地 0.08 秒后浮空；落地立即回到 Idle 第一帧 |
| `Assets/Resource/Scripts/Editor/` | 场景安装工具、资源导入工具和显式验证入口；不能被运行时直接依赖 |
| `Assets/Resource/Prefabs/`、`Assets/Resource/Art/`、`Assets/Resource/Resources/` | 预制体、美术及运行时资源 |
| `Assets/Plugins/JoyShockLibrary/` | 原生手柄库及说明 |
| `Assets/Tests/EditMode/` | 陀螺仪核心与相关回归测试，程序集 `GravityGame.Gyro.EditModeTests` |
| `Assets/Tests/PlayMode/` | 摆锤、旋转碰撞与自动移动测试，程序集 `GravityGame.Pendulum.PlayModeTests` |
| `Docs/` | 陀螺仪规格、摆锤和防挤压的设计/历史验收记录 |

流程入口：`LevelIntroUI` 负责关卡预览；`GameFlowState`、`MainMenuUI`、`GameHUD` 管理菜单与玩法状态；`SceneTransition` 负责切场景；`SettingsManager`、`LocalizationManager`、`SfxManager` 管理设置、语言和音效。

历史资料：`Docs/GyroSpec/仕样书_陀螺仪方向盘与调试控制台.txt`、其 `images/` 参考图、`OXYGEN_SYSTEM.md`、`Docs/PendulumFix/`、`Docs/PillarCrush/`。`HANDOFF.md` 与 `Assets/Resource/Scripts/CLAUDE.md` 含旧架构描述（例如 `Transform.RotateAround`），不能当作当前实现；以代码、场景配置及用户最新要求核对。

## 常用命令与操作

以下 PowerShell 命令从项目根目录运行，按需选择，不是每次任务都要全跑。

```powershell
# 查看已有修改，避免覆盖用户工作
git status --short
git diff --stat
git diff --check

# 查找脚本与功能入口
rg --files Assets/Resource/Scripts Assets/Tests
rg -n 'WorldRotator|SteeringExtractor|IsGameplayActive' Assets/Resource/Scripts

# 核对编辑器版本、依赖和已启用场景
Get-Content -LiteralPath 'ProjectSettings/ProjectVersion.txt'
Get-Content -LiteralPath 'Packages/manifest.json'
Get-Content -LiteralPath 'ProjectSettings/EditorBuildSettings.asset'

# 查看最近的 Unity 编辑器日志
Get-Content -LiteralPath "$env:LOCALAPPDATA/Unity/Editor/Editor.log" -Tail 100
```

- 运行：用匹配版本的 Unity 打开项目，打开所需场景并进入 Play；关卡预览与正式游戏是不同状态。
- 调试控制台：键盘反引号/波浪键（`Keyboard.backquoteKey`）或手柄 `Options + Create`。控制台属于运行时功能，普通构建也应保留；可拖动标题栏、拉伸边缘并调整 UI 比例。
- 测试：`Window > General > Test Runner`，选择对应 EditMode / PlayMode 测试程序集或单个用例。已连接 Unity MCP 时，也可用 `run_tests` 指定 `mode`、`assembly_names`，再用 `get_test_job` 读取结果。
- 构建：Unity 6 的 `File > Build Profiles`，检查目标平台和场景清单后构建。当前没有项目自定义的命令行构建方法，不要虚构 `-executeMethod` 入口。
- 场景工具：`Tools > Gravity Game > Install Oxygen System in Current Scene`；物理工具在 `Tools > Gravity Game > Physics`。这些工具可能修改场景或运行较长验证，仅在任务需要时使用。

测试优先用编辑器内 Test Runner 或 Unity MCP；`dotnet test` 不能代替 Unity 的测试与编译。测试启动不等于通过，要看结果。

## 重要玩法与技术约束

- 世界根节点旋转，玩家保持屏幕向下重力且自身不跟着旋转。不要为了新机制直接改全局重力或把玩家挂进旋转层级。
- 世界和独立运动几何沿用 Kinematic `Rigidbody2D` 的 `MoveRotation` / `MovePosition`，不要退回纯 Transform 位移旋转。同一个物理步连续调用多个 `MoveRotation` 不等于物理子步。
- `WorldRotator` 执行顺序为 -250，`PivotPendulum` 为 -200。摆锤应使用世界的下一步姿态计算锚点，避免读旧 `pivot.position` 引入一帧延迟；`hingePoint` 是板内 `Circle`，`gravityPoint`（`Grivity`）只决定臂长。
- 防推动开启时，玩家用 `intendedVelocity` 管理输入和重力速度，不继承运动平台注入的速度。保持 Dynamic、Continuous、Interpolate 和冻结 Z 旋转的配置；预览/死亡状态已有的临时切换除外。
- `CrushGuard` 的穿透修复是受限的位置修正，保留玩家原来所在的一侧；不能靠关闭碰撞、把玩家碰撞体改 Trigger 或任意传送掩盖穿墙。不要用修改全局 Baumgarte / Max Linear Correction 或柱子摩擦材质替代修复。
- `pauseAutoMoveWhileRotating` 只暂停自动移动的速度贡献，并保留滞回/恢复延迟。重力、碰撞及防推动仍须工作，不能把世界旋转等同于暂停整个游戏。
- `PlayerController.IsGameplayActive` 是玩法状态的重要入口。氧气消耗、动画、拾取等要尊重预览、暂停和死亡；死亡统一调用 `PlayerController.Die()`，沿用红光 → `SceneTransition` → 重开并回到预览的流程。
- Mode A 使用角速度映射后的传感器采样时间积分，不能再乘一遍渲染帧时间；Mode B 使用角度映射。累计世界角不能直接与包裹到 ±180° 的角度相减；输入侧还要保留侧立时的方向连续性。
- `WorldRotator.useGeometrySafetyClamp` 默认关闭，旋转手感优先；不要为修穿墙重新打开它或加新的全局限速，穿墙优先查碰撞层和 `CrushGuard`。
- 保持现有手感和 Inspector/场景参数，除非当前任务明确要求调整。不要用改灵敏度、阻尼或限速掩盖算法错误。
- HUD 固定在屏幕坐标，关卡道具随世界旋转。UI 订阅状态事件或缓存引用，不每帧查找玩家。
- 陀螺仪及控制台设置由 `GyroSettingsStore` 显式保存到 `Application.persistentDataPath/gyro_settings.json`；不要把临时调参默认为已保存的项目配置。

## 协作、修改与验证规则

1. 默认用中文沟通，代码标识符、命令、错误日志保持原文。复杂任务先给简短步骤及对应验收标准，说明关键假设；存在影响结果的歧义时明确提出，不默默选择。
2. 先读入口脚本、调用者和相关场景引用，再改代码。能从已有信息确定的常规选择直接执行，不反复请求已授权事项的确认。
3. 只实现当前请求。优先最小方案，不顺带重构、清理旧代码、添加推测性功能、单次使用的抽象或无必要的配置项。
4. 每处改动都应能对应任务要求。保留既有风格和用户尚未提交的修改；仅清理本次改动造成的无用代码或临时文件，不使用破坏性 Git 操作恢复工作区。
5. 新脚本放入合适的现有目录。新增/移动 `Assets` 资源时保留或生成配套 `.meta`，不能破坏 GUID、场景和预制体引用。优先通过 Unity 处理资源；手改 YAML 时仅改已确认字段。
6. 不手改 `Library/`、`Temp/`、`obj/`、生成的 `.csproj` / `.sln` 或包缓存来修项目代码。编辑器代码留在 `Editor/`；现有测试程序集不能直接引用默认 `Assembly-CSharp`，遵循已有程序集边界。
7. 修复缺陷时尽可能先用最小测试复现，再验证修复；Unity 编译无错误后再调用新类型。纯文档等低影响修改只检查内容和差异，不为此启动玩法或物理长跑。
8. 按当前任务范围选择验证。用户限定了测试集合时严格遵守；相关检查通过后，不自行追加长时间实机、多帧率、截图或重复全量测试。
9. 运行时改 Inspector、测试场景或调试参数后，区分临时状态与需要保存的改动；不要把测试参数无意保存进正式场景。
10. 汇报具体改了哪些文件、验证结果及尚未验证的限制。测试启动或编译成功不等于测试通过；不把历史报告中的数值写成本次实测。用户未要求时不另建报告文件。

本文件记录的是项目约定和入口，不是测试通过的证明。后续架构或命令发生变化时，只更新相关条目，避免再次形成与代码不符的指南。
