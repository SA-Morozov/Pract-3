using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

class ChatServer
{
    private static TcpListener tcpListener;
    private static UdpClient udpListener;
    private static readonly Dictionary<string, ClientInfo> clients = new Dictionary<string, ClientInfo>();
    private static readonly object lockObj = new object();
    private static bool isRunning = true;

    private const string ConfigFile = "server_config.txt";
    private const string ServerLogFile = "server.log";

    static void Main(string[] args)
    {
        string serverIp = "127.0.0.1";
        int tcpPort = 9000;
        int udpPort = 9001;

        LoadConfiguration(ref serverIp, ref tcpPort, ref udpPort);
        InitializeLogFile(ServerLogFile);

        LogServer($"Starting chat server on {serverIp}:{tcpPort} (UDP: {udpPort})");

        try
        {
            tcpListener = new TcpListener(IPAddress.Parse(serverIp), tcpPort);
            tcpListener.Start();
            LogServer("TCP server started.");

            udpListener = new UdpClient(udpPort);
            LogServer($"UDP server started on port {udpPort}.");

            Thread tcpAcceptThread = new Thread(AcceptClients);
            tcpAcceptThread.Start();

            Thread udpThread = new Thread(HandleUdpMessages);
            udpThread.Start();

            Console.WriteLine("Type 'exit' to stop the server.");
            while (true)
            {
                string command = Console.ReadLine();
                if (command != null && command.ToLower() == "exit")
                {
                    StopServer();
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            LogServer($"Server Error: {ex.Message}");
        }
    }

    private static void LoadConfiguration(ref string serverIp, ref int tcpPort, ref int udpPort)
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
                        else if (key.Equals("ServerPort", StringComparison.OrdinalIgnoreCase) && int.TryParse(value, out int parsedTcpPort))
                        {
                            tcpPort = parsedTcpPort;
                        }
                        else if (key.Equals("UdpPort", StringComparison.OrdinalIgnoreCase) && int.TryParse(value, out int parsedUdpPort))
                        {
                            udpPort = parsedUdpPort;
                        }
                    }
                }
                Console.WriteLine("Configuration loaded successfully from server_config.txt.");
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

    private static void AcceptClients()
    {
        while (isRunning)
        {
            try
            {
                TcpClient client = tcpListener.AcceptTcpClient();
                Thread clientThread = new Thread(() => HandleTcpClient(client));
                clientThread.Start();
            }
            catch (SocketException)
            {
                if (!isRunning)
                {
                    LogServer("Server stopped accepting clients.");
                }
            }
        }
    }

    private static void HandleTcpClient(TcpClient client)
    {
        string username = null;

        try
        {
            NetworkStream stream = client.GetStream();
            StreamReader reader = new StreamReader(stream, Encoding.UTF8);
            StreamWriter writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };

            writer.WriteLine("Enter your username:");
            username = reader.ReadLine()?.Trim();

            if (string.IsNullOrEmpty(username))
            {
                writer.WriteLine("Invalid username. Disconnecting...");
                client.Close();
                return;
            }

            lock (lockObj)
            {
                if (!clients.ContainsKey(username))
                {
                    clients[username] = new ClientInfo { TcpClient = client };
                    LogServer($"{username} connected.");
                    BroadcastMessage($"Server: {username} joined the chat.", username);
                }
                else
                {
                    writer.WriteLine("Username already in use. Disconnecting...");
                    client.Close();
                    return;
                }
            }

            while (client.Connected)
            {
                string message = reader.ReadLine();
                if (message == null) break;

                ProcessTcpMessage(username, message);
            }
        }
        catch (Exception ex)
        {
            LogServer($"Error with client {username}: {ex.Message}");
        }
        finally
        {
            DisconnectClient(username);
        }
    }

    private static void HandleUdpMessages()
    {
        IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);

        try
        {
            while (isRunning)
            {
                byte[] data = udpListener.Receive(ref remoteEndPoint);
                string message = Encoding.UTF8.GetString(data);

                lock (lockObj)
                {
                    string username = null;

                    foreach (var kvp in clients)
                    {
                        if (kvp.Value.UdpEndPoint?.Equals(remoteEndPoint) == true)
                        {
                            username = kvp.Key;
                            break;
                        }
                    }

                    if (username == null)
                    {
                        foreach (var kvp in clients)
                        {
                            if (kvp.Value.TcpClient != null && kvp.Value.UdpEndPoint == null)
                            {
                                kvp.Value.UdpEndPoint = remoteEndPoint;
                                username = kvp.Key;
                                LogServer($"UDP client registered: {username} ({remoteEndPoint})");
                                break;
                            }
                        }

                        if (username == null)
                        {
                            LogServer($"Unregistered UDP client: {remoteEndPoint}");
                            continue;
                        }
                    }

                    // Обработка команды /users
                    if (message.Equals("/users", StringComparison.OrdinalIgnoreCase))
                    {
                        string userList = string.Join(", ", clients.Keys);
                        byte[] responseBytes = Encoding.UTF8.GetBytes($"Active users: {userList}");
                        udpListener.Send(responseBytes, responseBytes.Length, remoteEndPoint);
                    }
                    else
                    {
                        BroadcastMessage($"{username}: {message}", username);
                    }
                }
            }
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.Interrupted)
        {
        }
        catch (Exception ex)
        {
            LogServer($"UDP Error: {ex.Message}");
        }
    }

    private static void ProcessTcpMessage(string username, string message)
    {
        // Личное сообщение
        if (message.StartsWith("@"))
        {
            string[] parts = message.Split(' ', 2);
            if (parts.Length == 2)
            {
                string recipient = parts[0].Substring(1).Trim();
                string privateMessage = parts[1];
                SendPrivateMessage(recipient, privateMessage, username);
            }
        }
        // Широковещательное сообщение через команду !broadcast
        else if (message.StartsWith("!broadcast"))
        {
            string broadcastMessage = message.Substring(10).Trim(); // Отрезаем "!broadcast" и пробел
            BroadcastMessage($"[Broadcast] {username}: {broadcastMessage}", null);
        }
        // Команда /users
        else if (message.Equals("/users", StringComparison.OrdinalIgnoreCase))
        {
            string userList = string.Join(", ", clients.Keys);
            SendMessage(clients[username].TcpClient, $"Active users: {userList}");
        }
        else
        {
            // Обычное сообщение
            BroadcastMessage($"{username}: {message}", username);
        }
    }

    private static void DisconnectClient(string username)
    {
        lock (lockObj)
        {
            if (clients.ContainsKey(username))
            {
                clients.Remove(username);
                LogServer($"{username} disconnected.");
                BroadcastMessage($"Server: {username} left the chat.", null);
            }
        }
    }

    private static void SendPrivateMessage(string recipient, string message, string sender)
    {
        string timestamp = DateTime.Now.ToString("HH:mm:ss");
        string formattedMessage = $"[{timestamp}] {sender} (private): {message}";

        lock (lockObj)
        {
            if (clients.TryGetValue(recipient, out var clientInfo))
            {
                SendMessage(clientInfo.TcpClient, formattedMessage);
            }
            else
            {
                SendMessage(clients[sender].TcpClient, $"User {recipient} not found.");
            }
        }
    }

    private static void BroadcastMessage(string message, string excludeUsername)
    {
        string timestamp = DateTime.Now.ToString("HH:mm:ss");
        string formattedMessage = $"[{timestamp}] {message}";

        lock (lockObj)
        {
            foreach (var kvp in clients)
            {
                if (kvp.Key != excludeUsername)
                {
                    if (kvp.Value.TcpClient != null)
                    {
                        SendMessage(kvp.Value.TcpClient, formattedMessage);
                    }
                }
            }
        }
    }

    private static void SendMessage(TcpClient client, string message)
    {
        try
        {
            NetworkStream stream = client.GetStream();
            StreamWriter writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };
            writer.WriteLine(message);
        }
        catch (Exception ex)
        {
            LogServer($"Failed to send message: {ex.Message}");
        }
    }

    private static void StopServer()
    {
        isRunning = false;

        tcpListener?.Stop();
        udpListener?.Close();

        lock (lockObj)
        {
            foreach (var client in clients.Values)
            {
                client.TcpClient?.Close();
            }
            clients.Clear();
        }

        LogServer("Server stopped.");
    }

    private static void LogServer(string message)
    {
        Console.WriteLine(message);
        File.AppendAllText(ServerLogFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - {message}{Environment.NewLine}");
    }

    private static void InitializeLogFile(string fileName)
    {
        if (!File.Exists(fileName))
        {
            File.Create(fileName).Close();
        }
    }

    private class ClientInfo
    {
        public TcpClient TcpClient { get; set; }
        public IPEndPoint UdpEndPoint { get; set; }
    }
}