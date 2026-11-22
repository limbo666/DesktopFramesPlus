using System;
using System.IO;
using System.Text;

namespace Desktop_Frames
{
    internal static class AtomicFile
    {
        internal static void WriteUtf8(string path, string content, bool replaceExisting = true)
        {
            // Keep the staged file on the same volume as the destination.
            string temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
            bool staged = false;
            try
            {
                using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    staged = true;
                    stream.Write(Encoding.UTF8.GetBytes(content));
                    stream.Flush(flushToDisk: true);
                }

                if (replaceExisting && File.Exists(path))
                    File.Replace(temporaryPath, path, null);
                else
                    File.Move(temporaryPath, path);

                staged = false;
            }
            finally
            {
                if (staged)
                {
                    try { File.Delete(temporaryPath); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.Settings, $"Could not remove temporary file {temporaryPath}: {ex.Message}");
                    }
                }
            }
        }
    }
}
