using System;
using System.Globalization;
using System.IO;

namespace Odinsons.ValheimLauncher
{
    /// <summary>The last accepted index envelope and the highest serial ever accepted, kept in one folder.</summary>
    public sealed class RegistryCache
    {
        private readonly string _indexPath;
        private readonly string _serialPath;

        public RegistryCache(string folder)
        {
            _indexPath = Path.Combine(folder, "index.json");
            _serialPath = Path.Combine(folder, "serial.txt");
        }

        public byte[] ReadEnvelope() => TryRead(_indexPath);

        /// <summary>The lowest serial a new index may carry; 0 when nothing was accepted yet.</summary>
        public long ReadSerialFloor()
        {
            byte[] bytes = TryRead(_serialPath);
            if (bytes == null) return 0;
            return long.TryParse(System.Text.Encoding.ASCII.GetString(bytes).Trim(), NumberStyles.None,
                CultureInfo.InvariantCulture, out long serial) ? serial : 0;
        }

        public void Store(byte[] envelope, long serial)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_indexPath));
            WriteAtomically(_indexPath, envelope);
            WriteAtomically(_serialPath, System.Text.Encoding.ASCII.GetBytes(serial.ToString(CultureInfo.InvariantCulture)));
        }

        private static byte[] TryRead(string path)
        {
            try
            {
                return File.Exists(path) ? File.ReadAllBytes(path) : null;
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }

        private static void WriteAtomically(string path, byte[] bytes)
        {
            string temp = path + ".tmp";
            File.WriteAllBytes(temp, bytes);
            File.Move(temp, path, true);
        }
    }
}
