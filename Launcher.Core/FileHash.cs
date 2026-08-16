using System;
using System.IO;
using System.Security.Cryptography;

namespace Odinsons.ValheimLauncher
{
    /// <summary>
    /// Checksums for build files. The one place where the algorithm is chosen.
    ///
    /// SHA-256 was picked over MD5, and it isn't about cryptographic strength — it's simply
    /// faster. Benchmarked on a 571 MB asset bundle from the build, on an i5-12600K:
    ///
    ///     in memory:   MD5  633 MB/s    SHA-256  2070 MB/s
    ///     from disk:   MD5  425 MB/s    SHA-256   790 MB/s
    ///
    /// The reason is the SHA-NI instructions, present in CPUs since roughly 2017; MD5 has no
    /// hardware acceleration and never will. A full check of the whole build costs 1.3s of CPU
    /// time instead of 4.2s. On very old machines without SHA-NI the ratio flips, but there
    /// everything is disk-bound anyway.
    ///
    /// The strength comes for free on top of that: producing an MD5 collision has long been
    /// practical, SHA-256 hasn't. On its own this doesn't protect against tampering in transit
    /// (the manifest arrives from the same place as the files), but it makes signing the
    /// manifest meaningful, should that ever be needed.
    /// </summary>
    public static class FileHash
    {
        /// <summary>Algorithm name in the manifest header.</summary>
        public const string AlgorithmName = "sha256";

        /// <summary>
        /// A file's hash, lowercase hexadecimal.
        /// Computed as a stream: asset bundles run into the hundreds of megabytes, and there's
        /// no reason to read them entirely into memory just for a checksum — especially since
        /// the check runs across several threads at once.
        /// </summary>
        public static string OfFile(string path)
        {
            using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                                          bufferSize: 1024 * 1024, FileOptions.SequentialScan);
            return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }

        public static string OfBytes(ReadOnlySpan<byte> data) =>
            Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
    }
}
