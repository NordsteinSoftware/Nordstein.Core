namespace Nordstein.Core.Common.Time;

/// <summary>
/// Aligns <see cref="DateTimeOffset"/> values with the microsecond precision a relational <c>timestamptz</c> column
/// (PostgreSQL) persists, so a value held in memory compares equal to the value read back.
/// </summary>
public static class MicrosecondPrecisionExtensions
{
    /// <summary>
    /// Returns the same instant in UTC (offset zero) with the ticks below one microsecond dropped (truncated toward
    /// <see cref="DateTimeOffset.MinValue"/>, never rounded). Idempotent; pure and thread-safe. Never throws: the
    /// result of any valid value, <see cref="DateTimeOffset.MinValue"/> and <see cref="DateTimeOffset.MaxValue"/>
    /// included, is a valid value.
    /// </summary>
    /// <param name="value">The value to truncate; any offset.</param>
    /// <returns>The truncated instant, with <see cref="DateTimeOffset.Offset"/> <see cref="TimeSpan.Zero"/>.</returns>
    public static DateTimeOffset TruncateToMicroseconds(this DateTimeOffset value)
        => new(value.UtcTicks / TimeSpan.TicksPerMicrosecond * TimeSpan.TicksPerMicrosecond, TimeSpan.Zero);
}
