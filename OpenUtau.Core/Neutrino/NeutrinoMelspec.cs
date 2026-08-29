using System;

namespace OpenUtau.Core.Neutrino {
    /// <summary>
    /// Editing of the mel spectrogram NEUTRINO writes between the melspec and the
    /// waveform prediction steps.
    ///
    /// File layout, measured against NEUTRINO Tau v3.2.2:
    ///   float32 little endian, frame major, <see cref="Bins"/> bins per frame,
    ///   one frame per f0 frame. Values are log10 amplitude, so +1.0 is +20 dB,
    ///   and silence is the constant <see cref="Floor"/>.
    ///
    /// The filterbank was identified by boosting a narrow band of bins and measuring
    /// where the output spectrum changed. A Slaney style mel fits to 1.5% RMS while
    /// HTK is off by more than 9%, so the Slaney formula is used here. fmin and fmax
    /// are an empirical fit and carry roughly 2% uncertainty, which is well under a
    /// semitone and therefore inaudible in a formant warp.
    /// </summary>
    static class NeutrinoMelspec {
        public const int Bins = 100;
        public const double Floor = -7.0;

        const double FMin = 50.0;
        const double FMax = 20000.0;

        /// <summary>
        /// Spectral tilt in dB per octave at tension = +-100.
        ///
        /// Brightness is spectral tilt: the glottal source rolls off at about -12 dB/oct and
        /// lip radiation adds +6 dB/oct, so a relaxed voice ends up near -6 dB/oct while a
        /// pressed one is several dB/oct flatter. A few dB per octave is therefore the range
        /// real singing covers.
        ///
        /// This used to be a ramp linear in bin index, which is not a constant slope: the mel
        /// axis is linear in Hz below 1 kHz and logarithmic above, so it came out at
        /// 0.8 dB/oct in the low band and 2.1 dB/oct in the high one, only +1.3 dB at 4 kHz.
        /// That was too weak to hear next to the level change.
        /// </summary>
        const double TiltDbPerOct = 3.0;

        /// <summary>
        /// Frequency the tilt pivots on. The level here is set by <see cref="GainDb"/> alone,
        /// which keeps the two constants independent of each other.
        /// </summary>
        const double TiltPivotHz = 1000.0;

        /// <summary>Overall level change in dB at tension = +-100.</summary>
        const double GainDb = 6.0;

        /// <summary>
        /// Number of mel cepstral coefficients kept as the spectral envelope.
        ///
        /// Warping the whole mel frame does not work: the low bins are narrow enough to
        /// resolve individual harmonics, so the warp moves the harmonics too and the vocoder
        /// follows them. Measured on a 260.9 Hz note, a full band warp of 2.0 came back as
        /// 521.7 Hz, an exact octave. Warping only the low quefrency envelope and adding the
        /// original fine structure back keeps the pitch at 260.9 Hz while the spectral
        /// centroid still moves by the intended factor.
        /// </summary>
        const int LifterOrder = 20;

        static readonly double[] centers = BuildCenters();
        static readonly double[] tiltShape = BuildTiltShape();
        static readonly double[][] envelopeFilter = BuildEnvelopeFilter();

        static double Mel(double f) {
            return f < 1000.0
                ? 3.0 * f / 200.0
                : 15.0 + 27.0 * Math.Log(f / 1000.0) / Math.Log(6.4);
        }

        static double MelInv(double m) {
            return m < 15.0
                ? 200.0 * m / 3.0
                : 1000.0 * Math.Exp((m - 15.0) * Math.Log(6.4) / 27.0);
        }

        static double MelStep => (Mel(FMax) - Mel(FMin)) / (Bins + 1);

        static double[] BuildCenters() {
            var result = new double[Bins];
            double melMin = Mel(FMin);
            double step = (Mel(FMax) - melMin) / (Bins + 1);
            for (int b = 0; b < Bins; b++) {
                result[b] = MelInv(melMin + (b + 1) * step);
            }
            return result;
        }

        /// <summary>Octaves of each bin relative to the pivot, so the tilt is a plain scale.</summary>
        static double[] BuildTiltShape() {
            var result = new double[Bins];
            for (int b = 0; b < Bins; b++) {
                result[b] = Math.Log2(centers[b] / TiltPivotHz);
            }
            return result;
        }

        /// <summary>
        /// Projection onto the first <see cref="LifterOrder"/> DCT-II basis vectors,
        /// precomputed so that extracting the envelope is a single matrix product.
        /// </summary>
        static double[][] BuildEnvelopeFilter() {
            var basis = new double[LifterOrder][];
            for (int k = 0; k < LifterOrder; k++) {
                double scale = Math.Sqrt((k == 0 ? 1.0 : 2.0) / Bins);
                var row = new double[Bins];
                for (int b = 0; b < Bins; b++) {
                    row[b] = scale * Math.Cos(Math.PI * k * (b + 0.5) / Bins);
                }
                basis[k] = row;
            }
            var filter = new double[Bins][];
            for (int i = 0; i < Bins; i++) {
                var row = new double[Bins];
                for (int j = 0; j < Bins; j++) {
                    double sum = 0;
                    for (int k = 0; k < LifterOrder; k++) {
                        sum += basis[k][i] * basis[k][j];
                    }
                    row[j] = sum;
                }
                filter[i] = row;
            }
            return filter;
        }

        /// <summary>Continuous bin index of a frequency. May fall outside [0, Bins - 1].</summary>
        static double BinOf(double f) {
            return (Mel(Math.Max(f, 1e-6)) - Mel(FMin)) / MelStep - 1.0;
        }

        /// <summary>Reads the envelope at a fractional bin, clamping to the edge values.</summary>
        static double SampleEnvelope(double[] envelope, double bin) {
            if (bin <= 0) {
                return envelope[0];
            }
            if (bin >= Bins - 1) {
                return envelope[Bins - 1];
            }
            int i = (int)bin;
            double t = bin - i;
            return envelope[i] * (1.0 - t) + envelope[i + 1] * t;
        }

        public static bool IsNoOp(double[] gender, double[] tension) {
            return IsAllZero(gender) && IsAllZero(tension);
        }

        static bool IsAllZero(double[] curve) {
            if (curve == null) {
                return true;
            }
            foreach (var v in curve) {
                if (v != 0) {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Applies a formant warp (gender) and a spectral tilt (tension) to a melspec.
        /// Both curves are per frame and use the OpenUtau range of -100 to 100 with 0
        /// meaning no change. Frames that are entirely at the floor are left untouched,
        /// otherwise silence would pick up energy from the warp.
        /// </summary>
        public static double[] Edit(double[] melspec, double[] gender, double[] tension) {
            int frames = melspec.Length / Bins;
            var result = new double[melspec.Length];
            Array.Copy(melspec, result, melspec.Length);
            for (int t = 0; t < frames; t++) {
                int offset = t * Bins;
                if (IsSilent(melspec, offset)) {
                    continue;
                }
                // Follows the UTAU g flag convention: negative raises the formants for a
                // younger, thinner voice, positive lowers them for a heavier adult one.
                // Symmetric on a log frequency axis, and never degenerate at the ends.
                double ratio = Math.Pow(2.0, -CurveAt(gender, t) / 100.0);
                // Positive tension is louder and brighter, negative is quieter and duller,
                // so the level and the tilt move together.
                double tension01 = CurveAt(tension, t) / 100.0;
                double tilt = tension01 * TiltDbPerOct / 20.0;
                double gain = tension01 * GainDb / 20.0;
                bool warp = Math.Abs(ratio - 1.0) > 1e-6;
                if (!warp && tension01 == 0) {
                    continue;
                }
                double[] envelope = null;
                if (warp) {
                    envelope = new double[Bins];
                    for (int b = 0; b < Bins; b++) {
                        var row = envelopeFilter[b];
                        double sum = 0;
                        for (int j = 0; j < Bins; j++) {
                            sum += row[j] * melspec[offset + j];
                        }
                        envelope[b] = sum;
                    }
                }
                for (int b = 0; b < Bins; b++) {
                    double value = melspec[offset + b];
                    if (warp) {
                        // Move the envelope, leave the harmonic fine structure in place.
                        value += SampleEnvelope(envelope, BinOf(centers[b] / ratio)) - envelope[b];
                    }
                    if (tension01 != 0) {
                        value += gain + tilt * tiltShape[b];
                    }
                    result[offset + b] = Math.Max(value, Floor);
                }
            }
            return result;
        }

        static bool IsSilent(double[] melspec, int offset) {
            for (int b = 0; b < Bins; b++) {
                if (melspec[offset + b] > Floor + 1e-6) {
                    return false;
                }
            }
            return true;
        }

        static double CurveAt(double[] curve, int frame) {
            if (curve == null || curve.Length == 0) {
                return 0;
            }
            return curve[Math.Min(frame, curve.Length - 1)];
        }
    }
}
