using AwesomeAssertions;
using Nordstein.Core.Common.Io;

namespace Nordstein.Core.Common.Tests.Io;

[TestClass]
public sealed class PortableSegmentTests
{
    [TestMethod]
    [DataRow("2026")]
    [DataRow("scans_in-1")]
    [DataRow("a")]
    public void IsPortable_Literal_Accepted(string segment)
    {
        segment.PortableSegmentViolation(32).Should().BeNull();
        segment.IsPortableSegment(32).Should().BeTrue();
    }

    [TestMethod]
    [DataRow("con")]
    [DataRow("nul")]
    [DataRow("aux")]
    [DataRow("prn")]
    [DataRow("com1")]
    [DataRow("lpt9")]
    public void IsPortable_ReservedName_Refused(string segment)
        => segment.PortableSegmentViolation(32).Should().Be(PortableSegmentRule.ReservedName);

    [TestMethod]
    [DataRow(".")]
    [DataRow("..")]
    public void IsPortable_DotSegments_Refused(string segment)
        => segment.PortableSegmentViolation(32).Should().Be(PortableSegmentRule.DotSegment);

    [TestMethod]
    public void IsPortable_TooLong_Refused()
        => new string('a', 33).PortableSegmentViolation(32).Should().Be(PortableSegmentRule.Length);

    [TestMethod]
    public void IsPortable_Empty_Refused()
        => string.Empty.PortableSegmentViolation(32).Should().Be(PortableSegmentRule.Length);

    [TestMethod]
    [DataRow("C:")]
    [DataRow("a\\b")]
    [DataRow("Upper")]
    [DataRow("a.b")]
    [DataRow("a b")]
    public void IsPortable_ForeignCharacter_Refused(string segment)
        => segment.PortableSegmentViolation(32).Should().Be(PortableSegmentRule.Charset);
}
