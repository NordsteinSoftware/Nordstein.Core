using AwesomeAssertions;
using Nordstein.Core.Common.Io.Internal;

namespace Nordstein.Core.Common.Tests.Io;

[TestClass]
public sealed class MountInfoTests
{
    private const string MountInfo = """
        22 1 8:1 / / rw,relatime shared:1 - ext4 /dev/sda1 rw,errors=remount-ro
        23 22 0:21 / /proc rw,nosuid,nodev,noexec,relatime shared:2 - proc proc rw
        30 22 0:26 / /tmp rw,nosuid,nodev shared:9 - tmpfs tmpfs rw
        41 22 0:35 / /mnt/archive rw,relatime shared:20 - nfs4 nas:/export/archive rw,vers=4.2
        42 22 0:36 / /mnt/share rw,relatime - cifs //nas/share rw,vers=3.1.1
        43 22 0:37 / /home/ops/remote rw,nosuid,nodev,relatime shared:22 - fuse.sshfs ops@host:/data rw
        44 22 0:38 / /var/lib/docker/overlay2/abc/merged rw,relatime - overlay overlay rw,lowerdir=/x
        45 22 0:39 / /mnt/with\040space rw,relatime - xfs /dev/sdb1 rw
        46 41 0:40 / /mnt/archive/local rw,relatime master:5 - ext4 /dev/sdc1 rw
        """;

    [TestMethod]
    [DataRow("/", "ext4")]
    [DataRow("/srv/data", "ext4")]
    [DataRow("/tmp", "tmpfs")]
    [DataRow("/tmp/x/y", "tmpfs")]
    [DataRow("/mnt/archive/blobs", "nfs4")]
    [DataRow("/mnt/share", "cifs")]
    [DataRow("/home/ops/remote/keys", "fuse.sshfs")]
    [DataRow("/var/lib/docker/overlay2/abc/merged/data", "overlay")]
    [DataRow("/mnt/with space/x", "xfs")]
    [DataRow("/mnt/archive/local/blobs", "ext4")]
    public void FilesystemTypeOf_PicksTheLongestMountPoint(string path, string expected)
    {
        MountInfo.FilesystemTypeOf(path).Should().Be(expected);
    }

    [TestMethod]
    public void FilesystemTypeOf_MatchesWholeSegmentsOnly()
    {
        MountInfo.FilesystemTypeOf("/tmpdata").Should().Be("ext4");
        MountInfo.FilesystemTypeOf("/mnt/archived").Should().Be("ext4");
    }

    [TestMethod]
    public void FilesystemTypeOf_WithTheSameMountPointTwice_PicksTheLaterMount()
    {
        const string stacked = """
            22 1 8:1 / / rw - ext4 /dev/sda1 rw
            50 22 0:50 / /data rw - ext4 /dev/sdb1 rw
            51 50 0:51 / /data rw - nfs4 nas:/data rw
            """;

        stacked.FilesystemTypeOf("/data/blobs").Should().Be("nfs4");
    }

    [TestMethod]
    public void FilesystemTypeOf_WithATrailingSeparator_MatchesTheSameMount()
    {
        MountInfo.FilesystemTypeOf("/tmp/").Should().Be("tmpfs");
    }

    [TestMethod]
    public void FilesystemTypeOf_WithoutAnyMatch_ReportsUnknown()
    {
        "30 22 0:26 / /tmp rw - tmpfs tmpfs rw".FilesystemTypeOf("/srv").Should().Be("unknown");
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("garbage")]
    [DataRow("22 1 8:1 / / rw shared:1 ext4 /dev/sda1 rw")]
    [DataRow("22 1 8:1 / / rw -")]
    public void FilesystemTypeOf_SkipsMalformedLines(string line)
    {
        line.FilesystemTypeOf("/").Should().Be("unknown");
    }
}
