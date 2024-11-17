using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using OpenUtau.App.ViewModels;
using OpenUtau.App.Views;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using ReactiveUI;
using Serilog;
using SharpCompress;

namespace OpenUtau.App.Controls {
    public partial class NotePropertiesControl : UserControl, ICmdSubscriber {
        private readonly NotePropertiesViewModel ViewModel;

        public static readonly DirectProperty<NotePropertiesControl, UVoicePart> VoicePartProperty =
            AvaloniaProperty.RegisterDirect<NotePropertiesControl, UVoicePart>(
                nameof(VoicePart),
                o => o.VoicePart,
                (o, v) => o.VoicePart = v);
        public static readonly DirectProperty<NotePropertiesControl, UProject> ProjectProperty =
            AvaloniaProperty.RegisterDirect<NotePropertiesControl, UProject>(
                nameof(Project),
                o => o.Project,
                (o, v) => o.Project = v);

        public UVoicePart VoicePart {
            get => _voicePart;
            private set => SetAndRaise(VoicePartProperty, ref _voicePart, value);
        }
        public UProject Project {
            get => _project;
            private set => SetAndRaise(ProjectProperty, ref _project, value);
        }

        private UVoicePart _voicePart;
        private UProject _project;

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change) {
            base.OnPropertyChanged(change);
            if (change.Property == VoicePartProperty ||
                change.Property == ProjectProperty) {
                InvalidateVisual();
                if(VoicePart != null) {
                    ((NotePropertiesViewModel)DataContext!).Part = VoicePart;
                }
                if (Project != null) {
                    ((NotePropertiesViewModel)DataContext!).Project = Project;
                }
                LoadPart();
            }
        }

        public NotePropertiesControl() {
            InitializeComponent();
            DataContext = ViewModel = new NotePropertiesViewModel();
            _voicePart = new UVoicePart();
            _project = new UProject();
            


            this.GetLogicalDescendants().OfType<TextBox>().ForEach(box => {
                box.AddHandler(GotFocusEvent, OnTextBoxGotFocus);
                box.AddHandler(LostFocusEvent, OnTextBoxLostFocus);
            });
            this.GetLogicalDescendants().OfType<Slider>().ForEach(slider => {
                slider.AddHandler(PointerPressedEvent, SliderPointerPressed, RoutingStrategies.Tunnel);
                slider.AddHandler(PointerReleasedEvent, SliderPointerReleased, RoutingStrategies.Tunnel);
                slider.AddHandler(PointerMovedEvent, SliderPointerMoved, RoutingStrategies.Tunnel);
            });

            MessageBus.Current.Listen<PianorollRefreshEvent>()
                .Subscribe(e => {
                    if (e.refreshItem == "Part") {
                        ((NotePropertiesViewModel)DataContext!).Part = VoicePart;
                        ((NotePropertiesViewModel)DataContext!).Project = Project;
                        LoadPart();
                    }
                });

            DocManager.Inst.AddSubscriber(this);
        }

        private void LoadPart() {
            if (NotePropertiesViewModel.PanelControlPressed) {
                NotePropertiesViewModel.PanelControlPressed = false;
                DocManager.Inst.EndUndoGroup();
            }
            NotePropertiesViewModel.NoteLoading = true;

            ViewModel.LoadPart();

            NotePropertiesViewModel.NoteLoading = false;
        }

        private string textBoxValue = string.Empty;
        void OnTextBoxGotFocus(object? sender, GotFocusEventArgs args) {
            Log.Debug("Note property textbox got focus");
            if(sender is TextBox text) {
                textBoxValue = text.Text ?? string.Empty;
            }
        }
        void OnTextBoxLostFocus(object? sender, RoutedEventArgs args) {
            Log.Debug("Note property textbox lost focus");
            if (sender is TextBox textBox && textBoxValue != textBox.Text && textBox.Tag is string tag && !string.IsNullOrEmpty(tag)) {
                DocManager.Inst.StartUndoGroup();
                NotePropertiesViewModel.PanelControlPressed = true;
                ViewModel.SetNoteParams(tag, textBox.Text);
                NotePropertiesViewModel.PanelControlPressed = false;
                DocManager.Inst.EndUndoGroup();
            }
        }

        void SliderPointerPressed(object? sender, PointerPressedEventArgs args) {
            Log.Debug("Slider pressed");
            if (sender is Control control) {
                var point = args.GetCurrentPoint(control);
                if (point.Properties.IsLeftButtonPressed) {
                    DocManager.Inst.StartUndoGroup();
                    NotePropertiesViewModel.PanelControlPressed = true;
                } else if (point.Properties.IsRightButtonPressed) {
                    if (control.Tag is string tag && !string.IsNullOrEmpty(tag)) {
                        DocManager.Inst.StartUndoGroup();
                        NotePropertiesViewModel.PanelControlPressed = true;
                        ViewModel.SetNoteParams(tag, null);
                        NotePropertiesViewModel.PanelControlPressed = false;
                        DocManager.Inst.EndUndoGroup();
                    }
                }
            }
        }
        void SliderPointerReleased(object? sender, PointerReleasedEventArgs args) {
            Log.Debug("Slider released");
            if (NotePropertiesViewModel.PanelControlPressed) {
                if (sender is Slider slider && slider.Tag is string tag && !string.IsNullOrEmpty(tag)) {
                    ViewModel.SetNoteParams(tag, (float)slider.Value);
                }
                NotePropertiesViewModel.PanelControlPressed = false;
                DocManager.Inst.EndUndoGroup();
            }
        }
        void SliderPointerMoved(object? sender, PointerEventArgs args) {
            if (sender is Slider slider && slider.Tag is string tag && !string.IsNullOrEmpty(tag)) {
                ViewModel.SetNoteParams(tag, (float)slider.Value);
            }
        }

        void VibratoEnableClicked(object sender, RoutedEventArgs e) {
            ViewModel.SetVibratoEnable();
        }

        void OnSavePortamentoPreset(object sender, RoutedEventArgs e) {
            if (VisualRoot is Window window) {
                var dialog = new TypeInDialog() {
                    Title = ThemeManager.GetString("notedefaults.preset.namenew"),
                    onFinish = name => ViewModel.SavePortamentoPreset(name),
                };
                dialog.ShowDialog(window);
            }
        }

        void OnRemovePortamentoPreset(object sender, RoutedEventArgs e) {
            ViewModel.RemoveAppliedPortamentoPreset();
        }

        void OnSaveVibratoPreset(object sender, RoutedEventArgs e) {
            if (VisualRoot is Window window) {
                var dialog = new TypeInDialog() {
                    Title = ThemeManager.GetString("notedefaults.preset.namenew"),
                    onFinish = name => ViewModel.SaveVibratoPreset(name),
                };
                dialog.ShowDialog(window);
            }
        }

        void OnRemoveVibratoPreset(object sender, RoutedEventArgs e) {
            ViewModel.RemoveAppliedVibratoPreset();
        }

        private void OnKeyDown(object? sender, KeyEventArgs e) {
            switch (e.Key) {
                case Key.Enter:
                    TopLevel.GetTopLevel(this)?.FocusManager?.ClearFocus();
                    e.Handled = true;
                    break;
                default:
                    break;
            }
        }

        public void OnNext(UCommand cmd, bool isUndo) {
            if (cmd is UNotification notif) {
                ((NotePropertiesViewModel)DataContext!).Part = notif.part as UVoicePart;
                ((NotePropertiesViewModel)DataContext!).Project = notif.project;
                if (cmd is LoadPartNotification) {
                    LoadPart();
                } else if (cmd is LoadProjectNotification) {
                    LoadPart();
                } else if (cmd is SingersRefreshedNotification) {
                    LoadPart();
                }
            } else if (cmd is TrackCommand) {
                if (cmd is RemoveTrackCommand removeTrack) {
                    if (ViewModel.Part != null && removeTrack.removedParts.Contains(ViewModel.Part)) {
                        LoadPart();
                    }
                }
            } else if (cmd is ConfigureExpressionsCommand) {
                if (VoicePart != null) {
                    LoadPart();
                }
            }
        }
    }
}
