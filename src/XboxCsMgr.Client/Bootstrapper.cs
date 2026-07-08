using System;
using Stylet;
using XboxCsMgr.Client.ViewModels;
using XboxCsMgr.XboxLive;
using XboxCsMgr.Helpers.Win32;
using Newtonsoft.Json;
using System.Collections.Generic;
using XboxCsMgr.XboxLive.Model.Authentication;
using XboxCsMgr.XboxLive.Services;
using System.Diagnostics;
using System.Linq;

namespace XboxCsMgr.Client
{
    public class AppBootstrapper : Bootstrapper<ShellViewModel>
    {
        public static XboxLiveConfig? XblConfig { get; internal set; }

        // Expose the auth service so ShellViewModel can use it after login
        internal static AuthenticateService? AuthService { get; private set; }

        protected override void ConfigureIoC(StyletIoC.IStyletIoCBuilder builder)
        {
            base.ConfigureIoC(builder);
            builder.Bind<IDialogFactory>().ToAbstractFactory();
        }

        protected override void OnStart()
        {
            // Create the AuthenticateService with null config — it handles null safely.
            // Actual authentication happens in ShellViewModel.OnViewLoaded after
            // the main window is fully started (so WindowManager is usable).
            AuthService = new AuthenticateService(XblConfig!);

            // Try to load any existing wincred tokens into a static cache for ShellViewModel
            LoadXblTokenCredentials();

            base.OnStart();
        }

        private static string? _cachedDeviceToken;
        private static string? _cachedUserToken;
        public static string? CachedDeviceToken => _cachedDeviceToken;
        public static string? CachedUserToken   => _cachedUserToken;

        private void LoadXblTokenCredentials()
        {
            Dictionary<string, string>? currentCredentials;
            try { currentCredentials = CredentialUtil.EnumerateCredentials(); }
            catch { return; }
            if (currentCredentials == null) return;

            var xblCredentials = currentCredentials
                .Where(k => (k.Key.Contains("Xbl|") || k.Key.Contains("XblGrts|"))
                         && (k.Key.Contains("Dtoken") || k.Key.Contains("Utoken")))
                .ToDictionary(p => p.Key, p => p.Value);

            foreach (var credential in xblCredentials)
            {
                try
                {
                    var fixedJson = credential.Value.TrimEnd('X').TrimEnd('\0');
                    if (string.IsNullOrWhiteSpace(fixedJson) || !fixedJson.Contains("{")) continue;
                    XboxLiveToken? token = JsonConvert.DeserializeObject<XboxLiveToken>(fixedJson);
                    if (token?.TokenData == null || token.TokenData.NotAfter <= DateTime.UtcNow) continue;
                    if (credential.Key.Contains("Dtoken") && _cachedDeviceToken == null)
                        _cachedDeviceToken = token.TokenData.Token;
                    else if (credential.Key.Contains("Utoken") && _cachedUserToken == null)
                        if (token.TokenData.Token != "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")
                            _cachedUserToken = token.TokenData.Token;
                }
                catch { /* skip malformed entries */ }
            }
        }
    }
}
