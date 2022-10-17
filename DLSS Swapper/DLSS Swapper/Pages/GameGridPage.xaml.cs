﻿using DLSS_Swapper.Data;
using DLSS_Swapper.Data.EpicGameStore;
using DLSS_Swapper.Data.GOGGalaxy;
using DLSS_Swapper.Data.Steam;
using DLSS_Swapper.Data.UbisoftConnect;
using DLSS_Swapper.Data.Xbox;
using DLSS_Swapper.Interfaces;
using DLSS_Swapper.UserControls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Security.Principal;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Input;
using Windows.Foundation;
using Windows.Foundation.Collections;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace DLSS_Swapper.Pages
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class GameGridPage : Page
    {
        public List<IGameLibrary> GameLibraries { get; } = new List<IGameLibrary>();

        bool _loadingGamesAndDlls = false;

        public GameGridPage()
        {
            this.InitializeComponent();
            DataContext = this;

        }


        async Task LoadGamesAsync()
        {
            // Added this check so if we get to here and this is true we probably crashed loading games last time and we should prompt for that.
            if (Settings.WasLoadingGames)
            {
                var richTextBlock = new RichTextBlock();
                var paragraph = new Paragraph()
                {
                    Margin = new Thickness(0, 0, 0, 0),
                };
                paragraph.Inlines.Add(new Run()
                {
                    Text = "DLSS Swapper had an issue loading game libraries.  Please try disabling a game library below. You can re-enable these options later in the settings.",
                });
                richTextBlock.Blocks.Add(paragraph);
                paragraph = new Paragraph()
                {
                    Margin = new Thickness(0, 0, 0, 0),
                };
                paragraph.Inlines.Add(new Run()
                {
                    Text = "If this keeps happening please file a bug report ",
                });
                var hyperLink = new Hyperlink()
                {
                    NavigateUri = new Uri("https://github.com/beeradmoore/dlss-swapper/issues"),

                };
                hyperLink.Inlines.Add(new Run()
                {
                    Text = "here"
                });
                paragraph.Inlines.Add(hyperLink);
                paragraph.Inlines.Add(new Run()
                {
                    Text = ".",
                });
                richTextBlock.Blocks.Add(paragraph);




                var grid = new Grid()
                {
                    RowSpacing = 10,
                };
                grid.RowDefinitions.Add(new RowDefinition());
                grid.RowDefinitions.Add(new RowDefinition());

                Grid.SetRow(richTextBlock, 0);
                grid.Children.Add(richTextBlock);


                var gameLibrarySelectorControl = new GameLibrarySelectorControl();

                Grid.SetRow(gameLibrarySelectorControl, 1);
                grid.Children.Add(gameLibrarySelectorControl);


                var dialog = new ContentDialog();
                dialog.Title = "Failed to load game libraries";
                dialog.PrimaryButtonText = "Save";
                dialog.SecondaryButtonText = "Cancel";
                dialog.DefaultButton = ContentDialogButton.Primary;
                dialog.Content = grid;
                dialog.XamlRoot = XamlRoot;
                dialog.RequestedTheme = Settings.AppTheme;
                var result = await dialog.ShowAsync();
                if (result == ContentDialogResult.Primary)
                {
                    gameLibrarySelectorControl.Save();
                }
            }

            Settings.WasLoadingGames = true;

            GameLibraries.Clear();


            // Auto game library loading.
            // Simply adding IGameLibrary interface means we will load the games.
            var loadGameTasks = new List<Task>(); 
            foreach (GameLibrary gameLibraryEnum in Enum.GetValues(typeof(GameLibrary)))
            {
                var gameLibrary = IGameLibrary.GetGameLibrary(gameLibraryEnum);
                if (gameLibrary.IsEnabled())
                {
                    GameLibraries.Add(gameLibrary);
                    loadGameTasks.Add(gameLibrary.ListGamesAsync());
                }
            }

            // Await them all to finish loading games.
            await Task.WhenAll(loadGameTasks);

            Settings.WasLoadingGames = false;

            DispatcherQueue.TryEnqueue(() =>
            {
                FilterGames();
            });
        }

        void FilterGames()
        {
            // TODO: Remove weird hack which otherwise causes MainGridView_SelectionChanged to fire when changing MainGridView.ItemsSource.
            MainGridView.SelectionChanged -= MainGridView_SelectionChanged;

            //MainGridView.ItemsSource = null;

            if (Settings.GroupGameLibrariesTogether)
            {

                var collectionViewSource = new CollectionViewSource()
                {
                    IsSourceGrouped = true,
                    Source = GameLibraries,
                };

                if (Settings.HideNonDLSSGames)
                {
                    collectionViewSource.ItemsPath = new PropertyPath("LoadedDLSSGames");
                }
                else
                {
                    collectionViewSource.ItemsPath = new PropertyPath("LoadedGames");
                }

                MainGridView.ItemsSource = collectionViewSource.View;
            }
            else
            {
                var games = new List<Game>();

                if (Settings.HideNonDLSSGames)
                {
                    foreach (var gameLibrary in GameLibraries)
                    {
                        games.AddRange(gameLibrary.LoadedGames.Where(g => g.HasDLSS == true));
                    }
                }
                else
                {
                    foreach (var gameLibrary in GameLibraries)
                    {
                        games.AddRange(gameLibrary.LoadedGames);
                    }
                }

                games.Sort();

                MainGridView.ItemsSource = games;
            }

            // TODO: Remove weird hack which otherwise causes MainGridView_SelectionChanged to fire when changing MainGridView.ItemsSource.
            MainGridView.SelectedIndex = -1;
            MainGridView.SelectionChanged += MainGridView_SelectionChanged;
        }


        async void Page_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadGamesAndDlls();
        }

        async void MainGridView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count == 0)
            {
                return;
            }

            MainGridView.SelectedIndex = -1;
            if (e.AddedItems[0] is Game game)
            {
                ContentDialog dialog;

                if (game.HasDLSS == false)
                {
                    dialog = new ContentDialog();
                    //dialog.Title = "Error";
                    dialog.PrimaryButtonText = "Okay";
                    dialog.DefaultButton = ContentDialogButton.Primary;
                    dialog.Content = $"DLSS was not detected in {game.Title}.";
                    dialog.XamlRoot = XamlRoot;
                    dialog.RequestedTheme = Settings.AppTheme;
                    await dialog.ShowAsync();
                    return;
                }

                var dlssPickerControl = new DLSSPickerControl(game);
                dialog = new ContentDialog();
                dialog.Title = "Select DLSS Version";
                dialog.PrimaryButtonText = "Swap";
                dialog.CloseButtonText = "Cancel";
                dialog.DefaultButton = ContentDialogButton.Primary;
                dialog.Content = dlssPickerControl;
                dialog.XamlRoot = XamlRoot;
                dialog.RequestedTheme = Settings.AppTheme;

                if (String.IsNullOrEmpty(game.BaseDLSSVersion) == false)
                {
                    dialog.SecondaryButtonText = "Reset";
                }

                var result = await dialog.ShowAsync();


                if (result == ContentDialogResult.Primary)
                {
                    var selectedDLSSRecord = dlssPickerControl.GetSelectedDLSSRecord();

                    if (selectedDLSSRecord.LocalRecord.IsDownloading == true || selectedDLSSRecord.LocalRecord.IsDownloaded == false)
                    {
                        // TODO: Initiate download here.
                        dialog = new ContentDialog();
                        dialog.Title = "Error";
                        dialog.CloseButtonText = "Okay";
                        dialog.DefaultButton = ContentDialogButton.Close;
                        dialog.Content = "Please download the DLSS record from the downloads page first.";
                        dialog.XamlRoot = XamlRoot;
                        dialog.RequestedTheme = Settings.AppTheme;
                        await dialog.ShowAsync();
                        return;
                    }

                    var didUpdate = game.UpdateDll(selectedDLSSRecord);

                    if (didUpdate.Success == false)
                    {
                        dialog = new ContentDialog();
                        dialog.Title = "Error";
                        dialog.PrimaryButtonText = "Okay";
                        dialog.DefaultButton = ContentDialogButton.Primary;
                        /*
                        // Disabled as I am unsure how to prompt to run as admin.
                        if (didUpdate.PromptToRelaunchAsAdmin == true)
                        {
                            dialog.SecondaryButtonText = "Relaunch as Administrator";
                        }
                        */
                        dialog.Content = didUpdate.Message;
                        dialog.XamlRoot = XamlRoot;
                        dialog.RequestedTheme = Settings.AppTheme;
                        var dialogResult = await dialog.ShowAsync();
                        /*
                        // Disabled as I am unsure how to prompt to run as admin.
                        if (didUpdate.PromptToRelaunchAsAdmin == true && dialogResult == ContentDialogResult.Secondary)
                        {
                            App.CurrentApp.RelaunchAsAdministrator();
                        }
                        */
                    }
                }
                else if (result == ContentDialogResult.Secondary)
                {
                    var didReset = game.ResetDll();

                    if (didReset.Success == false)
                    {
                        dialog = new ContentDialog();
                        dialog.Title = "Error";
                        dialog.PrimaryButtonText = "Okay";
                        dialog.DefaultButton = ContentDialogButton.Primary;
                        dialog.Content = didReset.Message;
                        /*
                        // Disabled as I am unsure how to prompt to run as admin.
                        if (didReset.PromptToRelaunchAsAdmin == true)
                        {
                            dialog.SecondaryButtonText = "Relaunch as Administrator";
                        }
                        */
                        dialog.XamlRoot = XamlRoot;
                        dialog.RequestedTheme = Settings.AppTheme;
                        var dialogResult = await dialog.ShowAsync();/*
                        // Disabled as I am unsure how to prompt to run as admin.
                        if (didReset.PromptToRelaunchAsAdmin == true && dialogResult == ContentDialogResult.Secondary)
                        {
                            App.CurrentApp.RelaunchAsAdministrator();
                        }
                        */
                    }
                }
            }
        }



        async Task LoadGamesAndDlls()
        {
            if (_loadingGamesAndDlls)
                return;

            _loadingGamesAndDlls = true;

            // TODO: Fade?
            LoadingStackPanel.Visibility = Visibility.Visible;

            var tasks = new List<Task>();
            tasks.Add(LoadGamesAsync());


            await Task.WhenAll(tasks);

            DispatcherQueue.TryEnqueue(() =>
            {
                LoadingStackPanel.Visibility = Visibility.Collapsed;
                _loadingGamesAndDlls = false;
            });
        }

        async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            await LoadGamesAndDlls();
        }

        async void FilterButton_Click(object sender, RoutedEventArgs e)
        {
            var gameFilterControl = new GameFilterControl();

            var dialog = new ContentDialog();
            dialog.Title = "Filter";
            dialog.PrimaryButtonText = "Apply";
            dialog.CloseButtonText = "Cancel";
            dialog.DefaultButton = ContentDialogButton.Primary;
            dialog.Content = gameFilterControl;
            dialog.XamlRoot = XamlRoot;
            dialog.RequestedTheme = Settings.AppTheme;


            var result = await dialog.ShowAsync();

            if (result == ContentDialogResult.Primary)
            {
                Settings.HideNonDLSSGames = gameFilterControl.IsHideNonDLSSGamesChecked();
                Settings.GroupGameLibrariesTogether = gameFilterControl.IsGroupGameLibrariesTogetherChecked();

                FilterGames();
            }
        }
    }
}
