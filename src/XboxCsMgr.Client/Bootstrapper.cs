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
        private AuthenticateService? authenticateService;
        private string? DeviceToken { get; set; }
        private string? UserToken { get; set; }

        protected override void ConfigureIoC(StyletIoC.IStyletIoCBuilder builder)
        {
            base.ConfigureIoC(builder);
            builder.Bind<IDialogFactory>().ToAbstractFactory();
        }

        protected override async void OnStart()
        {
            authenticateService = new AuthenticateService(XblConfig);
            LoadXblTokenCredentials();

            // Try wincred tokens if we have both
            if (!string.IsNullOrEmpty(UserToken) && !string.IsNullOrEmpty(DeviceToken))
            {
                try
                {
                    var result = await authenticateService.AuthorizeXsts(UserToken, DeviceToken);
                    if (result != null)
                    {
                        Debug.WriteLine("Authorized via wincred!");
                        XblConfig = new XboxLiveConfig(result.Token, result.DisplayClaims.XboxUserIdentity[0]);
                        this.RootViewModel.OnAuthComplete();
                        base.OnStart();
                        return;
                    }
                }
                catch (Exception ex) { Debug.WriteLine("Wincred XSTS failed: " + ex.Message); }
            }

            // No valid wincred tokens — open the WebView2 login dialog
            var wm = this.Container.Get<IWindowManager>();
            var loginVm = new LoginViewModel();
            this.RootViewModel.LoginView = loginVm;
            bool? loginResult = wm.ShowDialog(loginVm);

            if (loginResult == true && !string.IsNullOrEmpty(loginVm.AccessToken))
            {
                try
                {
                    var deviceResponse = await authenticateService.AuthenticateDeviceToken(loginVm.AccessToken, "10.0.19041");
                    var userResponse   = await authenticateService.AuthenticateUser(loginVm.AccessToken);
                    DeviceToken = deviceResponse?.Token;
                    UserToken   = userResponse?.Token;

                    if (!string.IsNullOrEmpty(UserToken) && !string.IsNullOrEmpty(DeviceToken))
                    {
                        var xsts = await authenticateService.AuthorizeXsts(UserToken, DeviceToken);
                        if (xsts != null)
                        {
                            XblConfig = new XboxLiveConfig(xsts.Token, xsts.DisplayClaims.XboxUserIdentity[0]);
                            this.RootViewModel.OnAuthComplete();
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show("Auth failed: " + ex.Message, "Xbox Login Error",
                        System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                }
            }

            base.OnStart();
        }

        private void LoadXblTokenCredentials()
        {
            Dictionary<string, string>? currentCredentials;
            try { currentCredentials = CredentialUtil.EnumerateCredentials(); }
            catch { return; }
            if (currentCredentials == null) return;

            // Accept both "Xbl|" (old format) and "XblGrts|" (new Gaming Services format)
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
                    if (credential.Key.Contains("Dtoken") && DeviceToken == null)
                        DeviceToken = token.TokenData.Token;
                    else if (credential.Key.Contains("Utoken") && UserToken == null)
                        if (token.TokenData.Token != "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")
                            UserToken = token.TokenData.Token;
                }
                catch { /* skip malformed entries */ }
            }
        }
    }
}
