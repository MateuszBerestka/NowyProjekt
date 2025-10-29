using System;
using System.Net;
using System.Threading.Tasks;

public class SpotifyCallbackServer
{
    private readonly TaskCompletionSource<string> _tcs = new TaskCompletionSource<string>();
    public Task<string> CodeTask => _tcs.Task;

    private HttpListener _listener;

    public void Start(int port = 5000)
    {
        _listener = new HttpListener();
        _listener.Prefixes.Add("http://+:5000/callback/");
        _listener.Start();

        _listener.BeginGetContext(async result =>
        {
            var context = _listener.EndGetContext(result);
            string code = context.Request.QueryString["code"];

            byte[] responseBytes = System.Text.Encoding.UTF8.GetBytes("Możesz zamknąć to okno.");
            context.Response.OutputStream.Write(responseBytes, 0, responseBytes.Length);
            context.Response.Close();

            _listener.Stop();
            _tcs.SetResult(code);
        }, null);
    }
}