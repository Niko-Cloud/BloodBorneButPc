using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace BloodborneLauncher.Core
{
    /// <summary>
    /// Secrets kept in settings.json encrypted for this Windows user (DPAPI): another user or PC
    /// cannot read them, and the file can be shared without them. Stored as "dpapi:" + base64; a
    /// value without the prefix is plain text from an older launcher, which reads as it is.
    /// Elsewhere (Linux): AES-GCM with a key of this user's own (secret.key beside settings.json,
    /// readable by this user only), stored as "local:" + base64 of nonce, tag and ciphertext.
    /// </summary>
    public static class Secret
    {
        private const string Prefix = "dpapi:";
        private const string LocalPrefix = "local:";
        private const int UiForbidden = 1; // CRYPTPROTECT_UI_FORBIDDEN

        [StructLayout(LayoutKind.Sequential)]
        private struct Blob { public int Size; public IntPtr Data; }

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CryptProtectData(ref Blob input, string? description, IntPtr entropy,
            IntPtr reserved, IntPtr prompt, int flags, out Blob output);

        [DllImport("crypt32.dll", SetLastError = true)]
        private static extern bool CryptUnprotectData(ref Blob input, IntPtr description, IntPtr entropy,
            IntPtr reserved, IntPtr prompt, int flags, out Blob output);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr memory);

        public static bool IsProtected(string stored) =>
            stored.StartsWith(Prefix, StringComparison.Ordinal) || stored.StartsWith(LocalPrefix, StringComparison.Ordinal);

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Interoperability", "CA1416", Justification = "Windows returns DPAPI first")]
        public static string Protect(string plain)
        {
            if (plain.Length == 0) return "";
            if (!OperatingSystem.IsWindows()) return LocalProtect(plain);
            var bytes = Transform(Encoding.UTF8.GetBytes(plain), encrypt: true);
            return bytes == null ? "" : Prefix + Convert.ToBase64String(bytes);
        }

        /// <summary>The plain text; empty when it cannot be read (another Windows user's).</summary>
        public static string Unprotect(string stored)
        {
            if (!IsProtected(stored)) return stored;
            if (stored.StartsWith(LocalPrefix, StringComparison.Ordinal)) return LocalUnprotect(stored[LocalPrefix.Length..]);
            // A Windows user's DPAPI value: only that user on that PC can read it.
            if (!OperatingSystem.IsWindows()) return "";
            try
            {
                var bytes = Transform(Convert.FromBase64String(stored[Prefix.Length..]), encrypt: false);
                return bytes == null ? "" : Encoding.UTF8.GetString(bytes);
            }
            catch (FormatException)
            {
                return "";
            }
        }

        // ---------------------------------------------------------------- Linux

        private static string KeyFile => Path.Combine(Paths.ConfigDir, "secret.key");

        /// <summary>This user's key, made on first use (readable by this user only).</summary>
        [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
        private static byte[]? LocalKey(bool create)
        {
            try
            {
                if (File.Exists(KeyFile) && File.ReadAllBytes(KeyFile) is { Length: 32 } key) return key;
                if (!create) return null;
                Directory.CreateDirectory(Paths.ConfigDir);
                key = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
                using (var file = new FileStream(KeyFile, new FileStreamOptions
                       {
                           Mode = FileMode.Create, Access = FileAccess.Write,
                           UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite,
                       }))
                    file.Write(key);
                return key;
            }
            catch (Exception)
            {
                return null;
            }
        }

        [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
        private static string LocalProtect(string plain)
        {
            if (LocalKey(create: true) is not { } key) return "";
            var data = Encoding.UTF8.GetBytes(plain);
            var nonce = System.Security.Cryptography.RandomNumberGenerator.GetBytes(12);
            var tag = new byte[16];
            var cipher = new byte[data.Length];
            using (var aes = new System.Security.Cryptography.AesGcm(key, tag.Length))
                aes.Encrypt(nonce, data, cipher, tag);
            return LocalPrefix + Convert.ToBase64String(nonce.Concat(tag).Concat(cipher).ToArray());
        }

        /// <summary>The plain text; empty when it cannot be read (another user's, or the key is gone).</summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Interoperability", "CA1416", Justification = "local: values are made on Linux only")]
        private static string LocalUnprotect(string encoded)
        {
            try
            {
                if (LocalKey(create: false) is not { } key) return "";
                var all = Convert.FromBase64String(encoded);
                if (all.Length < 28) return "";
                var plain = new byte[all.Length - 28];
                using (var aes = new System.Security.Cryptography.AesGcm(key, 16))
                    aes.Decrypt(all[..12], all[28..], all[12..28], plain);
                return Encoding.UTF8.GetString(plain);
            }
            catch (Exception)
            {
                return "";
            }
        }

        private static byte[]? Transform(byte[] bytes, bool encrypt)
        {
            var input = new Blob { Size = bytes.Length, Data = Marshal.AllocHGlobal(Math.Max(bytes.Length, 1)) };
            Blob output = default;
            try
            {
                Marshal.Copy(bytes, 0, input.Data, bytes.Length);
                var ok = encrypt
                    ? CryptProtectData(ref input, "Bloodborne launcher", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, UiForbidden, out output)
                    : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, UiForbidden, out output);
                if (!ok || output.Size < 0 || output.Size > 1 << 16) return null;
                var result = new byte[output.Size];
                Marshal.Copy(output.Data, result, 0, result.Length);
                return result;
            }
            finally
            {
                Marshal.Copy(new byte[bytes.Length], 0, input.Data, bytes.Length);
                Marshal.FreeHGlobal(input.Data);
                if (output.Data != IntPtr.Zero) LocalFree(output.Data);
            }
        }
    }
}
