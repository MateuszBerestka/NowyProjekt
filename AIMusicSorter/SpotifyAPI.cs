using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Newtonsoft.Json;
using AIMusicSorter.Models;
using System.Collections.Generic;

namespace AIMusicSorter
{
    public class SpotifyAPI
    {
       

        public async Task Authenticate()
        {
            
        }

        public async Task<List<TrackInfo>> GetPlaylistTracks(string playlistId)
        {
            // GET playlisty + audio features
            return new List<TrackInfo>();
        }
    }
}
