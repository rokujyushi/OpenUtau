using System;
using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using OpenUtau.App.ViewModels;
using OpenUtau.Core.Ustx;
using ReactiveUI;

namespace OpenUtau.App.Controls {
    class TickBackground : TemplatedControl {
        private static readonly IDashStyle DashStyle = new ImmutableDashStyle(new double[] { 2, 4 }, 0);

        public static readonly DirectProperty<TickBackground, int> ResolutionProperty =
            AvaloniaProperty.RegisterDirect<TickBackground, int>(
                nameof(Resolution),
                o => o.Resolution,
                (o, v) => o.Resolution = v);
        public static readonly DirectProperty<TickBackground, double> TickWidthProperty =
            AvaloniaProperty.RegisterDirect<TickBackground, double>(
                nameof(TickWidth),
                o => o.TickWidth,
                (o, v) => o.TickWidth = v);
        public static readonly DirectProperty<TickBackground, double> TickOffsetProperty =
            AvaloniaProperty.RegisterDirect<TickBackground, double>(
                nameof(TickOffset),
                o => o.TickOffset,
                (o, v) => o.TickOffset = v);
        public static readonly DirectProperty<TickBackground, int> TickOriginProperty =
            AvaloniaProperty.RegisterDirect<TickBackground, int>(
                nameof(TickOrigin),
                o => o.TickOrigin,
                (o, v) => o.TickOrigin = v);
        public static readonly DirectProperty<TickBackground, int> SnapDivProperty =
            AvaloniaProperty.RegisterDirect<TickBackground, int>(
                nameof(SnapDiv),
                o => o.SnapDiv,
                (o, v) => o.SnapDiv = v);
        public static readonly DirectProperty<TickBackground, ObservableCollection<int>?> SnapTicksProperty =
            AvaloniaProperty.RegisterDirect<TickBackground, ObservableCollection<int>?>(
                nameof(SnapTicks),
                o => o.SnapTicks,
                (o, v) => o.SnapTicks = v);
        public static readonly DirectProperty<TickBackground, bool> ShowBarNumberProperty =
            AvaloniaProperty.RegisterDirect<TickBackground, bool>(
                nameof(ShowBarNumber),
                o => o.ShowBarNumber,
                (o, v) => o.ShowBarNumber = v);
        public static readonly DirectProperty<TickBackground, bool> IsPianoRollProperty =
            AvaloniaProperty.RegisterDirect<TickBackground, bool>(
                nameof(IsPianoRoll),
                o => o.IsPianoRoll,
                (o, v) => o.IsPianoRoll = v);
        public static readonly DirectProperty<TickBackground, UVoicePart?> PartProperty =
            AvaloniaProperty.RegisterDirect<TickBackground, UVoicePart?>(
                nameof(Part),
                o => o.Part,
                (o, v) => o.Part = v);

        public int Resolution {
            get => _resolution;
            private set => SetAndRaise(ResolutionProperty, ref _resolution, value);
        }
        // Tick width in pixel.
        public double TickWidth {
            get => _tickWidth;
            private set => SetAndRaise(TickWidthProperty, ref _tickWidth, value);
        }
        public double TickOffset {
            get => _tickOffset;
            private set => SetAndRaise(TickOffsetProperty, ref _tickOffset, value);
        }
        public int TickOrigin {
            get => _tickOrigin;
            private set => SetAndRaise(TickOriginProperty, ref _tickOrigin, value);
        }
        public int SnapDiv {
            get => _snapDiv;
            set => SetAndRaise(SnapDivProperty, ref _snapDiv, value);
        }
        public ObservableCollection<int>? SnapTicks {
            get => _snapTicks;
            set => SetAndRaise(SnapTicksProperty, ref _snapTicks, value);
        }
        public bool ShowBarNumber {
            get => _showBarNumber;
            set => SetAndRaise(ShowBarNumberProperty, ref _showBarNumber, value);
        }
        public bool IsPianoRoll {
            get => _isPianoRoll;
            set => SetAndRaise(IsPianoRollProperty, ref _isPianoRoll, value);
        }
        public UVoicePart? Part {
            get => _part;
            set => SetAndRaise(PartProperty, ref _part, value);
        }

        private int _resolution = 480;
        private double _tickWidth;
        private double _tickOffset;
        private int _tickOrigin;
        private int _snapDiv;
        private ObservableCollection<int>? _snapTicks;
        private bool _showBarNumber;
        private bool _isPianoRoll;
        private UVoicePart? _part;

        private Pen penBar;
        private Pen penBeatUnit;
        private Pen penDanshed;
        private Pen penBookMark;

        public TickBackground() {
            penBar = new Pen(Foreground, 1);
            penBeatUnit = new Pen(Background, 1);
            penDanshed = new Pen(Background, 1) {
                DashStyle = DashStyle,
            };
            penBookMark = new Pen(Background, 2) {
                DashStyle = DashStyle,
            };
            MessageBus.Current.Listen<ThemeChangedEvent>()
                .Subscribe(e => InvalidateVisual());
            MessageBus.Current.Listen<TimeAxisChangedEvent>()
                .Subscribe(e => InvalidateVisual());
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change) {
            base.OnPropertyChanged(change);
            if (change.Property == ForegroundProperty) {
                penBar = new Pen(Foreground, 1);
            }
            if (change.Property == BackgroundProperty) {
                penBeatUnit = new Pen(Background, 1);
                penDanshed = new Pen(Background, 1) {
                    DashStyle = DashStyle,
                };
            }
            if (change.Property == ResolutionProperty ||
                change.Property == TickOriginProperty ||
                change.Property == TickWidthProperty ||
                change.Property == TickOffsetProperty ||
                change.Property == SnapDivProperty ||
                change.Property == PartProperty) {
                InvalidateVisual();
            }
        }

        public override void Render(DrawingContext context) {
            if (TickWidth <= 0) {
                return;
            }
            var project = Core.DocManager.Inst.Project;
            int snapUnit = project.resolution * 4 / SnapDiv;
            while (snapUnit * TickWidth < ViewConstants.MinTicklineWidth) {
                snapUnit *= 2; // Avoid drawing too dense.
            }
            double minLineTick = ViewConstants.MinTicklineWidth / TickWidth;
            double pixelOffset = (TickOffset + TickOrigin) * TickWidth;
            double leftTick = TickOffset + TickOrigin;
            double rightTick = TickOffset + TickOrigin + Bounds.Width / TickWidth;

            project.timeAxis.TickPosToBarBeat(TickOrigin, out int bar, out int beat, out int remainingTicks);
            if (bar > 0) {
                bar--;
            }
            int barTick = project.timeAxis.BarBeatToTickPos(bar, 0);
            SnapTicks?.Clear();
            while (barTick <= rightTick) {
                SnapTicks?.Add(barTick);
                // Bar lines and numbers.
                double x = Math.Round(barTick * TickWidth - pixelOffset) + 0.5;
                double y = IsPianoRoll ? 30 : 10;
                var textLayout = TextLayoutCache.Get((bar + 1).ToString(), ThemeManager.BarNumberBrush, 10);
                using (var state = context.PushTransform(Matrix.CreateTranslation(x + 3, y))) {
                    textLayout.Draw(context, new Point());
                }
                y = -0.5;
                context.DrawLine(penBar, new Point(x, y), new Point(x, Bounds.Height + 0.5f));
                // Lines between bars.
                var timeSig = project.timeAxis.TimeSignatureAtBar(bar);
                int nextBarTick = project.timeAxis.BarBeatToTickPos(bar + 1, 0);
                int ticksPerBeat = project.resolution * 4 * timeSig.beatPerBar / timeSig.beatUnit;
                int ticksPerLine = snapUnit;
                if (ticksPerBeat < snapUnit) {
                    ticksPerLine = ticksPerBeat;
                } else if (ticksPerBeat % snapUnit != 0) {
                    if (ticksPerBeat > minLineTick) {
                        ticksPerLine = ticksPerBeat;
                    } else {
                        ticksPerLine = nextBarTick - barTick;
                    }
                }
                if (nextBarTick > leftTick) {
                    for (int tick = barTick + ticksPerLine; tick < nextBarTick; tick += ticksPerLine) {
                        SnapTicks?.Add(tick);
                        project.timeAxis.TickPosToBarBeat(tick, out int snapBar, out int snapBeat, out int snapRemainingTicks);
                        var pen = snapRemainingTicks != 0 ? penDanshed : penBeatUnit;
                        x = Math.Round(tick * TickWidth - pixelOffset) + 0.5;
                        y = IsPianoRoll ? 48 : 24;
                        context.DrawLine(pen, new Point(x, y), new Point(x, Bounds.Height + 0.5f));
                    }
                }
                barTick = nextBarTick;
                bar++;
            }
            SnapTicks?.Add(barTick);

            double textY = 0;

            if (IsPianoRoll && Part != null) {
                foreach (var bookmark in Part.bookmarks) {
                    double x = Math.Round(bookmark.position * TickWidth - pixelOffset) + 0.5;
                    double y = 48;
                    context.DrawLine(penBookMark, new Point(x, 0), new Point(x, y));
                    var textLayout = TextLayoutCache.Get($"{bookmark.index}:{bookmark.text}", ThemeManager.BarNumberBrush, 10);
                    //context.DrawRectangle(
                    //    ThemeManager.NeutralAccentBrush,
                    //    ThemeManager.NeutralAccentPen,
                    //    new Rect(new Point(x, 0), new Size(4, textLayout.Height)));
                    using (var state = context.PushTransform(Matrix.CreateTranslation(x + 5, textY))) {
                        textLayout.Draw(context, new Point());
                    }
                }
                textY = textY + 20;
            }

            foreach (var tempo in project.tempos) {
                double x = Math.Round(tempo.position * TickWidth - pixelOffset) + 0.5;
                double y = IsPianoRoll ? 48 : 24;
                context.DrawLine(penDanshed, new Point(x, 0), new Point(x, y));
                var textLayout = TextLayoutCache.Get(tempo.bpm.ToString("#0.00"), ThemeManager.BarNumberBrush, 10);
                using (var state = context.PushTransform(Matrix.CreateTranslation(x + 3, textY))) {
                    textLayout.Draw(context, new Point());
                }
            }
            textY = textY + 10;

            foreach (var timeSig in project.timeSignatures) {
                int tick = project.timeAxis.BarBeatToTickPos(timeSig.barPosition, 0);
                var barTextLayout = TextLayoutCache.Get((timeSig.barPosition + 1).ToString(), ThemeManager.BarNumberBrush, 10);
                double x = Math.Round(tick * TickWidth - pixelOffset) + 0.5 + barTextLayout.Width + 4;
                var textLayout = TextLayoutCache.Get($"{timeSig.beatPerBar}/{timeSig.beatUnit}", ThemeManager.BarNumberBrush, 10);
                using (var state = context.PushTransform(Matrix.CreateTranslation(x + 3, textY))) {
                    textLayout.Draw(context, new Point());
                }
            }
        }
    }
}
