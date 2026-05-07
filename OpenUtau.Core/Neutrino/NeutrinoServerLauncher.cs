using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using OpenUtau.Core.Util;
using Serilog;

namespace OpenUtau.Core.Neutrino {
    static class NeutrinoServerLauncher {
        const int ServerPort = 12345;
        static readonly object lockObj = new object();
        static Process? serverProcess;

        public static void EnsureStarted(string serverExe) {
            if (string.IsNullOrEmpty(serverExe) || !File.Exists(serverExe)) {
                return;
            }

            var serverName = Path.GetFileNameWithoutExtension(serverExe);
            if (Process.GetProcessesByName(serverName).Any() || IsServerReady()) {
                Log.Information("NEUTRINO server already running: {ServerExe}", serverExe);
                return;
            }

            lock (lockObj) {
                if (serverProcess != null && !serverProcess.HasExited) {
                    return;
                }

                var startedProcess = ProcessRunner.StartBackground(
                    serverExe,
                    string.Empty,
                    Log.Logger,
                    workDir: Path.GetDirectoryName(serverExe));
                startedProcess.EnableRaisingEvents = true;
                startedProcess.Exited += (_, _) => {
                    lock (lockObj) {
                        if (ReferenceEquals(serverProcess, startedProcess)) {
                            serverProcess = null;
                        }
                    }
                };
                serverProcess = startedProcess;
                WaitForServerReady();
                Log.Information("Started NEUTRINO server in background: {ServerExe}", serverExe);
            }
        }

        static bool IsServerReady() {
            try {
                using var client = new TcpClient();
                return client.ConnectAsync("127.0.0.1", ServerPort).Wait(50);
            } catch {
                return false;
            }
        }

        static void WaitForServerReady() {
            for (int i = 0; i < 50; i++) {
                if (IsServerReady()) {
                    return;
                }
                Thread.Sleep(100);
            }
        }
    }
}
