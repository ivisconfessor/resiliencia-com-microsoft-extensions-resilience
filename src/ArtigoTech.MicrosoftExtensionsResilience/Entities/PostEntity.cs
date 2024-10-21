namespace ArtigoTech.MicrosoftExtensionsResilience.Entities
{
    public class PostEntity
    {
        public int Id { get; set; }
        public string Titulo { get; set; }
        public string Descricao { get; set; }
        public bool Processado { get; set; }
        public DateTime DataProcessamento { get; set; }
        public int UsuarioId { get; set; }
    }
}
