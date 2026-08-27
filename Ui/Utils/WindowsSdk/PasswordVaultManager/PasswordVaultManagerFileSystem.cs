using System;
using System.IO;
using System.Threading.Tasks;

namespace _1RM.Utils.WindowsSdk.PasswordVaultManager
{
    internal class PasswordVaultManagerFileSystem : IPasswordManager
    {
        private readonly string _localFolder;
        public PasswordVaultManagerFileSystem(string localFolder)
        {
            _localFolder = localFolder;
        }
        //private static readonly string LocalFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".1Remote");

        private string TokenPathOf(string key) => Path.Combine(_localFolder, key, "token");

        /// <summary>
        /// The real implementation. Protecting is an async WinRT call, so everything that can await does —
        /// both callers in <c>SecondaryVerificationHelper</c> already run in an async method.
        /// </summary>
        public async Task<string?> RetrieveAsync(string key)
        {
            try
            {
                var passwordFile = TokenPathOf(key);
                if (File.Exists(passwordFile))
                {
                    var encrypted = File.ReadAllText(passwordFile);
                    return await DataProtectionForLocal.Unprotect(encrypted).ConfigureAwait(false);
                }
            }
            catch (Exception)
            {
            }
            return null;
        }

        /// <summary>
        /// Protects and writes before returning. It used to hand both to <c>Task.Factory.StartNew</c> and
        /// then read the same key straight back, so the read raced the write and normally lost — leaving the
        /// caller believing the value had not been stored.
        /// </summary>
        public async Task AddAsync(string key, string password)
        {
            var dir = Path.Combine(_localFolder, key);
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var encrypted = await DataProtectionForLocal.Protect(password).ConfigureAwait(false);
            if (encrypted != null)
                File.WriteAllText(TokenPathOf(key), encrypted);
        }

        /// <summary>
        /// <see cref="IPasswordManager"/> is synchronous because the Credential Locker implementation behind
        /// it is. Going through <c>Task.Run</c> rather than blocking the async method directly is what makes
        /// this safe to call from the UI thread: the continuation runs on the pool and never needs the thread
        /// that is waiting here, so there is nothing to deadlock against. Prefer <see cref="RetrieveAsync"/>.
        /// </summary>
        public string? Retrieve(string key)
        {
            return Task.Run(() => RetrieveAsync(key)).GetAwaiter().GetResult();
        }

        /// <inheritdoc cref="Retrieve"/>
        public void Add(string key, string password)
        {
            Task.Run(() => AddAsync(key, password)).GetAwaiter().GetResult();
        }

        public void Remove(string key)
        {
            var passwordFile = Path.Combine(_localFolder, key);
            if (Directory.Exists(passwordFile))
            {
                Directory.Delete(passwordFile, true);
            }
        }
    }
}
