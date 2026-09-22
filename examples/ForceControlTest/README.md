# C# 力控测试

测试工程同时面向 `net462`、`net6.0`、`net8.0`。在 `CodroidCS` 目录运行：

```bash
dotnet run --project examples/ForceControlTest/ForceControlTest.csproj -f net8.0 -- 192.168.1.136 state
dotnet run --project examples/ForceControlTest/ForceControlTest.csproj -f net6.0 -- 192.168.1.136 state
```

测试模式：

| mode | 说明 |
|---|---|
| `state` | 只读 `GetForceState()` 和单字段 getter |
| `calibration` | 执行 `ZeroForceCalibration()` |
| `safety` | 设置过力保护和力数据健康监控参数 |
| `compliance` | 进入柔顺力控，轮询状态后停止 |
| `constant` | Z 向恒力，在线调参后停止 |
| `contact` | 接触检测，必须追加 `--allow-motion` |

接触检测示例：

```bash
dotnet run --project examples/ForceControlTest/ForceControlTest.csproj -f net8.0 -- 192.168.1.136 contact --allow-motion
```

注意：

- 当前 `InitForceControl()` 固定导纳 `algo=1`，不开放算法参数。
- `net462` 目标用于 Windows .NET Framework 4.6.2+，Linux / macOS 请运行 `net6.0` 或 `net8.0`。
