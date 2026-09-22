using System;
using System.Collections.Generic;
using System.Linq;

namespace Codroid;

/// <summary>
/// 轨迹空间类型：关节空间或笛卡尔空间。
/// </summary>
public enum TrajectorySpace
{
    /// <summary>关节空间，6 维（deg）。</summary>
    Joint,
    /// <summary>笛卡尔空间，6 维 [x,y,z,rx,ry,rz]，单位 mm + deg；姿态采用固定欧拉角 XYZ（外旋）。</summary>
    Cartesian,
}

/// <summary>
/// 速度规划方式。
/// </summary>
public enum TrajectoryProfile
{
    /// <summary>三次多项式时间标度（s 形）：起止速度为 0，平滑过渡，<b>不能严格匀速</b>。</summary>
    Cubic,

    /// <summary>梯形规划：加速 → 匀速 → 减速；笛卡尔下匀速段维持目标线速度。</summary>
    Trapezoidal,
}

/// <summary>
/// 轨迹生成请求参数。
/// </summary>
public sealed class TrajectoryRequest
{
    /// <summary>空间类型。</summary>
    public TrajectorySpace Space { get; init; }

    /// <summary>采样频率（Hz）。建议与实时控制 <c>duration</c> 的倒数对齐（如 duration=4ms ↔ 250Hz）。</summary>
    public double FrequencyHz { get; init; } = 250.0;

    /// <summary>
    /// 速度上限。与 <see cref="DurationSeconds"/> 二选一。
    /// <para>
    /// - 关节：deg/s，作用于位移最大的关节，其它按比例。
    /// </para>
    /// <para>
    /// - 笛卡尔：mm/s，作用于线位移；姿态用同一时间标度同步。
    /// </para>
    /// <para>
    /// - 配合 <see cref="TrajectoryProfile.Trapezoidal"/> 时为「匀速段速度」；配合 <see cref="TrajectoryProfile.Cubic"/> 时按平均速度推导时长（峰值约 1.5×）。
    /// </para>
    /// </summary>
    public double? Speed { get; init; }

    /// <summary>总时长（秒），与 <see cref="Speed"/> 二选一。</summary>
    public double? DurationSeconds { get; init; }

    /// <summary>速度规划：默认平滑 <see cref="TrajectoryProfile.Cubic"/>；要求严格匀速段时使用 <see cref="TrajectoryProfile.Trapezoidal"/>。</summary>
    public TrajectoryProfile Profile { get; init; } = TrajectoryProfile.Cubic;

    /// <summary>梯形规划的加速度上限。Joint：deg/s²；Cartesian：mm/s²。默认 1000。</summary>
    public double Acceleration { get; init; } = 1000.0;
}

/// <summary>
/// 轨迹中的一个采样点。
/// </summary>
public sealed class TrajectoryPoint
{
    /// <summary>从轨迹起点起算的时间，单位秒。</summary>
    public double TimeSeconds { get; init; }

    /// <summary>位置：关节空间为 6 个关节角（deg）；笛卡尔空间为 [x,y,z,rx,ry,rz]（mm + deg）。</summary>
    public double[] Position { get; init; } = Array.Empty<double>();
}

/// <summary>
/// 轨迹生成器：关节使用按轴线性插值 + 时间标度；笛卡尔位置走直线 + 时间标度，姿态用 SLERP（基于四元数）。
/// </summary>
public static class TrajectoryGenerator
{
    /// <summary>
    /// 生成从 <paramref name="start"/> 到 <paramref name="target"/> 的离散轨迹点序列。
    /// </summary>
    /// <param name="start">6 维起点。</param>
    /// <param name="target">6 维终点。</param>
    /// <param name="request">采样频率、速度/时长、规划方式等。</param>
    /// <returns>按时间升序的采样点；首点 t=0，末点 t=T。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="start"/>、<paramref name="target"/> 或 <paramref name="request"/> 为 null。</exception>
    /// <exception cref="ArgumentException">
    /// 起点或终点不是 6 维；或 <see cref="TrajectoryRequest.Speed"/> 与 <see cref="TrajectoryRequest.DurationSeconds"/> 未做到二选一；
    /// 或笛卡尔纯姿态运动尝试用线速度推导时长。
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <see cref="TrajectoryRequest.FrequencyHz"/>、<see cref="TrajectoryRequest.Speed"/>、<see cref="TrajectoryRequest.DurationSeconds"/> 或
    /// <see cref="TrajectoryRequest.Acceleration"/> 非正。
    /// </exception>
    /// <remarks>
    /// 关节空间输入/输出单位为度；笛卡尔空间输入/输出为 [x,y,z,rx,ry,rz]，单位 mm + deg。
    /// 多段轨迹拼接时请跳过后续段首点，避免端点重复；完整算法说明见 `TRAJECTORY_ALGORITHM.md`。
    /// </remarks>
    public static IEnumerable<TrajectoryPoint> Generate(
        IReadOnlyList<double> start,
        IReadOnlyList<double> target,
        TrajectoryRequest request)
    {
        ValidateInputs(start, target, request);

        return request.Space == TrajectorySpace.Joint
            ? GenerateJoint(start, target, request)
            : GenerateCartesian(start, target, request);
    }

    private static void ValidateInputs(IReadOnlyList<double> s, IReadOnlyList<double> t, TrajectoryRequest req)
    {
        if (s == null) throw new ArgumentNullException(nameof(s));
        if (t == null) throw new ArgumentNullException(nameof(t));
        if (req == null) throw new ArgumentNullException(nameof(req));
        if (s.Count != 6) throw new ArgumentException("起点必须是 6 维。", nameof(s));
        if (t.Count != 6) throw new ArgumentException("终点必须是 6 维。", nameof(t));
        if (req.FrequencyHz <= 0) throw new ArgumentOutOfRangeException(nameof(req), "FrequencyHz 必须大于 0。");
        if (req.Speed.HasValue == req.DurationSeconds.HasValue)
            throw new ArgumentException("Speed 与 DurationSeconds 必须二选一（且只能一个）。", nameof(req));
        if (req.Speed is <= 0) throw new ArgumentOutOfRangeException(nameof(req), "Speed 必须大于 0。");
        if (req.DurationSeconds is <= 0) throw new ArgumentOutOfRangeException(nameof(req), "DurationSeconds 必须大于 0。");
        if (req.Acceleration <= 0) throw new ArgumentOutOfRangeException(nameof(req), "Acceleration 必须大于 0。");
    }

    private static IEnumerable<TrajectoryPoint> GenerateJoint(
        IReadOnlyList<double> q0,
        IReadOnlyList<double> qf,
        TrajectoryRequest req)
    {
        double maxDelta = 0;
        for (int i = 0; i < 6; i++)
            maxDelta = Math.Max(maxDelta, Math.Abs(qf[i] - q0[i]));

        if (maxDelta < 1e-9)
        {
            yield return new TrajectoryPoint { TimeSeconds = 0, Position = q0.ToArray() };
            yield break;
        }

        var profile = ComputeProfile(maxDelta, req);
        double dt = 1.0 / req.FrequencyHz;
        int n = Math.Max(2, (int)Math.Ceiling(profile.T / dt) + 1);

        for (int k = 0; k < n; k++)
        {
            double t = Math.Min(k * dt, profile.T);
            double s = profile.ScaleAt(t);
            var pos = new double[6];
            for (int i = 0; i < 6; i++)
                pos[i] = q0[i] + s * (qf[i] - q0[i]);
            yield return new TrajectoryPoint { TimeSeconds = t, Position = pos };
        }
    }

    private static IEnumerable<TrajectoryPoint> GenerateCartesian(
        IReadOnlyList<double> p0,
        IReadOnlyList<double> pf,
        TrajectoryRequest req)
    {
        double dx = pf[0] - p0[0];
        double dy = pf[1] - p0[1];
        double dz = pf[2] - p0[2];
        double D = Math.Sqrt(dx * dx + dy * dy + dz * dz);

        // 时间标度的「距离」：
        //   - 有线位移：使用 D，使笛卡尔线速度可控
        //   - 无线位移（纯姿态）：用归一化 1.0 走时长模式
        IMotionProfile profile;
        if (D >= 1e-9)
        {
            profile = ComputeProfile(D, req);
        }
        else
        {
            if (req.Speed.HasValue)
                throw new ArgumentException("起止位置完全相同，无法用线速度推导时间；纯姿态运动请改用 DurationSeconds。", nameof(req));
            profile = ComputeProfile(1.0, req);
        }

        var q0 = EulerXyz.ToQuaternion(p0[3], p0[4], p0[5]);
        var qf = EulerXyz.ToQuaternion(pf[3], pf[4], pf[5]);

        double dt = 1.0 / req.FrequencyHz;
        int n = Math.Max(2, (int)Math.Ceiling(profile.T / dt) + 1);

        // 检测起点/终点四元数是否相同，若相同则整段使用原始欧拉角避免往返误差
        bool sameOrientation = EulerXyz.QuaternionApproxEqual(q0, qf);

        for (int k = 0; k < n; k++)
        {
            double t = Math.Min(k * dt, profile.T);
            double s = profile.ScaleAt(t);

            double x = p0[0] + s * dx;
            double y = p0[1] + s * dy;
            double z = p0[2] + s * dz;

            double rx, ry, rz;
            if (sameOrientation)
            {
                // 姿态不变，直接线性插值，完全避免四元数往返
                rx = p0[3] + s * (pf[3] - p0[3]);
                ry = p0[4] + s * (pf[4] - p0[4]);
                rz = p0[5] + s * (pf[5] - p0[5]);
            }
            else if (s <= 0)
            {
                // 端点使用原始欧拉角，避免四元数往返产生不同表示
                rx = p0[3]; ry = p0[4]; rz = p0[5];
            }
            else if (s >= 1)
            {
                rx = pf[3]; ry = pf[4]; rz = pf[5];
            }
            else
            {
                var q = EulerXyz.Slerp(q0, qf, s);
                (rx, ry, rz) = EulerXyz.FromQuaternion(q, p0[3], p0[4], p0[5]);
            }

            yield return new TrajectoryPoint
            {
                TimeSeconds = t,
                Position = new[] { x, y, z, rx, ry, rz },
            };
        }
    }

    private static IMotionProfile ComputeProfile(double D, TrajectoryRequest req) => req.Profile switch
    {
        TrajectoryProfile.Cubic => CubicProfile.From(D, req),
        TrajectoryProfile.Trapezoidal => TrapezoidalProfile.From(D, req),
        _ => throw new ArgumentOutOfRangeException(nameof(req)),
    };
}

internal interface IMotionProfile
{
    double T { get; }
    double ScaleAt(double t);
}

internal sealed class CubicProfile : IMotionProfile
{
    public double T { get; private init; }

    public double ScaleAt(double t)
    {
        if (t <= 0) return 0;
        if (t >= T) return 1;
        double tau = t / T;
        return 3 * tau * tau - 2 * tau * tau * tau;
    }

    public static CubicProfile From(double D, TrajectoryRequest req)
    {
        // Cubic: 时间标度 s(τ) 平均速度 = D/T；峰值速度 ≈ 1.5*D/T。
        // 当用户给 Speed，按平均速度推导：T = D / Speed。
        double T = req.DurationSeconds ?? D / req.Speed!.Value;
        return new CubicProfile { T = T };
    }
}

/// <summary>
/// 梯形速度规划：加速段(0..ta) → 匀速段(ta..T-ta) → 减速段(T-ta..T)。
/// 当给定加速度无法在指定时长内走完距离时，自动退化为三角形规划（无匀速段）。
/// </summary>
internal sealed class TrapezoidalProfile : IMotionProfile
{
    public double T { get; }
    public double D { get; }
    public double V { get; }
    public double A { get; }
    public double Ta { get; }

    private TrapezoidalProfile(double t, double d, double v, double a, double ta)
    {
        T = t; D = d; V = v; A = a; Ta = ta;
    }

    public double ScaleAt(double t)
    {
        if (t <= 0) return 0;
        if (t >= T) return 1;
        double s;
        if (t < Ta)
            s = 0.5 * A * t * t;
        else if (t > T - Ta)
        {
            double tt = T - t;
            s = D - 0.5 * A * tt * tt;
        }
        else
            s = 0.5 * A * Ta * Ta + V * (t - Ta);
        return MathPolyfills.Clamp(s / D, 0, 1);
    }

    public static TrapezoidalProfile From(double D, TrajectoryRequest req)
    {
        double a = req.Acceleration;
        return req.Speed is { } v
            ? FromSpeed(D, v, a)
            : FromDuration(D, req.DurationSeconds!.Value, a);
    }

    private static TrapezoidalProfile FromSpeed(double D, double v, double a)
    {
        double ta = v / a;
        double da = 0.5 * v * ta;
        if (2 * da >= D)
        {
            // 加速度太低或距离太短，无法到达匀速段，退化为三角形：峰值速度 = sqrt(a*D)
            double vp = Math.Sqrt(a * D);
            ta = vp / a;
            return new TrapezoidalProfile(2 * ta, D, vp, a, ta);
        }
        double tc = (D - 2 * da) / v;
        return new TrapezoidalProfile(2 * ta + tc, D, v, a, ta);
    }

    private static TrapezoidalProfile FromDuration(double D, double T, double a)
    {
        // D = v*(T - ta), ta = v/a → v² - a*T*v + a*D = 0
        double disc = a * a * T * T - 4 * a * D;
        if (disc < 0)
        {
            // 给定加速度不足以在 T 内走完 D，退化为三角形 + 等效加速度
            double vp = 2 * D / T;
            double aEff = 4 * D / (T * T);
            return new TrapezoidalProfile(T, D, vp, aEff, T / 2.0);
        }
        double v = (a * T - Math.Sqrt(disc)) / 2.0;
        double ta = v / a;
        return new TrapezoidalProfile(T, D, v, a, ta);
    }
}

/// <summary>
/// 固定欧拉角 XYZ（外旋，单位 deg）↔ 四元数（双精度）+ SLERP。
/// 约定：R = Rz(rz) * Ry(ry) * Rx(rx)（与 Codroid TCP 位姿一致）。
/// </summary>
internal static class EulerXyz
{
    /// <summary>四元数 (w, x, y, z)，归一化。</summary>
    public readonly record struct Quaternion(double W, double X, double Y, double Z);

    /// <summary>判断两个四元数是否近似相等（考虑 q 与 -q 等价）。</summary>
    public static bool QuaternionApproxEqual(Quaternion a, Quaternion b)
    {
        double dot = a.W * b.W + a.X * b.X + a.Y * b.Y + a.Z * b.Z;
        return Math.Abs(Math.Abs(dot) - 1.0) < 1e-12;
    }

    public static Quaternion ToQuaternion(double rxDeg, double ryDeg, double rzDeg)
    {
        const double D2R = Math.PI / 180.0;
        double a = rxDeg * D2R * 0.5;
        double b = ryDeg * D2R * 0.5;
        double c = rzDeg * D2R * 0.5;
        double cx = Math.Cos(a), sx = Math.Sin(a);
        double cy = Math.Cos(b), sy = Math.Sin(b);
        double cz = Math.Cos(c), sz = Math.Sin(c);
        // q = qz * qy * qx
        double w = cz * cy * cx + sz * sy * sx;
        double x = cz * cy * sx - sz * sy * cx;
        double y = cz * sy * cx + sz * cy * sx;
        double z = sz * cy * cx - cz * sy * sx;
        return new Quaternion(w, x, y, z);
    }

    public static (double Rx, double Ry, double Rz) FromQuaternion(Quaternion q)
    {
        return FromQuaternionCore(q, null);
    }

    /// <summary>
    /// 从四元数提取欧拉角，并选择最接近 <paramref name="refRxDeg"/>/<paramref name="refRyDeg"/>/<paramref name="refRzDeg"/> 的等效表示。
    /// 欧拉角存在多解：<c>(rx, ry, rz) ≡ (rx+π, π-ry, rz+π)</c>（等价旋转），
    /// 此重载保证返回与参考角度最接近的那组，避免轨迹中的「姿态跳变」。
    /// </summary>
    public static (double Rx, double Ry, double Rz) FromQuaternion(
        Quaternion q, double refRxDeg, double refRyDeg, double refRzDeg)
    {
        return FromQuaternionCore(q, (refRxDeg, refRyDeg, refRzDeg));
    }

    private static (double Rx, double Ry, double Rz) FromQuaternionCore(
        Quaternion q, (double rx, double ry, double rz)? reference)
    {
        const double R2D = 180.0 / Math.PI;
        double w = q.W, x = q.X, y = q.Y, z = q.Z;
        // R = Rz(γ)Ry(β)Rx(α) → R[2][0] = -sin β = 2(xz - wy)
        double sb = MathPolyfills.Clamp(2.0 * (w * y - x * z), -1.0, 1.0);
        double ry = Math.Asin(sb);
        double rx, rz;
        if (Math.Abs(sb) < 0.999999)
        {
            rx = Math.Atan2(2.0 * (y * z + w * x), 1.0 - 2.0 * (x * x + y * y));
            rz = Math.Atan2(2.0 * (x * y + w * z), 1.0 - 2.0 * (y * y + z * z));
        }
        else
        {
            // 万向锁附近：固定 rz=0，求 rx
            rx = Math.Atan2(-2.0 * (y * z - w * x), 1.0 - 2.0 * (x * x + z * z));
            rz = 0.0;
        }

        rx *= R2D;
        ry *= R2D;
        rz *= R2D;

        // 如果有参考欧拉角，选择最接近的等效表示
        if (reference.HasValue)
        {
            var (refRx, refRy, refRz) = reference.Value;
            // 候选1: (rx, ry, rz)
            // 候选2: (rx+180, 180-ry, rz+180) — 等价旋转
            double rx2 = rx + 180.0;
            double ry2 = 180.0 - ry;
            double rz2 = rz + 180.0;

            // 归一化到 [-180, 180]
            rx2 = NormalizeAngle180(rx2);
            ry2 = NormalizeAngle180(ry2);
            rz2 = NormalizeAngle180(rz2);

            double dist1 = AngleDistanceSq(rx, ry, rz, refRx, refRy, refRz);
            double dist2 = AngleDistanceSq(rx2, ry2, rz2, refRx, refRy, refRz);

            if (dist2 < dist1)
                return (rx2, ry2, rz2);
        }

        return (rx, ry, rz);
    }

    /// <summary>将角度归一化到 [-180, 180] 度。</summary>
    private static double NormalizeAngle180(double deg)
    {
        deg = deg % 360.0;
        if (deg > 180.0) deg -= 360.0;
        if (deg < -180.0) deg += 360.0;
        return deg;
    }

    /// <summary>两组欧拉角之间的平方距离（简单欧氏距离，单位度²）。</summary>
    private static double AngleDistanceSq(double rx1, double ry1, double rz1, double rx2, double ry2, double rz2)
    {
        double drx = rx1 - rx2;
        double dry = ry1 - ry2;
        double drz = rz1 - rz2;
        return drx * drx + dry * dry + drz * drz;
    }

    public static Quaternion Slerp(Quaternion q0, Quaternion q1, double t)
    {
        double dot = q0.W * q1.W + q0.X * q1.X + q0.Y * q1.Y + q0.Z * q1.Z;
        if (dot < 0)
        {
            q1 = new Quaternion(-q1.W, -q1.X, -q1.Y, -q1.Z);
            dot = -dot;
        }
        const double LerpThreshold = 0.9995;
        if (dot > LerpThreshold)
        {
            double w = q0.W + t * (q1.W - q0.W);
            double x = q0.X + t * (q1.X - q0.X);
            double y = q0.Y + t * (q1.Y - q0.Y);
            double z = q0.Z + t * (q1.Z - q0.Z);
            double n = Math.Sqrt(w * w + x * x + y * y + z * z);
            return new Quaternion(w / n, x / n, y / n, z / n);
        }
        double th0 = Math.Acos(dot);
        double th = th0 * t;
        double sTh = Math.Sin(th);
        double sTh0 = Math.Sin(th0);
        double s0 = Math.Cos(th) - dot * sTh / sTh0;
        double s1 = sTh / sTh0;
        return new Quaternion(
            s0 * q0.W + s1 * q1.W,
            s0 * q0.X + s1 * q1.X,
            s0 * q0.Y + s1 * q1.Y,
            s0 * q0.Z + s1 * q1.Z);
    }
}
