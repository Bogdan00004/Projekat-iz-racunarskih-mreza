using System;

namespace Biblioteka
{
    [Serializable]
    public class Knjiga
    {
        public string Naslov;
        public string Autor;
        public int Kolicina;

        public override string ToString()
        {
            return $"{Naslov} - {Autor} (kom: {Kolicina})";
        }
    }
}
