﻿using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using OpenUtau.App.ViewModels;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using ReactiveUI;

namespace OpenUtau.App.Controls {
    public partial class TrackHeader : UserControl, IDisposable {
        public static readonly DirectProperty<TrackHeader, double> TrackHeightProperty =
            AvaloniaProperty.RegisterDirect<TrackHeader, double>(
                nameof(TrackHeight),
                o => o.TrackHeight,
                (o, v) => o.TrackHeight = v);
        public static readonly DirectProperty<TrackHeader, Point> OffsetProperty =
            AvaloniaProperty.RegisterDirect<TrackHeader, Point>(
                nameof(Offset),
                o => o.Offset,
                (o, v) => o.Offset = v);
        public static readonly DirectProperty<TrackHeader, int> TrackNoProperty =
            AvaloniaProperty.RegisterDirect<TrackHeader, int>(
                nameof(TrackNo),
                o => o.TrackNo,
                (o, v) => o.TrackNo = v);

        public double TrackHeight {
            get => trackHeight;
            set => SetAndRaise(TrackHeightProperty, ref trackHeight, value);
        }
        public Point Offset {
            get => offset;
            set => SetAndRaise(OffsetProperty, ref offset, value);
        }
        public int TrackNo {
            get => trackNo;
            set => SetAndRaise(TrackNoProperty, ref trackNo, value);
        }

        private double trackHeight;
        private Point offset;
        private int trackNo;

        public TrackHeaderViewModel? ViewModel;

        private List<IDisposable> unbinds = new List<IDisposable>();

        private UTrack? track;

        public TrackHeader() {
            InitializeComponent();
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change) {
            base.OnPropertyChanged(change);
            if (change.Property == OffsetProperty ||
                change.Property == TrackNoProperty ||
                change.Property == TrackHeightProperty) {
                SetPosition();
            }
        }

        internal void Bind(UTrack track, TrackHeaderCanvas canvas) {
            this.track = track;
            unbinds.Add(this.Bind(TrackHeightProperty, canvas.GetObservable(TrackHeaderCanvas.TrackHeightProperty)));
            unbinds.Add(this.Bind(HeightProperty, canvas.GetObservable(TrackHeaderCanvas.TrackHeightProperty)));
            unbinds.Add(this.Bind(OffsetProperty, canvas.WhenAnyValue(x => x.TrackOffset, trackOffset => new Point(0, -trackOffset * TrackHeight))));
            SetPosition();
        }

        private void SetPosition() {
            Canvas.SetLeft(this, 0);
            Canvas.SetTop(this, Offset.Y + (track?.TrackNo ?? 0) * trackHeight);
        }

        void TrackNameButtonClicked(object sender, RoutedEventArgs args) {
            ViewModel?.Rename();
            args.Handled = true;
        }

        void SingerButtonClicked(object sender, RoutedEventArgs args) {
            if (SingerManager.Inst.Singers.Count > 0) {
                ViewModel?.RefreshSingers();
                SingersMenu.Open();
            }
            args.Handled = true;
        }

        void SingerButtonContextRequested(object sender, ContextRequestedEventArgs args) {
            args.Handled = true;
        }

        void PhonemizerButtonClicked(object sender, RoutedEventArgs args) {
            if (DocManager.Inst.PhonemizerFactories.Length > 0) {
                ViewModel?.RefreshPhonemizers();
                PhonemizersMenu.Open();
            }
            args.Handled = true;
        }

        void PhonemizerButtonContextRequested(object sender, ContextRequestedEventArgs args) {
            args.Handled = true;
        }

        void RendererButtonClicked(object sender, RoutedEventArgs args) {
            ViewModel?.RefreshRenderers();
            if (ViewModel?.RenderersMenuItems?.Count > 0) {
                RenderersMenu.Open();
            }
            args.Handled = true;
        }

        void RendererButtonContextRequested(object sender, ContextRequestedEventArgs args) {
            args.Handled = true;
        }

        void VolumeFaderPointerPressed(object sender, PointerPressedEventArgs args) {
            if (args.GetCurrentPoint((Visual?)sender).Properties.IsRightButtonPressed && ViewModel != null) {
                ViewModel.Volume = 0;
                args.Handled = true;
            }
        }

        void PanFaderPointerPressed(object sender, PointerPressedEventArgs args) {
            if (args.GetCurrentPoint((Visual?)sender).Properties.IsRightButtonPressed && ViewModel != null) {
                ViewModel.Pan = 0;
                args.Handled = true;
            }
        }

        void VolumeFaderContextRequested(object sender, ContextRequestedEventArgs args) {
            if (ViewModel != null) {
                ViewModel.Volume = 0;
            }
            args.Handled = true;
        }

        void PanFaderContextRequested(object sender, ContextRequestedEventArgs args) {
            if (ViewModel != null) {
                ViewModel.Pan = 0;
            }
            args.Handled = true;
        }

        void TrackSettingsButtonClicked(object sender, RoutedEventArgs args) {
            if (track?.Singer != null && track.Singer.Found) {
                if (VisualRoot is Window window) {
                    var dialog = new Views.TrackSettingsDialog(track);
                    dialog.ShowDialog(window);
                }
            }
        }

        void VolumeButtonClicked(object sender, RoutedEventArgs args) {
            if (sender is Button button && ViewModel != null) {
                if (button.Parent is Grid parentGrid) {
                    if (parentGrid.Children[2] is TextBox volumeTextBox) {
                        volumeTextBox.Text = ViewModel.Volume.ToString();
                        volumeTextBox.IsVisible = true;
                        button.IsVisible = false;
                        args.Handled = true;
                    }
                }
            }
        }
        void PanButtonClicked(object sender, RoutedEventArgs args) {
            if (sender is Button button && ViewModel != null) {
                if (button.Parent is Grid parentGrid) {
                    if (parentGrid.Children[5] is TextBox panTextBox) {
                        panTextBox.Text = ViewModel.Pan.ToString();
                        panTextBox.IsVisible = true;
                        button.IsVisible = false;
                        args.Handled = true;
                    }
                }
            }
        }
        void VolumeTextBoxEnter(object sender, KeyEventArgs args) {
            if (sender is TextBox textBox && ViewModel != null && Key.Enter.Equals(args.Key)) {
                if (textBox.Parent is Grid parentGrid) {
                    if (parentGrid.Children[0] is Slider volumeSlider) {
                        if (double.TryParse(textBox.Text, out double number)) {
                            number = number > volumeSlider.Minimum ? number < volumeSlider.Maximum ? number : volumeSlider.Maximum : volumeSlider.Minimum;
                            ViewModel.Volume = number;
                        }

                    } else
                    if (parentGrid.Children[1] is Button volumeButton) {
                        textBox.IsVisible = false;
                        volumeButton.IsVisible = true;
                        args.Handled = true;
                    }
                }
            }
        }
        void PanTextBoxEnter(object sender, KeyEventArgs args) {
            if (sender is TextBox textBox && ViewModel != null && Key.Enter.Equals(args.Key)) {
                if (textBox.Parent is Grid parentGrid) {
                    if (parentGrid.Children[3] is Slider panSlider) {
                        if (double.TryParse(textBox.Text, out double number)) {
                            number = number > panSlider.Minimum ? number < panSlider.Maximum ? number : panSlider.Maximum : panSlider.Minimum;
                            ViewModel.Pan = number;
                        }

                    } else
                    if (parentGrid.Children[4] is Button panButton) {
                        textBox.IsVisible = false;
                        panButton.IsVisible = true;
                        args.Handled = true;
                    }
                }
            }
        }

        void TextBoxLeave(object sender, PointerEventArgs args) {
            if (sender is TextBox textBox) {
                if (textBox.Parent is Grid parentGrid) {
                    if (parentGrid.Children[1] is Button volumeButton) {
                        textBox.IsVisible = false;
                        volumeButton.IsVisible = true;
                        args.Handled = true;
                    }
                    if (parentGrid.Children[4] is Button panButton) {
                        textBox.IsVisible = false;
                        panButton.IsVisible = true;
                        args.Handled = true;
                    }
                }
            }
        }


        public void Dispose() {
            unbinds.ForEach(u => u.Dispose());
            unbinds.Clear();
        }
    }
}
