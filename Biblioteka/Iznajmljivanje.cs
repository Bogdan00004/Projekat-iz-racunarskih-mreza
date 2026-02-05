using System;

namespace Biblioteka
{
    [Serializable]
    public class Iznajmljivanje
    {
        public string Knjiga;
        public int Clan;
        public DateTime DatumVracanja;
        public override string ToString()
        {
            return $"{Knjiga} | Clan={Clan} | Vratiti do: {DatumVracanja:dd.MM.yyyy}";
        }
    }
}
