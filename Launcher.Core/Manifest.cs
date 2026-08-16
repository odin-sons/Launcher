using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Odinsons.ValheimLauncher
{
    /// <summary>
    /// Build manifest: a list of files with checksums and sizes.
    ///
    /// The format is plain text, one line per entry:
    ///
    ///     MANIFEST 3 sha256
    ///     e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 5 .doorstop_version
    ///     2c26b46b68ffc68ff99b453c1d30413413422d706483bfa0f98a5e886266e7ae 241 changelog.txt
    ///
    /// The first line is a format marker, version, and hashing algorithm. The algorithm is
    /// deliberately pulled into the header, and it already paid off: moving from md5 to sha256
    /// didn't need a new format version. The algorithm itself is chosen in <see cref="FileHash"/>,
    /// along with the benchmarks for why.
    ///
    /// Then one line per file: hash, size, path. The path comes last and is taken as the entire
    /// rest of the line — otherwise names containing spaces would break, and the build has
    /// plenty of them (e.g. "Old Bearded One spawn_odin_priest.yml"). This is exactly why the
    /// path comes last in both md5sum and Debian's Release files, where the shape was borrowed from.
    ///
    /// Why not JSON or YAML: the data is a flat, uniform table with no nesting, and JSON would
    /// triple its size while losing line-by-line diffability. YAML is outright dangerous here:
    /// a string of all digits becomes a number in it, and a hash like 1234567890 is a perfectly
    /// valid value.
    /// </summary>
    public static class Manifest
    {
        public const string Marker = "MANIFEST";
        public const int CurrentVersion = 3;
        public const string DefaultAlgorithm = FileHash.AlgorithmName;

        public readonly record struct Entry(string Path, string Hash, long Size);

        public static List<Entry> Read(TextReader reader)
        {
            string header = ReadMeaningfulLine(reader)
                            ?? throw new InvalidDataException("Manifest is empty");

            string[] headerParts = header.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (headerParts.Length < 2 || headerParts[0] != Marker)
                throw new InvalidDataException($"Not a manifest: first line is '{header}'");

            if (!int.TryParse(headerParts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int version))
                throw new InvalidDataException($"Malformed manifest version in '{header}'");

            if (version > CurrentVersion)
                throw new InvalidDataException(
                    $"Manifest version {version} is newer than supported {CurrentVersion}");

            // The algorithm is actually verified, not just parsed. Otherwise a manifest
            // computed with a different algorithm wouldn't raise an error: every hash would
            // simply mismatch, the launcher would consider the whole build corrupted,
            // redownload all of it — and mismatch again. A clear message instead of that.
            string algorithm = headerParts.Length >= 3 ? headerParts[2] : DefaultAlgorithm;

            if (!string.Equals(algorithm, DefaultAlgorithm, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    $"Manifest uses '{algorithm}' hashes, this launcher only understands '{DefaultAlgorithm}'");

            var entries = new List<Entry>();
            int lineNumber = 1;

            for (string line = reader.ReadLine(); line is not null; line = reader.ReadLine())
            {
                lineNumber++;

                if (line.Length == 0 || line[0] == '#') continue;

                // Hash and size come before the first two spaces, everything else is the path.
                int firstGap = line.IndexOf(' ');
                if (firstGap <= 0)
                    throw new InvalidDataException($"Malformed entry at line {lineNumber}: '{line}'");

                int secondGap = line.IndexOf(' ', firstGap + 1);
                if (secondGap <= firstGap + 1)
                    throw new InvalidDataException($"Malformed entry at line {lineNumber}: '{line}'");

                string hash = line.Substring(0, firstGap);
                string sizeText = line.Substring(firstGap + 1, secondGap - firstGap - 1);
                string path = line.Substring(secondGap + 1).Trim('/').Replace("\\", "/");

                if (!long.TryParse(sizeText, NumberStyles.Integer, CultureInfo.InvariantCulture, out long size))
                    throw new InvalidDataException($"Malformed size at line {lineNumber}: '{sizeText}'");

                if (path.Length == 0)
                    throw new InvalidDataException($"Empty path at line {lineNumber}");

                entries.Add(new Entry(path, hash, size));
            }

            return entries;
        }

        public static List<Entry> ReadFile(string path)
        {
            using var reader = new StreamReader(path, Encoding.UTF8);
            return Read(reader);
        }

        public static void Write(TextWriter writer, IEnumerable<Entry> entries,
                                 string algorithm = DefaultAlgorithm)
        {
            writer.Write(Marker);
            writer.Write(' ');
            writer.Write(CurrentVersion.ToString(CultureInfo.InvariantCulture));
            writer.Write(' ');
            writer.Write(algorithm);
            writer.Write('\n');

            foreach (Entry entry in entries)
            {
                writer.Write(entry.Hash);
                writer.Write(' ');
                writer.Write(entry.Size.ToString(CultureInfo.InvariantCulture));
                writer.Write(' ');
                writer.Write(entry.Path);
                writer.Write('\n');
            }
        }

        public static void WriteFile(string path, IEnumerable<Entry> entries,
                                     string algorithm = DefaultAlgorithm)
        {
            // No BOM, and \n line endings: the file is read fine by Windows, Linux, and plain diff.
            using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
            Write(writer, entries, algorithm);
        }

        private static string ReadMeaningfulLine(TextReader reader)
        {
            for (string line = reader.ReadLine(); line is not null; line = reader.ReadLine())
                if (line.Length > 0 && line[0] != '#') return line;

            return null;
        }
    }
}
