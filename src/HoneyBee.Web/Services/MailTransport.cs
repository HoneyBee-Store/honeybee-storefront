using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using System.Text;
using System.Text.Json;

namespace HoneyBee.Web.Services;

public enum MailProvider
{
    /// <summary>Ordinary SMTP. Blocked on many corporate networks and on
    /// several Azure App Service tiers.</summary>
    Smtp = 0,

    /// <summary>Brevo's HTTP API. Goes out over 443 like any web request, so
    /// firewalls that drop SMTP do not affect it.</summary>
    Brevo = 1
}

/// <summary>One way of getting a message out.</summary>
public interface IMailTransport
{
    MailProvider Provider { get; }

    /// <summary>
    /// Sends, or throws with a message worth showing the owner. Callers that
    /// must not fail (order notifications) catch; the admin test button does
    /// not, so the real reason reaches the screen.
    /// </summary>
    Task SendAsync(MailSettings settings, string to, string subject,
                   string textBody, string? htmlBody, CancellationToken ct = default);
}

public class SmtpMailTransport : IMailTransport
{
    public MailProvider Provider => MailProvider.Smtp;

    public async Task SendAsync(MailSettings s, string to, string subject,
                                string textBody, string? htmlBody, CancellationToken ct = default)
    {
        using var message = new MailMessage
        {
            From = new MailAddress(s.From!, "HoneyBee Shop"),
            Subject = subject,
            Body = textBody,
            BodyEncoding = Encoding.UTF8,
            SubjectEncoding = Encoding.UTF8
        };
        message.To.Add(to);

        // Both parts are attached: clients that block HTML still show the text.
        if (!string.IsNullOrWhiteSpace(htmlBody))
        {
            message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(
                htmlBody, Encoding.UTF8, MediaTypeNames.Text.Html));
        }

        using var client = new SmtpClient(s.Host, s.Port)
        {
            EnableSsl = true,
            Timeout = s.TimeoutSeconds * 1000,
            Credentials = string.IsNullOrWhiteSpace(s.User)
                ? null
                : new NetworkCredential(s.User, s.Password)
        };

        await client.SendMailAsync(message, ct);
    }
}

/// <summary>
/// Sends through Brevo's REST API over HTTPS.
///
/// This exists because SMTP is not reachable from every network — the shop's
/// own office blocks port 587, and Azure App Service blocks it on several
/// tiers. An ordinary HTTPS request has neither problem.
/// </summary>
public class BrevoMailTransport : IMailTransport
{
    private const string Endpoint = "https://api.brevo.com/v3/smtp/email";

    private readonly IHttpClientFactory _factory;

    public BrevoMailTransport(IHttpClientFactory factory) => _factory = factory;

    public MailProvider Provider => MailProvider.Brevo;

    public async Task SendAsync(MailSettings s, string to, string subject,
                                string textBody, string? htmlBody, CancellationToken ct = default)
    {
        var payload = new
        {
            sender = new { name = "HoneyBee Shop", email = s.From },
            to = new[] { new { email = to } },
            subject,
            textContent = textBody,
            htmlContent = string.IsNullOrWhiteSpace(htmlBody) ? null : htmlBody
        };

        var client = _factory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(s.TimeoutSeconds);

        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint);
        request.Headers.Add("api-key", s.ApiKey);
        request.Headers.Add("accept", "application/json");
        request.Content = new StringContent(
            JsonSerializer.Serialize(payload,
                new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull }),
            Encoding.UTF8, "application/json");

        using var response = await client.SendAsync(request, ct);

        if (response.IsSuccessStatusCode) return;

        // Brevo answers failures as {"code":"...","message":"..."}. The message
        // is the useful half — "Key not found", "Sender not valid" — so it is
        // pulled out rather than reporting a bare status code.
        var body = await response.Content.ReadAsStringAsync(ct);
        var detail = ExtractMessage(body) ?? body;

        throw new InvalidOperationException(
            $"Brevo rejected the message ({(int)response.StatusCode}): {Trim(detail)}");
    }

    private static string? ExtractMessage(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty("message", out var m) ? m.GetString() : null;
        }
        catch (JsonException)
        {
            return null;   // not JSON — the raw body is shown instead
        }
    }

    private static string Trim(string s) =>
        s.Length <= 300 ? s : s[..300] + "…";
}
