using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Biblioteka;
namespace Server
{
    internal class Program
    {
        private const int TCP_PORT = 5000;  // PRISTUPNA
        private const int UDP_PORT = 5001;  // INFO

        private static Socket sTcpListen;
        private static Socket sUdpInfo;

        private static List<Socket> sTcpClients = new List<Socket>();
        private static List<Knjiga> sKnjige = new List<Knjiga>();

        private static int sNextId = 1000;

        private static Dictionary<string, DateTime> sPoslednjiZahtev = new Dictionary<string, DateTime>();

        private static string Key(string naslov, string autor)
        {
            return (naslov ?? "").Trim().ToLowerInvariant() + "|" + (autor ?? "").Trim().ToLowerInvariant();
        }

        static void Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            // TCP PRISTUPNA
            sTcpListen = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            sTcpListen.Bind(new IPEndPoint(IPAddress.Any, TCP_PORT));
            sTcpListen.Listen(10);
            sTcpListen.Blocking = false;

            // UDP INFO
            sUdpInfo = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            sUdpInfo.Bind(new IPEndPoint(IPAddress.Any, UDP_PORT));
            sUdpInfo.Blocking = false;

            string ip = GetLocalIPv4() ?? "127.0.0.1";
            Console.WriteLine($"INFO utičnica (UDP):      {ip}:{UDP_PORT}");
            Console.WriteLine($"PRISTUPNA utičnica (TCP): {ip}:{TCP_PORT}");
            Console.WriteLine();

            Console.WriteLine("Komande servera:");
            Console.WriteLine("  ADD  - dodaj knjigu");
            Console.WriteLine("  LIST - prikaži knjige");
            Console.WriteLine("  EXIT - ugasi server");
            Console.WriteLine();

            bool running = true;

            while (running)
            {
                // 1) Konzola (neblokirajuće)
                if (Console.KeyAvailable)
                {
                    string cmd = (Console.ReadLine() ?? "").Trim().ToUpperInvariant();

                    if (cmd == "ADD") AddBook();
                    else if (cmd == "LIST") ListBooks();
                    else if (cmd == "EXIT") running = false;
                    else Console.WriteLine("Nepoznata komanda. (ADD/LIST/EXIT)");
                }

                // 2) Select liste
                List<Socket> checkRead = new List<Socket>();
                List<Socket> checkError = new List<Socket>();

                checkRead.Add(sUdpInfo);
                checkError.Add(sUdpInfo);

                checkRead.Add(sTcpListen);
                checkError.Add(sTcpListen);

                foreach (var c in sTcpClients)
                {
                    checkRead.Add(c);
                    checkError.Add(c);
                }

                Socket.Select(checkRead, null, checkError, 100_000);

                // 3) Error sockets -> ukloni klijente
                if (checkError.Count > 0)
                {
                    foreach (var s in checkError.ToList())
                    {
                        if (s == sTcpListen || s == sUdpInfo) continue;
                        RemoveClient(s);
                    }
                }

                // 4) Read sockets -> obradi
                foreach (Socket s in checkRead)
                {
                    if (s == sTcpListen)
                    {
                        AcceptNewClient();
                    }
                    else if (s == sUdpInfo)
                    {
                        HandleUdpMessage();
                    }
                    else
                    {
                        // čitamo ako nešto dođe da ne visi socket.
                        HandleTcpClientMessage(s);
                    }
                }
            }

            // GAŠENJE
            try
            {
                foreach (var c in sTcpClients) c.Close();
            }
            catch (Exception ex)
            {
                Console.WriteLine("[GAŠENJE] Greška pri zatvaranju klijenata: " + ex.Message);
            }

            try { sTcpListen.Close(); }
            catch (Exception ex) { Console.WriteLine("[GAŠENJE] Greška pri zatvaranju TCP utičnice: " + ex.Message); }

            try { sUdpInfo.Close(); }
            catch (Exception ex) { Console.WriteLine("[GAŠENJE] Greška pri zatvaranju UDP utičnice: " + ex.Message); }

            Console.WriteLine("Server je uspešno ugašen.");
        }
        private static void HandleUdpMessage()
        {
            try
            {
                EndPoint remote = new IPEndPoint(IPAddress.Any, 0);
                byte[] buf = new byte[4096];
                int n = sUdpInfo.ReceiveFrom(buf, ref remote);
                if (n <= 0) return;

                string req = Encoding.UTF8.GetString(buf, 0, n).Trim();
                string resp = ObradiUdpZahtev(req);

                byte[] outData = Encoding.UTF8.GetBytes(resp);
                sUdpInfo.SendTo(outData, remote);
            }
            catch (SocketException se)
            {
                // 10035 = WSAEWOULDBLOCK
                if (se.ErrorCode != 10035)
                    Console.WriteLine("[UDP] Greška pri prijemu UDP poruke: " + se.Message + $" (kod={se.ErrorCode})");
            }
            catch (Exception ex)
            {
                Console.WriteLine("[UDP] Opšta greška pri obradi UDP poruke: " + ex.Message);
            }
        }
        private static string ObradiUdpZahtev(string req)
        {
            if (string.IsNullOrWhiteSpace(req))
                return "NE|PRAZAN_ZAHTEV|NIKAD";

            string[] parts = req.Split('|');

            // LISTA
            if (parts[0].Trim().ToUpperInvariant() == "LISTA")
            {
                var dostupne = sKnjige.Where(k => k != null && k.Kolicina > 0).ToList();

                StringBuilder sb = new StringBuilder();
                sb.Append("LISTA|").Append(dostupne.Count).Append('\n');

                foreach (var k in dostupne)
                    sb.Append(k.Naslov).Append('|').Append(k.Autor).Append('|').Append(k.Kolicina).Append('\n');

                return sb.ToString().TrimEnd('\n');
            }

            // PROVERA|Naslov|Autor
            if (parts[0].Trim().ToUpperInvariant() == "PROVERA")
            {
                if (parts.Length < 3)
                    return "NE|LOSE_FORMATIRANO|NIKAD";

                string naslov = parts[1].Trim();
                string autor = parts[2].Trim();
                string key = Key(naslov, autor);

                // vreme poslednjeg zahteva (ako postoji)
                string poslednjiStr = "NIKAD";
                if (sPoslednjiZahtev.TryGetValue(key, out DateTime dt))
                    poslednjiStr = dt.ToString("dd.MM.yyyy HH:mm:ss");

                // AŽURIRAJ poslednji zahtev (svaki PROVERA upit smatramo zahtevom)
                sPoslednjiZahtev[key] = DateTime.Now;
                poslednjiStr = sPoslednjiZahtev[key].ToString("dd.MM.yyyy HH:mm:ss");

                // nađi knjigu
                var knj = sKnjige.FirstOrDefault(k =>
                    k != null &&
                    string.Equals(k.Naslov?.Trim(), naslov, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(k.Autor?.Trim(), autor, StringComparison.OrdinalIgnoreCase));

                if (knj == null)
                    return "NE|NE_POSTOJI|" + poslednjiStr;

                if (knj.Kolicina > 0)
                    return "OK|" + knj.Kolicina + "|" + poslednjiStr;

                return "NE|NEMA_NA_STANJU|" + poslednjiStr;
            }

            return "NE|NEPOZNATA_KOMANDA|NIKAD";
        }
        private static void AcceptNewClient()
        {
            try
            {
                Socket client = sTcpListen.Accept();
                client.Blocking = false;
                sTcpClients.Add(client);

                int id = sNextId++;
                SendLine(client, id.ToString());

                IPEndPoint ep = (IPEndPoint)client.RemoteEndPoint;
                Console.WriteLine($"[TCP] Prijava klijenta: {ep.Address}:{ep.Port} -> dodeljen ID = {id}");
            }
            catch (SocketException se)
            {
                // 10035 = (očekivano u non-blocking režimu)
                if (se.ErrorCode != 10035)
                    Console.WriteLine("[TCP] Greška pri prihvatanju novog klijenta: " + se.Message + $" (kod={se.ErrorCode})");
            }
            catch (Exception ex)
            {
                Console.WriteLine("[TCP] Opšta greška pri prihvatanju klijenta: " + ex.Message);
            }
        }

        private static void HandleTcpClientMessage(Socket client)
        {
            try
            {
                byte[] buf = new byte[1024];
                int n = client.Receive(buf);

                if (n <= 0)
                {
                    RemoveClient(client);
                    return;
                }

                string msg = Encoding.UTF8.GetString(buf, 0, n).Trim();
                if (msg.Length > 0)
                    Console.WriteLine($"[TCP] Primljena poruka od klijenta: {msg}");
            }
            catch (SocketException se)
            {
                // 10035 =(nema podataka trenutno)
                if (se.ErrorCode != 10035)
                {
                    Console.WriteLine("[TCP] Greška pri prijemu poruke od klijenta: " + se.Message + $" (kod={se.ErrorCode})");
                    RemoveClient(client);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[TCP] Opšta greška pri prijemu poruke: " + ex.Message);
                RemoveClient(client);
            }
        }

        private static void RemoveClient(Socket client)
        {
            try
            {
                IPEndPoint ep = client.RemoteEndPoint as IPEndPoint;
                Console.WriteLine($"[TCP] Klijent je prekinuo vezu: {ep?.Address}:{ep?.Port}");
            }
            catch (Exception ex)
            {
                Console.WriteLine("[TCP] Greška pri čitanju podataka o klijentu: " + ex.Message);
            }

            try { client.Close(); }
            catch (Exception ex) { Console.WriteLine("[TCP] Greška pri zatvaranju klijenta: " + ex.Message); }

            sTcpClients.Remove(client);
        }

        private static void SendLine(Socket client, string line)
        {
            byte[] data = Encoding.UTF8.GetBytes(line + "\n");
            client.Send(data);
        }

        private static void AddBook() // dodaj knjigu
        {
            Knjiga k = new Knjiga();

            Console.Write("Naslov: ");
            k.Naslov = (Console.ReadLine() ?? "").Trim();

            Console.Write("Autor: ");
            k.Autor = (Console.ReadLine() ?? "").Trim();

            Console.Write("Količina: ");
            if (!int.TryParse(Console.ReadLine(), out int kol) || kol < 0)
            {
                Console.WriteLine("Neispravna količina.");
                return;
            }
            k.Kolicina = kol;

            sKnjige.Add(k);
            Console.WriteLine("Knjiga je uspešno dodata.");
        }

        private static void ListBooks() // prikaži knjige
        {
            if (sKnjige.Count == 0)
            {
                Console.WriteLine("Trenutno nema knjiga u evidenciji.");
                return;
            }

            Console.WriteLine("Knjige:");
            foreach (var k in sKnjige)
                Console.WriteLine(" - " + k);
        }

        private static string GetLocalIPv4()
        {
            try
            {
                var host = Dns.GetHostEntry(Dns.GetHostName());
                var ip = host.AddressList.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a));
                return ip?.ToString();
            }
            catch (Exception ex)
            {
                Console.WriteLine("[IP] Greška pri pronalaženju IPv4 adrese: " + ex.Message);
                return null;
            }
        }
    }
}
