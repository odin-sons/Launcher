using System.Buffers.Binary;
using System.Text;
using Odinsons.ValheimLauncher;
using Xunit;

namespace Launcher.Tests
{
    /// <summary>Detecting an arm64 slice from the Mach-O header of the game executable.</summary>
    public sealed class MacArchitectureTests
    {

        private static string WriteFile(TestPack pack, byte[] bytes)
        {
            string path = Path.Combine(pack.Root, "binary");
            File.WriteAllBytes(path, bytes);
            return path;
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void FatBinary_WithArm64Slice_IsDetected(bool fat64)
        {
            using var pack = new TestPack();
            Assert.True(MacArchitecture.MachOHasArm64Slice(WriteFile(pack, MachOFixtures.Fat(fat64, MachOFixtures.CpuTypeX64, MachOFixtures.CpuTypeArm64))));
        }

        [Fact]
        public void FatBinary_WithoutArm64Slice_IsNot()
        {
            using var pack = new TestPack();
            Assert.False(MacArchitecture.MachOHasArm64Slice(WriteFile(pack, MachOFixtures.Fat(false, MachOFixtures.CpuTypeX64))));
        }

        [Fact]
        public void ThinBinary_IsDetectedByItsCpuType()
        {
            using var pack = new TestPack();
            Assert.True(MacArchitecture.MachOHasArm64Slice(WriteFile(pack, MachOFixtures.Thin(MachOFixtures.CpuTypeArm64))));
            Assert.False(MacArchitecture.MachOHasArm64Slice(WriteFile(pack, MachOFixtures.Thin(MachOFixtures.CpuTypeX64))));
        }

        [Fact]
        public void NotMachO_MissingOrTruncatedFile_IsNotArm64()
        {
            using var pack = new TestPack();
            Assert.False(MacArchitecture.MachOHasArm64Slice(WriteFile(pack, Encoding.UTF8.GetBytes("just text"))));
            Assert.False(MacArchitecture.MachOHasArm64Slice(WriteFile(pack, new byte[3])));
            Assert.False(MacArchitecture.MachOHasArm64Slice(Path.Combine(pack.Root, "missing")));
        }

        [Fact]
        public void FatHeader_WithAbsurdSliceCount_IsRejected()
        {
            using var pack = new TestPack();
            byte[] bytes = MachOFixtures.Fat(false, MachOFixtures.CpuTypeArm64);
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(4), 5000);
            Assert.False(MacArchitecture.MachOHasArm64Slice(WriteFile(pack, bytes)));
        }
    }
}
