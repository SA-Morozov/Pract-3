using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;

class ChatClient
{
    private static string serverIp = "127.0.0.1";
    private static int tcpPort = 9000;
    private const string ConfigFile = "client_config.txt";

    static void Main(string[] args)
    {
        LoadConfiguration();

        try
        {
            using TcpClient tcpClient = new TcpClient(serverIp, tcpPort);
            using NetworkStream tcpStream = tcpClient.GetStream();
            using StreamReader tcpReader = new StreamReader(tcpStream, Encoding.UTF8);
            using StreamWriter tcpWriter = new StreamWriter(tcpStream, Encoding.UTF8) { AutoFlush = true };

            Console.WriteLine("Connected to the server.");

            // Чтение сообщения от сервера (запрос имени пользователя)
            string serverMessage = tcpReader.ReadLine();
            if (serverMessage == "Enter your username:")
            {
                // Отправка имени пользователя
                Console.Write("Enter your username: ");
                string username = Console.ReadLine();
                tcpWriter.WriteLine(username);  // Отправляем имя пользователя на сервер
            }

            // Поток для чтения сообщений от сервера
            Thread readThread = new Thread(() =>
            {
                try
                {
                    while (true)
                    {
                        string serverMessage = tcpReader.ReadLine();
                        if (serverMessage == null) break;
                        Console.WriteLine(serverMessage);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error reading from server: {ex.Message}");
                }
            });
            readThread.Start();

            // Основной поток для отправки сообщений на сервер
            while (true)
            {
                string message = Console.ReadLine();
                if (message != null)
                {
                    tcpWriter.WriteLine(message);  // Отправка сообщения на сервер
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
    }

    // Метод для загрузки конфигурации из файла
    private static void LoadConfiguration()
    {
        if (File.Exists(ConfigFile))
        {
            try
            {
                foreach (string line in File.ReadAllLines(ConfigFile))
                {
                    string[] parts = line.Split('=');
                    if (parts.Length == 2)
                    {
                        string key = parts[0].Trim();
                        string value = parts[1].Trim();

                        if (key.Equals("ServerIp", StringComparison.OrdinalIgnoreCase))
                        {
                            serverIp = value;
                        }
                        else if (key.Equals("ServerPort", StringComparison.OrdinalIgnoreCase) && int.TryParse(value, out int parsedPort))
                        {
                            tcpPort = parsedPort;
                        }
                    }
                }

                Console.WriteLine("Configuration loaded successfully.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading configuration file: {ex.Message}");
            }
        }
        else
        {
            Console.WriteLine("Configuration file not found. Using default settings.");
        }
    }
}
