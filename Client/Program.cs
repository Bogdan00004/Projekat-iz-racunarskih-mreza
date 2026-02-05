using System;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Client
{
    internal class Program
    {
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
                Console.WriteLine("Sačuvaj ovaj ID (kasnije ga šalješ u TCP porukama).");

                // za sada samo čekaj
                Console.WriteLine("ENTER za izlaz...");
                Console.ReadLine();

                s.Close();
            }
            catch (Exception ex)
            {
                Console.WriteLine("Greška: " + ex.Message);
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