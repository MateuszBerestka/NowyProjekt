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
using ScottPlot.Colormaps;

namespace AIMusicSorter
{
    public partial class Form1 : Form
    {
        // ---------------- Spotify creds (zamień na swoje jeśli trzeba) ----------------
        private const string clientId = "0bfda06feae040deb693994db468ecc6";
        private const string clientSecret = "00f55c657b884881bc0498582c12da32";
        private const string redirectUri = "https://arnulfo-pestersome-corruptibly.ngrok-free.dev/callback/";
        private const string Scopes = "user-top-read user-read-playback-state user-modify-playback-state user-read-currently-playing user-read-recently-played";

        private static readonly HttpClient httpClient = new HttpClient();

        // ---------------- UI elements ----------------
        private FancyButton btnLogin, btnFetch, btnCurrentTrack;
        private FlowLayoutPanel flpTracks;
        public Panel pnlCurrentTrack;
        private PictureBox pbCurrentCover;
        private Label lblCurrentTitle, lblCurrentArtist;
        private FancyButton btnPrev, btnPlay, btnNext;

        // ---------------- playback / tokens ----------------
        private string token;
        private string refreshToken;

        // ---------------- visual stuff ----------------
        private Timer rotationTimer;
        private float rotationAngle = 0f;
        private bool isPlaying = false;

        // Equalizer + background
        private Panel visualizerPanel;
        private Panel[] eqBars;
        private Timer visualizerTimer;
        private float bgPhase = 0f; // dla animacji tła
        private Timer progressTimer;
        private ProgressBar trackProgress;
        private TrackBar volumeSlider;
        private FlowLayoutPanel recentlyPanel;
        private string currentTrackId = null;
        private int lastDurationMs = 0;
        private int lastProgressMs = 0;

        // Cache audio features so we don't spam API
        private float cachedEnergy = 0.4f;
        private float cachedTempo = 100f;
        private DateTime cachedFeaturesTime = DateTime.MinValue;

        public Form1()
        {
            InitializeComponent();
            this.Text = "AI Music Sorter 🎧";
            this.BackColor = Color.FromArgb(6, 0, 18); // deep dark for neon
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.None;
            this.Size = new Size(1440, 810);
            this.DoubleBuffered = true;
            InitializeUI();
        }

        private void InitializeUI()
        {
            // ---------- bottom controls ----------
            btnLogin = new FancyButton("Zaloguj się")
            {
                Location = new Point(40, this.ClientSize.Height - 90),
                Size = new Size(180, 55)
            };
            btnLogin.Click += BtnLogin_Click;
            this.Controls.Add(btnLogin);

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
                    await LoadTopTracks();
                    flpTracks.Visible = true;
                    pnlCurrentTrack.Visible = false;
                }
            };
            this.Controls.Add(btnFetch);

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
                await ShowCurrentTrack();
            };
            this.Controls.Add(btnCurrentTrack);

            // ---------- tracks panel ----------
            flpTracks = new FlowLayoutPanel()
            {
                Location = new Point(40, 40),
                Size = new Size(this.ClientSize.Width - 420, this.ClientSize.Height - 160),
                AutoScroll = true,
                WrapContents = true,
                FlowDirection = FlowDirection.LeftToRight,
                BackColor = Color.FromArgb(12, 6, 28),
                Padding = new Padding(10),
                Visible = false
            };
            this.Controls.Add(flpTracks);

            // ---------- current track panel ----------
            pnlCurrentTrack = new Panel()
            {
                Size = new Size(this.ClientSize.Width, this.ClientSize.Height - 160),
                Location = new Point(0, 40),
                BackColor = this.BackColor,
                Visible = false
            };

            // Włączenie podwójnego buforowania (żeby animacje były płynne)
            typeof(Panel).InvokeMember(
                "DoubleBuffered",
                System.Reflection.BindingFlags.SetProperty |
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic,
                null,
                pnlCurrentTrack,
                new object[] { true }
            );
            pnlCurrentTrack.Paint += PnlCurrentTrack_Paint; // background neon gradient
            this.Controls.Add(pnlCurrentTrack);

            // cover (picture box) - custom paint
            pbCurrentCover = new PictureBox()
            {
                Size = new Size(320, 320),
                SizeMode = PictureBoxSizeMode.Zoom,
                Location = new Point(80, 60),
                BackColor = Color.Transparent
            };
            pbCurrentCover.Paint += PbCurrentCover_Paint;
            pbCurrentCover.Cursor = Cursors.Hand;
            pbCurrentCover.Click += async (s, e) => { await TogglePlayPause(); };
            pnlCurrentTrack.Controls.Add(pbCurrentCover);

            // labels
            lblCurrentTitle = new Label()
            {
                Text = "",
                Font = new Font("Segoe UI", 18, FontStyle.Bold),
                ForeColor = Color.White,
                AutoSize = true,
                Location = new Point(80, 400)
            };
            pnlCurrentTrack.Controls.Add(lblCurrentTitle);

            lblCurrentArtist = new Label()
            {
                Text = "",
                Font = new Font("Segoe UI", 13, FontStyle.Italic),
                ForeColor = Color.FromArgb(200, 200, 255),
                AutoSize = true,
                Location = new Point(80, 440)
            };
            pnlCurrentTrack.Controls.Add(lblCurrentArtist);

            // right side visualizer block
            int rightX = pbCurrentCover.Right + 40;
            visualizerPanel = new Panel()
            {
                Location = new Point(rightX, 60),
                Size = new Size(420, 260),
                BackColor = Color.FromArgb(12, 4, 24)
            };
            visualizerPanel.Paint += VisualizerPanel_Paint;
            pnlCurrentTrack.Controls.Add(visualizerPanel);

            // equalizer bars (we'll just store heights and draw in Paint for smoothness)
            eqBars = new Panel[6];
            for (int i = 0; i < eqBars.Length; i++)
            {
                // actually we won't add many nested controls — keep minimal
                var p = new Panel() { Size = new Size(1, 1) }; // placeholder
                eqBars[i] = p;
                visualizerPanel.Controls.Add(p);
            }

            // progress bar custom (we'll draw nicer bar)
            trackProgress = new ProgressBar()
            {
                Location = new Point(rightX, visualizerPanel.Bottom + 18),
                Size = new Size(420, 18),
                Minimum = 0,
                Maximum = 1000,
                Value = 0
            };
            pnlCurrentTrack.Controls.Add(trackProgress);

            // volume slider
            Label volLbl = new Label()
            {
                Text = "Głośność",
                ForeColor = Color.White,
                Location = new Point(rightX, trackProgress.Bottom + 8),
                AutoSize = true
            };
            pnlCurrentTrack.Controls.Add(volLbl);

            volumeSlider = new TrackBar()
            {
                Location = new Point(rightX, trackProgress.Bottom + 28),
                Size = new Size(420, 40),
                Minimum = 0,
                Maximum = 100,
                TickStyle = TickStyle.None,
                Value = 50
            };
            volumeSlider.Scroll += async (s, e) => await SetVolume(volumeSlider.Value);
            pnlCurrentTrack.Controls.Add(volumeSlider);

            // recently played
            Label recentLbl = new Label()
            {
                Text = "Ostatnio słuchane",
                ForeColor = Color.White,
                Location = new Point(rightX, volumeSlider.Bottom + 8),
                AutoSize = true
            };
            pnlCurrentTrack.Controls.Add(recentLbl);

            recentlyPanel = new FlowLayoutPanel()
            {
                Location = new Point(rightX, volumeSlider.Bottom + 28),
                Size = new Size(420, 180),
                AutoScroll = true,
                FlowDirection = FlowDirection.TopDown,
                BackColor = Color.FromArgb(8, 4, 16)
            };
            pnlCurrentTrack.Controls.Add(recentlyPanel);

            // bottom controls (prev/play/next)
            btnPrev = new FancyButton("⏮️") { Size = new Size(80, 50), Location = new Point(540, 520) };
            btnPlay = new FancyButton("⏯️") { Size = new Size(80, 50), Location = new Point(660, 520) };
            btnNext = new FancyButton("⏭️") { Size = new Size(80, 50), Location = new Point(780, 520) };

            btnPrev.Click += async (s, e) => await SkipTrack("previous");
            btnPlay.Click += async (s, e) => await TogglePlayPause();
            btnNext.Click += async (s, e) => await SkipTrack("next");

            pnlCurrentTrack.Controls.Add(btnPrev);
            pnlCurrentTrack.Controls.Add(btnPlay);
            pnlCurrentTrack.Controls.Add(btnNext);

            // ---------------- timers ----------------
            rotationTimer = new Timer();
            rotationTimer.Interval = 28; // ~35 FPS - rotacja w PictureBox
            rotationTimer.Tick += (s, e) =>
            {
                // Only rotate if playing
                if (isPlaying)
                {
                    rotationAngle += 1.6f * Math.Max(0.3f, cachedEnergy); // speed scales with energy
                    if (rotationAngle >= 360f) rotationAngle -= 360f;
                    pbCurrentCover.Invalidate(); // repaint
                }
            };

            visualizerTimer = new Timer();
            visualizerTimer.Interval = 120; // equalizer / bg tick
            visualizerTimer.Tick += VisualizerTimer_Tick;
            visualizerTimer.Start();

            progressTimer = new Timer();
            progressTimer.Interval = 700; // poll progress & state
            progressTimer.Tick += ProgressTimer_Tick;
            progressTimer.Start();
        }

        // ---------------- paint background neon gradient ----------------
        private void PnlCurrentTrack_Paint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // dynamic neon gradient using bgPhase
            bgPhase += 0.0075f;
            if (bgPhase > Math.PI * 2) bgPhase -= (float)(Math.PI * 2);

            int w = pnlCurrentTrack.Width;
            int h = pnlCurrentTrack.Height;

            // compute two colors from phase
            Color c1 = ColorFromHsv(
                290 + 40 * (float)Math.Sin(bgPhase),
                0.8f,
                0.12f
            ); // purple

            Color c2 = ColorFromHsv(
                200 + 60 * (float)Math.Cos(bgPhase * 1.2f),
                0.85f,
                0.08f
            ); // cyan-ish

            using (var lg = new LinearGradientBrush(new Rectangle(0, 0, w, h),
                c1, c2, LinearGradientMode.ForwardDiagonal))
            {
                g.FillRectangle(lg, 0, 0, w, h);
            }

            // subtle radial glow behind cover
            float cx = pbCurrentCover.Right - 60;
            float cy = pbCurrentCover.Top + pbCurrentCover.Height / 2f;
            using (var path = new GraphicsPath())
            {
                path.AddEllipse((int)(cx - 260), (int)(cy - 260), 520, 520);
                using (PathGradientBrush pgb = new PathGradientBrush(path))
                {
                    pgb.CenterColor = Color.FromArgb(40, 180, 120, 255);
                    pgb.SurroundColors = new Color[] { Color.FromArgb(0, 0, 0, 0) };
                    g.FillEllipse(pgb, cx - 260, cy - 260, 520, 520);
                }
            }
        }

        // ---------------- picturebox paint (vinyl) ----------------
        private void PbCurrentCover_Paint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;

            int size = Math.Min(pbCurrentCover.Width, pbCurrentCover.Height);
            Rectangle rect = new Rectangle((pbCurrentCover.Width - size) / 2, (pbCurrentCover.Height - size) / 2, size, size);

            // If no image, draw placeholder vinyl disc
            if (pbCurrentCover.Image == null)
            {
                using (var brush = new SolidBrush(Color.FromArgb(30, 30, 30)))
                {
                    g.FillEllipse(brush, rect);
                }
            }
            else
            {
                // rotate around center and clip to circle to get 'vinyl'
                using (var path = new GraphicsPath())
                {
                    path.AddEllipse(rect);
                    g.SetClip(path);

                    // center rotation
                    g.TranslateTransform(pbCurrentCover.Width / 2f, pbCurrentCover.Height / 2f);
                    g.RotateTransform(rotationAngle);
                    g.TranslateTransform(-pbCurrentCover.Width / 2f, -pbCurrentCover.Height / 2f);

                    // Draw image scaled to rect
                    g.DrawImage(pbCurrentCover.Image, rect);

                    // reset
                    g.ResetTransform();
                    g.ResetClip();

                    // Draw outer ring to look like vinyl
                    using (var pen = new Pen(Color.FromArgb(200, 20, 20, 20), Math.Max(3, size * 0.02f)))
                    {
                        g.DrawEllipse(pen, rect);
                    }
                }
            }

            // spindle
            float centerX = pbCurrentCover.Width / 2f;
            float centerY = pbCurrentCover.Height / 2f;
            float smallR = Math.Max(6, size * 0.03f);
            using (Brush b = new SolidBrush(Color.FromArgb(220, 30, 30, 30)))
            {
                g.FillEllipse(b, centerX - smallR, centerY - smallR, smallR * 2, smallR * 2);
            }

            // subtle shine (unrotated)
            using (var shine = new LinearGradientBrush(
                new Rectangle(rect.Left, rect.Top, rect.Width, rect.Height),
                Color.FromArgb(90, 255, 255, 255), Color.FromArgb(10, 255, 255, 255), 45f))
            {
                g.FillEllipse(shine, rect);
            }
        }

        // ---------------- visualizer paint (draw eq bars smooth) ----------------
        private void VisualizerPanel_Paint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int w = visualizerPanel.Width;
            int h = visualizerPanel.Height;

            int barCount = eqBars.Length;
            int gap = 12;
            int barW = (w - (barCount + 1) * gap) / barCount;
            for (int i = 0; i < barCount; i++)
            {
                // use stored height in Panel.Tag if available
                int barH = 40;
                if (eqBars[i].Tag is int val) barH = val;

                Rectangle r = new Rectangle(gap + i * (barW + gap), h - barH - 22, barW, barH);
                // neon gradient fill
                using (var p = new LinearGradientBrush(r, NeonColor(i), NeonColor(i + 1), LinearGradientMode.Vertical))
                {
                    g.FillRoundedRectangle(p, r, 6);
                }
                // small top glow
                using (var glow = new SolidBrush(Color.FromArgb(80, 255, 255, 255)))
                {
                    g.FillRoundedRectangle(glow, new Rectangle(r.Left, r.Top, r.Width, Math.Max(4, r.Height / 6)), 6);
                }
            }
        }

        // helper: get neon color lerp by index
        private Color NeonColor(int idx)
        {
            // cycle palette: magenta -> purple -> cyan -> lime
            switch (idx % 4)
            {
                case 0: return Color.FromArgb(220, 255, 100, 255);
                case 1: return Color.FromArgb(220, 190, 100, 255);
                case 2: return Color.FromArgb(220, 100, 220, 255);
                default: return Color.FromArgb(220, 100, 255, 180);
            }
        }

        // ---------------- visualizer timer tick ----------------
        private async void VisualizerTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                // fetch audio features every ~3 seconds only
                if ((DateTime.UtcNow - cachedFeaturesTime).TotalSeconds > 3 && !string.IsNullOrEmpty(currentTrackId))
                {
                    var (energy, tempo) = await GetAudioFeatures(currentTrackId);
                    cachedEnergy = (float)Math.Max(0.05, energy);
                    cachedTempo = (float)Math.Max(40.0, tempo);
                    cachedFeaturesTime = DateTime.UtcNow;
                }

                // animate eq bars smoothly
                Random rnd = new Random();
                for (int i = 0; i < eqBars.Length; i++)
                {
                    int target = (int)(20 + cachedEnergy * 200 + rnd.Next(0, 40) + (i % 2 == 0 ? 10 : 0));
                    target = Math.Min(visualizerPanel.Height - 32, target);
                    int current = eqBars[i].Tag is int c ? c : 8;
                    // lerp
                    int next = current + (int)((target - current) * 0.25f);
                    eqBars[i].Tag = next;
                }

                // background pulse speed influenced by tempo & energy
                float msPerBeat = cachedTempo > 0 ? (60000f / cachedTempo) : 500f;
                // accelerate bgPhase slightly
                bgPhase += (0.002f + cachedEnergy * 0.003f);

                // request repaint minimal
                visualizerPanel.Invalidate();
                pnlCurrentTrack.Invalidate(); // for bg glow
            }
            catch (Exception ex)
            {
                Debug.WriteLine("VisualizerTimer_Tick: " + ex.Message);
            }
        }

        // ---------------- progress timer: poll /me/player ----------------
        private async void ProgressTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                var resp = await SafeApiCall(async (t) =>
                {
                    var req = new HttpRequestMessage(HttpMethod.Get, "https://api.spotify.com/v1/me/player");
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", t);
                    return await httpClient.SendAsync(req);
                });

                if (resp == null) return;

                if (resp.StatusCode == HttpStatusCode.NoContent)
                {
                    isPlaying = false;
                    this.InvokeIfRequired(() => trackProgress.Value = 0);
                    StopRotation();
                    return;
                }

                if (!resp.IsSuccessStatusCode)
                {
                    Debug.WriteLine("ProgressTimer: " + resp.StatusCode);
                    return;
                }

                var json = JObject.Parse(await resp.Content.ReadAsStringAsync());
                bool playingNow = json["is_playing"]?.ToObject<bool>() ?? false;
                int progress = json["progress_ms"]?.ToObject<int>() ?? 0;
                int duration = json["item"]?["duration_ms"]?.ToObject<int>() ?? lastDurationMs;
                string id = json["item"]?["id"]?.ToString();

                // if track changed, update cover/meta and recently
                if (!string.IsNullOrEmpty(id) && id != currentTrackId)
                {
                    currentTrackId = id;
                    await UpdateCoverAndMetaFromTrackJson(json);
                    await LoadRecentlyPlayed();
                }

                lastDurationMs = duration;
                lastProgressMs = progress;

                if (duration > 0)
                {
                    int value = (int)((long)progress * 1000 / duration);
                    value = Math.Max(0, Math.Min(1000, value));
                    this.InvokeIfRequired(() => trackProgress.Value = value);
                }

                // rotation state sync
                if (playingNow && !rotationTimer.Enabled) StartRotation();
                if (!playingNow && rotationTimer.Enabled) StopRotation();

                isPlaying = playingNow;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("ProgressTimer_Tick: " + ex.Message);
            }
        }

        // ---------------- Update cover & labels helper ----------------
        private async Task UpdateCoverAndMetaFromTrackJson(JObject json)
        {
            try
            {
                if (json == null) return;
                if (json["item"] == null) return;

                string title = json["item"]["name"]?.ToString() ?? "";
                string artist = json["item"]["artists"]?[0]?["name"]?.ToString() ?? "";
                string imageUrl = json["item"]["album"]?["images"]?[0]?["url"]?.ToString();

                Image img = null;
                if (!string.IsNullOrEmpty(imageUrl))
                {
                    try
                    {
                        byte[] data = await httpClient.GetByteArrayAsync(imageUrl);
                        using (var ms = new MemoryStream(data))
                            img = Image.FromStream(ms);
                    }
                    catch { img = null; }
                }

                this.InvokeIfRequired(() =>
                {
                    if (img != null) pbCurrentCover.Image = img;
                    lblCurrentTitle.Text = title;
                    lblCurrentArtist.Text = artist;
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine("UpdateCover: " + ex.Message);
            }
        }

        // ---------------- Get audio features (cached) ----------------
        private async Task<(float energy, float tempo)> GetAudioFeatures(string trackId)
        {
            try
            {
                if (string.IsNullOrEmpty(trackId)) return (0.4f, 100f);

                var resp = await SafeApiCall(async (t) =>
                {
                    var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.spotify.com/v1/audio-features/{trackId}");
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", t);
                    return await httpClient.SendAsync(req);
                });

                if (resp == null || !resp.IsSuccessStatusCode)
                    return (cachedEnergy, cachedTempo);

                var j = JObject.Parse(await resp.Content.ReadAsStringAsync());
                float energy = j["energy"]?.ToObject<float>() ?? cachedEnergy;
                float tempo = j["tempo"]?.ToObject<float>() ?? cachedTempo;
                return (energy, tempo);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("GetAudioFeatures: " + ex.Message);
                return (cachedEnergy, cachedTempo);
            }
        }

        // ---------------- Load recent ----------------
        private async Task LoadRecentlyPlayed()
        {
            try
            {
                var resp = await SafeApiCall(async (t) =>
                {
                    var req = new HttpRequestMessage(HttpMethod.Get, "https://api.spotify.com/v1/me/player/recently-played?limit=5");
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", t);
                    return await httpClient.SendAsync(req);
                });

                if (resp == null || !resp.IsSuccessStatusCode) return;

                var json = JObject.Parse(await resp.Content.ReadAsStringAsync());
                var items = json["items"];

                this.InvokeIfRequired(() => recentlyPanel.Controls.Clear());

                foreach (var item in items)
                {
                    var track = item["track"];
                    string tname = track["name"]?.ToString();
                    string artist = track["artists"]?[0]?["name"]?.ToString();
                    string imgUrl = track["album"]?["images"]?[2]?["url"]?.ToString();

                    Image img = null;
                    if (!string.IsNullOrEmpty(imgUrl))
                    {
                        try
                        {
                            byte[] data = await httpClient.GetByteArrayAsync(imgUrl);
                            using (var ms = new MemoryStream(data))
                                img = Image.FromStream(ms);
                        }
                        catch { img = null; }
                    }

                    Panel p = new Panel() { Size = new Size(recentlyPanel.Width - 24, 52), BackColor = Color.FromArgb(18, 8, 40) };
                    PictureBox pb = new PictureBox() { Size = new Size(48, 48), Location = new Point(4, 2), SizeMode = PictureBoxSizeMode.Zoom, Image = img ?? new Bitmap(1, 1) };
                    Label l = new Label() { Text = $"{tname} — {artist}", ForeColor = Color.White, Location = new Point(58, 10), Size = new Size(p.Width - 66, 32) };

                    p.Controls.Add(pb);
                    p.Controls.Add(l);

                    // click -> try play
                    p.Cursor = Cursors.Hand;
                    p.Click += async (s, e) =>
                    {
                        try
                        {
                            var id = track["id"]?.ToString();
                            if (!string.IsNullOrEmpty(id))
                            {
                                var body = new JObject { ["uris"] = new JArray($"spotify:track:{id}") };
                                var r = await SafeApiCall(async (tkn) =>
                                {
                                    var req = new HttpRequestMessage(HttpMethod.Put, "https://api.spotify.com/v1/me/player/play");
                                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tkn);
                                    req.Content = new StringContent(body.ToString(), Encoding.UTF8, "application/json");
                                    return await httpClient.SendAsync(req);
                                });
                                await Task.Delay(300);
                                await ShowCurrentTrack();
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine("PlayRecent click: " + ex.Message);
                        }
                    };

                    this.InvokeIfRequired(() => recentlyPanel.Controls.Add(p));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("LoadRecentlyPlayed: " + ex.Message);
            }
        }

        // ---------------- Set volume ----------------
        private async Task SetVolume(int percent)
        {
            try
            {
                var resp = await SafeApiCall(async (t) =>
                {
                    var req = new HttpRequestMessage(HttpMethod.Put, $"https://api.spotify.com/v1/me/player/volume?volume_percent={percent}");
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", t);
                    req.Content = new StringContent(string.Empty);
                    return await httpClient.SendAsync(req);
                });
                if (resp == null) Debug.WriteLine("SetVolume: null resp");
            }
            catch (Exception ex)
            {
                Debug.WriteLine("SetVolume: " + ex.Message);
            }
        }

        // ---------------- Spotify auth / token / helpery ----------------
        private async void BtnLogin_Click(object sender, EventArgs e)
        {
            try
            {
                var server = new SpotifyCallbackServer();
                server.Start(5000);
                string authUrl = $"https://accounts.spotify.com/authorize?client_id={clientId}&response_type=code&redirect_uri={Uri.EscapeDataString(redirectUri)}&scope={Uri.EscapeDataString(Scopes)}&show_dialog=true";
                Process.Start(new ProcessStartInfo(authUrl) { UseShellExecute = true });
                string code = await server.CodeTask;
                token = await ExchangeCodeForTokenAsync(code);

                if (!string.IsNullOrEmpty(token))
                {
                    MessageBox.Show("Zalogowano pomyślnie 🔥");
                    btnLogin.Visible = false;
                    await ShowCurrentTrack();
                    await LoadRecentlyPlayed();
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
            // if no token yet
            if (string.IsNullOrEmpty(token))
                return null;

            var response = await apiAction(token);

            if (response?.StatusCode == HttpStatusCode.Unauthorized && !string.IsNullOrEmpty(refreshToken))
            {
                if (await RefreshTokenAsync())
                {
                    response = await apiAction(token);
                }
            }

            return response;
        }

        // ---------------- Main API features (tracks / player) ----------------
        private async Task LoadTopTracks()
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

            var active = devices.FirstOrDefault(d => d["is_active"]?.ToObject<bool>() == true);
            if (active != null)
                return active["id"]?.ToString();

            var desktop = devices.FirstOrDefault(d => d["type"]?.ToString().Equals("Computer", StringComparison.OrdinalIgnoreCase) == true);
            if (desktop != null)
                return desktop["id"]?.ToString();

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

            return response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NoContent;
        }

        public async Task ShowCurrentTrack()
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

            lblCurrentTitle.Location = new Point(80, 400);
            lblCurrentArtist.Location = new Point(80, 440);

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
                await Task.Delay(300);
                await ShowCurrentTrack();
            }
        }

        private async Task TogglePlayPause()
        {
            // find device
            string deviceId = await GetActiveDeviceId();

            if (string.IsNullOrEmpty(deviceId))
            {
                var wake = await SafeApiCall(async (t) =>
                {
                    var r = new HttpRequestMessage(HttpMethod.Get, "https://api.spotify.com/v1/me/player");
                    r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", t);
                    return await httpClient.SendAsync(r);
                });
                await Task.Delay(300);
                deviceId = await GetActiveDeviceId();
            }

            if (string.IsNullOrEmpty(deviceId))
            {
                MessageBox.Show("Nie znaleziono aktywnego urządzenia Spotify. Upewnij się, że masz włączonego Spotify i poprawne scope.");
                return;
            }

            var stateResponse = await SafeApiCall(async (t) =>
            {
                var req = new HttpRequestMessage(HttpMethod.Get, "https://api.spotify.com/v1/me/player");
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", t);
                return await httpClient.SendAsync(req);
            });

            if (stateResponse == null) return;

            var stateText = await stateResponse.Content.ReadAsStringAsync();
            bool isPlayingNow = false;
            if (stateResponse.IsSuccessStatusCode)
            {
                try
                {
                    var j = JObject.Parse(stateText);
                    isPlayingNow = j["is_playing"]?.ToObject<bool>() ?? false;
                }
                catch { isPlayingNow = false; }
            }

            string actionUrl = isPlayingNow
                ? $"https://api.spotify.com/v1/me/player/pause?device_id={Uri.EscapeDataString(deviceId)}"
                : $"https://api.spotify.com/v1/me/player/play?device_id={Uri.EscapeDataString(deviceId)}";

            var actionResponse = await SafeApiCall(async (t) =>
            {
                var req = new HttpRequestMessage(HttpMethod.Put, actionUrl);
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", t);
                req.Content = new StringContent(string.Empty);
                return await httpClient.SendAsync(req);
            });

            if (actionResponse == null) return;

            if (!actionResponse.IsSuccessStatusCode && actionResponse.StatusCode != HttpStatusCode.NoContent)
            {
                MessageBox.Show($"Nie udało się zmienić stanu odtwarzania: {actionResponse.StatusCode}");
            }
            else
            {
                if (isPlayingNow) StopRotation(); else StartRotation();
                await Task.Delay(300);
                await ShowCurrentTrack();
            }
        }

        // ---------------- rotation control ----------------
        private void StartRotation()
        {
            isPlaying = true;
            if (!rotationTimer.Enabled) rotationTimer.Start();
        }

        private void StopRotation()
        {
            isPlaying = false;
            if (rotationTimer.Enabled) rotationTimer.Stop();
        }

        // ---------------- helpers ----------------
        private void InvokeIfRequired(Action action)
        {
            if (this.IsHandleCreated && this.InvokeRequired)
                this.Invoke(action);
            else
                action();
        }

        // Simple HSV to Color
        private Color ColorFromHsv(float hue, float sat, float val)
        {
            // hue in degrees, sat 0..1, val 0..1
            hue = (hue % 360 + 360) % 360;
            int hi = Convert.ToInt32(Math.Floor(hue / 60)) % 6;
            double f = hue / 60 - Math.Floor(hue / 60);
            val = Math.Max(0, Math.Min(1, val));
            sat = Math.Max(0, Math.Min(1, sat));
            double v = val;
            double p = v * (1 - sat);
            double q = v * (1 - f * sat);
            double t = v * (1 - (1 - f) * sat);

            double r = 0, g = 0, b = 0;
            switch (hi)
            {
                case 0: r = v; g = t; b = p; break;
                case 1: r = q; g = v; b = p; break;
                case 2: r = p; g = v; b = t; break;
                case 3: r = p; g = q; b = v; break;
                case 4: r = t; g = p; b = v; break;
                case 5: r = v; g = p; b = q; break;
            }
            return Color.FromArgb(
                (int)(255 * r),
                (int)(255 * g),
                (int)(255 * b)
            );
        }

        // ---------------- extension for rounded rectangles drawing ----------------
        // small helper - GDI+ doesn't have rounded rectangle fill by default
    }

    // ---------------- helper extension methods for Graphics ----------------
    public static class GraphicsExtensions
    {
        public static void FillRoundedRectangle(this Graphics g, Brush b, Rectangle r, int radius)
        {
            using (GraphicsPath gp = RoundedRect(r, radius))
            {
                g.FillPath(b, gp);
            }
        }
        public static void FillRoundedRectangle(this Graphics g, Brush b, RectangleF r, int radius)
        {
            using (GraphicsPath gp = RoundedRect(r, radius))
            {
                g.FillPath(b, gp);
            }
        }
        public static void DrawRoundedRectangle(this Graphics g, Pen p, Rectangle r, int radius)
        {
            using (GraphicsPath gp = RoundedRect(r, radius))
            {
                g.DrawPath(p, gp);
            }
        }
        private static GraphicsPath RoundedRect(Rectangle r, int radius)
        {
            return RoundedRect(new RectangleF(r.Left, r.Top, r.Width, r.Height), radius);
        }
        private static GraphicsPath RoundedRect(RectangleF r, int radius)
        {
            GraphicsPath gp = new GraphicsPath();
            float d = radius * 2f;
            gp.AddArc(r.Left, r.Top, d, d, 180, 90);
            gp.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            gp.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            gp.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            gp.CloseFigure();
            return gp;
        }
    }

    // ---------------- FancyCard and FancyButton (unchanged style, with circular cover) ----------------
    public class FancyCard : Panel
    {
        public FancyCard(string title, string artist, string album, int popularity, Image cover)
        {
            this.Size = new Size(260, 340);
            this.Margin = new Padding(12);
            this.BackColor = Color.FromArgb(18, 8, 40);
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
                ForeColor = Color.FromArgb(150, 255, 180),
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

            this.Click += async (s, e) =>
            {
                try
                {
                    var parent = this.FindForm() as Form1;
                    if (parent != null)
                    {
                        // jeśli jesteśmy w wątku GUI, Invoke nie zrobi nic złego
                        parent.BeginInvoke(new Action(async () =>
                        {
                            parent.Controls.Add(parent.pnlCurrentTrack);
                            await parent.ShowCurrentTrack();
                        }));
                    }
                }
                catch { }
            };
        }

        private void MakePictureBoxCircular(PictureBox pic)
        {
            using (GraphicsPath path = new GraphicsPath())
            {
                path.AddEllipse(0, 0, pic.Width, pic.Height);
                pic.Region = new Region(path);
            }
            pic.BackColor = Color.Transparent;
            pic.SizeMode = PictureBoxSizeMode.Zoom;
            // do reapply on resize
            pic.Resize += (s, e) =>
            {
                using (GraphicsPath path = new GraphicsPath())
                {
                    path.AddEllipse(0, 0, pic.Width, pic.Height);
                    pic.Region = new Region(path);
                }
            };
        }
    }

    public class FancyButton : Button
    {
        private Color baseColor = Color.FromArgb(30, 30, 30);
        private Color hoverColor = Color.FromArgb(120, 200, 255);
        private Color clickColor = Color.FromArgb(100, 180, 255);

        public FancyButton(string text)
        {
            this.Text = text;
            this.FlatStyle = FlatStyle.Flat;
            this.FlatAppearance.BorderSize = 0;
            this.BackColor = baseColor;
            this.ForeColor = Color.White;
            this.Font = new Font("Segoe UI", 10, FontStyle.Bold);
            this.Cursor = Cursors.Hand;
            this.Padding = new Padding(6);

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
                this.Region = Region.FromHrgn(CreateRoundRectRgn(0, 0, this.Width, this.Height, 18, 18));
            }
        }

        [System.Runtime.InteropServices.DllImport("Gdi32.dll", EntryPoint = "CreateRoundRectRgn")]
        private static extern IntPtr CreateRoundRectRgn(
            int nLeftRect, int nTopRect, int nRightRect, int nBottomRect,
            int nWidthEllipse, int nHeightEllipse
        );
    }

    // ---------------- minimal placeholder for SpotifyCallbackServer ----------------
    // If you already have your own class, keep it; otherwise use this simple HTTP listener
    // (It was used in your previous code. If you have a working implementation, remove this.)
    public class SpotifyCallbackServer
    {
        private readonly TaskCompletionSource<string> tcs = new TaskCompletionSource<string>();
        public Task<string> CodeTask => tcs.Task;
        private HttpListener listener;
        public void Start(int port)
        {
            try
            {
                listener = new HttpListener();
                listener.Prefixes.Add($"http://+:{port}/callback/");
                listener.Start();
                listener.BeginGetContext(HandleContext, null);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("SpotifyCallbackServer Start error: " + ex.Message);
            }
        }
        private void HandleContext(IAsyncResult ar)
        {
            try
            {
                if (listener == null) return;
                var ctx = listener.EndGetContext(ar);
                var req = ctx.Request;
                var code = req.QueryString["code"];
                var resp = ctx.Response;
                string html = "<html><body><h2>Możesz zamknąć to okno.</h2></body></html>";
                byte[] bytes = Encoding.UTF8.GetBytes(html);
                resp.ContentLength64 = bytes.Length;
                resp.OutputStream.Write(bytes, 0, bytes.Length);
                resp.OutputStream.Close();

                tcs.TrySetResult(code ?? "");
                listener.Stop();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("HandleContext error: " + ex.Message);
            }
        }
    }
}
