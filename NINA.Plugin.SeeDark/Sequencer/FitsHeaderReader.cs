using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace NINA.Plugin.SeeDark.Sequencer {

    internal static class FitsHeaderReader {

        internal static Dictionary<string, string>? ReadHeaders(string path)
            => ReadHeaders(path, out _);

        internal static Dictionary<string, string>? ReadHeaders(string path, out long dataOffset) {
            dataOffset = 0;
            try {
                var h = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                var buf = new byte[2880];
                bool end = false;
                long off = 0;
                while (!end && fs.Read(buf, 0, 2880) == 2880) {
                    off += 2880;
                    for (int i = 0; i < 36 && !end; i++) {
                        var kw = Encoding.ASCII.GetString(buf, i * 80, 8).TrimEnd();
                        if (kw == "END") { end = true; break; }
                        if (buf[i * 80 + 8] == (byte)'=') {
                            var vc = Encoding.ASCII.GetString(buf, i * 80 + 10, 70);
                            h[kw] = ParseValue(vc);
                        }
                    }
                }
                dataOffset = off;
                return h;
            } catch { return null; }
        }

        internal static string ParseValue(string vc) {
            var v = vc.TrimStart();
            if (v.StartsWith("'")) {
                int end = v.IndexOf('\'', 1);
                return end < 0 ? v[1..].TrimEnd() : v[1..end].TrimEnd();
            }
            int slash = v.IndexOf('/');
            return (slash < 0 ? v : v[..slash]).Trim();
        }
    }
}
