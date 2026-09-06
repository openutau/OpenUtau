using System;
using NetMQ;
using NetMQ.Sockets;
using Serilog;

namespace OpenUtau.Core.Enunu {
    class EnunuClient : Util.SingletonBase<EnunuClient> {
        internal T SendRequest<T>(string[] args) {
            return SendRequest<T>(args, "15555");
        }
        internal T SendRequest<T>(string[] args, string port,int second = 300 ) {
            using (var client = new RequestSocket()) {
                client.Connect($"tcp://localhost:{port}");
                string request = Json.Serialize(args);
                Log.Information($"EnunuProcess sending {request}");
                client.SendFrame(request);
                client.TryReceiveFrameString(TimeSpan.FromSeconds(second), out string? message);
                Log.Information($"EnunuProcess received {message}");
                if (string.IsNullOrEmpty(message)) {
                    return (T)Activator.CreateInstance(typeof(T))!;
                }
                return Json.Deserialize<T>(message ?? string.Empty)!;
            }
        }
    }
}
