using System;
using System.IO;

namespace Desktop_Frames
{
    internal static class ProfileDirectory
    {
        internal static string GetRegisteredPath(string root, string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name.EndsWith('.') || name.EndsWith(' ') ||
                name.IndexOfAny(new[] { '/', '\\', ':' }) >= 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new IOException("Invalid profile directory name.");

            string path = Path.Combine(root, name);
            if (!IsLocalDirectory(root) || !IsLocalDirectory(path))
                throw new IOException("The profile directory must be a physical directory inside Profiles.");
            return path;
        }

        internal static bool IsLocalDirectory(string path)
        {
            try
            {
                const FileAttributes directoryFlags = FileAttributes.Directory | FileAttributes.ReparsePoint;
                return (File.GetAttributes(path) & directoryFlags) == FileAttributes.Directory;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }
    }
}
