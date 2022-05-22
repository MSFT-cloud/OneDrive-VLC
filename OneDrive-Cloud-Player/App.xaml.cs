﻿using LibVLCSharp.Shared;
using Microsoft.Identity.Client;
using OneDrive_Cloud_Player.Models.GraphData;
using OneDrive_Cloud_Player.Services;
using OneDrive_Cloud_Player.Views;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Storage;
using Windows.ApplicationModel.Core;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using Microsoft.Extensions.DependencyInjection;

namespace OneDrive_Cloud_Player
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    sealed partial class App : Application
    {
        //Other classes can now call this class with the use of 'App.Current'. 
        public static new App Current => (App)Application.Current;
        public CoreDispatcher UIDispatcher { get; private set; }
        public IPublicClientApplication PublicClientApplication { get; private set; }
        public string[] Scopes { get; private set; }
        public CacheHelper CacheHelper { get; private set; }
        public ApplicationDataContainer UserSettings { get; private set; }

        public IServiceProvider Container { get; }

        /// <summary>
        /// The current version of the application data structure.
        /// </summary>
        /// <remarks>
        /// If the structure of the application data changes (which is currently determined by the
        /// requiredSettings dictionary in <see cref="App.UpdateAppDataFormat(SetVersionRequest)"/>),
        /// this number should be incremented at the very same time (meaning compile-time). If not,
        /// the application data could enter an inconsistent state.
        /// </remarks>
        private const int appDataVersion = 1;

        private List<CachedDriveItem> mediaItemList;
        /// <summary>
        /// This list holds a copy of the current item list with only playable media files.
        /// The VideoPlayerPageViewmodel and the MainPageViewmodel communicate the current media list this way.
        /// </summary>
        public List<CachedDriveItem> MediaItemList
        {
            get { return mediaItemList; }
            set
            {
                // Filter the list for playable media.
                mediaItemList = App.Current.CacheHelper.FilterPlayableMedia(value);
            }
        }

        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            Core.Initialize();
            this.InitializeComponent();
            this.LoadUserSettings();
            Container = ConfigureDependencyInjection();
            this.CreateScopedPublicClientApplicationInstance();
            this.Suspending += Application_Suspending;
            this.CacheHelper = new CacheHelper();
        }



        IServiceProvider ConfigureDependencyInjection()
        {
            var serviceCollection = new ServiceCollection();

            // Add services for dependency injection here.

            return serviceCollection.BuildServiceProvider();
        }

        /// <summary>
        /// Load the user settings from disk and check if they contain all the required entries.
        /// </summary>
        private void LoadUserSettings()
        {
            // Load the saved settings from disk and check the version.
            UserSettings = ApplicationData.Current.LocalSettings;
            if (ApplicationData.Current.Version < appDataVersion)
            {
                ApplicationData.Current.SetVersionAsync(appDataVersion, UpdateAppDataFormat).AsTask().Wait();
            }
        }

        /// <summary>
        /// Update the application data format. Afterwards, the version will be incremented.
        /// </summary>
        /// <param name="request">Different upgrade paths are enabled by checking the current
        /// and desired properties of this parameter.</param>
        private void UpdateAppDataFormat(SetVersionRequest request)
        {
            // Create a dictionary with the required settings and their default values.
            Dictionary<string, object> requiredSettings = new Dictionary<string, object>
            {
                { "MediaVolume", 100 },
                { "ShowDefaultSubtitles", true }
            };

            // Verify disk settings against default settings.
            foreach (string key in requiredSettings.Keys)
            {
                if (!UserSettings.Values.ContainsKey(key))
                {
                    // Required setting not found on disk, so add it.
                    UserSettings.Values[key] = requiredSettings.GetValueOrDefault(key);
                }
            }

            // Check disk settings for unneeded entries.
            foreach (string diskEntry in UserSettings.Values.Keys)
            {
                if (!requiredSettings.ContainsKey(diskEntry))
                {
                    // Disk settings contain more info than needed.
                    UserSettings.Values.Remove(diskEntry);
                }
            }
        }

        /// <summary>
        /// Check whether or not the user credentials are cached via MSAL
        /// </summary>
        /// <returns></returns>
        public async Task<bool> IsLoggedIn()
        {
            IEnumerable<IAccount> Accounts = await PublicClientApplication.GetAccountsAsync();
            return Accounts.Count() != 0;
        }

        /// <summary>
        /// Invoked when the application is launched normally by the end user.  Other entry points
        /// will be used such as when the application is launched to open a specific file.
        /// </summary>
        /// <param name="e">Details about the launch request and process.</param>
        protected override async void OnLaunched(LaunchActivatedEventArgs e)
        {
            Frame rootFrame = Window.Current.Content as Frame;

            // Do not repeat app initialization when the Window already has content,
            // just ensure that the window is active
            if (rootFrame == null)
            {
                // Create a Frame to act as the navigation context and navigate to the first page
                rootFrame = new Frame();

                rootFrame.NavigationFailed += OnNavigationFailed;

                if (e.PreviousExecutionState == ApplicationExecutionState.Terminated)
                {
                    //TODO: Load state from previously suspended application
                }

                // Place the frame in the current Window
                Window.Current.Content = rootFrame;
            }

            if (e.PrelaunchActivated == false)
            {
                if (rootFrame.Content == null)
                {
                    // When the navigation stack isn't restored navigate to the first page,
                    // configuring the new page by passing required information as a navigation
                    // parameter
                    if (await IsLoggedIn())
                    {
                        await this.CacheHelper.Initialize(false);
                        rootFrame.Navigate(typeof(MainPage), e.Arguments);
                    }
                    else
                    {
                        rootFrame.Navigate(typeof(LoginPage), e.Arguments);
                    }
                }
                // Ensure the current window is active
                Window.Current.Activate();
            }

            UIDispatcher = CoreApplication.MainView.CoreWindow.Dispatcher;
        }

        /// <summary>
        /// Invoked when Navigation to a certain page fails
        /// </summary>
        /// <param name="sender">The Frame which failed navigation</param>
        /// <param name="e">Details about the navigation failure</param>
        void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
        {
            throw new Exception("Failed to load Page " + e.SourcePageType.FullName);
        }

        /// <summary>
        /// Create a plublic client application instance and set it to the PublicClientApplication property.
        /// </summary>
        private void CreateScopedPublicClientApplicationInstance()
        {
            this.PublicClientApplication = PublicClientApplicationBuilder.Create("cfc49d19-b88e-4986-8862-8b5de253d0fd")
                .WithRedirectUri("https://login.microsoftonline.com/common/oauth2/nativeclient")
                .Build();

            Scopes = new[]
                {
                    "offline_access",
                    "openid",
                    "profile",
                    "user.read",
                    "Files.Read.All"
                };
        }

        /// <summary>
        /// Invoked when application execution is being suspended.  Application state is saved
        /// without knowing whether the application will be terminated or resumed with the contents
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void Application_Suspending(object sender, SuspendingEventArgs e)
        {
            var deferral = e.SuspendingOperation.GetDeferral();
            await this.CacheHelper.WriteGraphCache();
            deferral.Complete();
        }
    }
}
