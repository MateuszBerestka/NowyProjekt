using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
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
        private const string Scopes = "user-top-read user-read-playback-state user-modify-playback-state user-read-currently-playing";

        private static readonly HttpClient httpClient = new HttpClient();

        private FancyButton btnLogin, btnFetch, btnCurrentTrack;
        private FlowLayoutPanel flpTracks;
        private Panel pnlCurrentTrack;
        private PictureBox pbCurrentCover;
        private Label lblCurrentTitle, lblCurrentArtist;
        private FancyButton btnPrev, btnPlay, btnNext;

        private string token;
        private string refreshToken; // Token do odświeżania

        // rotation / visualizer
        private Timer rotationTimer;
        private float rotationAngle = 0f;
        private bool isPlaying = false;

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
            // przycisk logowania
            btnLogin = new FancyButton("Zaloguj się")
            {
                Location = new Point(40, this.ClientSize.Height - 90),
                Size = new Size(180, 55)
            };
            btnLogin.Click += BtnLogin_Click;
            this.Controls.Add(btnLogin);

            // przycisk top 10
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
                {
                    await LoadTopTracks(token);
                    flpTracks.Visible = true;
                    pnlCurrentTrack.Visible = false;
                }
            };
            this.Controls.Add(btnFetch);

            // przycisk aktualnej muzyki
            btnCurrentTrack = new FancyButton("Aktualnie grana muzyka")
            {
                Location = new Point(440, this.ClientSize.Height - 90),
                Size = new Size(250, 55)
            };
            btnCurrentTrack.Click += async (s, e) =>
            {
                if (string.IsNullOrEmpty(token))
                {
                    MessageBox.Show("Najpierw się zaloguj 😎");
                    return;
                }
                await ShowCurrentTrack(token);
            };
            this.Controls.Add(btnCurrentTrack);

            // flowlayout dla top 10
            flpTracks = new FlowLayoutPanel()
            {
                Location = new Point(40, 40),
                Size = new Size(this.ClientSize.Width - 80, this.ClientSize.Height - 160),
                AutoScroll = true,
                WrapContents = true,
                FlowDirection = FlowDirection.LeftToRight,
                BackColor = Color.FromArgb(15, 15, 15),
                Padding = new Padding(10),
                Visible = false
            };
            this.Controls.Add(flpTracks);

            // panel dla aktualnej muzyki
            pnlCurrentTrack = new Panel()
            {
                Size = new Size(this.ClientSize.Width, this.ClientSize.Height - 160),
                Location = new Point(0, 40),
                BackColor = Color.FromArgb(20, 20, 20),
                Visible = false
            };
            this.Controls.Add(pnlCurrentTrack);

            // picturebox - okładka (winyl)
            pbCurrentCover = new PictureBox()
            {
                Size = new Size(320, 320),
                SizeMode = PictureBoxSizeMode.Zoom,
                Location = new Point((pnlCurrentTrack.Width - 320) / 2, 100),
                // UWAGA: Ustaw BackColor na tło nadrzędnego panelu, aby ułatwić maskowanie
                // LUB POZOSTAW Color.Transparent - obie metody działają w innym zakresie
                BackColor = pnlCurrentTrack.BackColor // Ustawienie koloru tła panelu
            };
            // pbCurrentCover.Region = new Region(new GraphicsPath()); // <--- Usuń lub zakomentuj
            pbCurrentCover.Paint += PbCurrentCover_Paint;
            pnlCurrentTrack.Controls.Add(pbCurrentCover);

            // rotation timer
            rotationTimer = new Timer();
            rotationTimer.Interval = 30; // ~33 FPS
            rotationTimer.Tick += (s, e) =>
            {
                // opcjonalnie: możesz zmieniać prędkość na podstawie głośności
                rotationAngle += 2.5f;
                if (rotationAngle >= 360f) rotationAngle -= 360f;
                pbCurrentCover.Invalidate();
            };

            lblCurrentTitle = new Label()
            {
                Text = "",
                Font = new Font("Segoe UI", 16, FontStyle.Bold),
                ForeColor = Color.White,
                AutoSize = true,
                Location = new Point((pnlCurrentTrack.Width / 2) - 100, 440)
            };
            pnlCurrentTrack.Controls.Add(lblCurrentTitle);

            lblCurrentArtist = new Label()
            {
                Text = "",
                Font = new Font("Segoe UI", 13, FontStyle.Italic),
                ForeColor = Color.Gray,
                AutoSize = true,
                Location = new Point((pnlCurrentTrack.Width / 2) - 80, 480)
            };
            pnlCurrentTrack.Controls.Add(lblCurrentArtist);

            // controls
            btnPrev = new FancyButton("⏮️") { Size = new Size(80, 50), Location = new Point(540, 520) };
            btnPlay = new FancyButton("⏯️") { Size = new Size(80, 50), Location = new Point(660, 520) };
            btnNext = new FancyButton("⏭️") { Size = new Size(80, 50), Location = new Point(780, 520) };

            btnPrev.Click += async (s, e) => await SkipTrack("previous");
            btnPlay.Click += async (s, e) => await TogglePlayPause();
            btnNext.Click += async (s, e) => await SkipTrack("next");

            pnlCurrentTrack.Controls.Add(btnPrev);
            pnlCurrentTrack.Controls.Add(btnPlay);
            pnlCurrentTrack.Controls.Add(btnNext);
        }

        // --- Paint: rysuje okładkę jako okrąg i obraca ---
        // --- Paint: rysuje okładkę jako okrąg i obraca ---
        private void PbCurrentCover_Paint(object sender, PaintEventArgs e)
        {
            if (pbCurrentCover.Image == null) return;

            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int size = Math.Min(pbCurrentCover.Width, pbCurrentCover.Height);
            Rectangle rect = new Rectangle((pbCurrentCover.Width - size) / 2, (pbCurrentCover.Height - size) / 2, size, size);

            // 1. Zabezpieczenie przed artefaktami i rysowanie maski
            using (GraphicsPath vinylPath = new GraphicsPath())
            {
                vinylPath.AddEllipse(rect);

                // Ustawienie regionu do przycięcia RYSOWANEGO obrazu (winylu)
                g.SetClip(vinylPath);

                // 2. Rotacja
                g.TranslateTransform(pbCurrentCover.Width / 2f, pbCurrentCover.Height / 2f);
                g.RotateTransform(rotationAngle);
                g.TranslateTransform(-pbCurrentCover.Width / 2f, -pbCurrentCover.Height / 2f);

                // 3. Rysuj obraz dopasowany do rect
                g.DrawImage(pbCurrentCover.Image, rect);

                // 4. Reset transform (MUSI BYĆ PRZED RYSOWANIEM MASKI!)
                g.ResetTransform();
                g.ResetClip(); // Resetujemy Clip

                // 5. Rysowanie MASKI na kwadratowe rogi!
                // Teraz rysujemy prostokąt, który jest RÓŻNICĄ pomiędzy PictureBoxem a kołem
                using (Region maskRegion = new Region(pbCurrentCover.ClientRectangle))
                {
                    // Odejmij kształt okręgu od prostokątnego obszaru PictureBox
                    maskRegion.Exclude(vinylPath);

                    // Ustaw Clip na ten obszar maski
                    g.SetClip(maskRegion, CombineMode.Replace);

                    // Wypełnij ten obszar tłem panelu nadrzędnego
                    using (Brush b = new SolidBrush(pnlCurrentTrack.BackColor))
                    {
                        g.FillRectangle(b, pbCurrentCover.ClientRectangle);
                    }

                    // Wyczyść Clip
                    g.ResetClip();
                }
            }

            // 6. mały środek (spindle)
            float centerX = pbCurrentCover.Width / 2f;
            float centerY = pbCurrentCover.Height / 2f;
            float smallR = Math.Max(6, size * 0.03f);
            // Rysujemy wrzeciono na samym wierzchu
            using (Brush b = new SolidBrush(Color.FromArgb(220, 30, 30, 30)))
            {
                g.FillEllipse(b, centerX - smallR, centerY - smallR, smallR * 2, smallR * 2);
            }
        }

        private void StartRotation()
        {
            if (!rotationTimer.Enabled) rotationTimer.Start();
            isPlaying = true;
        }

        private void StopRotation()
        {
            if (rotationTimer.Enabled) rotationTimer.Stop();
            isPlaying = false;
        }

        // ========== Spotify auth / token / helpery ==========
        private async void BtnLogin_Click(object sender, EventArgs e)
        {
            try
            {
                var server = new SpotifyCallbackServer();
                server.Start(5000);
                string authUrl = $"https://accounts.spotify.com/authorize?client_id={clientId}&response_type=code&redirect_uri={Uri.EscapeDataString(redirectUri)}&scope={Uri.EscapeDataString(Scopes)}";
                Process.Start(new ProcessStartInfo(authUrl) { UseShellExecute = true });
                string code = await server.CodeTask;
                token = await ExchangeCodeForTokenAsync(code);

                if (!string.IsNullOrEmpty(token))
                {
                    MessageBox.Show("Zalogowano pomyślnie 🔥");
                    btnLogin.Visible = false;
                }
                else
                {
                    MessageBox.Show("Logowanie nie powiodło się. Spróbuj ponownie.");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Krytyczny błąd logowania: {ex.Message}");
            }
        }

        private async Task<string> ExchangeCodeForTokenAsync(string code)
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

            var response = await httpClient.SendAsync(request);
            var responseText = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                MessageBox.Show($"Błąd API: {response.StatusCode}\nTreść: {responseText}");
                return null;
            }

            JObject json;
            try
            {
                json = JObject.Parse(responseText);
                // ZAPISUJEMY REFRESH TOKEN
                refreshToken = json["refresh_token"]?.ToString();
                return json["access_token"]?.ToString();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Niepoprawny JSON: {ex.Message}\nTreść: {responseText}");
                return null;
            }
        }

        private async Task<bool> RefreshTokenAsync()
        {
            if (string.IsNullOrEmpty(refreshToken)) return false;

            var request = new HttpRequestMessage(HttpMethod.Post, "https://accounts.spotify.com/api/token");
            request.Content = new FormUrlEncodedContent(new List<KeyValuePair<string, string>>()
            {
                new KeyValuePair<string,string>("grant_type","refresh_token"),
                new KeyValuePair<string,string>("refresh_token", refreshToken)
            });

            string auth = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", auth);

            var response = await httpClient.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                var json = JObject.Parse(await response.Content.ReadAsStringAsync());
                token = json["access_token"]?.ToString();
                return true;
            }
            else
            {
                MessageBox.Show($"Błąd odświeżania tokena: {response.StatusCode}. Wymagane ponowne logowanie.");
                return false;
            }
        }

        private async Task<HttpResponseMessage> SafeApiCall(Func<string, Task<HttpResponseMessage>> apiAction)
        {
            // Próba 1
            var response = await apiAction(token);

            // Jeśli błąd 401 i mamy refreshToken
            if (response?.StatusCode == HttpStatusCode.Unauthorized && !string.IsNullOrEmpty(refreshToken))
            {
                // Odśwież token
                if (await RefreshTokenAsync())
                {
                    // Próba 2 z nowym tokenem
                    response = await apiAction(token);
                }
            }

            return response;
        }

        // ========== Main API features (tracks / player) ==========

        private async Task LoadTopTracks(string accessToken)
        {
            flpTracks.Controls.Clear();

            var response = await SafeApiCall(async (t) =>
            {
                using (var request = new HttpRequestMessage(HttpMethod.Get, "https://api.spotify.com/v1/me/top/tracks?limit=20"))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", t);
                    return await httpClient.SendAsync(request);
                }
            });

            if (response == null || !response.IsSuccessStatusCode)
            {
                MessageBox.Show($"Błąd API: {response?.StatusCode}");
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

                Image albumImage = null;
                try
                {
                    byte[] bytes = await httpClient.GetByteArrayAsync(imageUrl);
                    using (var ms = new System.IO.MemoryStream(bytes))
                    {
                        albumImage = Image.FromStream(ms);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Błąd pobierania okładki: {ex.Message}");
                    albumImage = new Bitmap(1, 1);
                }

                var card = new FancyCard(title, artist, album, popularity, albumImage);
                flpTracks.Controls.Add(card);
            }
        }

        private async Task<string> GetActiveDeviceId()
        {
            var response = await SafeApiCall(async (t) =>
            {
                var req = new HttpRequestMessage(HttpMethod.Get, "https://api.spotify.com/v1/me/player/devices");
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", t);
                return await httpClient.SendAsync(req);
            });

            if (response == null)
            {
                Debug.WriteLine("GetActiveDeviceId: response == null");
                return null;
            }

            var respText = await response.Content.ReadAsStringAsync();
            Debug.WriteLine($"GetActiveDeviceId: status={response.StatusCode}, body={respText}");

            if (!response.IsSuccessStatusCode)
                return null;

            var json = JObject.Parse(respText);
            var devices = json["devices"]?.ToList();
            if (devices == null || devices.Count == 0)
                return null;

            // 1) aktywne urządzenie
            var active = devices.FirstOrDefault(d => d["is_active"]?.ToObject<bool>() == true);
            if (active != null)
                return active["id"]?.ToString();

            // 2) preferuj komputer
            var desktop = devices.FirstOrDefault(d => d["type"]?.ToString().Equals("Computer", StringComparison.OrdinalIgnoreCase) == true);
            if (desktop != null)
                return desktop["id"]?.ToString();

            // 3) fallback: pierwszy
            return devices.FirstOrDefault()?["id"]?.ToString();
        }

        private async Task<bool> TransferPlaybackToDevice(string deviceId)
        {
            if (string.IsNullOrEmpty(deviceId)) return false;

            var response = await SafeApiCall(async (t) =>
            {
                var req = new HttpRequestMessage(HttpMethod.Put, "https://api.spotify.com/v1/me/player");
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", t);
                var body = new JObject { ["device_ids"] = new JArray(deviceId) };
                req.Content = new StringContent(body.ToString(), Encoding.UTF8, "application/json");
                return await httpClient.SendAsync(req);
            });

            if (response == null)
            {
                Debug.WriteLine("TransferPlaybackToDevice: response == null");
                return false;
            }

            var txt = await response.Content.ReadAsStringAsync();
            Debug.WriteLine($"TransferPlaybackToDevice: status={response.StatusCode}, body={txt}");

            // 204 No Content = success
            return response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NoContent;
        }

        private async Task ShowCurrentTrack(string accessToken)
        {
            var response = await SafeApiCall(async (t) =>
            {
                using (var request = new HttpRequestMessage(HttpMethod.Get, "https://api.spotify.com/v1/me/player/currently-playing"))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", t);
                    return await httpClient.SendAsync(request);
                }
            });

            if (response == null) return;

            if (response.StatusCode == HttpStatusCode.NoContent)
            {
                MessageBox.Show("Nic nie jest aktualnie odtwarzane 😅");
                StopRotation();
                return;
            }

            if (!response.IsSuccessStatusCode)
            {
                MessageBox.Show($"Błąd API: {response.StatusCode}");
                StopRotation();
                return;
            }

            var json = JObject.Parse(await response.Content.ReadAsStringAsync());

            if (json["item"] == null)
            {
                MessageBox.Show("Brak utworu w aktywnym odtwarzaczu.");
                StopRotation();
                return;
            }

            string title = json["item"]["name"].ToString();
            string artist = json["item"]["artists"][0]["name"].ToString();
            string imageUrl = json["item"]["album"]["images"][0]["url"].ToString();
            bool playing = json["is_playing"]?.ToObject<bool>() ?? false;

            try
            {
                byte[] data = await httpClient.GetByteArrayAsync(imageUrl);
                using (var ms = new System.IO.MemoryStream(data))
                    pbCurrentCover.Image = Image.FromStream(ms);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Błąd pobierania okładki: {ex.Message}");
                pbCurrentCover.Image = null;
            }

            lblCurrentTitle.Text = title;
            lblCurrentArtist.Text = artist;

            lblCurrentTitle.Location = new Point((pnlCurrentTrack.Width - lblCurrentTitle.Width) / 2, 440);
            lblCurrentArtist.Location = new Point((pnlCurrentTrack.Width - lblCurrentArtist.Width) / 2, 480);

            flpTracks.Visible = false;
            pnlCurrentTrack.Visible = true;

            if (playing) StartRotation();
            else StopRotation();
        }

        private async Task SkipTrack(string direction)
        {
            string url = $"https://api.spotify.com/v1/me/player/{direction}";

            var response = await SafeApiCall(async (t) =>
            {
                using (var request = new HttpRequestMessage(HttpMethod.Post, url))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", t);
                    request.Content = new StringContent(string.Empty);
                    return await httpClient.SendAsync(request);
                }
            });

            if (response == null) return;

            if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.NoContent)
            {
                MessageBox.Show($"Błąd przy pomijaniu utworu: {response.StatusCode}");
            }
            else
            {
                await Task.Delay(500);
                await ShowCurrentTrack(token);
            }
        }

        private async Task TogglePlayPause()
        {
            // 1) spróbuj znaleźć deviceId
            string deviceId = await GetActiveDeviceId();

            // jeśli nic nie ma -> spróbuj "obudzić" playera i ponowić
            if (string.IsNullOrEmpty(deviceId))
            {
                Debug.WriteLine("TogglePlayPause: brak deviceId, próba obudzenia playera (/me/player)");
                var wake = await SafeApiCall(async (t) =>
                {
                    var r = new HttpRequestMessage(HttpMethod.Get, "https://api.spotify.com/v1/me/player");
                    r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", t);
                    return await httpClient.SendAsync(r);
                });

                Debug.WriteLine($"Wake /me/player status: {wake?.StatusCode}");
                await Task.Delay(600);

                // spróbuj jeszcze raz pobrać urządzenia
                deviceId = await GetActiveDeviceId();
            }

            // jeśli dalej nic, spróbuj przenieść playback na pierwszy znaleziony device z devices endpointa
            if (string.IsNullOrEmpty(deviceId))
            {
                Debug.WriteLine("TogglePlayPause: dalej brak deviceId, spróbuję pobrać pierwszy device i przetransferować playback.");
                var responseAllDevices = await SafeApiCall(async (t) =>
                {
                    var req = new HttpRequestMessage(HttpMethod.Get, "https://api.spotify.com/v1/me/player/devices");
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", t);
                    return await httpClient.SendAsync(req);
                });

                if (responseAllDevices != null && responseAllDevices.IsSuccessStatusCode)
                {
                    var txt = await responseAllDevices.Content.ReadAsStringAsync();
                    var j = JObject.Parse(txt);
                    var first = j["devices"]?.First;
                    var fallbackId = first?["id"]?.ToString();
                    if (!string.IsNullOrEmpty(fallbackId))
                    {
                        Debug.WriteLine($"TogglePlayPause: próbuję transferu na deviceId={fallbackId}");
                        var ok = await TransferPlaybackToDevice(fallbackId);
                        if (ok)
                            deviceId = fallbackId;
                    }
                }
            }

            if (string.IsNullOrEmpty(deviceId))
            {
                MessageBox.Show("Nie znaleziono aktywnego urządzenia Spotify. Upewnij się, że masz włączonego Spotify na PC/telefonie i że aplikacja ma uprawnienia (scope) user-modify-playback-state.");
                return;
            }

            Debug.WriteLine($"TogglePlayPause: użyję deviceId = {deviceId}");

            // 2) sprawdź aktualny stan odtwarzacza
            var stateResponse = await SafeApiCall(async (t) =>
            {
                var req = new HttpRequestMessage(HttpMethod.Get, "https://api.spotify.com/v1/me/player");
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", t);
                return await httpClient.SendAsync(req);
            });

            if (stateResponse == null)
            {
                Debug.WriteLine("TogglePlayPause: stateResponse == null");
                return;
            }

            var stateText = await stateResponse.Content.ReadAsStringAsync();
            Debug.WriteLine($"TogglePlayPause: /me/player status={stateResponse.StatusCode}, body={stateText}");

            bool isPlayingNow = false;
            if (stateResponse.StatusCode == HttpStatusCode.NoContent)
                isPlayingNow = false;
            else if (stateResponse.IsSuccessStatusCode)
            {
                try
                {
                    var j = JObject.Parse(stateText);
                    isPlayingNow = j["is_playing"]?.ToObject<bool>() ?? false;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"TogglePlayPause: błąd parsowania state JSON: {ex.Message}");
                    isPlayingNow = false;
                }
            }

            // 3) utwórz poprawny URL (bez literówek!)
            string actionUrl = isPlayingNow
                ? $"https://api.spotify.com/v1/me/player/pause?device_id={Uri.EscapeDataString(deviceId)}"
                : $"https://api.spotify.com/v1/me/player/play?device_id={Uri.EscapeDataString(deviceId)}";

            Debug.WriteLine($"TogglePlayPause: actionUrl = {actionUrl}");

            var actionResponse = await SafeApiCall(async (t) =>
            {
                var req = new HttpRequestMessage(HttpMethod.Put, actionUrl);
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", t);
                req.Content = new StringContent(string.Empty);
                return await httpClient.SendAsync(req);
            });

            if (actionResponse == null)
            {
                Debug.WriteLine("TogglePlayPause: actionResponse == null");
                return;
            }

            var actionTxt = await actionResponse.Content.ReadAsStringAsync();
            Debug.WriteLine($"TogglePlayPause: action status={actionResponse.StatusCode}, body={actionTxt}");

            if (!actionResponse.IsSuccessStatusCode && actionResponse.StatusCode != HttpStatusCode.NoContent)
            {
                MessageBox.Show($"Nie udało się zmienić stanu odtwarzania: {actionResponse.StatusCode}\n{actionTxt}");
            }
            else
            {
                // update local rotation state
                if (isPlayingNow) StopRotation();
                else StartRotation();

                await Task.Delay(400);
                await ShowCurrentTrack(token);
            }

        }
      

        // --- FancyCard & FancyButton (jak miałeś) ---
    }
   
    // Klasa FancyCard (bez zmian)
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

            MakePictureBoxCircular(pb); 

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
                Font = new Font("Segoe UI", 9),
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
        private void MakePictureBoxCircular(PictureBox pic)
        {
            System.Drawing.Drawing2D.GraphicsPath path = new System.Drawing.Drawing2D.GraphicsPath();
            path.AddEllipse(0, 0, pic.Width, pic.Height);
            pic.Region = new Region(path);
            pic.BackColor = Color.Transparent;
            pic.SizeMode = PictureBoxSizeMode.Zoom;
        }
    }

    // Klasa FancyButton (bez zmian)
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

            this.MouseEnter += (s, e) => this.BackColor = hoverColor;
            this.MouseLeave += (s, e) => this.BackColor = baseColor;
            this.MouseDown += (s, e) => this.BackColor = clickColor;
            this.MouseUp += (s, e) => this.BackColor = hoverColor;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (this.Width > 0 && this.Height > 0)
            {
                if (this.Region != null) this.Region.Dispose();
                this.Region = Region.FromHrgn(CreateRoundRectRgn(0, 0, this.Width, this.Height, 20, 20));
            }
       
        }

        [System.Runtime.InteropServices.DllImport("Gdi32.dll", EntryPoint = "CreateRoundRectRgn")]
        private static extern IntPtr CreateRoundRectRgn(
            int nLeftRect, int nTopRect, int nRightRect, int nBottomRect,
            int nWidthEllipse, int nHeightEllipse
        );
       
    }

}