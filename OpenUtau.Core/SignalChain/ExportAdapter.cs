﻿using System;
using NAudio.Wave;
using OpenUtau.Core.Util;

namespace OpenUtau.Core.SignalChain {
    class ExportAdapter : ISampleProvider {
        private readonly WaveFormat waveFormat;
        private readonly ISignalSource source;
        private int position;

        public WaveFormat WaveFormat => waveFormat;

        public ExportAdapter(ISignalSource source) {
            waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);
            this.source = source;
        }

        public ExportAdapter(ISignalSource source, int channels, int samplingRate) {
            waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(samplingRate, channels);
            this.source = source;
        }

        public int Read(float[] buffer, int offset, int count) {
            for (int i = offset; i < offset + count; ++i) {
                buffer[i] = 0;
            }
            if (!source.IsReady(position, count)) {
                throw new Exception("All sources must be ready when exporting.");
            } else {
                int pos = source.Mix(position, buffer, offset, count);
                int n = Math.Max(0, pos - position);
                position = pos;
                return n;
            }
        }
    }
}
