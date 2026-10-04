using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace Verrdoss.App;

public sealed record UpdateOffer(Version Version, Uri Installer);

public sealed class UpdateCheck
{
    public UpdateOffer? Offer { get; init; }
    public string Message { get; init; } = "";
}

public static class UpdateChecker
{
    private static readonly string[] AllowedHosts = ["github.com", "objects.githubusercontent.com", "release-assets.githubusercontent.com"];

    public static Version Current =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0);

    public static async Task<UpdateCheck> CheckAsync(CancellationToken cancellation = default)
    {
        if (!UpdateSource.IsConfigured)
        {
            return new UpdateCheck
            {
                Message = "La source des mises à jour n'est pas configurée."
            };
        }

        using var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"{Brand.Name}/{Current}");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        var url = $"https://api.github.com/repos/{UpdateSource.Owner}/{UpdateSource.Repo}/releases/latest";
        using var response = await client.GetAsync(url, cancellation);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return new UpdateCheck { Message = "Aucune mise à jour publiée." };
        if ((int)response.StatusCode == 403 || (int)response.StatusCode == 429)
            return new UpdateCheck { Message = "GitHub limite les vérifications pour le moment." };
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation));
        var root = document.RootElement;
        var tag = root.TryGetProperty("tag_name", out var tagElement) ? tagElement.GetString() : null;
        if (!TryParseTag(tag, out var remote) || remote <= Current)
            return new UpdateCheck { Message = "Vous êtes à jour." };

        if (!root.TryGetProperty("assets", out var assets))
            return new UpdateCheck { Message = "La release ne contient pas l'installateur." };

        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
            var download = asset.TryGetProperty("browser_download_url", out var urlElement) ? urlElement.GetString() : null;
            if (!string.Equals(name, UpdateSource.AssetName, StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(download))
                continue;
            if (!Uri.TryCreate(download, UriKind.Absolute, out var installer) || !IsAllowed(installer))
                return new UpdateCheck { Message = "L'adresse de l'installateur n'est pas reconnue." };
            return new UpdateCheck
            {
                Offer = new UpdateOffer(remote, installer),
                Message = $"La version {remote} est disponible."
            };
        }

        return new UpdateCheck { Message = "La release ne contient pas l'installateur." };
    }

    public static async Task<string> DownloadAsync(Uri installer, CancellationToken cancellation = default)
    {
        if (!IsAllowed(installer))
            throw new InvalidOperationException("L'adresse de l'installateur n'est pas reconnue.");

        var destination = Path.Combine(Path.GetTempPath(), UpdateSource.AssetName);
        using var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"{Brand.Name}/{Current}");
        var current = installer;
        for (var hop = 0; hop < 5; hop++)
        {
            using var response = await client.GetAsync(current, HttpCompletionOption.ResponseHeadersRead, cancellation);
            if (response.StatusCode is HttpStatusCode.Moved or HttpStatusCode.Redirect or HttpStatusCode.RedirectMethod or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
            {
                var next = response.Headers.Location ?? throw new InvalidOperationException("Redirection sans adresse.");
                if (!next.IsAbsoluteUri)
                    next = new Uri(current, next.OriginalString);
                if (!IsAllowed(next))
                    throw new InvalidOperationException("L'adresse de l'installateur n'est pas reconnue.");
                current = next;
                continue;
            }
            response.EnsureSuccessStatusCode();
            await using var input = await response.Content.ReadAsStreamAsync(cancellation);
            await using var output = File.Create(destination);
            await input.CopyToAsync(output, cancellation);
            return destination;
        }

        throw new InvalidOperationException("Trop de redirections.");
    }

    private static bool TryParseTag(string? tag, out Version version)
    {
        version = new Version(0, 0);
        if (string.IsNullOrWhiteSpace(tag))
            return false;
        var text = tag.Trim();
        if (text.StartsWith('v') || text.StartsWith('V'))
            text = text[1..];
        var dash = text.IndexOf('-');
        if (dash > 0)
            text = text[..dash];
        return Version.TryParse(text, out version!);
    }

    private static bool IsAllowed(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttps && AllowedHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase);
}
