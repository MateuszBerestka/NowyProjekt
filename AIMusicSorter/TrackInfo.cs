namespace AIMusicSorter.Models
{
    public class TrackInfo
    {
        public string Title { get; set; }
        public string Artist { get; set; }
        public string Album { get; set; }
        public string[] Genres { get; set; }
        public double Tempo { get; set; }
        public int Popularity { get; set; }
    }
}