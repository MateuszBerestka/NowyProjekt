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
        private string clientId = "0bfda06feae040deb693994db468ecc6";
        private string clientSecret = "00f55c657b884881bc0498582c12da32";
        private string accessToken;

        public async Task Authenticate()
        {
            // tutaj POST do https://accounts.spotify.com/api/token
            // i zapis accessToken
        }

        public async Task<List<TrackInfo>> GetPlaylistTracks(string playlistId)
        {
            // GET playlisty + audio features
            return new List<TrackInfo>();
        }
    }
}
