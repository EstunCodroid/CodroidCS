using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Codroid;

/// <summary>
/// CRI 实时控制下发器：以 UDP 周期向控制器（默认端口 9030）发送 <see cref="CommandPacketLength"/>
/// 字节的 <c>CommandData</c> 包。
/// <para>
/// 字段布局（小端，固定 64 字节）：
/// <code>
/// Int64    timestamp;         // [0..7]   保留 0
/// Float64  position[6];       // [8..55]  关节 / 末端目标位置
/// UInt8    type;              // [56]     0=关节, 1=末端
/// UInt8    nc[7];             // [57..63] 保留
/// </code>
/// </para>
/// <para>
/// <b>单位</b>：SDK 对外约定 mm/deg；UDP 线上与 CRI 实时数据流对齐为 m/rad，故默认在发送前做 deg→rad、mm→m 转换。
/// 若实测固件用 mm/deg，构造时将 <c>convertToSi=false</c> 即可。
/// </para>
/// </summary>
public sealed class CriRealtimeDispatcher : IDisposable
{
    /// <summary>控制器 CRI 实时控制端口（UDP）。</summary>
    public const int DefaultControllerUdpPort = 9030;

    /// <summary>命令包固定长度。</summary>
    public const int CommandPacketLength = 64;

    private const int TypeOffset = 56;
    private const int PositionOffset = 8;

    private readonly UdpClient _udp;
    private readonly IPEndPoint _target;
    private readonly bool _convertToSi;
    private int _disposed;
    private bool _firstFrameDebug = true;
    private bool _firstSendDebug = true;

    /// <summary>
    /// 创建下发器。
    /// </summary>
    /// <param name="controllerIp">控制器 IP。</param>
    /// <param name="controllerUdpPort">控制器 UDP 端口，默认 <see cref="DefaultControllerUdpPort"/>。</param>
    /// <param name="convertToSi">是否在发送前做 deg→rad、mm→m 转换；默认 <c>true</c>，与 CRI 实时数据流单位一致。</param>
    /// <exception cref="ArgumentException"><paramref name="controllerIp"/> 为空或不是合法 IP 字符串。</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="controllerUdpPort"/> 不在 1~65535。</exception>
    /// <remarks>
    /// 构造后可重复调用 <see cref="SendCommand"/> 或 <see cref="SendTrajectory"/>；使用完毕后请调用 <see cref="Dispose"/> 释放 UDP 套接字。
    /// </remarks>
    public CriRealtimeDispatcher(string controllerIp, int controllerUdpPort = DefaultControllerUdpPort, bool convertToSi = true)
    {
        if (string.IsNullOrWhiteSpace(controllerIp)) throw new ArgumentException("controllerIp 不能为空。", nameof(controllerIp));
        if (controllerUdpPort is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(controllerUdpPort));
        if (!IPAddress.TryParse(controllerIp, out var ip)) throw new ArgumentException("controllerIp 必须是 IPv4/IPv6 字符串。", nameof(controllerIp));

        _udp = new UdpClient();
        _target = new IPEndPoint(ip, controllerUdpPort);
        _convertToSi = convertToSi;
    }

    /// <summary>
    /// 单帧下发。<paramref name="position6"/> 必须为 6 元素。
    /// </summary>
    /// <param name="position6">
    /// 六维目标位置。<paramref name="space"/> 为 <see cref="TrajectorySpace.Joint"/> 时单位为度；
    /// 为 <see cref="TrajectorySpace.Cartesian"/> 时为 [x,y,z,rx,ry,rz]，单位 mm + deg。
    /// </param>
    /// <param name="space">目标空间类型，决定命令包 type 字段及单位换算规则。</param>
    /// <param name="ct">取消令牌；取消时不再发送并抛出 <see cref="OperationCanceledException"/>。</param>
    /// <returns>表示 UDP 帧发送完成的任务。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="position6"/> 为 null。</exception>
    /// <exception cref="ArgumentException"><paramref name="position6"/> 不是 6 元素。</exception>
    /// <exception cref="ObjectDisposedException">下发器已释放。</exception>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> 已取消。</exception>
    /// <exception cref="SocketException">底层 UDP 发送失败。</exception>
    public async Task SendCommand(IReadOnlyList<double> position6, TrajectorySpace space, CancellationToken ct = default)
    {
        EnsureNotDisposed();
        if (position6 == null) throw new ArgumentNullException(nameof(position6));
        if (position6.Count != 6) throw new ArgumentException("position6 必须为 6 元素。", nameof(position6));
        ct.ThrowIfCancellationRequested();

        var buffer = new byte[CommandPacketLength];
        for (int i = 0; i < 6; i++)
        {
            double v = position6[i];
            if (_convertToSi) v = ToSi(v, i, space);
            WriteDoubleLittleEndian(buffer, PositionOffset + i * 8, v);
        }
        buffer[TypeOffset] = (byte)(space == TrajectorySpace.Joint ? 0 : 1);

        // 首帧调试：打印 hex dump
        if (_firstFrameDebug)
        {
            _firstFrameDebug = false;
            var hex = BitConverter.ToString(buffer, 0, Math.Min(64, buffer.Length));
            Console.WriteLine($"  [CRI-DEBUG] 首帧 hex dump (64 bytes): {hex}");
            Console.WriteLine($"  [CRI-DEBUG] type byte[56]={buffer[56]} → {(space == TrajectorySpace.Joint ? "关节" : "末端")}");
            Console.WriteLine($"  [CRI-DEBUG] target={_target}");
            Console.WriteLine($"  [CRI-DEBUG] convertToSi={_convertToSi}");
        }

#if NET462
        int sent = _udp.Send(buffer, CommandPacketLength, _target);
        if (_firstSendDebug)
        {
            _firstSendDebug = false;
            Console.WriteLine($"  [CRI-DEBUG] UdpClient.Send returned: {sent} bytes (expected {CommandPacketLength})");
        }
        await Task.CompletedTask;
#else
        int sent = await _udp.SendAsync(buffer.AsMemory(0, CommandPacketLength), _target, ct).ConfigureAwait(false);
        if (_firstSendDebug)
        {
            _firstSendDebug = false;
            Console.WriteLine($"  [CRI-DEBUG] UdpClient.SendAsync returned: {sent} bytes (expected {CommandPacketLength})");
        }
#endif
    }

    /// <summary>
    /// 按固定周期下发整条轨迹。<paramref name="periodMs"/> 应与
    /// <c>CodroidClient.StartCriControl(durationMs:)</c> 保持一致。
    /// </summary>
    /// <param name="trajectory">按时间顺序排列的轨迹点序列，通常由 <see cref="TrajectoryGenerator.Generate"/> 生成。</param>
    /// <param name="space">目标空间类型，必须与轨迹点坐标语义一致。</param>
    /// <param name="periodMs">UDP 下发周期（毫秒），应等于 `StartCriControl(durationMs: ...)` 的 duration。</param>
    /// <param name="ct">取消令牌；取消时停止后续帧下发。</param>
    /// <returns>表示整条轨迹下发完成的任务。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="trajectory"/> 为 null。</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="periodMs"/> 不在 (0, 1000]。</exception>
    /// <exception cref="ArgumentException">轨迹中某个点的 <see cref="TrajectoryPoint.Position"/> 不是 6 元素。</exception>
    /// <exception cref="ObjectDisposedException">下发器已释放。</exception>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> 已取消。</exception>
    /// <exception cref="SocketException">底层 UDP 发送失败。</exception>
    /// <remarks>
    /// 第一帧立即发送，之后每个周期滴答发一帧；如某帧计算/发送耗时超过周期，
    /// 后续帧会立即追发以追平节奏。
    /// </remarks>
    public async Task SendTrajectory(
        IEnumerable<TrajectoryPoint> trajectory,
        TrajectorySpace space,
        int periodMs,
        CancellationToken ct = default)
    {
        EnsureNotDisposed();
        if (trajectory == null) throw new ArgumentNullException(nameof(trajectory));
        if (periodMs is <= 0 or > 1000) throw new ArgumentOutOfRangeException(nameof(periodMs), "periodMs 必须在 (0, 1000] ms。");

#if NET462
        await SendTrajectoryNet462(trajectory, space, periodMs, ct).ConfigureAwait(false);
#else
        // 使用 Stopwatch + SpinWait 精确计时，避免 PeriodicTimer 在 4ms 周期下
        // 受系统时钟分辨率（~15.6ms）影响导致 UDP 帧发送周期不稳定。
        var stopwatch = Stopwatch.StartNew();
        long ticksPerPeriod = (long)(Stopwatch.Frequency * periodMs / 1000.0);
        long nextTick = stopwatch.ElapsedTicks;
        var spinWait = new SpinWait();

        foreach (var point in trajectory)
        {
            ct.ThrowIfCancellationRequested();

            // 等待到当前周期的目标时刻
            while (stopwatch.ElapsedTicks < nextTick)
                spinWait.SpinOnce();

            await SendCommand(point.Position, space, ct).ConfigureAwait(false);
            nextTick += ticksPerPeriod;
        }

        // 等待一个周期，确保最后一帧已到达控制器后再返回，避免调用方过早 StopCriControl
        await Task.Delay(periodMs, ct).ConfigureAwait(false);
#endif
    }

#if NET462
    private async Task SendTrajectoryNet462(
        IEnumerable<TrajectoryPoint> trajectory,
        TrajectorySpace space,
        int periodMs,
        CancellationToken ct)
    {
        if (periodMs != 4)
        {
            Trace.TraceWarning(
                $"[Codroidsdk][net462] CRI SendTrajectory periodMs={periodMs} is outside the default 250Hz SLA. " +
                "The default supported period is 4ms. Higher-frequency or custom-period control requires on-site validation.");
        }

        double periodMsActual = periodMs;
        var stopwatch = Stopwatch.StartNew();
        long ticksPerPeriod = (long)(Stopwatch.Frequency * periodMsActual / 1000.0);
        long nextTick = stopwatch.ElapsedTicks;

        int frameCount = 0;
        double sumPeriodMs = 0;
        double maxPeriodMs = 0;
        int overruns = 0;          // 超过 6ms
        int consecutiveMiss = 0;   // 连续丢周期
        int maxConsecutiveMiss = 0;
        int udpExceptions = 0;

        long prevTick = stopwatch.ElapsedTicks;

        foreach (var point in trajectory)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                await SendCommand(point.Position, space, ct).ConfigureAwait(false);
            }
            catch (SocketException)
            {
                udpExceptions++;
                throw;
            }

            frameCount++;

            long now = stopwatch.ElapsedTicks;
            double actualPeriodMs = (now - prevTick) * 1000.0 / Stopwatch.Frequency;
            prevTick = now;

            if (frameCount > 1)
            {
                sumPeriodMs += actualPeriodMs;
                if (actualPeriodMs > maxPeriodMs) maxPeriodMs = actualPeriodMs;
                if (actualPeriodMs > 6.0)
                {
                    overruns++;
                    consecutiveMiss++;
                    if (consecutiveMiss > maxConsecutiveMiss) maxConsecutiveMiss = consecutiveMiss;
                }
                else
                {
                    consecutiveMiss = 0;
                }
            }

            nextTick += ticksPerPeriod;
            long remainingTicks = nextTick - stopwatch.ElapsedTicks;

            if (remainingTicks > 0)
            {
                // 使用 Stopwatch + SpinWait 精确等待，避免 Thread.Sleep(1) 在默认
                // 系统时钟分辨率（~15.6ms）下实际睡眠过长，导致控制器缓冲区耗尽。
                var spinWait = new SpinWait();
                while (stopwatch.ElapsedTicks < nextTick)
                {
                    spinWait.SpinOnce();
                }
            }
        }

        // 等待一个周期，确保最后一帧已到达控制器后再返回
        Thread.Sleep(periodMs);

        double avgPeriodMs = frameCount > 1 ? sumPeriodMs / (frameCount - 1) : 0;
        double elapsedSec = stopwatch.ElapsedMilliseconds / 1000.0;

        Trace.TraceInformation(
            $"[Codroidsdk][net462] CRI SendTrajectory statistics:\n" +
            $"  Duration: {elapsedSec:F2}s\n" +
            $"  Frames sent: {frameCount}\n" +
            $"  Average period: {avgPeriodMs:F3}ms\n" +
            $"  Max period: {maxPeriodMs:F3}ms\n" +
            $"  Overruns (>6ms): {overruns}\n" +
            $"  Max consecutive overruns: {maxConsecutiveMiss}\n" +
            $"  UDP exceptions: {udpExceptions}");
    }
#endif

    private static double ToSi(double value, int index, TrajectorySpace space)
    {
        const double Deg2Rad = Math.PI / 180.0;
        const double Mm2M = 1e-3;
        if (space == TrajectorySpace.Joint)
            return value * Deg2Rad;
        return index < 3 ? value * Mm2M : value * Deg2Rad;
    }

    private static void WriteDoubleLittleEndian(byte[] buffer, int offset, double value)
    {
#if NET462
        byte[] bytes = BitConverter.GetBytes(value);
        if (!BitConverter.IsLittleEndian) Array.Reverse(bytes);
        Array.Copy(bytes, 0, buffer, offset, 8);
#else
        BinaryPrimitives.WriteDoubleLittleEndian(buffer.AsSpan(offset, 8), value);
#endif
    }

    private void EnsureNotDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
            throw new ObjectDisposedException(nameof(CriRealtimeDispatcher));
    }

    /// <summary>关闭底层 UDP 套接字。</summary>
    /// <remarks><see cref="SendCommand"/> / <see cref="SendTrajectory"/> 在此之后调用会抛 <see cref="ObjectDisposedException"/>。</remarks>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _udp.Dispose();
    }
}
