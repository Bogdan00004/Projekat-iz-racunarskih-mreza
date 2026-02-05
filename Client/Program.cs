using System;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Client
{
    internal class Program
    {
        private const int UDP_PORT = 5001;
        static void Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            Console.Write("IP servera (npr 127.0.0.1): ");
            string ipStr = (Console.ReadLine() ?? "127.0.0.1").Trim();

            Console.Write("TCP port (npr 5000): ");
            if (!int.TryParse(Console.ReadLine(), out int port)) port = 5000;

            try
            {
                Socket s = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                s.Connect(IPAddress.Parse(ipStr), port);

                // primi ID kao liniju
                string idLine = RecvLine(s);

                if (!int.TryParse(idLine, out int id))
                {
                    Console.WriteLine("Neispravan ID od servera.");
                    s.Close();
                    return;
                }

                Console.WriteLine($"Prijava uspešna. Moj ID: {id}");

                IPEndPoint serverUdp = new IPEndPoint(IPAddress.Parse(ipStr), UDP_PORT);

                Socket udp = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                udp.ReceiveTimeout = 2000; // 2 sekunde timeout

                bool run = true;
                while (run)
                {
                    Console.WriteLine();
                    Console.WriteLine("UDP meni:");
                    Console.WriteLine("  1 - Provera knjige");
                    Console.WriteLine("  2 - Lista dostupnih knjiga");
                    Console.WriteLine("  0 - Izlaz");
                    Console.Write("Izbor: ");
                    string izbor = (Console.ReadLine() ?? "").Trim();

                    if (izbor == "0")
                    {
                        run = false;
                    }
                    else if (izbor == "1")
                    {
                        Console.Write("Naslov: ");
                        string naslov = (Console.ReadLine() ?? "").Trim();
                        Console.Write("Autor: ");
                        string autor = (Console.ReadLine() ?? "").Trim();

                        string req = $"PROVERA|{naslov}|{autor}";
                        string resp = UdpRequest(udp, serverUdp, req);
                        Console.WriteLine("Odgovor servera: " + resp);
                    }
                    else if (izbor == "2")
                    {
                        string resp = UdpRequest(udp, serverUdp, "LISTA");
                        Console.WriteLine("Odgovor servera:");
                        Console.WriteLine(resp.Replace('|', ' '));
                    }
                    else
                    {
                        Console.WriteLine("Nepoznata opcija.");
                    }
                }

                udp.Close();
                s.Close();
                return;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Greška: " + ex.Message);
            }
        }
        private static string UdpRequest(Socket udp, IPEndPoint server, string text)
        {
            try
            {
                byte[] data = Encoding.UTF8.GetBytes(text);
                udp.SendTo(data, server);

                EndPoint remote = new IPEndPoint(IPAddress.Any, 0);
                byte[] buf = new byte[8192];
                int n = udp.ReceiveFrom(buf, ref remote);
                return Encoding.UTF8.GetString(buf, 0, n).Trim();
            }
            catch (SocketException)
            {
                return "NE|UDP_TIMEOUT";
            }
            catch (Exception ex)
            {
                return "NE|UDP_GRESKA|" + ex.Message;
            }
        }
        private static string RecvLine(Socket s)
        {
            StringBuilder sb = new StringBuilder();
            byte[] b = new byte[1];

            while (true)
            {
                int n = s.Receive(b);
                if (n <= 0) break;

                char c = (char)b[0];
                if (c == '\n') break;
                if (c != '\r') sb.Append(c);
            }
            return sb.ToString().Trim();
        }
    }
}