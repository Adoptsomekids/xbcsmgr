using Stylet;
using System;
using System.Windows;
using XboxCsMgr.Client.Events;
using XboxCsMgr.XboxLive;
using XboxCsMgr.XboxLive.Services;

namespace XboxCsMgr.Client.ViewModels
{
    public interface IDialogFactory
    {
        LoginViewModel CreateLoginDialog();
    }

    /// <summary>
    /// The ShellViewModel represents the ShellView which is the base window
    /// of the application. It's responsible for containing and managing other
    /// sub-views, and triggering login after the window is ready.
    /// </summary>
    public class ShellViewModel : Screen, IHandle<LoadSaveDetailsEvent>
    {
        private readonly IWindowManager windowManager;
        private readonly IDialogFactory dialogFactory;
        private readonly IEventAggregator _events;

        private XboxLiveConfig? _xblConfig => AppBootstrapper.XblConfig;

        private GameViewModel? _gameView;
        public GameViewModel? GameView
        {
            get => _gameView;
            set => SetAndNotify(ref _gameView, value);
        }

        private SaveViewModel? _saveView;
        public SaveViewModel? SaveView
        {
            get => _saveView;
            set => SetAndNotify(ref this._saveView, value);
        }

        public ShellViewModel(IWindowManager windowManager, IDialogFactory dialogFactory, IEventAggregator events)
        {
            this.windowManager = windowManager;
            this.dialogFactory = dialogFactory;
            _events = events;
            _events.Subscribe(this);
        }

        /// <summary>
        /// Called by Stylet after the view is fully loaded — safe to show dialogs here.
        /// </summary>
        protected override async void OnViewLoaded()
        {
            base.OnViewLoaded();

            var authService = AppBootstrapper.AuthService;
            if (authService == null) return;

            // Strategy 1: try wincred tokens loaded by Bootstrapper
            var deviceToken = AppBootstrapper.CachedDeviceToken;
            var userToken   = AppBootstrapper.CachedUserToken;

            if (!string.IsNullOrEmpty(userToken) && !string.IsNullOrEmpty(deviceToken))
            {
                try
                {
                    var result = await authService.AuthorizeXsts(userToken, deviceToken);
                    if (result != null)
                    {
                        AppBootstrapper.XblConfig = new XboxLiveConfig(
                            result.Token, result.DisplayClaims.XboxUserIdentity[0]);
                        OnAuthComplete();
                        return;
                    }
                }
                catch { /* wincred tokens invalid — fall through to WebView2 login */ }
            }

            // Strategy 2: WebView2 login dialog (safe to show now — view is loaded)
            try
            {
                var loginVm = new LoginViewModel();
                bool? loginResult = windowManager.ShowDialog(loginVm);

                if (loginResult == true && !string.IsNullOrEmpty(loginVm.AccessToken))
                {
                    var deviceResponse = await authService.AuthenticateDeviceToken(loginVm.AccessToken, "10.0.19041");
                    var userResponse   = await authService.AuthenticateUser(loginVm.AccessToken);
                    var dTok = deviceResponse?.Token;
                    var uTok = userResponse?.Token;

                    if (!string.IsNullOrEmpty(uTok) && !string.IsNullOrEmpty(dTok))
                    {
                        var xsts = await authService.AuthorizeXsts(uTok, dTok);
                        if (xsts != null)
                        {
                            AppBootstrapper.XblConfig = new XboxLiveConfig(
                                xsts.Token, xsts.DisplayClaims.XboxUserIdentity[0]);
                            OnAuthComplete();
                            return;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Login failed: " + ex.Message, "Xbox Login Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void Handle(LoadSaveDetailsEvent message)
        {
            SaveView = new SaveViewModel(_events, _xblConfig!, message.PackageFamilyName, message.ServiceConfigurationId);
        }

        /// <summary>
        /// Called once authentication is complete — loads the game list.
        /// </summary>
        public void OnAuthComplete()
        {
            GameView = new GameViewModel(_events, _xblConfig!);
        }
    }
}
