using System.Buffers.Binary;

namespace Launcher.Tests
{
    /// <summary>Minimal Mach-O headers for tests that need real file content to inspect.</summary>
    public static class MachOFixtures
    {
        public const int CpuTypeX64 = 0x01000007;
        public const int CpuTypeArm64 = 0x0100000C;

        public static byte[] Thin(int cpuType)
        {
            var bytes = new byte[32];
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, 0xFEEDFACF);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), cpuType);
            return bytes;
        }

        public static byte[] Fat(bool fat64, params int[] cpuTypes)
        {
            int entrySize = fat64 ? 32 : 20;
            var bytes = new byte[8 + entrySize * cpuTypes.Length];
            BinaryPrimitives.WriteUInt32BigEndian(bytes, fat64 ? 0xCAFEBABFu : 0xCAFEBABEu);
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(4), (uint)cpuTypes.Length);
            for (int i = 0; i < cpuTypes.Length; i++)
                BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(8 + i * entrySize), cpuTypes[i]);
            return bytes;
        }
    }
}
