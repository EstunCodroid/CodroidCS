# Codroid C# SDK 版本说明

## v2.1.12（2026-08-21）

### 变更

- **机器人设置接口适配新固件 API**（协议 2.2.9）：
  - `GetRobotParameters`：优先新接口 `Robot/getTools` + `Robot/getCoordinates`，404 时自动回退到旧接口 `Robot/GetRobotParameter`（协议 19.7）。注意：新接口不返回 Payload 数据。
  - `SetDefaultToolId`：优先 `Robot/setDefaultTool`，404 回退 `Robot/SaveRobotParameter`
  - `SetDefaultUserCoordinateId`：优先 `Robot/setDefaultCoordinate`，404 回退 `Robot/SaveRobotParameter`
  - `SaveToolFrames`：优先 `Robot/setTools`（db 含 `defaultToolId` + `Tool` 数组），404 回退
  - `SaveUserCoordinateFrames`：优先 `Robot/setCoordinates`（db 含 `defaultCoordinateId` + `Coordinate` 数组），404 回退
  - `SetDefaultPayloadId`：改为转发至 `SetPayload`（`Robot/setPayload`，新旧固件均支持）
- **`SavePayloadFrames` / `SetPayloadFrame` 标记 `[Obsolete]`**：新固件已移除 `Robot/SaveRobotParameter` 的 Payload 写入能力，请使用 `SetPayload(payloadId)` 代替
- **`RobotFrame` 新增字段**：`Name`（工具名称）、`XOffset`/`YOffset`/`ZOffset`（工具偏置，mm），与本地文档 2.2.9 对齐
- **JSON 序列化修复**：`RobotFrame` / `RobotPayloadFrame` 属性添加 `[JsonPropertyName]` 小写映射，修复控制器 `type must be number` 错误

### 兼容性

- 新旧固件均可使用，SDK 自动探测接口可用性（404 回退），调用方无需修改代码

---

## v2.1.11（2026-07-13）

### 新增

- **力控接口更新**：新增 `ZeroForceCalibration`、`InitForceControl`、`StartForceControl`、`StopForceControl`、`TuneForceParams`、`StartContactDetection`、`SetOverforceProtection`、`SetForceDataHealth`、`GetForceState` 及单字段状态 getter
- **力控测试工程**：新增 `examples/ForceControlTest`，同时覆盖 `net462`、`net6.0` 与 `net8.0`

### Breaking Change

- **移除 `FTSensorDriftCalibration`**：新协议使用 `ZeroForceCalibration(calibrationTimeMs)`
- **`InitForceControl` 固定导纳**：当前固定下发 `algo=1`，不开放算法参数

---

## v2.1.10（2026-06-03）

### Breaking Change

- **`MotionWaitOptions` 容差属性彻底移除**：`JointToleranceDeg`、`CartesianPositionToleranceMm`、`CartesianOrientationToleranceDeg` 已删除，旧代码引用会编译报错

---

## v2.1.8（2026-06-03）

### 新增

- **`CposToCposPose` / `CposToCposDouble`**（`Robot/cpostocpos`）：笛卡尔坐标系/工具系换算；`cp` 为 `CartesianPoint`，`coor1`/`tool1`/`coor2`/`tool2` 为必填 `double[6]`

### Breaking Change

- **`*Sync` 阻塞运动完成判定逻辑简化**：仅依据 CRI `InMotion` 标志（曾运动 + 连续 `SettledSamples` 次停稳），**不再**比对关节角或 TCP 与目标点误差
  - 碰撞、急停、报警仍会抛 `InvalidOperationException`（这些场景会触发 `has_alarm` / `emergency_stop`）
  - 外部 `StopRobotMove` 打断视为正常结束
  - 移除「运动已停止，但未到达目标点」异常
- **`MotionWaitOptions` 容差属性废弃**：`JointToleranceDeg`、`CartesianPositionToleranceMm`、`CartesianOrientationToleranceDeg` 标记 `[Obsolete]`，不再生效；仅 `Timeout`、`PollInterval`、`CriStaleTimeout`、`SettledSamples` 仍有效

---

## v2.1.7（2026-05-30）

### 改进

- **`Data` 属性标记废弃**：标记 `[Obsolete]`，提示使用 `CriData`（返回线程安全深拷贝）
- **`AposToCpos` / `AposToCposPose` 参数改可选**：`userFrame`/`toolFrame` 默认 `null`，不传时指令中不包含 `coor`/`tool` 字段，与 Python 行为对齐
- **`CposToApos` / `CposToAposJoints` 参数改可选**：`referenceJointDegrees` 默认 `null`，优先使用当前关节角度（`CriData.JointPosition`），CRI 未启动时兜底 `[20,20,20,20,20,20]`

---

## v2.1.6（2026-05-30）

### 改进

- **`Disconnect` 自动停止 CRI 数据推送**：之前需要手动调用 `StopCriDataPush` 再断开，否则控制器会残留脏状态。现在 `Disconnect` 会自动尝试停止 CRI 推送（失败不阻塞断开）

---

## v2.1.5（2026-05-30）

### Bug Fixes

- **修复阻塞运动欧拉角到达判定**：`180°` 和 `-180°` 是同一姿态，但之前直接算差值 `|180-(-180)|=360°`，导致判定永远不通过。现在归一化到 `[-180, 180]` 后再比较

### 测试

- 三个测试项目（Net462/Net6/Net8）新增 `blend` 和 `relativeBlend` 演示步骤

---

## v2.1.4（2026-05-30）

### Breaking Change

- **`blend` 参数类型变更**：`double` → `double?`，默认值从 `25` 改为 `null`
  - 之前不传 `blend` 会自动应用 25mm 平滑过渡，现在不传表示**无过渡**
  - 如需保持旧行为，请显式传入 `blend: 25`
- **`relativeBlend` 参数类型变更**：`double?`（默认 `null`）
  - 之前不传会使用默认值，现在不传表示**不使用相对平滑**
  - 如需保持旧行为，请显式传入 `relativeBlend: 0`
- **`blend` 与 `relativeBlend` 互斥**：同时传入时 `relativeBlend` 无效
- **`coor` / `tool` 语义明确**：`null` 表示指令中不包含该字段（非"使用默认坐标系"）

### 涉及方法

- `MoveInstruction` 工厂方法：`MovJ`、`MovL`、`MovC`、`MovCircle`
- `CodroidClient`：`MovJ`、`MovL`、`MovC`、`MovCircle` 及其 `*Sync` 变体（共 12 个方法）

### 文档

- 所有 API 文档参数表同步更新，补充 `blend`/`relativeBlend` 互斥说明和 `coor`/`tool` 的 null 语义
- SDK 手册版本号升级至 v2.1.4

---

## v2.1.3（2026-05-29）

### 修复

- 修复 `AsyncTcpClient.cs` 中 `InvokePublishHandlers` 参数 `ty` 的空引用警告
- 修复 `MotionPoints.cs` 中 `ValidateSix` 方法 `values` 的空引用警告
- 修复 `CodroidIo.cs` 中 `ParseStringZeroOne` 方法 `s` 的空引用警告

---

## v2.1.2（2026-05-29）

### 新增

- **阻塞式运动 API**（Sync Motion）：
  - `MoveSync`、`MovJSync`（JointPoint / CartesianPoint）、`MovLSync`（CartesianPoint / JointPoint）、`MovCSync`、`MovCircleSync`
  - `MotionWaitOptions` 类：可配置超时、轮询间隔、CRI 过期判定、稳定采样数、关节/笛卡尔容差
  - **⚠️ 前置条件**：使用 `*Sync` 方法前必须先调用 `StartCriDataPush` 启动 CRI 数据推送
- **`StopMoveTo()`**：发送 `type=-1` 停止 MoveTo 运动
- **`WaitForCriData(timeout)`**：阻塞等待首帧 CRI 数据到达
- `MoveToType.Stop = -1` 枚举值
- **机器人设置 API（协议 19.x）**：
  - `GetRobotParameters`、`SetCollisionSensitivity`、`SetPayload`
  - `SetDefaultPayloadId`、`SetDefaultToolId`、`SetDefaultUserCoordinateId`
  - `SetToolFrame`、`SetPayloadFrame`、`SetUserCoordinateFrame`
  - `SaveToolFrames`、`SavePayloadFrames`、`SaveUserCoordinateFrames`
- **运动 API 强类型**：
  - `JointPoint`、`CartesianPoint`、`MoveInstruction` 及工厂方法
  - `MovePoint`、`MoveToTarget`
- **.NET Framework 4.6.2 支持**
- 新增示例：阻塞运动演示、机器人设置演示

### 依赖

- 控制器固件 **≥ 2.3.2.10**

---

## v2.1.1（2026-05-21）

### 功能

- 基础 TCP JSON 指令通道
- IO 接口：`GetDi`、`GetDo`、`GetAi`、`GetAo`、`SetDo`、`SetAo`
- 寄存器接口：`GetRegisterValue`、`SetRegisterValue`、`SetExtendArrayType`、`RemoveExtendArray`
- 运动控制：`MovJ`、`MovL`、`MovC`、`MovCircle`、`Move`、`PauseRobotMotion`、`ResumeRobotMotion`、`StopRobotMove`
- Jog：`StartJog`、`StopJog`、`JogHeartbeat`
- MoveTo：`MoveTo`、`MoveToHeartbeat`
- 工程/脚本：`RunScript`、`Run`、`RunByIndex`、`RunStep`、`PauseProject`、`ResumeProject`、`StopProject`
- 全局变量：`GetGlobalVars`、`SaveGlobalVar`、`SaveGlobalVars`、`RemoveGlobalVars`
- 运动学：`AposToCpos`、`CposToApos`、`CalculateRelativePose`
- CRI 实时数据：`StartCriDataPush`、`StopCriDataPush`、`StartCriControl`、`StopCriControl`
- Publish/Subscribe：`SubscribePublishTopic`
