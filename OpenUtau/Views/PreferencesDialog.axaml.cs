using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using OpenUtau.App.ViewModels;
using OpenUtau.Core;

namespace OpenUtau.App.Views {
    public partial class PreferencesDialog : Window {
        public PreferencesDialog() {
            InitializeComponent();
        }

        void ResetAddlSingersPath(object sender, RoutedEventArgs e) {
            ((PreferencesViewModel)DataContext!).SetAddlSingersPath(string.Empty);
        }

        async void SelectAddlSingersPath(object sender, RoutedEventArgs e) {
            var path = await FilePicker.OpenFolderAboutSinger(this, "prefs.paths.addlsinger");
            if (string.IsNullOrEmpty(path)) {
                return;
            }
            if (Directory.Exists(path)) {
                ((PreferencesViewModel)DataContext!).SetAddlSingersPath(path);
            }
        }

        void ReloadSingers(object sender, RoutedEventArgs e) {
            DocManager.Inst.ExecuteCmd(new LoadingNotification(typeof(PreferencesDialog), true, "singer"));
            try {
                SingerManager.Inst.SearchAllSingers();
                DocManager.Inst.ExecuteCmd(new SingersRefreshedNotification());
            } catch (Exception ex) {
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(ex));
            } finally {
                DocManager.Inst.ExecuteCmd(new LoadingNotification(typeof(PreferencesDialog), false, "singer"));
            }
        }

        async void SelectEnunuPath(object sender, RoutedEventArgs e) {
            var type = OS.IsWindows() ? FilePicker.BAT : OS.IsMacOS() ? FilePicker.APP : FilePickerFileTypes.All;
            var path = await FilePicker.OpenFile(this, "prefs.rendering.enunupath", type);
            if (string.IsNullOrEmpty(path)) {
                return;
            }
            if (OS.AppExists(path)) {
                ((PreferencesViewModel)DataContext!).SetEnunuPath(path);
            }
        }

        async void SelectVoicevoxPath(object sender, RoutedEventArgs e) {
            var type = OS.IsWindows() ? FilePicker.EXE : OS.IsMacOS() ? FilePicker.APP : FilePickerFileTypes.All;
            var path = await FilePicker.OpenFile(this, "prefs.rendering.voicevoxpath", type);
            if (string.IsNullOrEmpty(path)) {
                return;
            }
            if (OS.AppExists(path)) {
                ((PreferencesViewModel)DataContext!).SetVoicevoxPath(path);
            }
        }

        void ResetVLabelerPath(object sender, RoutedEventArgs e) {
            ((PreferencesViewModel)DataContext!).SetVLabelerPath(string.Empty);
        }

        async void SelectVLabelerPath(object sender, RoutedEventArgs e) {
            var type = OS.IsWindows() ? FilePicker.EXE : OS.IsMacOS() ? FilePicker.APP : FilePickerFileTypes.All;
            var path = await FilePicker.OpenFile(this, "prefs.advanced.vlabelerpath", type);
            if (string.IsNullOrEmpty(path)) {
                return;
            }
            if (OS.AppExists(path)) {
                ((PreferencesViewModel)DataContext!).SetVLabelerPath(path);
            }
        }

        private void OnKeyDown(object? sender, KeyEventArgs e) {
            switch (e.Key) {
                case Key.Enter:
                    e.Handled = true;
                    break;
                default:
                    break;
            }
        }
    }
}
