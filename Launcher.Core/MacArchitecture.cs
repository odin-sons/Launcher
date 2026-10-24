using System;
using System.Buffers.Binary;
using System.IO;
using System.Runtime.InteropServices;

namespace Odinsons.ValheimLauncher
{
    /// <summary>
    /// What the macOS launch decision needs to know about the machine and the game binary:
    /// whether the host is Apple Silicon and whether the executable has an arm64 slice.
    /// <see cref="Pretend"/> swaps both answers so tests can run every branch on any host.
    /// </summary>
    public static class MacArchitecture
    {
        private const uint FatMagic = 0xCAFEBABE;
        private const uint FatMagic64 = 0xCAFEBABF;
        private const uint MachMagic64 = 0xFEEDFACF;
        private const int CpuTypeArm64 = 0x0100000C;
        private const int MaxFatSlices = 32;

        private static (bool AppleSilicon, bool HasArm64Slice)? _forced;

        public static bool AppleSiliconHost =>
            _forced?.AppleSilicon ?? RuntimeInformation.OSArchitecture == Architecture.Arm64;

        public static bool HasArm64Slice(string executablePath) =>
            _forced?.HasArm64Slice ?? MachOHasArm64Slice(executablePath);

        public static IDisposable Pretend(bool appleSilicon, bool executableHasArm64Slice)
        {
            var previous = _forced;
            _forced = (appleSilicon, executableHasArm64Slice);
            return new Scope(() => _forced = previous);
        }

        /// <summary>Reads the Mach-O header (thin or fat) and reports whether it contains an arm64 slice.</summary>
        public static bool MachOHasArm64Slice(string path)
        {
            try
            {
                using FileStream stream = File.OpenRead(path);
                var header = new byte[8 + 32 * MaxFatSlices];
                int read = stream.Read(header, 0, header.Length);
                return ContainsArm64(header.AsSpan(0, read));
            }
            catch
            {
                return false;
            }
        }

        private static bool ContainsArm64(ReadOnlySpan<byte> header)
        {
            if (header.Length < 8) return false;

            uint magic = BinaryPrimitives.ReadUInt32BigEndian(header);
            if (magic == FatMagic || magic == FatMagic64)
            {
                uint count = BinaryPrimitives.ReadUInt32BigEndian(header[4..]);
                if (count == 0 || count > MaxFatSlices) return false;

                int entrySize = magic == FatMagic ? 20 : 32;
                for (int i = 0; i < count; i++)
                {
                    int offset = 8 + i * entrySize;
                    if (offset + 4 > header.Length) return false;
                    if (BinaryPrimitives.ReadInt32BigEndian(header[offset..]) == CpuTypeArm64) return true;
                }
                return false;
            }

            return BinaryPrimitives.ReadUInt32LittleEndian(header) == MachMagic64 &&
                   BinaryPrimitives.ReadInt32LittleEndian(header[4..]) == CpuTypeArm64;
        }

        private sealed class Scope : IDisposable
        {
            private readonly Action _onDispose;
            public Scope(Action onDispose) => _onDispose = onDispose;
            public void Dispose() => _onDispose();
        }
    }
}
