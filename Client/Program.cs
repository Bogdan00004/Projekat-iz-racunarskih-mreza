using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Client
{
    internal class Program
    {
        private static List<string> sMojeIznajmljene = new List<string>();
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
                    Console.WriteLine("Glavni meni:");
                    Console.WriteLine("  1 - Provera knjige");
                    Console.WriteLine("  2 - Lista dostupnih knjiga");
                    Console.WriteLine("  3 - Iznajmi knjigu");
                    Console.WriteLine("  4 - Moje iznajmljene knjige");
                    Console.WriteLine("  5 - Vrati knjigu");
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
                    else if (izbor == "3")
                    {
                        Console.Write("Naslov: ");
                        string naslov = (Console.ReadLine() ?? "").Trim();

                        Console.Write("Autor: ");
                        string autor = (Console.ReadLine() ?? "").Trim();

                        // TCP zahtev
                        string req = $"IZNAJMI|{id}|{naslov}|{autor}";
                        SendTcpLine(s, req);

                        string resp = RecvLine(s); // server vraća jednu liniju

                        if (resp.StartsWith("OK|IZNAJMLJENO|", StringComparison.OrdinalIgnoreCase))
                        {
                            string[] p = resp.Split('|');
                            string datumVracanja = (p.Length >= 3) ? p[2] : "?";

                            Console.WriteLine("Knjiga je uspešno iznajmljena.");
                            Console.WriteLine("Vratiti do: " + datumVracanja);

                            sMojeIznajmljene.Add(naslov + "|" + autor);
                            Console.WriteLine("Sačuvano u listi iznajmljenih knjiga.");
                        }
                        else
                        {
                            Console.WriteLine("Odgovor servera: " + resp);
                        }
                    }
                    else if (izbor == "4")
                    {
                        if (sMojeIznajmljene.Count == 0)
                        {
                            Console.WriteLine("Nemaš iznajmljenih knjiga.");
                        }
                        else
                        {
                            Console.WriteLine("Moje iznajmljene knjige:");
                            foreach (var x in sMojeIznajmljene)
                                Console.WriteLine(" - " + x);
                        }
                    }
                    else if (izbor == "5")
                    {
                        if (sMojeIznajmljene.Count == 0)
                        {
                            Console.WriteLine("Nemaš iznajmljenih knjiga za vraćanje.");
                            continue;
                        }

                        Console.WriteLine("Izaberi knjigu za vraćanje (unesi broj):");
                        for (int i = 0; i < sMojeIznajmljene.Count; i++)
                            Console.WriteLine($"  {i + 1} - {sMojeIznajmljene[i]}");

                        Console.Write("Broj: ");
                        if (!int.TryParse(Console.ReadLine(), out int idx) || idx < 1 || idx > sMojeIznajmljene.Count)
                        {
                            Console.WriteLine("Neispravan izbor.");
                            continue;
                        }

                        string knjStr = sMojeIznajmljene[idx - 1]; // "Naslov|Autor"
                        string[] p = knjStr.Split('|');
                        string naslov = p[0].Trim();
                        string autor = (p.Length > 1) ? p[1].Trim() : "";

                        string req = $"VRATI|{id}|{naslov}|{autor}";
                        SendTcpLine(s, req);

                        string resp = RecvLine(s);

                        if (resp.StartsWith("OK|VRACENO", StringComparison.OrdinalIgnoreCase))
                        {
                            // ukloni iz lokalne evidencije
                            sMojeIznajmljene.RemoveAt(idx - 1);
                            Console.WriteLine("Knjiga je uspešno vraćena i uklonjena iz tvoje evidencije.");
                        }
                        else
                        {
                            Console.WriteLine("Odgovor servera: " + resp);
                        }
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
        private static void SendTcpLine(Socket s, string line)
        {
            byte[] data = Encoding.UTF8.GetBytes(line + "\n");
            s.Send(data);
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