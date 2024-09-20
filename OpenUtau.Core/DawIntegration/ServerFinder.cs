﻿using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace OpenUtau.Core.DawIntegration {
    public class ServerFinder {
        private static string getServerPath() {
            var temp = Path.GetTempPath();

            return $"{temp}OpenUtau/PluginServers";
        }
        public static async Task<List<Server>> FindServers() {
            var path = getServerPath();
            if (!Directory.Exists(path)) {
                return new List<Server>();
            }

            var di = new DirectoryInfo(path);
            var files = di.GetFiles("*.json");

            var servers = new List<Server>();
            foreach (FileInfo file in files) {
                try {
                    var json = await File.ReadAllTextAsync(file.FullName);
                    var server = JsonConvert.DeserializeObject<Server>(json);
                    if (server != null && CheckPortUsing(server.Port)) {
                        servers.Add(server);
                        continue;
                    }
                } catch {
                    // Ignore invalid server files
                }
                // Delete invalid server files
                file.Delete();
            }

            return servers;
        }

        private static bool CheckPortUsing(int port) {
            var tcpListener = default(TcpListener);

            try {
                var ipAddress = IPAddress.Parse("127.0.0.1");

                tcpListener = new TcpListener(ipAddress, port);
                tcpListener.Start();

                return false;
            } catch (SocketException) {
            } finally {
                if (tcpListener != null)
                    tcpListener.Stop();
            }

            return true;
        }
    }

    public class Server {
        public int Port { get; }
        public string Name { get; }

        [JsonConstructor]
        Server(int port, string name) {
            Port = port;
            Name = name;
        }

        public override string ToString() {
            return Name;
        }
    }
}
