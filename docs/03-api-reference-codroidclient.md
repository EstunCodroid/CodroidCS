# CodroidClient API Reference / CodroidClient API 参考

**Class / 类:** `CodroidClient`
**Namespace / 命名空间:** `Codroid`
**Source / 源文件:** `CodroidSDK/Codroid.cs`

---

## Constructor / 构造函数

```csharp
public CodroidClient(string ip)
```

| Parameter | Type | Description / 说明 |
|-----------|------|-------------------|
| `ip` | `string` | Controller IP address / 控制器 IP 地址 |

TCP port is fixed at **9001**.

TCP 端口固定为 **9001**。

```csharp
var robot = new CodroidClient("192.168.8.136");
```

---

## Properties / 属性

| Property | Type | Description / 说明 |
|----------|------|-------------------|
| `CriData` | `CriRealTimeData` | Thread-safe clone of CRI data snapshot / CRI 数据快照的线程安全副本 |
| `Data` | `CriRealTimeData` | Direct reference to internal CRI buffer (faster, not thread-safe) / 内部 CRI 缓冲区直接引用（更快，非线程安全） |

```csharp
// Thread-safe (returns clone) / 线程安全（返回副本）
double[] joints = robot.CriData.JointPosition;

// Direct reference (faster) / 直接引用（更快）
double[] joints2 = robot.Data.JointPosition;
```

---

## Event / 事件

```csharp
public event Action<CriRealTimeData>? CriDataReceived
```

Fires after each valid CRI UDP frame is parsed. The parameter is a thread-safe clone.

每当解析完一个有效的 CRI UDP 帧后触发。参数是线程安全的副本。

```csharp
robot.CriDataReceived += data =>
{
    Console.WriteLine($"Joints: {string.Join(", ", data.JointPosition)}");
    Console.WriteLine($"TCP: {string.Join(", ", data.TcpPose)}");
    Console.WriteLine($"InMotion: {data.InMotion}");
};
```

---

## 1. Connection Management / 连接管理

### Connect

```csharp
public async Task Connect()
```

**EN:** Establishes TCP connection to the controller.
**ZH:** 建立与控制器的 TCP 连接。

```csharp
await robot.Connect();
```

---

### ConnectRemoteAndSwitchOn

```csharp
public async Task ConnectRemoteAndSwitchOn()
```

**EN:** Connects TCP, enters remote mode via auto, then powers on. This is the recommended one-call setup.
**ZH:** 连接 TCP、经 auto 切换远程模式、然后上电。推荐的一键初始化方法。

**Standard connection pattern / 标准连接写法：**

```csharp
await robot.ConnectRemoteAndSwitchOn();
await robot.StartCriDataPush("192.168.8.150", 18888);
await robot.WaitForCriData(5.0);
```

---

### Disconnect

```csharp
public void Disconnect()
```

**EN:** Stops CRI UDP listener and disconnects TCP. Always call in `finally`.
**ZH:** 停止 CRI UDP 监听并断开 TCP。始终在 `finally` 中调用。

```csharp
try
{
    // Standard connection pattern / 标准连接写法
    await robot.ConnectRemoteAndSwitchOn();
    await robot.StartCriDataPush("192.168.8.150", 18888);
    await robot.WaitForCriData(5.0);

    // ... operations ...
}
finally
{
    robot.Disconnect();
}
```

---

## 2. Mode Switching / 模式切换

### SwitchOn / SwitchOff

```csharp
public async Task<CommonResponse> SwitchOn()
public async Task<CommonResponse> SwitchOff()
```

**EN:** Power on / power off the robot.
**ZH:** 机器人上电 / 下电。

| Method | Protocol / 协议指令 |
|--------|-------------------|
| `SwitchOn()` | `Robot/switchOn` |
| `SwitchOff()` | `Robot/switchOff` |

```csharp
await robot.SwitchOn();
// ... operations ...
await robot.SwitchOff();
```

---

### ToManual / ToAuto / ToRemote

```csharp
public Task<CommonResponse> ToManual()
public Task<CommonResponse> ToAuto()
public Task<CommonResponse> ToRemote()
```

**EN:** Switch to manual / auto / remote mode. Requires firmware 2.3.2.6+.
**ZH:** 切换到手动 / 自动 / 远程模式。需要固件 2.3.2.6+。

| Method | Protocol |
|--------|----------|
| `ToManual()` | `Robot/toManual` |
| `ToAuto()` | `Robot/toAuto` |
| `ToRemote()` | `Robot/toRemote` |

---

### EnterManualModeViaAuto / EnterRemoteModeViaAuto

```csharp
public async Task<CommonResponse> EnterManualModeViaAuto()
public async Task<CommonResponse> EnterRemoteModeViaAuto()
```

**EN:** Switches to auto first, then to manual / remote. Satisfies the controller's "must go through auto" restriction.
**ZH:** 先切换到自动，再切换到手动/远程。满足控制器"必须经过自动模式"的限制。

```csharp
await robot.EnterRemoteModeViaAuto();
```

---

### ToSimulation / ToActual

```csharp
public Task<CommonResponse> ToSimulation()
public Task<CommonResponse> ToActual()
```

**EN:** Switch to simulation / real-machine mode.
**ZH:** 切换到仿真 / 实机模式。

---

### StartDrag / StopDrag

```csharp
public Task<CommonResponse> StartDrag()
public Task<CommonResponse> StopDrag()
```

**EN:** Enter / exit drag mode. Requires firmware 2.3.2.6+.
**ZH:** 进入 / 退出拖拽模式。需要固件 2.3.2.6+。

---

### ClearSystemError

```csharp
public Task<CommonResponse> ClearSystemError()
```

**EN:** Clears the system error state.
**ZH:** 清除系统错误状态。

| Method | Protocol |
|--------|----------|
| `ClearSystemError()` | `System/clearError` |

---

## 3. Motion Commands (Non-Blocking) / 运动指令（非阻塞）

All motion methods send the command and return immediately. Use `*Sync` variants for blocking wait.

所有运动方法发送指令后立即返回。使用 `*Sync` 变体进行阻塞等待。

### MovJ — Joint Move / 关节运动

```csharp
public Task<CommonResponse> MovJ(JointPoint target, double speed, double acc,
    double? blend = null, double[]? coor = null, double[]? tool = null, double? relativeBlend = null)

public Task<CommonResponse> MovJ(CartesianPoint target, double speed, double acc,
    double? blend = null, double[]? coor = null, double[]? tool = null, double? relativeBlend = null)
```

| Parameter | Type | Default | Description / 说明 |
|-----------|------|---------|-------------------|
| `target` | `JointPoint` / `CartesianPoint` | — | Target position / 目标位置 |
| `speed` | `double` | — | Speed / 速度 |
| `acc` | `double` | — | Acceleration / 加速度 |
| `blend` | `double?` | null | Blend radius. Mutually exclusive with `relativeBlend` — if both set, `relativeBlend` is ignored. Omit for no transition / 平滑半径。与 `relativeBlend` 互斥——同时传入时 `relativeBlend` 无效。不传表示无过渡 |
| `coor` | `double[]?` | null | User coordinate frame. `null` = omitted from command / 用户坐标系。`null` 时指令中不包含该字段 |
| `tool` | `double[]?` | null | Tool coordinate frame. `null` = omitted from command / 工具坐标系。`null` 时指令中不包含该字段 |
| `relativeBlend` | `double?` | null | Relative blend (0–100). Mutually exclusive with `blend` — if both set, this is ignored / 相对平滑比（0–100）。与 `blend` 互斥——同时传入时此参数无效 |

```csharp
// Joint target / 关节目标
await robot.MovJ(JointPoint.Degrees(new[] { 0, 0, 90, 0, 90, 0 }), speed: 40, acc: 100);

// Cartesian target (joint motion to TCP pose) / 笛卡尔目标（关节运动到 TCP 位姿）
await robot.MovJ(CartesianPoint.MmDeg(new[] { 400, 0, 300, 180, 0, 0 }), speed: 40, acc: 100);
```

---

### MovL — Linear Move / 直线运动

```csharp
public Task<CommonResponse> MovL(CartesianPoint target, double speed, double acc,
    double? blend = null, double[]? coor = null, double[]? tool = null, double? relativeBlend = null)

public Task<CommonResponse> MovL(JointPoint target, double speed, double acc,
    double? blend = null, double[]? coor = null, double[]? tool = null, double? relativeBlend = null)
```

| Parameter | Type | Default | Description / 说明 |
|-----------|------|---------|-------------------|
| `target` | `CartesianPoint` / `JointPoint` | — | Target position / 目标位置 |
| `speed` | `double` | — | Speed (mm/s for linear) / 速度（直线 mm/s） |
| `acc` | `double` | — | Acceleration / 加速度 |
| `blend` | `double?` | null | Blend radius (mm). Mutually exclusive with `relativeBlend` — if both set, `relativeBlend` is ignored. Omit for no transition / 平滑半径（mm）。与 `relativeBlend` 互斥——同时传入时 `relativeBlend` 无效。不传表示无过渡 |
| `coor` | `double[]?` | null | User coordinate frame (6 elements). `null` = omitted from command / 用户坐标系（6 个元素）。`null` 时指令中不包含该字段 |
| `tool` | `double[]?` | null | Tool coordinate frame (6 elements). `null` = omitted from command / 工具坐标系（6 个元素）。`null` 时指令中不包含该字段 |
| `relativeBlend` | `double?` | null | Relative blend ratio (0–100). Mutually exclusive with `blend` — if both set, this is ignored / 相对平滑比（0–100）。与 `blend` 互斥——同时传入时此参数无效 |

```csharp
// Cartesian linear move / 笛卡尔直线运动
await robot.MovL(CartesianPoint.MmDegWithRef(pose, robot.CriData.JointPosition),
    speed: 150, acc: 500);

// Linear move to joint target / 直线运动到关节目标
await robot.MovL(JointPoint.Degrees(new[] { 10, 20, 90, 0, 90, 0 }), speed: 100, acc: 300);
```

---

### MovC — Circular Move / 圆弧运动

```csharp
public Task<CommonResponse> MovC(CartesianPoint middle, CartesianPoint target,
    double speed, double acc, double? blend = null,
    double[]? coor = null, double[]? tool = null, double? relativeBlend = null)
```

| Parameter | Type | Default | Description / 说明 |
|-----------|------|---------|-------------------|
| `middle` | `CartesianPoint` | — | Intermediate point (on arc) / 中间点（圆弧上） |
| `target` | `CartesianPoint` | — | End point / 终点 |
| `speed` | `double` | — | Speed (mm/s) / 速度（mm/s） |
| `acc` | `double` | — | Acceleration / 加速度 |
| `blend` | `double?` | null | Blend radius (mm). Mutually exclusive with `relativeBlend` — if both set, `relativeBlend` is ignored. Omit for no transition / 平滑半径（mm）。与 `relativeBlend` 互斥——同时传入时 `relativeBlend` 无效。不传表示无过渡 |
| `coor` | `double[]?` | null | User coordinate frame. `null` = omitted from command / 用户坐标系。`null` 时指令中不包含该字段 |
| `tool` | `double[]?` | null | Tool coordinate frame. `null` = omitted from command / 工具坐标系。`null` 时指令中不包含该字段 |
| `relativeBlend` | `double?` | null | Relative blend ratio (0–100). Mutually exclusive with `blend` — if both set, this is ignored / 相对平滑比（0–100）。与 `blend` 互斥——同时传入时此参数无效 |

```csharp
await robot.MovC(
    CartesianPoint.MmDeg(new[] { 450, 100, 300, 180, 0, 0 }),
    CartesianPoint.MmDeg(new[] { 500, 0, 300, 180, 0, 0 }),
    speed: 100, acc: 300);
```

---

### MovCircle — Full Circle Move / 整圆运动

```csharp
public Task<CommonResponse> MovCircle(CartesianPoint middle, CartesianPoint target,
    int circleNum, double speed, double acc, double? blend = null,
    double[]? coor = null, double[]? tool = null, double? relativeBlend = null)
```

| Parameter | Type | Default | Description / 说明 |
|-----------|------|---------|-------------------|
| `middle` | `CartesianPoint` | — | Intermediate point / 中间点 |
| `target` | `CartesianPoint` | — | End point / 终点 |
| `circleNum` | `int` | — | Number of full circles / 整圆圈数 |
| `speed` | `double` | — | Speed (mm/s) / 速度（mm/s） |
| `acc` | `double` | — | Acceleration / 加速度 |
| `blend` | `double?` | null | Blend radius (mm). Mutually exclusive with `relativeBlend` — if both set, `relativeBlend` is ignored. Omit for no transition / 平滑半径（mm）。与 `relativeBlend` 互斥——同时传入时 `relativeBlend` 无效。不传表示无过渡 |
| `coor` | `double[]?` | null | User coordinate frame. `null` = omitted from command / 用户坐标系。`null` 时指令中不包含该字段 |
| `tool` | `double[]?` | null | Tool coordinate frame. `null` = omitted from command / 工具坐标系。`null` 时指令中不包含该字段 |
| `relativeBlend` | `double?` | null | Relative blend ratio (0–100). Mutually exclusive with `blend` — if both set, this is ignored / 相对平滑比（0–100）。与 `blend` 互斥——同时传入时此参数无效 |

```csharp
await robot.MovCircle(
    CartesianPoint.MmDeg(mid),
    CartesianPoint.MmDeg(end),
    circleNum: 1, speed: 80, acc: 200);
```

---

### Move — Multi-Segment Path / 多段路径

```csharp
public async Task<CommonResponse> Move(IReadOnlyList<MoveInstruction> instructions)
```

**EN:** Sends a list of motion instructions as a single path command.
**ZH:** 将一组运动指令作为单条路径命令发送。

| Parameter | Type | Description / 说明 |
|-----------|------|-------------------|
| `instructions` | `IReadOnlyList<MoveInstruction>` | List of move instructions to execute as a path / 作为路径执行的运动指令列表 |

```csharp
await robot.Move(new[]
{
    MoveInstruction.MovJ(JointPoint.Degrees(new[] { 0, 0, 90, 0, 90, 0 }), 40, 100),
    MoveInstruction.MovL(CartesianPoint.MmDegWithRef(pose, robot.CriData.JointPosition), 150, 500),
    MoveInstruction.MovC(CartesianPoint.MmDeg(mid), CartesianPoint.MmDeg(end), 100, 300),
});
```

---

## 4. Blocking Motion Commands / 阻塞运动指令

`*Sync` methods send the motion command, then **block until CRI confirms the robot has reached the target**. They return `true` on success, or throw on error/timeout.

`*Sync` 方法发送运动指令后**阻塞直到 CRI 确认机器人到达目标**。成功返回 `true`，错误/超时抛出异常。

**Prerequisite / 前置条件:** `StartCriDataPush` must be active.

**必须先调用** `StartCriDataPush`。

### MovJSync

```csharp
public bool MovJSync(JointPoint target, double speed, double acc,
    MotionWaitOptions? wait = null, double? blend = null,
    double[]? coor = null, double[]? tool = null, double? relativeBlend = null)

public bool MovJSync(CartesianPoint target, double speed, double acc,
    MotionWaitOptions? wait = null, double? blend = null,
    double[]? coor = null, double[]? tool = null, double? relativeBlend = null)
```

| Parameter | Type | Default | Description / 说明 |
|-----------|------|---------|-------------------|
| `target` | `JointPoint` / `CartesianPoint` | — | Target position / 目标位置 |
| `speed` | `double` | — | Speed (deg/s for joint) / 速度（关节 deg/s） |
| `acc` | `double` | — | Acceleration / 加速度 |
| `wait` | `MotionWaitOptions?` | null | Wait options (timeout, tolerance, etc.) / 等待选项（超时、容差等） |
| `blend` | `double?` | null | Blend radius (mm). Mutually exclusive with `relativeBlend` — if both set, `relativeBlend` is ignored. Omit for no transition / 平滑半径（mm）。与 `relativeBlend` 互斥——同时传入时 `relativeBlend` 无效。不传表示无过渡 |
| `coor` | `double[]?` | null | User coordinate frame. `null` = omitted from command / 用户坐标系。`null` 时指令中不包含该字段 |
| `tool` | `double[]?` | null | Tool coordinate frame. `null` = omitted from command / 工具坐标系。`null` 时指令中不包含该字段 |
| `relativeBlend` | `double?` | null | Relative blend ratio (0–100). Mutually exclusive with `blend` — if both set, this is ignored / 相对平滑比（0–100）。与 `blend` 互斥——同时传入时此参数无效 |

```csharp
var wait = new MotionWaitOptions
{
    Timeout = TimeSpan.FromSeconds(90),
};

robot.MovJSync(JointPoint.Degrees(new[] { 0, 0, 90, 0, 90, 0 }), 40, 100, wait);
```

---

### MovLSync

```csharp
public bool MovLSync(CartesianPoint target, double speed, double acc,
    MotionWaitOptions? wait = null, double? blend = null,
    double[]? coor = null, double[]? tool = null, double? relativeBlend = null)

public bool MovLSync(JointPoint target, double speed, double acc,
    MotionWaitOptions? wait = null, double? blend = null,
    double[]? coor = null, double[]? tool = null, double? relativeBlend = null)
```

| Parameter | Type | Default | Description / 说明 |
|-----------|------|---------|-------------------|
| `target` | `CartesianPoint` / `JointPoint` | — | Target position / 目标位置 |
| `speed` | `double` | — | Speed (mm/s for linear) / 速度（直线 mm/s） |
| `acc` | `double` | — | Acceleration / 加速度 |
| `wait` | `MotionWaitOptions?` | null | Wait options (timeout, tolerance, etc.) / 等待选项（超时、容差等） |
| `blend` | `double?` | null | Blend radius (mm). Mutually exclusive with `relativeBlend` — if both set, `relativeBlend` is ignored. Omit for no transition / 平滑半径（mm）。与 `relativeBlend` 互斥——同时传入时 `relativeBlend` 无效。不传表示无过渡 |
| `coor` | `double[]?` | null | User coordinate frame. `null` = omitted from command / 用户坐标系。`null` 时指令中不包含该字段 |
| `tool` | `double[]?` | null | Tool coordinate frame. `null` = omitted from command / 工具坐标系。`null` 时指令中不包含该字段 |
| `relativeBlend` | `double?` | null | Relative blend ratio (0–100). Mutually exclusive with `blend` — if both set, this is ignored / 相对平滑比（0–100）。与 `blend` 互斥——同时传入时此参数无效 |

```csharp
robot.MovLSync(
    CartesianPoint.MmDegWithRef(pose, robot.CriData.JointPosition),
    speed: 150, acc: 500,
    wait: new MotionWaitOptions
    {
        Timeout = TimeSpan.FromSeconds(60),
    });
```

---

### MovCSync

```csharp
public bool MovCSync(CartesianPoint middle, CartesianPoint target,
    double speed, double acc, MotionWaitOptions? wait = null,
    double? blend = null, double[]? coor = null, double[]? tool = null, double? relativeBlend = null)
```

| Parameter | Type | Default | Description / 说明 |
|-----------|------|---------|-------------------|
| `middle` | `CartesianPoint` | — | Intermediate point (on arc) / 中间点（圆弧上） |
| `target` | `CartesianPoint` | — | End point / 终点 |
| `speed` | `double` | — | Speed (mm/s) / 速度（mm/s） |
| `acc` | `double` | — | Acceleration / 加速度 |
| `wait` | `MotionWaitOptions?` | null | Wait options / 等待选项 |
| `blend` | `double?` | null | Blend radius (mm). Mutually exclusive with `relativeBlend` — if both set, `relativeBlend` is ignored. Omit for no transition / 平滑半径（mm）。与 `relativeBlend` 互斥——同时传入时 `relativeBlend` 无效。不传表示无过渡 |
| `coor` | `double[]?` | null | User coordinate frame. `null` = omitted from command / 用户坐标系。`null` 时指令中不包含该字段 |
| `tool` | `double[]?` | null | Tool coordinate frame. `null` = omitted from command / 工具坐标系。`null` 时指令中不包含该字段 |
| `relativeBlend` | `double?` | null | Relative blend ratio (0–100). Mutually exclusive with `blend` — if both set, this is ignored / 相对平滑比（0–100）。与 `blend` 互斥——同时传入时此参数无效 |

```csharp
robot.MovCSync(
    CartesianPoint.MmDeg(mid), CartesianPoint.MmDeg(end),
    speed: 100, acc: 300);
```

---

### MovCircleSync

```csharp
public bool MovCircleSync(CartesianPoint middle, CartesianPoint target,
    int circleNum, double speed, double acc, MotionWaitOptions? wait = null,
    double? blend = null, double[]? coor = null, double[]? tool = null, double? relativeBlend = null)
```

| Parameter | Type | Default | Description / 说明 |
|-----------|------|---------|-------------------|
| `middle` | `CartesianPoint` | — | Intermediate point / 中间点 |
| `target` | `CartesianPoint` | — | End point / 终点 |
| `circleNum` | `int` | — | Number of full circles / 整圆圈数 |
| `speed` | `double` | — | Speed (mm/s) / 速度（mm/s） |
| `acc` | `double` | — | Acceleration / 加速度 |
| `wait` | `MotionWaitOptions?` | null | Wait options / 等待选项 |
| `blend` | `double?` | null | Blend radius (mm). Mutually exclusive with `relativeBlend` — if both set, `relativeBlend` is ignored. Omit for no transition / 平滑半径（mm）。与 `relativeBlend` 互斥——同时传入时 `relativeBlend` 无效。不传表示无过渡 |
| `coor` | `double[]?` | null | User coordinate frame. `null` = omitted from command / 用户坐标系。`null` 时指令中不包含该字段 |
| `tool` | `double[]?` | null | Tool coordinate frame. `null` = omitted from command / 工具坐标系。`null` 时指令中不包含该字段 |
| `relativeBlend` | `double?` | null | Relative blend ratio (0–100). Mutually exclusive with `blend` — if both set, this is ignored / 相对平滑比（0–100）。与 `blend` 互斥——同时传入时此参数无效 |

---

### MoveSync

```csharp
public bool MoveSync(IReadOnlyList<MoveInstruction> instructions, MotionWaitOptions? wait = null)
```

**EN:** Sends multi-segment path and blocks until the last segment target is reached.
**ZH:** 发送多段路径并阻塞直到最后一段目标到达。

| Parameter | Type | Default | Description / 说明 |
|-----------|------|---------|-------------------|
| `instructions` | `IReadOnlyList<MoveInstruction>` | — | List of move instructions / 运动指令列表 |
| `wait` | `MotionWaitOptions?` | null | Wait options (timeout, tolerance, etc.) / 等待选项（超时、容差等） |

```csharp
robot.MoveSync(new[]
{
    MoveInstruction.MovJ(JointPoint.Degrees(j1), 40, 100),
    MoveInstruction.MovL(CartesianPoint.MmDegWithRef(p2, refJ), 150, 500),
});
```

---

## 5. Motion Control / 运动控制

### PauseRobotMotion

```csharp
public async Task<CommonResponse> PauseRobotMotion()
```

**EN:** Pauses the current motion.
**ZH:** 暂停当前运动。

| Method | Protocol |
|--------|----------|
| `PauseRobotMotion()` | `Robot/pause` |

---

### ResumeRobotMotion

```csharp
public async Task<CommonResponse> ResumeRobotMotion()
```

**EN:** Resumes paused motion.
**ZH:** 恢复暂停的运动。

| Method | Protocol |
|--------|----------|
| `ResumeRobotMotion()` | `Robot/resume` |

---

### StopRobotMove

```csharp
public async Task<CommonResponse> StopRobotMove()
```

**EN:** Stops the current motion immediately.
**ZH:** 立即停止当前运动。

| Method | Protocol |
|--------|----------|
| `StopRobotMove()` | `Robot/stopMove` |

---

## 6. MoveTo Commands / MoveTo 指令

### MoveTo

```csharp
public async Task<CommonResponse> MoveTo(MoveToKind kind, MoveToTarget? target = null)
```

**EN:** Moves to a preset or planned position. Requires heartbeat while running.
**ZH:** 移动到预设或规划位置。运行期间需要心跳。

| Method | Protocol |
|--------|----------|
| `MoveTo(kind, target)` | `Robot/moveTo` |

```csharp
// Move to home position / 移动到原点
await robot.MoveTo(MoveToKind.Home);

// Move to safe position / 移动到安全位
await robot.MoveTo(MoveToKind.Safe);

// Move to specific joint position / 移动到指定关节位置
await robot.MoveTo(MoveToKind.JointPlanned, MoveToTarget.Joint(JointPoint.Degrees(joints)));
```

---

### MoveToHeartbeat

```csharp
public async Task<CommonResponse> MoveToHeartbeat()
```

**EN:** Sends heartbeat to maintain MoveTo motion. Call at ~500ms intervals.
**ZH:** 发送心跳以维持 MoveTo 运动。约每 500ms 调用一次。

| Method | Protocol |
|--------|----------|
| `MoveToHeartbeat()` | `Robot/moveToHeartbeat` |

---

### StopMoveTo

```csharp
public async Task<CommonResponse> StopMoveTo()
```

**EN:** Stops the current MoveTo / RunTo motion.
**ZH:** 停止当前 MoveTo / RunTo 运动。

| Method | Protocol |
|--------|----------|
| `StopMoveTo()` | `Robot/moveTo` (type=-1) |

---

## 7. Jog Commands / Jog 指令

### StartJog

```csharp
public async Task<CommonResponse> StartJog(RobotJogParameters parameters)
```

**EN:** Starts jogging. Requires heartbeat at ~500ms intervals.
**ZH:** 启动 Jog。需要约每 500ms 发送心跳。

| Method | Protocol |
|--------|----------|
| `StartJog(parameters)` | `Robot/jog` |

```csharp
var jogParams = RobotJogParameters.Create(
    RobotJogMode.Joint,   // Joint mode / 关节模式
    speed: 10,             // Speed / 速度
    index: 0,              // Axis index (0-5) / 轴索引 (0-5)
    RobotJogFrameType.User, // User frame / 用户坐标系
    coorId: 0              // Coordinate ID / 坐标系 ID
);

await robot.StartJog(jogParams);

// Keep sending heartbeat / 持续发送心跳
while (jogging)
{
    await Task.Delay(500);
    await robot.JogHeartbeat();
}

await robot.StopJog();
```

---

### StopJog

```csharp
public async Task<CommonResponse> StopJog()
```

**EN:** Stops jogging.
**ZH:** 停止 Jog。

| Method | Protocol |
|--------|----------|
| `StopJog()` | `Robot/stopJog` |

---

### JogHeartbeat

```csharp
public async Task<CommonResponse> JogHeartbeat()
```

**EN:** Sends heartbeat to maintain jog state. Call at ~500ms intervals.
**ZH:** 发送心跳以维持 Jog 状态。约每 500ms 调用一次。

| Method | Protocol |
|--------|----------|
| `JogHeartbeat()` | `Robot/jogHeartbeat` |

---

## 8. IO Operations / IO 操作

### GetDi / GetDo / GetAi / GetAo

```csharp
public async Task<int> GetDi(int port)      // Read DI / 读取数字输入
public async Task<int> GetDo(int port)      // Read DO / 读取数字输出
public async Task<double> GetAi(int port)   // Read AI / 读取模拟输入
public async Task<double> GetAo(int port)   // Read AO / 读取模拟输出
```

| Method | Returns | Protocol |
|--------|---------|----------|
| `GetDi(port)` | `int` (0 or 1) | `IOManager/GetIOValue` |
| `GetDo(port)` | `int` (0 or 1) | `IOManager/GetIOValue` |
| `GetAi(port)` | `double` | `IOManager/GetIOValue` |
| `GetAo(port)` | `double` | `IOManager/GetIOValue` |

```csharp
int di0 = await robot.GetDi(0);
Console.WriteLine($"DI 0 = {di0}");

double ai1 = await robot.GetAi(1);
Console.WriteLine($"AI 1 = {ai1:F3}");
```

---

### SetDo / SetAo

```csharp
public async Task<CommonResponse> SetDo(int port, int value)     // Write DO (0 or 1)
public async Task<CommonResponse> SetAo(int port, double value)  // Write AO
```

| Method | Protocol |
|--------|----------|
| `SetDo(port, value)` | `IOManager/SetIOValue` |
| `SetAo(port, value)` | `IOManager/SetIOValue` |

```csharp
await robot.SetDo(10, 1);   // Set DO 10 high / 设置 DO 10 为高
await robot.SetDo(10, 0);   // Set DO 10 low / 设置 DO 10 为低
await robot.SetAo(0, 3.14); // Set AO 0 to 3.14
```

---

### GetIoValues (Batch Read / 批量读取)

```csharp
public async Task<CommonResponse> GetIoValues(IReadOnlyList<(string Type, int Port)> pins)
```

```csharp
var pins = new (string Type, int Port)[]
{
    ("DI", 0), ("DI", 1), ("DO", 10), ("AI", 0)
};

CommonResponse resp = await robot.GetIoValues(pins);
// Results in resp.db / 结果在 resp.db 中
```

---

## 9. Register Operations / 寄存器操作

### GetRegisterValue

```csharp
public async Task<RegisterReadValue> GetRegisterValue(int address)
```

```csharp
RegisterReadValue reg = await robot.GetRegisterValue(49100);
int intVal = reg.GetInt32();
double dblVal = reg.GetDouble();
```

---

### GetRegisterValues (Batch Read / 批量读取)

```csharp
public async Task<IReadOnlyList<RegisterReadValue>> GetRegisterValues(IReadOnlyList<int> addresses)
```

```csharp
var addresses = new[] { 49100, 49101, 49102 };
IReadOnlyList<RegisterReadValue> values = await robot.GetRegisterValues(addresses);

for (int i = 0; i < values.Count; i++)
    Console.WriteLine($"Register {addresses[i]} = {values[i].GetInt32()}");
```

---

### SetRegisterValue

```csharp
public async Task<CommonResponse> SetRegisterValue(int address, int value)
public async Task<CommonResponse> SetRegisterValue(int address, double value)
```

```csharp
await robot.SetRegisterValue(49100, 42);
await robot.SetRegisterValue(49101, 3.14);
```

---

### SetExtendArrayType / RemoveExtendArray

```csharp
public async Task<CommonResponse> SetExtendArrayType(int index, string type)
public async Task<CommonResponse> RemoveExtendArray(int index)
```

**EN:** Manage extend-array elements (index 0~999).
**ZH:** 管理扩展数组元素（索引 0~999）。

```csharp
await robot.SetExtendArrayType(0, RegisterExtendArrayValueType.Int32);
await robot.RemoveExtendArray(0);
```

---

## 10. Robot Settings (19.x Protocol) / 机器人设置（19.x 协议）

### SetManualMoveRate / SetAutoMoveRate

```csharp
public async Task<CommonResponse> SetManualMoveRate(int percent)
public async Task<CommonResponse> SetAutoMoveRate(int percent)
```

**EN:** Set manual / auto motion rate (1~100%).
**ZH:** 设置手动/自动运动倍率（1~100%）。

```csharp
await robot.SetManualMoveRate(50);  // 50% speed / 50% 速度
await robot.SetAutoMoveRate(100);   // Full speed / 全速
```

---

### SetCollisionSensitivity

```csharp
public async Task<CommonResponse> SetCollisionSensitivity(int sensitivity)
```

**EN:** Set collision detection sensitivity (0~100). Firmware 2.3.2.10+.
**ZH:** 设置碰撞检测灵敏度（0~100）。固件 2.3.2.10+。

```csharp
await robot.SetCollisionSensitivity(50);
```

---

### SetPayload

```csharp
public async Task<CommonResponse> SetPayload(int payloadId)
```

**EN:** Set active payload slot (0~15). Firmware 2.3.2.10+.
**ZH:** 设置当前载荷槽位（0~15）。固件 2.3.2.10+。

```csharp
await robot.SetPayload(1); // Use payload slot 1 / 使用载荷槽位 1
```

---

### GetRobotParameters

```csharp
public async Task<RobotParameters> GetRobotParameters()
```

**EN:** Gets all setting-interface parameters. Uses new API (`Robot/getTools` + `Robot/getCoordinates`) with automatic fallback to legacy `Robot/GetRobotParameter` (protocol 19.7) on 404. Note: new API does not return Payload data; Payload list will be empty on new firmware.
**ZH:** 获取所有设置界面参数。优先使用新接口（`Robot/getTools` + `Robot/getCoordinates`），404 时自动回退到旧接口 `Robot/GetRobotParameter`（协议 19.7）。注意：新接口不返回 Payload 数据，新固件上 Payload 列表为空。

```csharp
RobotParameters param = await robot.GetRobotParameters();
Console.WriteLine($"Default Tool: {param.DefaultToolId}");
Console.WriteLine($"Default Payload: {param.DefaultPayloadId}");
Console.WriteLine($"Max Payload: {param.MaxPayload} kg");
```

---

### SetDefaultPayloadId / SetDefaultToolId / SetDefaultUserCoordinateId

```csharp
public Task<CommonResponse> SetDefaultPayloadId(int payloadId)     // 0~15
public Task<CommonResponse> SetDefaultToolId(int toolId)            // 0~15
public Task<CommonResponse> SetDefaultUserCoordinateId(int coordinateId) // 0~15
```

**EN:** Set default payload / tool / user coordinate frame slot.
- `SetDefaultPayloadId` forwards to `SetPayload` (`Robot/setPayload`, available on both old and new firmware).
- `SetDefaultToolId` uses new API `Robot/setDefaultTool`, falls back to `Robot/SaveRobotParameter` on 404.
- `SetDefaultUserCoordinateId` uses new API `Robot/setDefaultCoordinate`, falls back to `Robot/SaveRobotParameter` on 404.

**ZH:** 设置默认载荷/工具/用户坐标系槽位。
- `SetDefaultPayloadId` 转发至 `SetPayload`（`Robot/setPayload`，新旧固件均支持）。
- `SetDefaultToolId` 优先新接口 `Robot/setDefaultTool`，404 时回退到 `Robot/SaveRobotParameter`。
- `SetDefaultUserCoordinateId` 优先新接口 `Robot/setDefaultCoordinate`，404 时回退到 `Robot/SaveRobotParameter`。

```csharp
await robot.SetDefaultToolId(2);
await robot.SetDefaultPayloadId(1);
await robot.SetDefaultUserCoordinateId(0);
```

---

### SaveToolFrames / SetToolFrame

```csharp
public Task<CommonResponse> SaveToolFrames(IReadOnlyList<RobotFrame> frames)
public async Task<CommonResponse> SetToolFrame(int frameId, RobotFrame frame)
```

**EN:** Save the full tool frame table (must include id 0~15, id=0 must be all zeros) / Modify a single tool frame (read-then-write, id 1~15 only). Uses new API `Robot/setTools` (db includes `defaultToolId` + `Tool` array), falls back to `Robot/SaveRobotParameter` on 404.
**ZH:** 保存完整工具坐标系表（必须包含 id 0~15，id=0 必须全零）/ 修改单个工具坐标系（先读后写，仅 id 1~15）。优先新接口 `Robot/setTools`（db 含 `defaultToolId` + `Tool` 数组），404 时回退到 `Robot/SaveRobotParameter`。

```csharp
// Set single tool frame / 设置单个工具坐标系
await robot.SetToolFrame(1, new RobotFrame
{
    Id = 1, X = 0, Y = 0, Z = 100, A = 0, B = 0, C = 0
});
```

---

### SavePayloadFrames / SetPayloadFrame `[Obsolete]`

```csharp
[Obsolete("New firmware removed Payload table write via SaveRobotParameter; use SetPayload(payloadId).")]
public Task<CommonResponse> SavePayloadFrames(IReadOnlyList<RobotPayloadFrame> frames)
public async Task<CommonResponse> SetPayloadFrame(int frameId, RobotPayloadFrame frame)
```

**EN:** Save full payload frame table / Modify single payload frame (id 1~15). **Obsolete**: new firmware removed `Robot/SaveRobotParameter` Payload write capability. Use `SetPayload(payloadId)` instead.
**ZH:** 保存完整载荷坐标系表 / 修改单个载荷坐标系（id 1~15）。**已过时**：新固件已移除 `Robot/SaveRobotParameter` 的 Payload 写入能力，请使用 `SetPayload(payloadId)`。

```csharp
// Obsolete - only works on legacy firmware / 已过时 - 仅旧固件可用
await robot.SetPayloadFrame(1, new RobotPayloadFrame
{
    Id = 1, M = 2.5, Mx = 0, My = 0, Mz = 50
});
```

---

### SaveUserCoordinateFrames / SetUserCoordinateFrame

```csharp
public Task<CommonResponse> SaveUserCoordinateFrames(IReadOnlyList<RobotFrame> frames)
public async Task<CommonResponse> SetUserCoordinateFrame(int frameId, RobotFrame frame)
```

**EN:** Save full user coordinate frame table / Modify single user coordinate frame (id 1~15). Uses new API `Robot/setCoordinates` (db includes `defaultCoordinateId` + `Coordinate` array), falls back to `Robot/SaveRobotParameter` on 404.
**ZH:** 保存完整用户坐标系表 / 修改单个用户坐标系（id 1~15）。优先新接口 `Robot/setCoordinates`（db 含 `defaultCoordinateId` + `Coordinate` 数组），404 时回退到 `Robot/SaveRobotParameter`。

```csharp
await robot.SetUserCoordinateFrame(1, new RobotFrame
{
    Id = 1, X = 100, Y = 200, Z = 0, A = 0, B = 0, C = 45
});
```

---

## 11. CRI Real-Time Data / CRI 实时数据

### StartCriDataPush

```csharp
public async Task<CommonResponse> StartCriDataPush(string udpIp, int udpPort)
```

**EN:** Starts local UDP listener and requests controller to push CRI real-time data. Fixed params: 100ms period, high-precision, mask 0xFFFF, 308-byte UDP packet.
**ZH:** 启动本地 UDP 监听并请求控制器推送 CRI 实时数据。固定参数：100ms 周期、高精度、mask 0xFFFF、308 字节 UDP 包。

| Parameter | Type | Description / 说明 |
|-----------|------|-------------------|
| `udpIp` | `string` | Local IP address for UDP reception / 本地 UDP 接收 IP 地址 |
| `udpPort` | `int` | Local UDP port for receiving CRI data / 本地 UDP 端口，用于接收 CRI 数据 |

| Method | Protocol |
|--------|----------|
| `StartCriDataPush(udpIp, udpPort)` | `CRI/StartDataPush` |

```csharp
await robot.StartCriDataPush("192.168.8.150", 18888);

robot.CriDataReceived += data =>
{
    Console.WriteLine($"Joints: {string.Join(", ", data.JointPosition)}");
};
```

---

### StopCriDataPush

```csharp
public async Task<CommonResponse> StopCriDataPush(string? udpIp = null, int? udpPort = null)
```

**EN:** Requests controller to stop CRI data push and closes local UDP listener.
**ZH:** 请求控制器停止 CRI 数据推送并关闭本地 UDP 监听。

| Parameter | Type | Default | Description / 说明 |
|-----------|------|---------|-------------------|
| `udpIp` | `string?` | null | UDP IP used when starting (for protocol stop message) / 启动时使用的 UDP IP（用于协议停止消息） |
| `udpPort` | `int?` | null | UDP port used when starting / 启动时使用的 UDP 端口 |

| Method | Protocol |
|--------|----------|
| `StopCriDataPush(ip, port)` | `CRI/StopDataPush` |

```csharp
await robot.StopCriDataPush("192.168.8.150", 18888);
```

---

## 12. CRI Real-Time Control / CRI 实时控制

### StartCriControl

```csharp
public async Task<CommonResponse> StartCriControl(int filterType = 1, int durationMs = 4, int startBuffer = 5)
```

**EN:** Enables CRI real-time control mode.
**ZH:** 启用 CRI 实时控制模式。

| Parameter | Type | Default | Description / 说明 |
|-----------|------|---------|-------------------|
| `filterType` | `int` | 1 | 0=off, 1=average, 2=2nd-order LP, 3=elliptic |
| `durationMs` | `int` | 4 | Control period (1~16ms, must divide 1000) / 控制周期 |
| `startBuffer` | `int` | 5 | Start buffer frames (1~100) / 起始缓冲帧数 |

| Method | Protocol |
|--------|----------|
| `StartCriControl(...)` | `CRI/StartControl` |

```csharp
await robot.StartCriControl(filterType: 1, durationMs: 4, startBuffer: 5);
```

---

### StopCriControl

```csharp
public async Task<CommonResponse> StopCriControl()
```

**EN:** Disables CRI real-time control mode.
**ZH:** 禁用 CRI 实时控制模式。

| Method | Protocol |
|--------|----------|
| `StopCriControl()` | `CRI/StopControl` |

---

## 13. Project Execution / 项目执行

### EnterRemoteScriptMode

```csharp
public async Task<CommonResponse> EnterRemoteScriptMode()
```

**EN:** Requests entering remote script mode.
**ZH:** 请求进入远程脚本模式。

| Method | Protocol |
|--------|----------|
| `EnterRemoteScriptMode()` | `project/enterRemoteScriptMode` |

---

### RunScript

```csharp
public async Task<CommonResponse> RunScript(
    string mainScript,
    IReadOnlyDictionary<string, string>? subThreads = null,
    IReadOnlyDictionary<string, string>? subPrograms = null,
    IReadOnlyDictionary<string, string>? interrupts = null,
    IReadOnlyDictionary<string, object>? vars = null)
```

**EN:** Sends a script for immediate execution.
**ZH:** 发送脚本立即执行。

| Parameter | Type | Default | Description / 说明 |
|-----------|------|---------|-------------------|
| `mainScript` | `string` | — | Main script content to execute / 要执行的主脚本内容 |
| `subThreads` | `IReadOnlyDictionary<string, string>?` | null | Sub-thread scripts (name → code) / 子线程脚本（名称 → 代码） |
| `subPrograms` | `IReadOnlyDictionary<string, string>?` | null | Sub-program scripts (name → code) / 子程序脚本（名称 → 代码） |
| `interrupts` | `IReadOnlyDictionary<string, string>?` | null | Interrupt handler scripts (name → code) / 中断处理脚本（名称 → 代码） |
| `vars` | `IReadOnlyDictionary<string, object>?` | null | Variables to inject / 要注入的变量 |

```csharp
await robot.RunScript(mainScript: "movej(j1, v50) sub1() end");
```

---

### Run / RunByIndex / RunStep

```csharp
public async Task<CommonResponse> Run(string projectID)
public async Task<CommonResponse> RunByIndex(int index)
public async Task<CommonResponse> RunStep(string projectID)
```

**EN:** Start a project by ID / index / single-step.
**ZH:** 按 ID / 索引 启动项目 / 单步执行。

```csharp
await robot.Run("project_001");
await robot.RunByIndex(0);
await robot.RunStep("project_001");
```

---

### PauseProject / ResumeProject / StopProject

```csharp
public async Task<CommonResponse> PauseProject()
public async Task<CommonResponse> ResumeProject()
public async Task<CommonResponse> StopProject()
```

| Method | Protocol |
|--------|----------|
| `PauseProject()` | `project/pause` |
| `ResumeProject()` | `project/resume` |
| `StopProject()` | `project/stop` |

---

## 14. Publish/Subscribe / 发布订阅

### SubscribePublishTopic

```csharp
public async Task<PublishTopicSubscription> SubscribePublishTopic(
    string topicTy, Action<PublishNotification> handler, int tcMilliseconds = 100)
```

**EN:** Subscribes to a TCP topic push. Returns a disposable subscription handle.
**ZH:** 订阅 TCP 主题推送。返回可释放的订阅句柄。

| Parameter | Type | Default | Description / 说明 |
|-----------|------|---------|-------------------|
| `topicTy` | `string` | — | Topic name, e.g. `PublishTopics.RobotStatus` / 主题名，如 `PublishTopics.RobotStatus` |
| `handler` | `Action<PublishNotification>` | — | Callback for notifications / 通知回调 |
| `tcMilliseconds` | `int` | 100 | Protocol `tc` field in ms / 协议 `tc` 字段（毫秒） |

```csharp
using var sub = await robot.SubscribePublishTopic(
    PublishTopics.RobotStatus,
    notification =>
    {
        Console.WriteLine($"Topic: {notification.Ty}");
        Console.WriteLine($"Data: {notification.Db}");
    });

// Subscription active until sub.Dispose() / 订阅在 sub.Dispose() 前有效
await Task.Delay(10000);
// sub.Dispose() called by 'using' / 'using' 会自动调用 sub.Dispose()
```

---

## 15. Global Variables / 全局变量

### GetGlobalVars / GetGlobalVarsCatalog

```csharp
public async Task<CommonResponse> GetGlobalVars()
public async Task<IReadOnlyDictionary<string, GlobalVarCatalogEntry>> GetGlobalVarsCatalog()
```

```csharp
var catalog = await robot.GetGlobalVarsCatalog();
foreach (var (name, entry) in catalog)
{
    Console.WriteLine($"{name} = {entry.Value} ({entry.Remark})");
}
```

---

### SaveGlobalVar / SaveGlobalVars

```csharp
public Task<CommonResponse> SaveGlobalVar(string name, object value, string? remark = null)
public async Task<CommonResponse> SaveGlobalVars(IReadOnlyCollection<GlobalVarSaveItem> items)
```

**SaveGlobalVar parameters / SaveGlobalVar 参数：**

| Parameter | Type | Default | Description / 说明 |
|-----------|------|---------|-------------------|
| `name` | `string` | — | Variable name (validated by `GlobalVarNaming`) / 变量名（由 `GlobalVarNaming` 校验） |
| `value` | `object` | — | Value (JSON-serializable or `GlobalVarRawJson`) / 值（可 JSON 序列化或 `GlobalVarRawJson`） |
| `remark` | `string?` | null | Optional remark / 可选备注 |

**SaveGlobalVars parameters / SaveGlobalVars 参数：**

| Parameter | Type | Description / 说明 |
|-----------|------|-------------------|
| `items` | `IReadOnlyCollection<GlobalVarSaveItem>` | Collection of variables to save / 要保存的变量集合 |

```csharp
// Single / 单个
await robot.SaveGlobalVar("counter", 42, "test counter");

// Batch / 批量
await robot.SaveGlobalVars(new[]
{
    new GlobalVarSaveItem("x", 100.0, "X position"),
    new GlobalVarSaveItem("y", 200.0, "Y position"),
});
```

---

### RemoveGlobalVars

```csharp
public async Task<CommonResponse> RemoveGlobalVars(IEnumerable<string> names)
```

**EN:** Deletes specified global variables. Deleting nonexistent variables is not an error.
**ZH:** 删除指定全局变量。删除不存在的变量不会报错。

| Parameter | Type | Description / 说明 |
|-----------|------|-------------------|
| `names` | `IEnumerable<string>` | Variable names to delete / 要删除的变量名集合 |

```csharp
await robot.RemoveGlobalVars(new[] { "counter", "x", "y" });
```

---

## 16. Kinematics / 运动学

### AposToCpos / AposToCposPose (Forward Kinematics / 正运动学)

```csharp
public async Task<CommonResponse> AposToCpos(double[] jointDegrees, double[] userFrame, double[] toolFrame, double[]? externalAxisPositions = null)
public async Task<double[]> AposToCposPose(double[] jointDegrees, double[] userFrame, double[] toolFrame, double[]? externalAxisPositions = null)
```

**EN:** Forward kinematics: joint space → Cartesian space. `AposToCposPose` returns [x,y,z,rx,ry,rz] in mm+deg.
**ZH:** 正运动学：关节空间 → 笛卡尔空间。`AposToCposPose` 返回 [x,y,z,rx,ry,rz]，单位 mm+deg。

| Parameter | Type | Default | Description / 说明 |
|-----------|------|---------|-------------------|
| `jointDegrees` | `double[]` | — | 6 joint angles in degrees / 6 个关节角度（度） |
| `userFrame` | `double[]` | — | User coordinate frame [x,y,z,rx,ry,rz] (mm+deg) / 用户坐标系 [x,y,z,rx,ry,rz]（mm+deg） |
| `toolFrame` | `double[]` | — | Tool coordinate frame [x,y,z,rx,ry,rz] (mm+deg) / 工具坐标系 [x,y,z,rx,ry,rz]（mm+deg） |
| `externalAxisPositions` | `double[]?` | null | External axis positions / 外部轴位置 |

```csharp
double[] joints = { 0, 0, 90, 0, 90, 0 };
double[] userFrame = { 0, 0, 0, 0, 0, 0 };
double[] toolFrame = { 0, 0, 100, 0, 0, 0 };

double[] pose = await robot.AposToCposPose(joints, userFrame, toolFrame);
Console.WriteLine($"TCP: [{string.Join(", ", pose.Select(v => v.ToString("F1")))}]");
// TCP: [400.0, 0.0, 300.0, 180.0, 0.0, 0.0]
```

---

### CposToApos / CposToAposJoints (Inverse Kinematics / 逆运动学)

```csharp
public async Task<CommonResponse> CposToApos(double[] cartesianMmDeg, double[] referenceJointDegrees, double[]? externalAxisPositions = null)
public async Task<double[]> CposToAposJoints(double[] cartesianMmDeg, double[] referenceJointDegrees, double[]? externalAxisPositions = null)
```

**EN:** Inverse kinematics: Cartesian → joint space. `CposToAposJoints` returns 6 joint angles in degrees.
**ZH:** 逆运动学：笛卡尔 → 关节空间。`CposToAposJoints` 返回 6 个关节角度（度）。

| Parameter | Type | Default | Description / 说明 |
|-----------|------|---------|-------------------|
| `cartesianMmDeg` | `double[]` | — | TCP pose [x,y,z,rx,ry,rz] in mm+deg / TCP 位姿 [x,y,z,rx,ry,rz]，单位 mm+deg |
| `referenceJointDegrees` | `double[]` | — | Reference joints for IK solver (6 angles in deg) / IK 求解器的参考关节（6 个角度，度） |
| `externalAxisPositions` | `double[]?` | null | External axis positions / 外部轴位置 |

```csharp
double[] pose = { 400, 0, 300, 180, 0, 0 };
double[] refJoints = { 0, 0, 90, 0, 90, 0 };

double[] joints = await robot.CposToAposJoints(pose, refJoints);
Console.WriteLine($"Joints: [{string.Join(", ", joints.Select(v => v.ToString("F2")))}]");
```

---

### CalculateRelativePose / CalculateRelativePoseResult

```csharp
public async Task<CommonResponse> CalculateRelativePose(double[] tcpPoseWorld, double[] offset, RelativePoseCoorType coorType, double[]? tcpPoseInPosCoorFrame = null, double[]? userCoorFrame = null)
public async Task<double[]> CalculateRelativePoseResult(double[] tcpPoseWorld, double[] offset, RelativePoseCoorType coorType, double[]? tcpPoseInPosCoorFrame = null, double[]? userCoorFrame = null)
```

**EN:** Calculates a relative pose / offset in user or tool coordinate frame.
**ZH:** 在用户或工具坐标系中计算相对位姿/偏移。

| Parameter | Type | Description / 说明 |
|-----------|------|-------------------|
| `tcpPoseWorld` | `double[]` | Current TCP pose in world frame / 世界坐标系中的当前 TCP 位姿 |
| `offset` | `double[]` | [dx,dy,dz,drx,dry,drz] offset / 偏移量 |
| `coorType` | `RelativePoseCoorType` | User or Tool / 用户或工具坐标系 |
| `tcpPoseInPosCoorFrame` | `double[]?` | TCP pose in position coordinate frame |
| `userCoorFrame` | `double[]?` | User coordinate frame definition |

```csharp
double[] currentPose = { 400, 0, 300, 180, 0, 0 };
double[] offset = { 50, 0, 0, 0, 0, 0 }; // Move +50mm in X / X 方向偏移 +50mm

double[] newPose = await robot.CalculateRelativePoseResult(
    currentPose, offset, RelativePoseCoorType.User);

Console.WriteLine($"New pose: [{string.Join(", ", newPose)}]");
```
