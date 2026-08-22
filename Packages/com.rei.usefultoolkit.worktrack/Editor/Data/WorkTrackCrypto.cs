using System;
using System.Security.Cryptography;
using System.Text;

namespace UsefulToolkit.Editor.WorkTrack
{
    /// <summary>
    /// WorkTrackの保存データをテキストエディタ等で直接読めないようにするための簡易的な暗号化。
    /// 鍵はソースコードに埋め込まれているため第三者の解析からデータを守るものではなく、
    /// あくまで「うっかり素のJSONが見える」ことを防ぐ程度の保護であることに留意する。
    /// </summary>
    internal static class WorkTrackCrypto
    {
        private const string Passphrase = "UsefulToolkit.WorkTrack.v1";

        public static string Encrypt(string plainText)
        {
            using var aes = Aes.Create();
            aes.Key = DeriveKey();
            aes.GenerateIV();

            using var encryptor = aes.CreateEncryptor();
            var plainBytes = Encoding.UTF8.GetBytes(plainText);
            var cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

            var combined = new byte[aes.IV.Length + cipherBytes.Length];
            Buffer.BlockCopy(aes.IV, 0, combined, 0, aes.IV.Length);
            Buffer.BlockCopy(cipherBytes, 0, combined, aes.IV.Length, cipherBytes.Length);

            return Convert.ToBase64String(combined);
        }

        public static string Decrypt(string cipherText)
        {
            var combined = Convert.FromBase64String(cipherText);

            using var aes = Aes.Create();
            aes.Key = DeriveKey();

            var ivLength = aes.BlockSize / 8;
            var iv = new byte[ivLength];
            Buffer.BlockCopy(combined, 0, iv, 0, ivLength);
            aes.IV = iv;

            using var decryptor = aes.CreateDecryptor();
            var cipherBytes = new byte[combined.Length - ivLength];
            Buffer.BlockCopy(combined, ivLength, cipherBytes, 0, cipherBytes.Length);

            var plainBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
            return Encoding.UTF8.GetString(plainBytes);
        }

        private static byte[] DeriveKey()
        {
            using var sha = SHA256.Create();
            return sha.ComputeHash(Encoding.UTF8.GetBytes(Passphrase));
        }
    }
}
