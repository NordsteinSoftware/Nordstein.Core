namespace Nordstein.Core.Common.Culture;

/// <summary>
/// The order in which a text writes the day, month and year of a numeric date. Numbered from 1 so
/// the default value is not a member.
/// </summary>
public enum DateOrder
{
    /// <summary>
    /// Day, month, year: <c>31.01.2027</c>, <c>31/01/2027</c>.
    /// </summary>
    Dmy = 1,

    /// <summary>
    /// Month, day, year: <c>01/31/2027</c>.
    /// </summary>
    Mdy = 2,

    /// <summary>
    /// Year, month, day: <c>2027/01/31</c>, <c>2027.01.31</c>.
    /// </summary>
    Ymd = 3,
}
