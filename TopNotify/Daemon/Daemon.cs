using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using System.IO.Pipes;
using TopNotify.Common;
using TopNotify.GUI;

namespace TopNotify.Daemon
{
    public class Daemon
    {
        const string PipeName = "samsidparty_topnotify";

        public static Daemon Instance;

        public InterceptorManager Manager;

        public Daemon() {
            Instance = this;

            TrayIcon.Setup();

            Thread managerThread = new Thread(CreateManager);
            managerThread.Start();

            Task.Run(PipeListener);

            TrayIcon.MainLoop();
        }

        /// <summary>
        /// Listens for messages that affect the app lifecycle. Named pipe replaces the old
        /// mailslot transport, which had no delivery confirmation - a settings change sent
        /// while the daemon wasn't yet listening (or under any other timing edge case) would
        /// silently vanish, leaving the daemon on stale config with no indication to the user.
        /// </summary>
        async Task PipeListener()
        {
            while (true)
            {
                try
                {
                    using (var server = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous))
                    {
                        await server.WaitForConnectionAsync();

                        using (var reader = new StreamReader(server, Encoding.UTF8, false, 1024, leaveOpen: true))
                        using (var writer = new StreamWriter(server, Encoding.UTF8, 1024, leaveOpen: true) { AutoFlush = true })
                        {
                            var msg = await reader.ReadLineAsync();

                            if (msg == "UpdateConfig") // Runs when the user changes a setting from the GUI
                            {
                                InterceptorManager.Instance.OnSettingsChanged();
                            }

                            await writer.WriteLineAsync("ACK");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Program.Logger.Warning(ex, "Daemon pipe listener iteration failed, restarting listener");
                }
            }
        }

        /// <summary>
        /// This should be called from an external (non-daemon) process to send a message to the daemon.
        /// Returns true only once the daemon has acknowledged receipt - callers can use this to warn
        /// the user if a settings change didn't actually reach the running daemon.
        /// </summary>
        public static bool SendCommandToDaemon(string message, int timeoutMs = 1000)
        {
            try
            {
                using (var client = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut))
                {
                    client.Connect(timeoutMs);

                    using (var writer = new StreamWriter(client, Encoding.UTF8, 1024, leaveOpen: true) { AutoFlush = true })
                    using (var reader = new StreamReader(client, Encoding.UTF8, false, 1024, leaveOpen: true))
                    {
                        writer.WriteLine(message);
                        return reader.ReadLine() == "ACK";
                    }
                }
            }
            catch (Exception ex)
            {
                Program.Logger.Warning(ex, $"SendCommandToDaemon('{message}') failed to reach the daemon");
                return false;
            }
        }

        public void CreateManager()
        {
            Manager = new InterceptorManager();
            Manager.Start();
        }
    }
}
