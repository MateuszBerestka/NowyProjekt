using LiveCharts;
using LiveCharts.WinForms;
//using LiveCharts.Wpf;
using System.Linq;
using System.Windows.Forms;
using System;

public partial class Form1 : Form
{
    private LiveCharts.WinForms.PieChart pieChart;

    public Form1()
    {
        InitializeComponent();

        SetupChart();

        btnFetchPlaylist.Click += BtnFetchPlaylist_Click;
    }

    private void SetupChart()
    {
        pieChart = new PieChart
        {
            Dock = DockStyle.Fill
        };
        panelChart.Controls.Add(pieChart);
    }

    private void BtnFetchPlaylist_Click(object sender, EventArgs e)
    {
        // przykładowe dane
        var tracks = new[]
        {
            new { Title="Song 1", Artist="Artist A", Album="Album X", Genres=new string[]{"Pop"}, Tempo=120, Popularity=85 },
            new { Title="Song 2", Artist="Artist B", Album="Album Y", Genres=new string[]{"Rock"}, Tempo=140, Popularity=70 },
            new { Title="Song 3", Artist="Artist C", Album="Album Z", Genres=new string[]{"Pop"}, Tempo=130, Popularity=60 },
        };

        dgvTracks.Rows.Clear();
        foreach (var t in tracks)
        {
            dgvTracks.Rows.Add(t.Title, t.Artist, t.Album, string.Join(", ", t.Genres), t.Tempo, t.Popularity);
        }

        DrawGenreChart(tracks);
    }

    private void DrawGenreChart(dynamic[] tracks)
    {
        // zliczamy gatunki
        var genreCounts = tracks
            .SelectMany(t => t.Genres)
            .GroupBy(g => g)
            .ToDictionary(g => g.Key, g => g.Count());

        var series = genreCounts.Select(g =>
            new PieSeries<double> { Values = new double[] { g.Value }, Name = g.Key }).ToArray();

        pieChart.Series = series;
        pieChart.Refresh();
    }
}
