using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Windows.Forms;
using Newtonsoft.Json.Linq;

namespace AIMusicSorter
{
    public partial class Form1 : Form
    {
        private const string clientId = "0bfda06feae040deb693994db468ecc6";
        private const string clientSecret = "00f55c657b884881bc0498582c12da32";
        private const string redirectUri = "https://arnulfo-pestersome-corruptibly.ngrok-free.dev/callback/";

        private FancyButton btnFetch;
        private string token;

        public Form1()
        {
            InitializeComponent();
            this.Text = "AI Music Sorter 🎧";
            this.BackColor = Color.FromArgb(10, 10, 10);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.None;
            this.Size = new Size(1440, 810);
            this.DoubleBuffered = true;

            InitializeUI();
        }

        private void InitializeUI()
        {
            // przycisk login
            btnLogin = new FancyButton("Zaloguj się")
            {
                Location = new Point(40, this.ClientSize.Height - 90),
                Size = new Size(180, 55)
            };
            btnLogin.Click += BtnLogin_Click;
            this.Controls.Add(btnLogin);

            // przycisk pobierania
            btnFetch = new FancyButton("Pobierz Top 10")
            {
                Location = new Point(240, this.ClientSize.Height - 90),
                Size = new Size(180, 55)
            };
            btnFetch.Click += async (s, e) =>
            {
                if (string.IsNullOrEmpty(token))
                    MessageBox.Show("Najpierw się zaloguj 😎");
                else
                    await LoadTopTracks(token);
            };
            this.Controls.Add(btnFetch);

            // panel z muzyką
            flpTracks = new FlowLayoutPanel()
            {
                Location = new Point(40, 40),
                Size = new Size(this.ClientSize.Width - 80, this.ClientSize.Height - 160),
                AutoScroll = true,
                WrapContents = true,
                FlowDirection = FlowDirection.LeftToRight,
                BackColor = Color.FromArgb(15, 15, 15),
                Padding = new Padding(10)
            };
            this.Controls.Add(flpTracks);
        }

        private async void BtnLogin_Click(object sender, EventArgs e)
        {
            var server = new SpotifyCallbackServer();
            server.Start(5000);
            string authUrl = $"https://accounts.spotify.com/authorize?client_id={clientId}&response_type=code&redirect_uri={Uri.EscapeDataString(redirectUri)}&scope=user-top-read";
            Process.Start(new ProcessStartInfo(authUrl) { UseShellExecute = true });
            string code = await server.CodeTask;
            token = await ExchangeCodeForTokenAsync(code);
            MessageBox.Show("Zalogowano pomyślnie 🔥");
        }

        private async Task<string> ExchangeCodeForTokenAsync(string code)
        {
            using (var client = new HttpClient())
            {
                var request = new HttpRequestMessage(HttpMethod.Post, "https://accounts.spotify.com/api/token");
                request.Content = new FormUrlEncodedContent(new List<KeyValuePair<string, string>>()
                {
                    new KeyValuePair<string,string>("grant_type","authorization_code"),
                    new KeyValuePair<string,string>("code", code),
                    new KeyValuePair<string,string>("redirect_uri", redirectUri)
                });

                string auth = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}"));
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", auth);

                var response = await client.SendAsync(request);
                var json = JObject.Parse(await response.Content.ReadAsStringAsync());
                return json["access_token"].ToString();
            }
        }

        private async Task LoadTopTracks(string accessToken)
        {
            flpTracks.Controls.Clear();

            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                var response = await client.GetAsync("https://api.spotify.com/v1/me/top/tracks?limit=20");
                if (!response.IsSuccessStatusCode)
                {
                    MessageBox.Show($"Błąd API: {response.StatusCode}");
                    return;
                }

                var json = JObject.Parse(await response.Content.ReadAsStringAsync());
                var tracks = json["items"];

                foreach (var track in tracks)
                {
                    string title = track["name"].ToString();
                    string artist = track["artists"][0]["name"].ToString();
                    string album = track["album"]["name"].ToString();
                    int popularity = (int)track["popularity"];
                    string imageUrl = track["album"]["images"][0]["url"].ToString();

                    Image albumImage;
                    using (var wc = new WebClient())
                    {
                        byte[] bytes = wc.DownloadData(imageUrl);
                        using (var ms = new System.IO.MemoryStream(bytes))
                        {
                            albumImage = Image.FromStream(ms);
                        }
                    }

                    var card = new FancyCard(title, artist, album, popularity, albumImage);
                    flpTracks.Controls.Add(card);
                }
            }
        }
    }

    public class FancyCard : Panel
    {
        public FancyCard(string title, string artist, string album, int popularity, Image cover)
        {
            this.Size = new Size(260, 340);
            this.Margin = new Padding(12);
            this.BackColor = Color.FromArgb(25, 25, 25);
            this.Padding = new Padding(8);
            this.Cursor = Cursors.Hand;

            PictureBox pb = new PictureBox()
            {
                Image = cover,
                SizeMode = PictureBoxSizeMode.Zoom,
                Size = new Size(240, 200),
                Location = new Point(10, 10)
            };

            Label lblArtist = new Label()
            {
                Text = artist,
                Font = new Font("Segoe UI", 11, FontStyle.Bold),
                ForeColor = Color.LimeGreen,
                Location = new Point(10, 220),
                AutoSize = true
            };

            Label lblTitle = new Label()
            {
                Text = title,
                Font = new Font("Segoe UI", 10),
                ForeColor = Color.White,
                Location = new Point(10, 245),
                AutoSize = true
            };

            Label lblAlbum = new Label()
            {
                Text = album,
                Font = new Font("Segoe UI", 9, FontStyle.Italic),
                ForeColor = Color.Gray,
                Location = new Point(10, 270),
                AutoSize = true
            };

            Label lblPop = new Label()
            {
                Text = "Popularność: " + popularity,
                Font = new Font("Segoe UI", 9, FontStyle.Regular),
                ForeColor = Color.FromArgb(150, 150, 150),
                Location = new Point(10, 295),
                AutoSize = true
            };

            this.Controls.Add(pb);
            this.Controls.Add(lblArtist);
            this.Controls.Add(lblTitle);
            this.Controls.Add(lblAlbum);
            this.Controls.Add(lblPop);
        }
    }

    public class FancyButton : Button
    {
        private Color baseColor = Color.FromArgb(30, 30, 30);
        private Color hoverColor = Color.LimeGreen;
        private Color clickColor = Color.FromArgb(0, 150, 0);

        public FancyButton(string text)
        {
            this.Text = text;
            this.FlatStyle = FlatStyle.Flat;
            this.FlatAppearance.BorderSize = 0;
            this.BackColor = baseColor;
            this.ForeColor = Color.White;
            this.Font = new Font("Segoe UI", 10, FontStyle.Bold);
            this.Cursor = Cursors.Hand;
            this.Region = Region.FromHrgn(CreateRoundRectRgn(0, 0, 200, 50, 20, 20));

            this.MouseEnter += (s, e) => this.BackColor = hoverColor;
            this.MouseLeave += (s, e) => this.BackColor = baseColor;
            this.MouseDown += (s, e) => this.BackColor = clickColor;
            this.MouseUp += (s, e) => this.BackColor = hoverColor;
        }

        [System.Runtime.InteropServices.DllImport("Gdi32.dll", EntryPoint = "CreateRoundRectRgn")]
        private static extern IntPtr CreateRoundRectRgn(
            int nLeftRect, int nTopRect, int nRightRect, int nBottomRect,
            int nWidthEllipse, int nHeightEllipse
        );
    }
}
