using Newtonsoft.Json;
using _1RM.Utils;
using Shawn.Utils;

namespace _1RM.Service.Backup
{
    /// <summary>
    /// Where backups are uploaded. One destination is enough: this is an off-machine copy of a personal
    /// configuration, not a fleet backup policy.
    /// </summary>
    public class WebDavConfig : NotifyPropertyChangedBase
    {
        private string _url = "";
        /// <summary>
        /// The collection to put archives in, for example
        /// <c>https://cloud.example.com/remote.php/dav/files/me/1Remote/</c>.
        /// </summary>
        public string Url
        {
            get => _url;
            set
            {
                if (SetAndNotifyIfChanged(ref _url, value?.Trim() ?? ""))
                {
                    RaisePropertyChanged(nameof(IsUsable));
                    RaisePropertyChanged(nameof(IsHttps));
                    RaisePropertyChanged(nameof(IsInsecure));
                }
            }
        }

        private string _userName = "";
        public string UserName
        {
            get => _userName;
            set => SetAndNotifyIfChanged(ref _userName, value ?? "");
        }

        [JsonProperty(nameof(Password))]
        private string EncryptedPassword { get; set; } = "";

        /// <summary>
        /// Plain to the rest of the app, enciphered in the profile — the same split every other stored
        /// secret uses, including an empty one staying empty rather than becoming cipher text.
        /// </summary>
        [JsonIgnore]
        public string Password
        {
            get => string.IsNullOrEmpty(EncryptedPassword)
                ? ""
                : UnSafeStringEncipher.DecryptOrReturnOriginalString(EncryptedPassword);
            set
            {
                var plain = value ?? "";
                if (plain == Password) return;
                EncryptedPassword = plain.Length == 0 ? "" : UnSafeStringEncipher.SimpleEncrypt(plain);
                RaisePropertyChanged();
            }
        }

        private bool _allowInsecureHttp;
        /// <summary>
        /// Lets a plain <c>http://</c> destination be used. Off by default and deliberately awkward: the
        /// client sends Basic authentication pre-emptively and the archive it uploads is the entire
        /// configuration, credential database included, so an http destination puts the password and every
        /// stored secret on the wire in the clear. The only defensible use is a loopback or lab endpoint.
        /// </summary>
        public bool AllowInsecureHttp
        {
            get => _allowInsecureHttp;
            set
            {
                if (SetAndNotifyIfChanged(ref _allowInsecureHttp, value))
                {
                    RaisePropertyChanged(nameof(IsUsable));
                    RaisePropertyChanged(nameof(IsInsecure));
                }
            }
        }

        [JsonIgnore]
        public bool IsHttps => Url.StartsWith("https://", System.StringComparison.OrdinalIgnoreCase);

        [JsonIgnore]
        private bool IsPlainHttp => Url.StartsWith("http://", System.StringComparison.OrdinalIgnoreCase);

        /// <summary>True when the destination is configured and is actually sending traffic in the clear.</summary>
        [JsonIgnore]
        public bool IsInsecure => IsPlainHttp && AllowInsecureHttp;

        [JsonIgnore]
        public bool IsUsable => IsHttps || (IsPlainHttp && AllowInsecureHttp);

        /// <summary>The collection URL with exactly one trailing slash, which is how WebDAV names a folder.</summary>
        public string NormalizedUrl => Url.TrimEnd('/') + "/";

        /// <summary>The absolute URL of one archive inside the collection.</summary>
        public string UrlOf(string fileName) => NormalizedUrl + System.Uri.EscapeDataString(fileName);
    }
}
