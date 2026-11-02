using System.Diagnostics;
using System.Text.Json;
using KeyboardMouse.Native;
using KeyboardMouse.Platform;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace KeyboardMouse.Ui;

/// <summary>A command sent by the guide page (see guide.html, <c>send(...)</c>).</summary>
public sealed record GuideCommand(string Name, JsonElement Payload)
{
    public bool GetBool(string property) =>
        Payload.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.True;

    public string? GetString(string property) =>
        Payload.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    public JsonElement? Get(string property) =>
        Payload.TryGetProperty(property, out var v) ? v : null;
}

/// <summary>
/// The guide / settings window: guide.html rendered in WebView2. The page is
/// served from embedded resources under a private origin, so it never touches
/// the network. Closing the window disposes it (and the browser processes);
/// the app keeps running in the tray.
/// </summary>
public sealed class GuideWindow : Form
{
    private const string Origin = "https://app.keyboardmouse.invalid/";
    private const string RuntimeDownloadUrl = "https://go.microsoft.com/fwlink/p/?LinkId=2124703";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly WebView2 _web = new();
    private readonly string _startPage;
    private bool _ready;
    private object? _pendingState;

    public GuideWindow(Icon icon, string startPage = "home")
    {
        _startPage = startPage;

        Text = "KeyboardMouse";
        Icon = icon;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(1120, 760);
        MinimumSize = new Size(820, 560);

        bool dark = SystemUsesDarkTheme();
        BackColor = dark ? Color.FromArgb(11, 16, 32) : Color.FromArgb(244, 246, 251);
        _web.DefaultBackgroundColor = BackColor;
        _web.Dock = DockStyle.Fill;
        Controls.Add(_web);
    }

    /// <summary>Raised on the UI thread for every command from the page.</summary>
    public event Action<GuideCommand>? CommandReceived;

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        int on = SystemUsesDarkTheme() ? 1 : 0;
        Dwm.DwmSetWindowAttribute(Handle, Dwm.DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, sizeof(int));
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        try
        {
            await InitializeAsync();
        }
        catch (WebView2RuntimeNotFoundException)
        {
            ShowRuntimeMissing();
        }
        catch (Exception ex)
        {
            Log.Error("Could not start the guide window", ex);
            ShowRuntimeMissing();
        }
    }

    private async Task InitializeAsync()
    {
        string dataFolder = Path.Combine(Log.DirectoryPath, "WebView2");
        var environment = await CoreWebView2Environment.CreateAsync(null, dataFolder);
        await _web.EnsureCoreWebView2Async(environment);
        if (IsDisposed)
        {
            return;
        }

        var core = _web.CoreWebView2;
        var settings = core.Settings;
        settings.AreDefaultContextMenusEnabled = false;
        settings.IsStatusBarEnabled = false;
        settings.IsZoomControlEnabled = false;
        settings.AreBrowserAcceleratorKeysEnabled = false;
        settings.IsGeneralAutofillEnabled = false;
        settings.IsPasswordAutosaveEnabled = false;
#if !DEBUG
        settings.AreDevToolsEnabled = false;
#endif

        core.AddWebResourceRequestedFilter(Origin + "*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += OnWebResourceRequested;
        core.NavigationStarting += (_, args) =>
        {
            if (!args.Uri.StartsWith(Origin, StringComparison.OrdinalIgnoreCase))
            {
                args.Cancel = true;
            }
        };
        core.NewWindowRequested += (_, args) => args.Handled = true;
        core.NavigationCompleted += (_, args) =>
        {
            if (!args.IsSuccess)
            {
                Log.Error($"Guide page failed to load: {args.WebErrorStatus}");
            }
        };
        core.WebMessageReceived += OnWebMessageReceived;

        core.Navigate(Origin + "guide.html#" + _startPage);
    }

    private void OnWebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        string path = new Uri(e.Request.Uri).AbsolutePath.TrimStart('/');
        (string resource, string type)? file = path switch
        {
            "guide.html" => ("KeyboardMouse.Ui.guide.html", "text/html; charset=utf-8"),
            "logo.png" => ("KeyboardMouse.Ui.logo.png", "image/png"),
            _ => null
        };

        var environment = _web.CoreWebView2.Environment;
        var stream = file is null ? null : typeof(GuideWindow).Assembly.GetManifestResourceStream(file.Value.resource);
        e.Response = stream is null
            ? environment.CreateWebResourceResponse(null, 404, "Not Found", "")
            : environment.CreateWebResourceResponse(
                stream,
                200,
                "OK",
                $"Content-Type: {file!.Value.type}\r\nCache-Control: no-cache");
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("cmd", out var cmd) ||
                cmd.ValueKind != JsonValueKind.String)
            {
                return;
            }

            var command = new GuideCommand(cmd.GetString()!, root.Clone());
            if (command.Name == "ready")
            {
                _ready = true;
                if (_pendingState is not null)
                {
                    PushState(_pendingState);
                }
            }

            CommandReceived?.Invoke(command);
        }
        catch (Exception ex)
        {
            Log.Error("Bad message from the guide page", ex);
        }
    }

    /// <summary>Sends the app state; buffered until the page says it is ready.</summary>
    public void PushState(object state)
    {
        _pendingState = state;
        Post(new { type = "state", state });
    }

    public void ShowPage(string page) => Post(new { type = "page", page });

    public void Toast(string textKey) => Post(new { type = "toast", text = textKey });

    /// <summary>Lights up a key on the Keys page. Only called while this window is active.</summary>
    public void PostKey(string key, bool down) => Post(new { type = "key", key, down });

    private void Post(object message)
    {
        if (!_ready || IsDisposed || _web.CoreWebView2 is null)
        {
            return;
        }

        try
        {
            _web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(message, JsonOptions));
        }
        catch (Exception ex)
        {
            Log.Error("Could not post to the guide page", ex);
        }
    }

    /// <summary>Fallback when the Edge WebView2 runtime is not installed.</summary>
    private void ShowRuntimeMissing()
    {
        if (IsDisposed)
        {
            return;
        }

        Controls.Clear();
        _web.Dispose();

        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            Padding = new Padding(40),
            BackColor = Color.White,
            WrapContents = false
        };
        panel.Controls.Add(new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI", 16f, FontStyle.Bold),
            Text = "KeyboardMouse is running"
        });
        panel.Controls.Add(new Label
        {
            AutoSize = true,
            MaximumSize = new Size(640, 0),
            Font = new Font("Segoe UI", 10.5f),
            Margin = new Padding(0, 12, 0, 18),
            Text = "This guide window needs the Microsoft Edge WebView2 Runtime, which is " +
                   "not installed on this PC. The app itself works without it: tap Caps Lock " +
                   "to turn on mouse mode, use I J K L to move and Space to click.\r\n\r\n" +
                   "Install the free runtime from Microsoft to see the full guide."
        });

        var download = new Button { Text = "Download WebView2 Runtime", AutoSize = true, Padding = new Padding(10, 4, 10, 4) };
        download.Click += (_, _) => OpenUrl(RuntimeDownloadUrl);
        panel.Controls.Add(download);
        Controls.Add(panel);
    }

    public static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error("Could not open " + url, ex);
        }
    }

    private static bool SystemUsesDarkTheme()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
        }
        catch
        {
            return false;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _web.Dispose();
        }

        base.Dispose(disposing);
    }
}
