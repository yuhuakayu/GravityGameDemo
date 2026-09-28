# 氧气系统交付说明

氧气系统已配置到 `Assets/Scenes/Stage1.unity` 和 `Assets/Scenes/Stage2.unity`。玩家氧气、喷气冲刺、左下角读条、喷口特效及关卡氧气瓶的引用已保存；这两个场景无需补拖组件引用。

运行验证见主任务报告。2026-09-23 已完成 Unity 编译、9 项组件/物理运行检查、自然耗氧与暂停、实际触发器拾取，以及自然耗尽和冲刺恰好扣到零两条死亡重开路径。重开后确认氧气 100、预览冻结和瓶子恢复。

输入使用 Input System 的键盘状态事件与虚拟 DualSense 状态事件验证：分别扣除 10 点，摇杆方向正确；喷气粒子已确认播放且发射轴与冲刺反向。此项属于输入事件测试，实体 DS4 / DualSense 的手感与 Steam 映射仍按下文步骤验收。Stage2 的组件、UI 引用和道具旋转父级已检查。

同次维护把 Unity MCP 项目包和本地服务从 10.1.2 更新到稳定版 10.2.0。`Packages/manifest.json` 与 `Packages/packages-lock.json` 已固定 `v10.2.0`（commit `30d22075093d1d35dfb0091c1c7550e9ad948577`），实时连接已恢复。运行检查结束后已退出 Play 模式，临时测试参数未保存到场景。

## 文件与接入位置

| 文件 | 用途 |
| --- | --- |
| `Assets/Resource/Scripts/Oxygen/PlayerOxygen.cs` | 氧气数值、自然消耗、补氧、扣氧及变化事件；零氧调用已有 `PlayerController.Die()` |
| `Assets/Resource/Scripts/Oxygen/OxygenHUD.cs` | 通过事件更新横向 `Image.fillAmount`，低氧变红并闪烁 |
| `Assets/Resource/Scripts/Oxygen/PlayerJetpack.cs` | 触摸板和键盘输入、本地方向转换、冲刺扣费、冷却与碰撞 |
| `Assets/Resource/Scripts/Oxygen/JetpackEffects.cs` | 冲刺反方向喷气、可选拖尾及预留音效 |
| `Assets/Resource/Scripts/Oxygen/OxygenTank.cs` | 氧气瓶触发拾取、补氧、浮动与缩放消失 |
| `Assets/Resource/Scripts/Editor/OxygenSystemSetup.cs` | 当前场景安装菜单及资源生成 |
| `Assets/Resource/Scripts/Editor/OxygenSystemValidation.cs` | 显式调用的运行检查工具，不会在导入时自动运行 |
| `Assets/Resource/Prefabs/OxygenTank.prefab` | 可复制到关卡中的氧气瓶预制体 |
| `Assets/Resource/Art/Oxygen/OxygenTank.png` | 氧气瓶贴图 |
| `Assets/Resource/Art/Oxygen/OxygenFill.png` | 氧气条填充贴图 |
| `Assets/Resource/Art/Oxygen/JetpackParticle.png`、`JetpackParticle.mat` | 喷气与拖尾资源 |

上述 Unity 资源均配有 `.meta` 文件。另修改了：

- `Assets/Resource/Scripts/PlayerController.cs`：公开游戏状态、朝向和预览入口；由原有物理更新调度冲刺，死亡或禁用时取消冲刺。
- `Assets/Resource/Scripts/LevelIntroUI.cs`：预览时暂停玩家玩法，正式开始时通知玩家开始游戏。
- `Assets/Scenes/Stage1.unity`、`Assets/Scenes/Stage2.unity`：保存玩家组件、UI、喷口和关卡氧气瓶。

死亡仍使用原有红光、场景过渡和关卡重开流程。场景重载会重新生成满氧玩家和氧气瓶。

## Inspector 参数

| 组件 | 参数 | 默认值 / 含义 |
| --- | --- | --- |
| PlayerOxygen | `maxOxygen` | 100 |
| PlayerOxygen | `drainPerSecond` | 2，每秒自然消耗 |
| PlayerOxygen | `dashCost` | 10，每次冲刺消耗 |
| PlayerJetpack | `dashSpeed` | 18 |
| PlayerJetpack | `dashDuration` | 0.18 秒 |
| PlayerJetpack | `dashCooldown` | 0.5 秒，从上次冲刺结束起计算，暂停期间不计时 |
| PlayerJetpack | `gravityMultiplier` | 0.1，冲刺期间重力倍率 |
| PlayerJetpack | `collisionSkin` | 0.01，固体前安全距离 |
| PlayerJetpack | `keyboardDashKey` | Left Shift |
| PlayerJetpack | `stickDeadzone` | 0.2 |
| PlayerJetpack | `allowKeyboardDirection` | 开启，允许 WASD / 方向键指定冲刺方向 |
| OxygenHUD | `label` | O2 |
| OxygenHUD | `normalColor` / `lowOxygenColor` | 青色 / 红色，均可自定义 |
| OxygenHUD | `lowOxygenThreshold` | 0.25，严格低于最大氧气的 25% 时报警 |
| OxygenHUD | `blinkFrequency` | 2，每秒闪烁次数；设为 0 时保持红色 |
| OxygenHUD | `minBlinkAlpha` | 0.3 |
| JetpackEffects | `enableTrail` | 关闭；开启后冲刺时产生拖尾 |
| JetpackEffects | `positionBehindDash` | 开启，喷口随冲刺方向移动到玩家后方 |
| JetpackEffects | `nozzleDistance` / `nozzleLocalOffset` | 脚本默认 0.35 / (0, 0.05)；安装工具按玩家碰撞体计算，已配置场景约为 1.1595 / (0.1247, 2.3460) |
| OxygenTank | `restoreAmount` | 30 |
| OxygenTank | `bobAmplitude` | 0.12，视觉子物体的上下浮动幅度 |
| OxygenTank | `bobFrequency` | 1.2，每秒浮动次数 |
| OxygenTank | `pickupDuration` | 0.18 秒；设为 0 时立即消失 |
| OxygenTank | `pickupPopScale` | 1.25，消失前短暂放大倍率 |

粒子寿命、速度、大小、颜色、发射量以及拖尾宽度和持续时间，也可直接在 `JetpackNozzle` 的 Particle System / Trail Renderer 中调整。

## 输入与方向

项目现有脚本使用 Unity Input System。冲刺沿用该方式，扫描 Input System 中的 `DualShockGamepad`，读取 `touchpadButton`。左摇杆有输入时使用摇杆方向；无方向输入时使用玩家当前朝向。输入向量先按玩家本地坐标计算，再通过玩家 Transform 转成物理运动方向。世界旋转不会把关卡的左右误当作玩家的左右。

键盘按 Left Shift 冲刺；可同时按 WASD 或方向键指定方向，包括斜向。键盘方向仅用于冲刺方向，不改变已有普通移动方式。摇杆超过死区时优先使用摇杆方向。

Steam Input 可能只向 Unity 暴露虚拟 Xbox 手柄，此时 Unity 无法直接读取被隐藏的 DualShock 触摸板。可在当前游戏的 Steam Input 布局中，把“触摸板按下”映射为键盘 Left Shift；若修改了 `keyboardDashKey`，映射需同步。原有世界旋转配置继续沿用。实体 DualShock / DualSense 的识别情况与 Steam 布局需要在实际设备上检查。

## 场景设置与扩展

Stage1、Stage2 已绑定以下引用：

- 玩家上的 `PlayerJetpack` 指向原 `PlayerController`、`Rigidbody2D`、`PlayerOxygen` 和 `JetpackEffects`。
- `JetpackEffects` 指向玩家子物体 `JetpackNozzle` 及其 Particle System、AudioSource、Trail Renderer。
- `OxygenHUD` 指向本场景玩家的 `PlayerOxygen`、横向填充 Image 和数值 Text。UI 在屏幕 Canvas 左下角，不放在旋转世界下。
- `OxygenTank` 位于关卡旋转根节点下，视觉子物体负责浮动，触发器负责拾取。

新增关卡时，退出 Play 模式，打开包含 `PlayerController` 和 `WorldRotator` 的关卡，再执行 `Tools > Gravity Game > Install Oxygen System in Current Scene`，最后保存场景。菜单会添加缺失的玩家组件、HUD 和喷口；关卡没有氧气瓶时会放置一个示例。安装后应检查瓶子位置，避免落在墙内或尖刺上。

需要更多氧气瓶时，将 `OxygenTank.prefab` 拖入 `WorldRoot`，或复制已有瓶子，然后调整位置与 `restoreAmount`。不要把瓶子移到不随关卡旋转的根节点下。更换玩家实例时，通过 `OxygenHUD.Bind(newPlayerOxygen)` 重新绑定 UI。

喷射音效尚未提供素材。将 AudioClip 拖入 `JetpackNozzle` 的 AudioSource 的 Audio Resource / Clip 字段即可；保留 Play On Awake 关闭，冲刺组件会负责播放和停止。可在玩家 `JetpackEffects` 中勾选 `enableTrail` 开启拖尾。需要固定喷口位置时，关闭 `positionBehindDash` 后手动摆放喷口。

## 对外接口

- `AddOxygen(float amount)`：增加氧气并限制在最大值以内；忽略无效、非正数及死亡后的补氧。
- `TryConsume(float amount)`：返回扣除是否成功；预览、暂停、死亡、无效数量或余额不足时失败且不扣除。
- `OxygenChanged`：`Action<float, float>`，依次传递当前值、最大值。UI 订阅事件并在绑定时读取当前值，不逐帧查找玩家。
- `CurrentOxygen`、`MaxOxygen`、`DashCost`、`NormalizedOxygen`：供其他系统读取。

恰好用完剩余氧气也会进入死亡流程，因此最后一次扣氧达到零时不会继续启动冲刺。

## 自测步骤

1. **预览与暂停**：分别打开 Stage1、Stage2 进入 Play。预览时等待数秒，氧气应保持 100，冲刺和瓶子拾取应无效。正式开始后观察读条；使用现有暂停功能暂停，氧气和冲刺冷却不应继续计时。
2. **自然消耗与红闪**：在安全位置正式开始，默认每秒减少 2。低于 25 时读条变红并闪烁。可临时在 Inspector 提高自然消耗加快检查，退出 Play 后确认保存的参数仍是预期值。补回 25% 以上应恢复正常色。
3. **冲刺与扣费**：按 Left Shift，应出现短时冲刺和反方向喷气，每次额外扣除 10 氧气；自然消耗仍同时发生。松开方向再冲刺应朝当前面朝方向。冲刺结束后 0.5 秒内再次按键应无效，不额外扣氧。剩余氧气低于 10 时冲刺应失败。
4. **手柄输入**：连接 DS4 / DualSense，在 Game 视图聚焦后按下触摸板。分别测试摇杆静止、水平、竖直和斜向。若 Steam 只暴露虚拟 Xbox 手柄，按上文配置触摸板到 Left Shift 后再测。
5. **地面、墙壁与旋转**：贴地水平冲刺应正常移动，朝墙冲刺应在墙前停止，薄墙与 Tilemap 边缘不能穿过。旋转关卡后再测试方向，冲刺方向应符合玩家本地坐标。UI 始终固定在屏幕左下角；冲刺结束、暂停或死亡后重力应恢复，喷气停止发射。
6. **氧气瓶**：先消耗氧气再接触瓶子，应回复 30 且不超过 100；瓶子短暂放大后缩小消失，同一瓶不能重复领取。检查待机浮动及随关卡旋转的效果。
7. **耗尽与重开**：在安全位置让氧气自然降至 0，检查与尖刺相同的红光死亡、场景过渡、关卡重开和预览流程；重开后氧气应回满、瓶子重新出现，预览等待期间不继续耗氧。
8. **可选表现**：勾选 `enableTrail` 后检查冲刺拖尾；添加测试 AudioClip 后检查仅在冲刺期间播放。确认普通行走、跳跃、世界旋转及原有尖刺死亡仍正常。

编辑器检查入口 `Resource.Scripts.Editor.OxygenSystemValidation.RunImmediateChecks()` 要求在 Play 模式并暂停 Editor 后显式调用。它会临时操作状态和物理进行检查，不能代替真实按键、完整死亡重开及视觉验收。
