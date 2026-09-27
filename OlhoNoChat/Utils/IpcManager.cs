using System.IO;
using System.IO.Pipes;
using System.Text;
using Application = System.Windows.Application;

namespace OlhoNoChat;

public static class IpcManager
{
    // A unique pipe name
#if DEBUG
    private const string PipeName = "OlhoNoChat_Pipe_7A6C5D4B_Dev";
#else
    private const string PipeName = "OlhoNoChat_Pipe_7A6C5D4B";
#endif

    // A special command to just activate the existing window.
    public const string ShowWindowCommand = "::SHOW_WINDOW_COMMAND::";

    private static NamedPipeServerStream _pipeServer;

    public static event Action<string[]> ArgumentsReceived;

    public static bool StartServer()
    {
        try
        {
            // Create the server. If this fails, another instance is running.
            _pipeServer = new NamedPipeServerStream(PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            Task.Run(ListenForConnections);
            return true; // We are the first instance.
        }
        catch
        {
            // Usually an IOException: the pipe name is already in use by another instance.
            _pipeServer = null; // Ensure it's null if we failed.
            return false; // We are a subsequent instance.
        }
    }

    private static async Task ListenForConnections()
    {
        // Add a null check, as StartServer might have failed.
        while (_pipeServer != null)
        {
            try
            {
                // Wait for a client to connect.
                await _pipeServer.WaitForConnectionAsync();

                using (var reader = new StreamReader(_pipeServer, Encoding.UTF8, true, 1024, true))
                {
                    var message = await reader.ReadToEndAsync();

                    // Disconnect to allow the pipe to be used by the next client.
                    _pipeServer.Disconnect();

                    if (!string.IsNullOrEmpty(message))
                    {
                        var args = message.Split(new[] { "|||" }, StringSplitOptions.None);

                        // Use the dispatcher to invoke the event on the UI thread.
                        if (Application.Current != null)
                        {
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                if (ArgumentsReceived != null)
                                {
                                    ArgumentsReceived.Invoke(args);
                                }
                            });
                        }
                    }
                }
            }
            catch
            {
                // If the pipe breaks or is closed, stop listening.
                break;
            }
        }
    }

    public static async Task SendArgumentsToFirstInstance(string[] args)
    {
        try
        {
            using (var pipeClient = new NamedPipeClientStream(".", PipeName, PipeDirection.Out))
            {
                // Set a short timeout.
                await pipeClient.ConnectAsync(200);

                if (pipeClient.IsConnected)
                {
                    // Join all args into a single string to send.
                    var message = args.Length > 0 ? string.Join("|||", args) : ShowWindowCommand;

                    using (var writer = new StreamWriter(pipeClient, Encoding.UTF8))
                    {
                        await writer.WriteAsync(message);
                        await writer.FlushAsync();
                    }
                }
            }
        }
        catch
        {
            // The first instance didn't answer in time: nothing else to do
        }
    }

    public static void StopServer()
    {
        // Add null check before trying to close.
        if (_pipeServer != null)
        {
            _pipeServer.Close();
            _pipeServer = null;
        }
    }
}