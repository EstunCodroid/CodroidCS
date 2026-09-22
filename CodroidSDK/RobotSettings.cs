using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

#pragma warning disable CS1591

namespace Codroid
{
    /// <summary>工具坐标系 / 用户坐标系单帧（<c>x,y,z,a,b,c</c>，工具帧含 <c>name</c> 及偏置）。</summary>
    public sealed class RobotFrame
    {
        [JsonPropertyName("id")]
        public int Id { get; init; }

        [JsonPropertyName("x")]
        public double X { get; init; }
        [JsonPropertyName("y")]
        public double Y { get; init; }
        [JsonPropertyName("z")]
        public double Z { get; init; }
        [JsonPropertyName("a")]
        public double A { get; init; }
        [JsonPropertyName("b")]
        public double B { get; init; }
        [JsonPropertyName("c")]
        public double C { get; init; }

        /// <summary>工具/坐标系名称。</summary>
        [JsonPropertyName("name")]
        public string Name { get; init; } = "";

        /// <summary>沿工具坐标系的偏置，单位 mm（仅工具帧使用，默认 0 时不发送）。</summary>
        [JsonPropertyName("xOffset")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public double XOffset { get; init; }

        /// <summary>沿工具坐标系的偏置，单位 mm（仅工具帧使用，默认 0 时不发送）。</summary>
        [JsonPropertyName("yOffset")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public double YOffset { get; init; }

        /// <summary>沿工具坐标系的偏置，单位 mm（仅工具帧使用，默认 0 时不发送）。</summary>
        [JsonPropertyName("zOffset")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public double ZOffset { get; init; }
    }

    /// <summary>负载坐标系单帧（<c>m, mx, my, mz</c> + 惯性张量）。</summary>
    public sealed class RobotPayloadFrame
    {
        [JsonPropertyName("id")]
        public int Id { get; init; }

        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("m")]
        public double M { get; init; }
        [JsonPropertyName("mx")]
        public double Mx { get; init; }
        [JsonPropertyName("my")]
        public double My { get; init; }
        [JsonPropertyName("mz")]
        public double Mz { get; init; }

        /// <summary>惯性张量（kg·m²）。</summary>
        [JsonPropertyName("ixx")]
        public double Ixx { get; init; }
        [JsonPropertyName("ixy")]
        public double Ixy { get; init; }
        [JsonPropertyName("ixz")]
        public double Ixz { get; init; }
        [JsonPropertyName("iyy")]
        public double Iyy { get; init; }
        [JsonPropertyName("iyz")]
        public double Iyz { get; init; }
        [JsonPropertyName("izz")]
        public double Izz { get; init; }
    }

    /// <summary><c>Robot/GetRobotParameter</c> 返回的设置界面参数快照。</summary>
    public sealed class RobotParameters
    {
        [JsonPropertyName("defaultToolId")]
        public int DefaultToolId { get; init; }

        [JsonPropertyName("defaultPayloadId")]
        public int DefaultPayloadId { get; init; }

        [JsonPropertyName("defaultCoordinateId")]
        public int DefaultCoordinateId { get; init; }

        [JsonPropertyName("maxPayload")]
        public double MaxPayload { get; init; }

        [JsonPropertyName("Tool")]
        public List<RobotFrame> Tool { get; init; } = new();

        [JsonPropertyName("Payload")]
        public List<RobotPayloadFrame> Payload { get; init; } = new();

        [JsonPropertyName("Coordinate")]
        public List<RobotFrame> Coordinate { get; init; } = new();
    }

    internal static class RobotSettingsValidation
    {
        public const int MinSlotId = 0;
        public const int MaxSlotId = 15;
        public const int WritableMinSlotId = 1;
        public const double ZeroEpsilon = 1e-9;

        public static void ValidateDefaultSlotId(int id, string paramName)
        {
            if (id is < MinSlotId or > MaxSlotId)
            {
                throw new ArgumentOutOfRangeException(
                    paramName,
                    id,
                    $"默认编号须在 {MinSlotId}~{MaxSlotId}。");
            }
        }

        public static void ValidateWritableFrameId(int frameId, string paramName)
        {
            if (frameId is < WritableMinSlotId or > MaxSlotId)
            {
                throw new ArgumentOutOfRangeException(
                    paramName,
                    frameId,
                    $"可修改的坐标系/工具槽位 id 须为 {WritableMinSlotId}~{MaxSlotId}；id=0 为保留项不可修改。");
            }
        }

        public static void ValidateFrameIdMatches(int frameId, RobotFrame frame)
        {
            Polyfills.ThrowIfNull(frame);
            if (frame.Id != frameId)
            {
                throw new ArgumentException(
                    $"frame.Id（{frame.Id}）须与 frameId（{frameId}）一致。",
                    nameof(frame));
            }
        }

        public static void ValidateFrameIdMatches(int frameId, RobotPayloadFrame frame)
        {
            Polyfills.ThrowIfNull(frame);
            if (frame.Id != frameId)
            {
                throw new ArgumentException(
                    $"frame.Id（{frame.Id}）须与 frameId（{frameId}）一致。",
                    nameof(frame));
            }
        }

        public static void ValidateReservedToolFrameUnchanged(RobotFrame frame)
        {
            EnsureReservedSlotZero(frame.Id, nameof(frame));
            EnsureToolFrameIsZero(frame);
        }

        public static void ValidateReservedPayloadFrameUnchanged(RobotPayloadFrame frame)
        {
            EnsureReservedSlotZero(frame.Id, nameof(frame));
            EnsurePayloadFrameIsZero(frame);
        }

        public static void ValidateToolFramesForSave(IReadOnlyList<RobotFrame> frames, string paramName)
        {
            ValidateFullSlotList(frames, paramName, f => f.Id, ValidateReservedToolFrameUnchanged);
        }

        public static void ValidatePayloadFramesForSave(
            IReadOnlyList<RobotPayloadFrame> frames,
            string paramName)
        {
            ValidateFullSlotList(frames, paramName, f => f.Id, ValidateReservedPayloadFrameUnchanged);
        }

        private static void EnsureReservedSlotZero(int id, string paramName)
        {
            if (id == 0)
            {
                return;
            }

            throw new ArgumentException("id=0 为控制器保留默认项，不允许通过写接口修改。", paramName);
        }

        private static void EnsureToolFrameIsZero(RobotFrame frame)
        {
            if (!IsZero(frame.X) || !IsZero(frame.Y) || !IsZero(frame.Z)
                || !IsZero(frame.A) || !IsZero(frame.B) || !IsZero(frame.C))
            {
                throw new ArgumentException(
                    "id=0 的工具/用户坐标系项必须保持全零，不可修改。",
                    nameof(frame));
            }
        }

        private static void EnsurePayloadFrameIsZero(RobotPayloadFrame frame)
        {
            if (!IsZero(frame.M) || !IsZero(frame.Mx) || !IsZero(frame.My) || !IsZero(frame.Mz)
                || !IsZero(frame.Ixx) || !IsZero(frame.Ixy) || !IsZero(frame.Ixz)
                || !IsZero(frame.Iyy) || !IsZero(frame.Iyz) || !IsZero(frame.Izz))
            {
                throw new ArgumentException(
                    "id=0 的负载坐标系项必须保持全零，不可修改。",
                    nameof(frame));
            }
        }

        private static void ValidateFullSlotList<T>(
            IReadOnlyList<T> frames,
            string paramName,
            Func<T, int> idSelector,
            Action<T> validateReservedZero)
        {
            Polyfills.ThrowIfNull(frames, paramName);
            if (frames.Count != MaxSlotId + 1)
            {
                throw new ArgumentException(
                    $"须提供 {MaxSlotId + 1} 项（id {MinSlotId}~{MaxSlotId}）。",
                    paramName);
            }

            var seen = new HashSet<int>();
            foreach (var frame in frames)
            {
                Polyfills.ThrowIfNull(frame);
                int id = idSelector(frame);
                if (id is < MinSlotId or > MaxSlotId)
                {
                    throw new ArgumentException($"列表中存在非法 id={id}。", paramName);
                }

                if (!seen.Add(id))
                {
                    throw new ArgumentException($"列表中 id={id} 重复。", paramName);
                }

                if (id == 0)
                {
                    validateReservedZero(frame);
                }
            }

            for (int i = MinSlotId; i <= MaxSlotId; i++)
            {
                if (!seen.Contains(i))
                {
                    throw new ArgumentException($"缺少 id={i} 的项。", paramName);
                }
            }
        }

        private static bool IsZero(double v) => Math.Abs(v) <= ZeroEpsilon;
    }

    internal static class RobotSettingsSerialization
    {
        public static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            NumberHandling = JsonNumberHandling.AllowReadingFromString
        };

        /// <summary>
        /// 从 Robot/GetRobotParameter（旧接口）响应 db 解析完整参数。
        /// </summary>
        public static RobotParameters ParseFromDb(JsonElement db)
        {
            if (db.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                throw new InvalidOperationException("GetRobotParameter 响应 db 为空。");
            }

            var parameters = JsonSerializer.Deserialize<RobotParameters>(db.GetRawText(), JsonOptions);
            if (parameters == null)
            {
                throw new InvalidOperationException("无法反序列化 RobotParameters。");
            }

            return parameters;
        }

        /// <summary>
        /// 从 Robot/getTools（新接口）响应 db 解析工具参数。
        /// </summary>
        public static (int defaultToolId, List<RobotFrame> tool) ParseToolsFromDb(JsonElement db)
        {
            if (db.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                throw new InvalidOperationException("getTools 响应 db 为空。");
            }

            using var doc = JsonDocument.Parse(db.GetRawText());
            var root = doc.RootElement;
            int defaultToolId = root.TryGetProperty("defaultToolId", out var dti) ? dti.GetInt32() : 0;
            var tool = root.TryGetProperty("Tool", out var toolEl)
                ? JsonSerializer.Deserialize<List<RobotFrame>>(toolEl.GetRawText(), JsonOptions) ?? new()
                : new();
            return (defaultToolId, OrderFramesById(tool));
        }

        /// <summary>
        /// 从 Robot/getCoordinates（新接口）响应 db 解析坐标系参数。
        /// </summary>
        public static (int defaultCoordinateId, List<RobotFrame> coordinate) ParseCoordinatesFromDb(JsonElement db)
        {
            if (db.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                throw new InvalidOperationException("getCoordinates 响应 db 为空。");
            }

            using var doc = JsonDocument.Parse(db.GetRawText());
            var root = doc.RootElement;
            int defaultCoordinateId = root.TryGetProperty("defaultCoordinateId", out var dci) ? dci.GetInt32() : 0;
            var coordinate = root.TryGetProperty("Coordinate", out var coordEl)
                ? JsonSerializer.Deserialize<List<RobotFrame>>(coordEl.GetRawText(), JsonOptions) ?? new()
                : new();
            return (defaultCoordinateId, OrderFramesById(coordinate));
        }

        /// <summary>
        /// 从 Robot/getPayloads（新接口）响应 db 解析负载参数。
        /// </summary>
        public static (int defaultPayloadId, double maxPayload, List<RobotPayloadFrame> payload) ParsePayloadsFromDb(JsonElement db)
        {
            if (db.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                throw new InvalidOperationException("getPayloads 响应 db 为空。");
            }

            using var doc = JsonDocument.Parse(db.GetRawText());
            var root = doc.RootElement;
            int defaultPayloadId = root.TryGetProperty("defaultPayloadId", out var dpi) ? dpi.GetInt32() : 0;
            double maxPayload = root.TryGetProperty("maxPayload", out var mp) ? mp.GetDouble() : 0;
            var payload = root.TryGetProperty("Payload", out var payloadEl)
                ? JsonSerializer.Deserialize<List<RobotPayloadFrame>>(payloadEl.GetRawText(), JsonOptions) ?? new()
                : new();
            return (defaultPayloadId, maxPayload, OrderPayloadFramesById(payload));
        }

        /// <summary>
        /// 用新接口（getTools + getCoordinates + getPayloads）的数据合并为 RobotParameters。
        /// </summary>
        public static RobotParameters BuildFromNewApi(
            int defaultToolId, List<RobotFrame> tool,
            int defaultCoordinateId, List<RobotFrame> coordinate,
            int defaultPayloadId, double maxPayload, List<RobotPayloadFrame> payload)
        {
            return new RobotParameters
            {
                DefaultToolId = defaultToolId,
                DefaultCoordinateId = defaultCoordinateId,
                DefaultPayloadId = defaultPayloadId,
                MaxPayload = maxPayload,
                Tool = tool,
                Payload = payload,
                Coordinate = coordinate
            };
        }

        public static List<RobotFrame> MergeToolFrame(
            IReadOnlyList<RobotFrame> current,
            int frameId,
            RobotFrame updated)
        {
            var merged = current.ToList();
            int index = merged.FindIndex(f => f.Id == frameId);
            if (index < 0)
            {
                throw new InvalidOperationException($"当前参数中不存在 Tool id={frameId}。");
            }

            merged[index] = updated;
            return merged;
        }

        public static List<RobotPayloadFrame> MergePayloadFrame(
            IReadOnlyList<RobotPayloadFrame> current,
            int frameId,
            RobotPayloadFrame updated)
        {
            var merged = current.ToList();
            int index = merged.FindIndex(f => f.Id == frameId);
            if (index < 0)
            {
                throw new InvalidOperationException($"当前参数中不存在 Payload id={frameId}。");
            }

            merged[index] = updated;
            return merged;
        }

        public static List<RobotFrame> MergeCoordinateFrame(
            IReadOnlyList<RobotFrame> current,
            int frameId,
            RobotFrame updated)
        {
            var merged = current.ToList();
            int index = merged.FindIndex(f => f.Id == frameId);
            if (index < 0)
            {
                throw new InvalidOperationException($"当前参数中不存在 Coordinate id={frameId}。");
            }

            merged[index] = updated;
            return merged;
        }

        public static List<RobotFrame> OrderFramesById(IReadOnlyList<RobotFrame> frames) =>
            frames.OrderBy(f => f.Id).ToList();

        public static List<RobotPayloadFrame> OrderPayloadFramesById(IReadOnlyList<RobotPayloadFrame> frames) =>
            frames.OrderBy(f => f.Id).ToList();

        public static object BuildDefaultPayloadIdDb(int payloadId) =>
            new Dictionary<string, int> { ["defaultPayloadId"] = payloadId };

        public static object BuildDefaultToolIdDb(int toolId) =>
            new Dictionary<string, int> { ["defaultToolId"] = toolId };

        public static object BuildDefaultCoordinateIdDb(int coordinateId) =>
            new Dictionary<string, int> { ["defaultCoordinateId"] = coordinateId };

        public static object BuildToolDb(IReadOnlyList<RobotFrame> frames) =>
            new Dictionary<string, List<RobotFrame>> { ["Tool"] = OrderFramesById(frames) };

        /// <summary>
        /// 构造新接口 Robot/setTools 的 db（含 defaultToolId + Tool 列表）。
        /// </summary>
        public static object BuildSetToolsDb(int defaultToolId, IReadOnlyList<RobotFrame> frames) =>
            new Dictionary<string, object>
            {
                ["defaultToolId"] = defaultToolId,
                ["Tool"] = OrderFramesById(frames)
            };

        public static object BuildPayloadDb(IReadOnlyList<RobotPayloadFrame> frames) =>
            new Dictionary<string, List<RobotPayloadFrame>>
            {
                ["Payload"] = OrderPayloadFramesById(frames)
            };

        /// <summary>
        /// 构造新接口 Robot/setPayloads 的 db（含 defaultPayloadId + maxPayload + Payload 列表）。
        /// </summary>
        public static object BuildSetPayloadsDb(
            int defaultPayloadId, double maxPayload, IReadOnlyList<RobotPayloadFrame> frames) =>
            new Dictionary<string, object>
            {
                ["defaultPayloadId"] = defaultPayloadId,
                ["maxPayload"] = maxPayload,
                ["Payload"] = OrderPayloadFramesById(frames)
            };

        public static object BuildCoordinateDb(IReadOnlyList<RobotFrame> frames) =>
            new Dictionary<string, List<RobotFrame>> { ["Coordinate"] = OrderFramesById(frames) };

        /// <summary>
        /// 构造新接口 Robot/setCoordinates 的 db（含 defaultCoordinateId + Coordinate 列表）。
        /// </summary>
        public static object BuildSetCoordinatesDb(int defaultCoordinateId, IReadOnlyList<RobotFrame> frames) =>
            new Dictionary<string, object>
            {
                ["defaultCoordinateId"] = defaultCoordinateId,
                ["Coordinate"] = OrderFramesById(frames)
            };
    }
}
