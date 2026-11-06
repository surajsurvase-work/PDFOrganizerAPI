using System.Security.Cryptography;
using System.Text;

namespace PDFOrganizerAPI.Services
{
   

    public static class EncryptionDecryption
    {
        private static Aes GetEncryptionAlgorithm()
        {
            Aes aes = Aes.Create();
            var key = Encoding.UTF8.GetBytes("2031179320323798");
            var iv = Encoding.UTF8.GetBytes("2032311120311793");
            aes.Key = key;
            aes.IV = iv;
            return aes;
        }
        public static string DecryptString(string cipherText)
        {
            Aes aes = GetEncryptionAlgorithm();
            byte[] buffer = Convert.FromBase64String(cipherText);
            MemoryStream memoryStream = new MemoryStream(buffer);
            ICryptoTransform decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
            CryptoStream cryptoStream = new CryptoStream(memoryStream, decryptor, CryptoStreamMode.Read);
            StreamReader streamReader = new StreamReader(cryptoStream);
            return streamReader.ReadToEnd();
        }
        public static string Encrypt(string clearText)
        {
            byte[] encrypted;
            Aes aes = GetEncryptionAlgorithm();
            ICryptoTransform encryptor = aes.CreateEncryptor(aes.Key, aes.IV);
            using (MemoryStream ms = new MemoryStream())
            {
                using (CryptoStream cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
                {
                    using (StreamWriter sw = new StreamWriter(cs))
                        sw.Write(clearText);
                    encrypted = ms.ToArray();
                }
            }
            var base64 = Convert.ToBase64String(encrypted);
            return base64;
        }
    }
}
