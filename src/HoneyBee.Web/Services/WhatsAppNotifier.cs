using System.Text;

namespace HoneyBee.Web.Services;

/// <summary>
/// Where a WhatsApp notification goes, and the key that authorises it.
/// </summary>
public class WhatsAppSettings
{
    private string? _apiKey;
    private string? _phone;

    /// <summary>
    /// The shop's own number in international form. Punctuation is stripped —
    /// the API wants digits with a country code and nothing else.
    /// </summary>
    public string? Phone
    {
        get => _phone;
        set => _phone = value is null
            ? null
            : new string(value.Where(char.IsDigit).ToArray());
    }

    /// <summary>Key from CallMeBot, obtained by messaging their bot once.</summary>
    public string? ApiKey
    {
        get => _apiKey;
        set => _apiKey = value is null
            ? null
            : new string(value.Where(c => !char.IsWhiteSpace(c)).ToArray());
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Phone) && !string.IsNullOrWhiteSpace(ApiKey);
}

/// <summary>
/// Sends a WhatsApp message to the shop's own number when an order arrives.
///
/// The wa.me link on the confirmation page cannot do this: it opens WhatsApp
/// with the text ready and waits for the customer to press send, which Meta
/// enforces so that no website can send messages as someone. That leaves the
/// shop depending on the customer to complete the notification.
///
/// This goes the other way — the server tells the owner directly, at the same
/// moment as the email, whether or not the customer presses anything.
///
/// CallMeBot is a personal bridge rather than the official Business API: no
/// Meta business account, no message templates, and it only reaches the one
/// number that authorised it. That is the whole requirement here. It is also
/// unofficial and could stop working, which is why a failure is logged and
/// ignored rather than allowed to affect the order.
/// </summary>
public class WhatsAppNotifier
{
    private const string Endpoint = "https://api.callmebot.com/whatsapp.php";

    /// <summary>
    /// Long messages get truncated by the service and cost nothing to avoid.
    /// The email carries the full detail; this is a nudge to go and read it.
    /// </summary>
    private const int MaxLength = 900;

    private readonly IHttpClientFactory _factory;

    public WhatsAppNotifier(IHttpClientFactory factory) => _factory = factory;

    /// <summary>
    /// Sends, or throws with something worth showing the owner. The admin's
    /// test button lets that surface; the order path catches it.
    /// </summary>
    public async Task SendAsync(
        WhatsAppSettings settings, string message, CancellationToken ct = default)
    {
        var text = message.Length > MaxLength
            ? message[..MaxLength] + "…"
            : message;

        var url = $"{Endpoint}?phone={Uri.EscapeDataString(settings.Phone!)}" +
                  $"&apikey={Uri.EscapeDataString(settings.ApiKey!)}" +
                  $"&text={Uri.EscapeDataString(text)}";

        var client = _factory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(20);

        using var response = await client.GetAsync(url, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"CallMeBot returned {(int)response.StatusCode}: {Summarise(body)}");
        }

        // It answers 200 with an HTML page even when it refuses, so the body
        // has to be read rather than trusting the status code.
        if (body.Contains("APIKey", StringComparison.OrdinalIgnoreCase)
            && body.Contains("invalid", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "CallMeBot rejected the API key. Message the bot again to get a fresh one.");
        }

        if (body.Contains("not registered", StringComparison.OrdinalIgnoreCase)
            || body.Contains("wasn't found", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"CallMeBot does not recognise this number: {Summarise(body)}");
        }
    }

    /// <summary>Strips the HTML wrapper so the error reads as a sentence.</summary>
    private static string Summarise(string body)
    {
        var text = new StringBuilder();
        var inTag = false;

        foreach (var c in body)
        {
            if (c == '<') inTag = true;
            else if (c == '>') inTag = false;
            else if (!inTag) text.Append(c);
        }

        var cleaned = string.Join(' ',
            text.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        return cleaned.Length > 200 ? cleaned[..200] + "…" : cleaned;
    }
}
