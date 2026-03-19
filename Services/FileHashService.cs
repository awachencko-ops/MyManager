using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Replica
{
    public static class FileHashService
    {
        public static bool TryComputeSha256(string path, out string hash, out string error)
        {
            hash = string.Empty;
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(path))
            {
                error = "path is empty";
                return false;
            }

            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var sha = SHA256.Create();
                var bytes = sha.ComputeHash(stream);
                hash = Convert.ToHexString(bytes);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public static string ComputeSha256OrEmpty(string path)
        {
            return TryComputeSha256(path, out var hash, out _) ? hash : string.Empty;
        }
    }
}
