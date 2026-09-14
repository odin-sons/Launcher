using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Odinsons.ValheimLauncher
{
    public class IniFile
    {
        private readonly string _path;
        private readonly Dictionary<string, Dictionary<string, string>> _sections;

        public IniFile(string path)
        {
            _path = path ?? throw new ArgumentNullException(nameof(path));
            _sections = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

            try
            {
                if (!File.Exists(_path))
                {
                    File.WriteAllText(_path, "", new UTF8Encoding(false));
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to create file {_path}: {ex.Message}", ex);
            }
        }

        public async Task LoadAsync()
        {
            try
            {
                if (!File.Exists(_path))
                    return;

                string[] lines = await File.ReadAllLinesAsync(_path, Encoding.UTF8);
                ParseLines(lines);
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to read file {_path}: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Synchronous counterpart of LoadAsync — for call sites that can't await, most notably
        /// a constructor that needs the result before returning (MainWindow's window-placement
        /// restore, which has to run before Show()). Blocking on LoadAsync via
        /// .GetAwaiter().GetResult() from a UI-thread constructor deadlocked instead: whatever
        /// synchronization context that thread owns is exactly what File.ReadAllLinesAsync's
        /// continuation needs to resume on, and that thread is the one sitting blocked waiting
        /// for it.
        /// </summary>
        public void Load()
        {
            try
            {
                if (!File.Exists(_path))
                    return;

                ParseLines(File.ReadAllLines(_path, Encoding.UTF8));
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to read file {_path}: {ex.Message}", ex);
            }
        }

        private void ParseLines(string[] lines)
        {
            string currentSection = "";
            foreach (var line in lines.AsSpan())
            {
                string trimmedLine = line.Trim();
                if (string.IsNullOrEmpty(trimmedLine) || trimmedLine.StartsWith(';') || trimmedLine.StartsWith('#'))
                    continue;

                if (trimmedLine.StartsWith('[') && trimmedLine.EndsWith(']'))
                {
                    currentSection = trimmedLine[1..^1].Trim();
                    _sections.TryAdd(currentSection, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
                }
                else if (!string.IsNullOrEmpty(currentSection))
                {
                    int separatorIndex = trimmedLine.IndexOf('=');
                    if (separatorIndex >= 0)
                    {
                        string key = trimmedLine[..separatorIndex].Trim();
                        string value = trimmedLine[(separatorIndex + 1)..].Trim();
                        _sections[currentSection][key] = value;
                    }
                }
            }
        }

        public async Task WriteAsync(string key, string value, string section)
        {
            SetValue(key, value, section);

            try
            {
                using var writer = new StreamWriter(_path, false, new UTF8Encoding(false));
                foreach (var sec in _sections)
                {
                    await writer.WriteLineAsync($"[{sec.Key}]");
                    foreach (var kvp in sec.Value)
                    {
                        await writer.WriteLineAsync($"{kvp.Key}={kvp.Value}");
                    }
                    await writer.WriteLineAsync();
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to write file {_path}: {ex.Message}", ex);
            }
        }

        /// <summary>Synchronous counterpart of WriteAsync — same deadlock reasoning as Load.</summary>
        public void Write(string key, string value, string section)
        {
            SetValue(key, value, section);

            try
            {
                using var writer = new StreamWriter(_path, false, new UTF8Encoding(false));
                foreach (var sec in _sections)
                {
                    writer.WriteLine($"[{sec.Key}]");
                    foreach (var kvp in sec.Value)
                    {
                        writer.WriteLine($"{kvp.Key}={kvp.Value}");
                    }
                    writer.WriteLine();
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to write file {_path}: {ex.Message}", ex);
            }
        }

        private void SetValue(string key, string value, string section)
        {
            if (!_sections.ContainsKey(section))
                _sections[section] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            _sections[section][key] = value;
        }

        public string Read(string key, string section)
        {
            return _sections.TryGetValue(section, out var sectionDict) && sectionDict.TryGetValue(key, out var value)
                ? value
                : null;
        }
    }
}