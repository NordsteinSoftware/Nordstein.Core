using AwesomeAssertions;

namespace Nordstein.Core.Licensing.Tests;

/// <summary>
/// <see cref="LicensingModule"/>'s constructor refuses a missing configuration or tier policy.
/// </summary>
[TestClass]
public sealed class LicensingModuleArgumentTests
{
    [TestMethod]
    public void Ctor_NullConfiguration_Throws()
    {
        Action act = () => _ = new LicensingModule((new LicensingConfiguration[1])[0], new TestTierPolicy());

        act.Should().Throw<ArgumentNullException>().WithParameterName("configuration");
    }

    [TestMethod]
    public void Ctor_NullPolicy_Throws()
    {
        Action act = () => _ = new LicensingModule(Module.Factory.Configuration(), (new ILicenseTierPolicy[1])[0]);

        act.Should().Throw<ArgumentNullException>().WithParameterName("policy");
    }
}
