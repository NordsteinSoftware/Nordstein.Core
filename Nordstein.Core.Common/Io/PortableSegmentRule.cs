namespace Nordstein.Core.Common.Io;

/// <summary>The rule a path segment broke in <see cref="PortableSegment.PortableSegmentViolation"/>.</summary>
public enum PortableSegmentRule
{
    /// <summary>Empty, or longer than the caller's cap.</summary>
    Length,

    /// <summary><c>.</c> or <c>..</c>: the segment would leave or repeat its directory.</summary>
    DotSegment,

    /// <summary>A character outside <c>[a-z0-9_-]</c>.</summary>
    Charset,

    /// <summary>A name Windows reserves for a device (<c>con</c>, <c>nul</c>, <c>aux</c>, <c>prn</c>, <c>com1</c>-<c>com9</c>, <c>lpt1</c>-<c>lpt9</c>).</summary>
    ReservedName,
}
