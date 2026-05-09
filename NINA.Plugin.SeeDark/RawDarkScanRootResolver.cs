using System;
using System.IO;

namespace NINA.Plugin.SeeDark {

    /// <summary>
    /// Resolves the directory used to discover raw DARK FITS (stacker, same-night counts, optional purge)
    /// from NINA profile image file path, optionally narrowed when the DARK pattern
    /// places <c>$$IMAGETYPE$$</c> in a path segment (e.g. <c>CALIBRATION\$$IMAGETYPE$$s\</c>).
    /// </summary>
    internal static class RawDarkScanRootResolver {

        public static string Resolve(string ninaImageFilePath, string? ninaDarkPattern) {
            if (string.IsNullOrWhiteSpace(ninaImageFilePath))
                return "";

            var imageRoot = ninaImageFilePath.Trim();
            var narrowed = TryBuildImagetypeSubfolderRoot(imageRoot, ninaDarkPattern);
            if (narrowed != null && Directory.Exists(narrowed))
                return narrowed;

            return Path.GetFullPath(imageRoot);
        }

        /// <summary>
        /// Builds <c>ImagePath</c> + path segments through the first <c>$$IMAGETYPE$$</c> folder token expanded as <c>DARK</c>,
        /// or <c>null</c> if the pattern does not use image type as a folder or path tokens before that are not fixed literals.
        /// </summary>
        private static string? TryBuildImagetypeSubfolderRoot(string imagePath, string? pattern) {
            if (string.IsNullOrWhiteSpace(pattern))
                return null;

            var normalized = pattern.Replace('/', '\\');
            var segments = normalized.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0)
                return null;

            int imagetypeIndex = -1;
            for (int i = 0; i < segments.Length; i++) {
                if (segments[i].IndexOf("$$IMAGETYPE$$", StringComparison.OrdinalIgnoreCase) >= 0) {
                    imagetypeIndex = i;
                    break;
                }
            }
            if (imagetypeIndex < 0)
                return null;

            for (int j = 0; j < imagetypeIndex; j++) {
                if (segments[j].IndexOf("$$", StringComparison.Ordinal) >= 0)
                    return null;
            }

            string current = imagePath;
            for (int j = 0; j <= imagetypeIndex; j++) {
                var seg = segments[j];
                if (j == imagetypeIndex)
                    seg = seg.Replace("$$IMAGETYPE$$", "DARK", StringComparison.OrdinalIgnoreCase);
                if (seg.IndexOf("$$", StringComparison.Ordinal) >= 0)
                    return null;
                current = Path.Combine(current, seg);
            }
            return Path.GetFullPath(current);
        }
    }
}
