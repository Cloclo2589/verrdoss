using System.IO.Pipes;
using System.Text;
using System.Windows;

namespace Verrdoss.App;

public static class SingleInstance
{
    public const string PipeName = "Verrdoss.SingleInstance";

    public static void Send(string verb, string path)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(2000);
            using var writer = new StreamWriter(client, Encoding.UTF8) { AutoFlush = true };
            writer.WriteLine(verb + "\t" + path);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "VerrDoss est déjà ouvert, mais le dossier n'a pas pu être transmis.\n" + ex.Message,
                Brand.Name,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    public static void Listen(Action<string> onPath)
    {
        Task.Run(async () =>
        {
            while (true)
            {
                try
                {
                    await using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    await server.WaitForConnectionAsync().ConfigureAwait(false);
                    using var reader = new StreamReader(server, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
                    var line = await reader.ReadLineAsync().ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(line))
                        onPath(line);
                }
                catch
                {
                    await Task.Delay(300).ConfigureAwait(false);
                }
            }
        });
    }
}
