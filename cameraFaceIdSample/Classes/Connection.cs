﻿using System;
using System.Diagnostics;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading.Tasks;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;

namespace cameraFaceIdSample.Classes
{
    internal class Connection
    {
        private StreamSocket _socket;
        private MainPage _server;

        public Connection(StreamSocket socket, MainPage server)
        {
            _socket = socket;
            _server = server;
            Task.Run(() => Listen());
        }

        private async Task Listen()
        {
            Debug.WriteLine($"Sending connection acknowledgement");
            await _socket.OutputStream.WriteAsync(Encoding.UTF8.GetBytes("0").AsBuffer());
            while (true)
            {
                Debug.WriteLine($"Listening for socket command...");
                IBuffer inbuffer = new Windows.Storage.Streams.Buffer(36);
                await _socket.InputStream.ReadAsync(inbuffer, 36, InputStreamOptions.Partial);
                string command = Encoding.UTF8.GetString(inbuffer.ToArray());
                Debug.WriteLine($"Command received: {command}");
                Guid guid = Guid.Empty;
                Guid.TryParse(command, out guid);
                byte[] data = _server.GetCurrentVideoDataAsync(guid);
                if (data != null)
                    await _socket.OutputStream.WriteAsync(data.AsBuffer());
                else
                    Debug.WriteLine($"Could not intialise, video does not exist");
                await Task.Delay(50);
            }
        }
    }
}
