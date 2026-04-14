using System;
using System.IO;
using System.Security.Cryptography;

namespace Core
{
    public class CryptoManager
    {
        private const int KeySize = 32; // 256 bits
        private const int Iterations = 100000;

        public static byte[] GenerateRandomBytes(int length)
        {
            byte[] bytes = new byte[length];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }
            return bytes;
        }

        public static byte[] DeriveKeyFromPassword(string password, byte[] salt)
        {
            using (var pbkdf2 = new Rfc2898DeriveBytes(password, salt, Iterations, HashAlgorithmName.SHA256))
            {
                return pbkdf2.GetBytes(KeySize);
            }
        }

        public static byte[] WrapKey(byte[] mdk, byte[] kek)
        {
            using (Aes aes = Aes.Create())
            {
                aes.Key = kek;
                aes.GenerateIV();
                using (MemoryStream ms = new MemoryStream())
                {
                    ms.Write(aes.IV, 0, aes.IV.Length);
                    using (CryptoStream cs = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write))
                    {
                        cs.Write(mdk, 0, mdk.Length);
                    }
                    return ms.ToArray();
                }
            }
        }

        public static byte[] UnwrapKey(byte[] wrappedMdk, byte[] kek)
        {
            using (Aes aes = Aes.Create())
            {
                aes.Key = kek;
                byte[] iv = new byte[16];
                Array.Copy(wrappedMdk, 0, iv, 0, 16);
                aes.IV = iv;

                using (MemoryStream ms = new MemoryStream())
                {
                    using (CryptoStream cs = new CryptoStream(new MemoryStream(wrappedMdk, 16, wrappedMdk.Length - 16), aes.CreateDecryptor(), CryptoStreamMode.Read))
                    {
                        cs.CopyTo(ms);
                    }
                    return ms.ToArray();
                }
            }
        }

        public static async System.Threading.Tasks.Task EncryptFileAsync(string inputFile, string outputFile, byte[] mdk, IProgress<int> progress = null)
        {
            using (Aes aes = Aes.Create())
            {
                aes.Key = mdk;
                aes.GenerateIV();

                using (FileStream fsOutput = new FileStream(outputFile, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
                {
                    await fsOutput.WriteAsync(aes.IV, 0, aes.IV.Length);
                    using (CryptoStream cs = new CryptoStream(fsOutput, aes.CreateEncryptor(), CryptoStreamMode.Write))
                    using (FileStream fsInput = new FileStream(inputFile, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true))
                    {
                        byte[] buffer = new byte[81920];
                        int bytesRead;
                        long totalRead = 0;
                        long fileLength = fsInput.Length;

                        while ((bytesRead = await fsInput.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        {
                            await cs.WriteAsync(buffer, 0, bytesRead);
                            totalRead += bytesRead;
                            if (fileLength > 0 && progress != null)
                            {
                                int percentage = (int)((totalRead * 100) / fileLength);
                                progress.Report(percentage);
                            }
                        }
                        if (!cs.HasFlushedFinalBlock)
                            cs.FlushFinalBlock();
                    }
                }
            }
        }

        public static async System.Threading.Tasks.Task DecryptFileAsync(string inputFile, string outputFile, byte[] mdk, IProgress<int> progress = null)
        {
            using (Aes aes = Aes.Create())
            {
                aes.Key = mdk;

                using (FileStream fsInput = new FileStream(inputFile, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true))
                {
                    byte[] iv = new byte[16];
                    int read = await fsInput.ReadAsync(iv, 0, iv.Length);
                    if (read < 16) throw new Exception("Invalid file format");
                    aes.IV = iv;
                    long fileLength = fsInput.Length - 16;
                    fileLength = fileLength > 0 ? fileLength : 1;

                    using (CryptoStream cs = new CryptoStream(fsInput, aes.CreateDecryptor(), CryptoStreamMode.Read))
                    using (FileStream fsOutput = new FileStream(outputFile, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
                    {
                        byte[] buffer = new byte[81920];
                        int bytesRead;
                        long totalRead = 0;

                        while ((bytesRead = await cs.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        {
                            await fsOutput.WriteAsync(buffer, 0, bytesRead);
                            totalRead += bytesRead;
                            if (progress != null)
                            {
                                int percentage = (int)((totalRead * 100) / fileLength);
                                if (percentage > 100) percentage = 100;
                                progress.Report(percentage);
                            }
                        }
                    }
                }
            }
        }
    }
}
