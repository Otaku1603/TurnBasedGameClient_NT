# TurnBasedGameClient_NT

本项目是回合制在线 PvP 对战游戏的 Unity 客户端，配套服务端 `TurnBasedGameServer_NT`。由早期客户端 `TurnBasedGameClient` 重构而来：玩法 / UI / 资产沿用，网络层整体重写为 WebSocket + Protobuf v2，工程结构改为 asmdef 分层 + DOTS 战斗模拟。旧脚本保留在 `Assets/_Legacy~`（Unity 不导入），仅作移植参考。

## 技术栈

- **引擎**：Unity 2022.3.62f3（Built-in Render Pipeline）
- **语言**：C#
- **依赖注入**：VContainer
- **战斗模拟**：Unity DOTS（Entities 1.3.15 + Burst）
- **表现**：GameObject + UGUI + DOTween（Hybrid）
- **网络**：`System.Net.WebSockets` + Google.Protobuf（协议 v2）
- **资源**：Addressables（目标：业务层不再用 `Resources.Load`）
- **REST**：UnityWebRequest + Newtonsoft.Json

## 架构

采用按程序集（asmdef）划分的分层结构，依赖方向单向向上：

```
Game.Core        纯 C#，不引用 UnityEngine（协议模型、状态、工具）
  ▲
Game.Sim         DOTS 战斗模拟世界（BattleSimWorld + Burst ISystem）
  ▲
Game.Presentation 表现层（动画、HUD、场景物体）
  ▲
Game.App         组合根（VContainer 容器、入口、生命周期）

旁路：Game.Network（WS/REST/事件总线）、Game.UI、Game.Proto（protoc 生成）、Game.Tests.*
```

- **服务端权威**：客户端不做伤害 / 命中预判，盘面以服务端 `BATTLE_UPDATE` 为唯一事实；`Game.Sim` 仅做表现层的最小重演。
- **DOTS 战斗**：`BattleSimWorld` 承载回合门、倒计时等 ISystem；战斗 HUD 只读取 Sim 状态，技能按钮由 `ownedSkillIds` 减去 `cooldowns` 键集合得出可用技能。

## 核心模块

- **`Game.Network`**：
  - `WsTransport`：WebSocket 收发（二进制帧，帧内直接 `GameMessage.Parser.ParseFrom`）。
  - `GameConnection`：信封封装（`version=2` + 自增 `requestId`）、请求-响应关联、推送分发。
  - `PushEventBus`：把 `requestId=0` 的推送按 `type` 投递给订阅方。
  - `ConnectionStateMachine`：连接 / 认证 / 匹配 / 对战状态流转与自动重连。
  - `RateLimiter`：客户端发送节流（与服务端限额对齐）。
  - `RestClient`：REST 调用（登录、配置字典、商城、邮件、社交等）。
- **`Game.App` / `Game.Presentation` / `Game.UI`**：组合根与大厅 / 战斗 / MORE 面板等界面与表现。
- **`Game.Sim`**：DOTS 战斗世界与系统。
- **`Game.Proto`**：由服务端 `game.proto` 生成的 C# 协议类（勿手改）。

## 核心流程

1. REST `POST /api/auth/login` 取 JWT；
2. 建立 WS 连接，发送 `LOGIN{token}`，服务端绑定会话；
3. `MATCH_REQUEST` → 等 `MATCH_SUCCESS` 推送；
4. `BATTLE_READY` → `BATTLE_START`；
5. 按 `currentActorUserId` 发 `BATTLE_ACTION`（USE_SKILL / DEFEND / USE_ITEM），每步收 `BATTLE_UPDATE`；
6. `BATTLE_END` 结算；胜者奖励经系统邮件下发；
7. 断线重连后发 `BATTLE_REJOIN` 恢复盘面。

动画分派只用 `ActionType` 枚举 + `skillId` 查表，不用技能名字符串判断。

## 协议代码生成

```cmd
tools\gen_proto.cmd
```

源 = 服务端 `src/main/proto/game.proto`（`csharp_namespace = GameClient.Proto`），产物 = `Assets/Plugins/Proto/Gen/Game.cs`。改协议后需重新生成，不要手改产物。

## 启动指南

1. 用 Unity 2022.3.62f3 打开本工程。
2. 在登录界面的 DevSettings 中填写服务器地址（存 PlayerPrefs），或修改 `Game.Network/NetworkConfig.cs` 默认值。
3. 打开登录场景并运行；注册 / 登录后进入大厅，可匹配对战。
4. 需要本地联调时，先按服务端 `README` 启动服务端集群。

无头编译与 EditMode 测试：

```bash
Unity.exe -batchmode -nographics -quit -accept-apiupdate -projectPath <proj> -logFile <log>
# 追加 -runTests -testPlatform EditMode -testResults <xml> 可跑 EditMode（部分用例需集群在线）
```

## 已知简化

- MORE 面板中的任务 / 公会目前为 IMGUI 的实用展示，正式 UI 待补。
- `Game.Sim` 的部分战斗系统为表现层最小实现。
- 内存表现需真机 Profiler 验证，EditMode 不能替代。

## 许可证

MIT，见 [LICENSE](LICENSE)。

