# Stage1 摆板验证入口

所有物理验证需先打开 Stage1 并进入 Play。它们只克隆真实碰撞几何到独立 PhysicsScene2D，不保存关卡改动。

- `Tools > Gravity Game > Physics > Validate Stage1 Pendulums`：两块板顶 × 首转方向 × 模拟 60/240 Hz 调度，共 8 个用例。
- `Tools > Gravity Game > Physics > Validate Stage1 Side And Far Wall`：两块板侧、合法房间内侧远墙（玩家历史旋转中心）、同一远墙（固定初始 WorldRoot 原点旋转中心），共 4 种放置 × 模拟 60/240 Hz 调度 = 8 个用例。
- `Tools > Gravity Game > Physics > Start Stage1 Real Frame Validation`：真实 Unity PlayerLoop，先设置 60 FPS 上限，再设置不限帧；每种配置运行上述 6 种放置。JSON 同时记录实际 Update 次数、时间和平均帧率。可用对应的 `Stop` 菜单中断并恢复源场景组件、物理开关、时间倍率和帧率设置。
- Test Runner 的 `GravityGame.Tests.PlayMode.PendulumStagePlayModeTests`：通过反射调用 Editor 验证入口，严格断言 8 + 8 个同步用例。失败会以 NUnit 失败报告，不依赖控制台文本。

每例仅初始化一次玩家，随后连续顺时针 360°、逆时针 360°，各重复 10 次；不会每圈重置或把玩家重新放回平台。根据真实角度抵达目标才开始下一段。每半圈目标设有 120 秒 watchdog，避免停滞被误记为通过。碰撞覆盖数也必须大于零。

验证要求实际刚体铰链距离小于 0.01、板长轴方向误差小于 0.1°、每步剩余穿透为零、无超过 0.0001 的实际 Distance 穿透、原侧穿越为零。修复后的速度和位置由生产 PlayerController / CrushGuard 计算。场景碰撞层未加入 Guard mask 仍会被物理重叠审核发现，不会通过过滤层隐藏。

模拟 60/240 Hz 的同步入口不是实际渲染 FPS 证据。真实帧率入口记录的平均 FPS 可能低于配置上限，必须按 JSON 数据报告。它验证生产 FixedUpdate、同 Stage1 几何、受控旋转目标与旋转中心；玩家输入、自动移动以及落地探针路径不在本组覆盖范围。玩家历史旋转中心用例覆盖空中历史队列分支，未布置 groundCheck。固定 WorldRoot 原点属于测试夹具压力配置，不会写回 Stage1。

结果保存在本目录 `Pendulum-*.json`。旧 `Pendulum-additional-quick.json` 保留了发现初始放置错误的诊断：Tilemap 使用 Outline Composite，玩家可能完全位于实心瓦片区而不触碰轮廓，所以仅 `Distance` 无法验证起点。修正后的夹具要求玩家整个初始 bounds 没有占用实际可碰撞瓦片，并仅选择朝向房间内部的边侧。旧 `Pendulum-additional-valid-quick.json` 是中间版本，仍选到了外轮廓外侧；不能将这两份报告作为合法房间起点的验收证据。

本文件说明测试方法，不声称最新实现已通过。以最终运行生成的 JSON 和 NUnit 结果为准。
