using System.Diagnostics;

namespace Game.Network
{
    /// <summary>
    /// 单调时钟（毫秒）。可在任意线程使用（无 Unity API 限制），
    /// 用于超时/重连计时；帧无关且不受系统时间回拨影响。
    /// </summary>
    public static class Clock
    {
        private static readonly double TicksToMs = 1000.0 / Stopwatch.Frequency;

        public static long Millis() => (long)(Stopwatch.GetTimestamp() * TicksToMs);
    }
}
