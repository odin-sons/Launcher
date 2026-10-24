using System.Buffers.Binary;
using System.Text;
using Odinsons.ValheimLauncher;
using Xunit;

namespace Launcher.Tests
{
    /// <summary>Detecting an arm64 slice from the Mach-O header of the game executable.</summary>
    public sealed class MacArchitectureTests
    {
        private const int CpuTypeX64 = 0x01000007;
        private const int CpuTypeArm64 = 0x0100000C;

        private static string WriteFile(TestPack pack, byte[] bytes)
        {
            string path = Path.Combine(pack.Root, "binary");
            File.WriteAllBytes(path, bytes);
            return path;
        }

        private static byte[] Thin(int cpuType)
        {
            var bytes = new byte[32];
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, 0xFEEDFACF);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), cpuType);
            return bytes;
        }

        private static byte[] Fat(bool fat64, params int[] cpuTypes)
        {
            int entrySize = fat64 ? 32 : 20;
            var bytes = new byte[8 + entrySize * cpuTypes.Length];
            BinaryPrimitives.WriteUInt32BigEndian(bytes, fat64 ? 0xCAFEBABFu : 0xCAFEBABEu);
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(4), (uint)cpuTypes.Length);
            for (int i = 0; i < cpuTypes.Length; i++)
                BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(8 + i * entrySize), cpuTypes[i]);
            return bytes;
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void FatBinary_WithArm64Slice_IsDetected(bool fat64)
        {
            using var pack = new TestPack();
            Assert.True(MacArchitecture.MachOHasArm64Slice(WriteFile(pack, Fat(fat64, CpuTypeX64, CpuTypeArm64))));
        }

        [Fact]
        public void FatBinary_WithoutArm64Slice_IsNot()
        {
            using var pack = new TestPack();
            Assert.False(MacArchitecture.MachOHasArm64Slice(WriteFile(pack, Fat(false, CpuTypeX64))));
        }

        [Fact]
        public void ThinBinary_IsDetectedByItsCpuType()
        {
            using var pack = new TestPack();
            Assert.True(MacArchitecture.MachOHasArm64Slice(WriteFile(pack, Thin(CpuTypeArm64))));
            Assert.False(MacArchitecture.MachOHasArm64Slice(WriteFile(pack, Thin(CpuTypeX64))));
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
            byte[] bytes = Fat(false, CpuTypeArm64);
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(4), 5000);
            Assert.False(MacArchitecture.MachOHasArm64Slice(WriteFile(pack, bytes)));
        }
    }
}
