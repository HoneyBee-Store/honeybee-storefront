using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using System.Text;
using HoneyBee.Web.Models;

namespace HoneyBee.Web.Services;

public class ShopSettings
{
    /// <summary>The shop's own WhatsApp number, in international form.</summary>
    public string WhatsAppNumber { get; set; } = "+962799423449";
    public string OrderEmail { get; set; } = "khaledehmide3@gmail.com";
}

public class SmtpSettings
{
    public string? Host { get; set; }
    public int Port { get; set; } = 587;
    public string? User { get; set; }
    public string? Password { get; set; }
    public string? From { get; set; }

    /// <summary>
    /// How long to wait on the mail server before giving up. Without this,
    /// System.Net.Mail waits 100 seconds by default, so an unreachable server
    /// would hold a customer on the checkout button for well over a minute.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 15;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Host)
        && !string.IsNullOrWhiteSpace(From)
        // A server that wants a username wants the password too. Without this
        // the admin screen would report "on" while every send failed on auth,
        // which is the most confusing state to leave the shop in.
        && (string.IsNullOrWhiteSpace(User) || !string.IsNullOrWhiteSpace(Password));
}

public class OrderNotifier
{
    private readonly SmtpSettings _smtp;
    private readonly ShopSettings _shop;
    private readonly ILogger<OrderNotifier> _log;

    public OrderNotifier(SmtpSettings smtp, ShopSettings shop, ILogger<OrderNotifier> log)
    {
        _smtp = smtp;
        _shop = shop;
        _log = log;
    }

    /// <summary>
    /// Plain-text summary of an order, used for the WhatsApp message and as the
    /// email's plain-text alternative, so every channel says the same thing.
    /// </summary>
    public string BuildSummary(Order order)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"طلب جديد / New order — {order.OrderNumber}");
        sb.AppendLine();
        sb.AppendLine($"الاسم / Name: {order.CustomerName}");
        sb.AppendLine($"الهاتف / Phone: {order.Phone}");
        sb.AppendLine($"الاستلام / Pickup: {order.PickupLocation?.NameAr} ({order.PickupLocation?.NameEn})");
        sb.AppendLine();
        sb.AppendLine("المنتجات / Items:");

        foreach (var item in order.Items)
        {
            var size = item.SizeKg == 1m ? "1 kg" : $"{item.SizeKg:0.##} kg";
            var line = item.LineTotal is null ? "" : $" = {item.LineTotal:0.###} JOD";
            sb.AppendLine($"• {item.NameSnapshot} — {size} × {item.Quantity}{line}");
        }

        if (order.Total is not null)
        {
            sb.AppendLine();
            sb.AppendLine($"الإجمالي / Total: {order.Total:0.###} JOD");
        }

        if (!string.IsNullOrWhiteSpace(order.CustomerNotes))
        {
            sb.AppendLine();
            sb.AppendLine($"ملاحظات / Notes: {order.CustomerNotes}");
        }

        return sb.ToString();
    }

    /// <summary>
    /// The HTML body. Email clients strip stylesheets and most modern CSS, so
    /// this is a table with inline styles — the one layout that renders the
    /// same in Gmail, Outlook and iOS Mail.
    /// </summary>
    public string BuildEmailHtml(Order order)
    {
        static string E(string? s) => WebUtility.HtmlEncode(s ?? "");

        var rows = new StringBuilder();
        foreach (var item in order.Items)
        {
            var size = item.SizeKg == 1m ? "1 kg" : $"{item.SizeKg:0.##} kg";
            var total = item.LineTotal is null ? "—" : $"{item.LineTotal:0.###} JOD";
            rows.Append($"""
                <tr>
                  <td style="padding:10px 12px;border-bottom:1px solid #EFE7D6;">{E(item.NameSnapshot)}</td>
                  <td style="padding:10px 12px;border-bottom:1px solid #EFE7D6;white-space:nowrap;">{size}</td>
                  <td style="padding:10px 12px;border-bottom:1px solid #EFE7D6;text-align:center;">{item.Quantity}</td>
                  <td style="padding:10px 12px;border-bottom:1px solid #EFE7D6;white-space:nowrap;text-align:end;">{total}</td>
                </tr>
                """);
        }

        var totalBlock = order.Total is null
            ? ""
            : $"""
               <tr>
                 <td colspan="3" style="padding:12px;font-weight:700;text-align:end;">الإجمالي / Total</td>
                 <td style="padding:12px;font-weight:700;white-space:nowrap;text-align:end;">{order.Total:0.###} JOD</td>
               </tr>
               """;

        var notesBlock = string.IsNullOrWhiteSpace(order.CustomerNotes)
            ? ""
            : $"""
               <p style="margin:18px 0 0;padding:12px;background:#FAF4E8;border-inline-start:3px solid #C8952B;">
                 <strong>ملاحظات / Notes:</strong><br>{E(order.CustomerNotes)}
               </p>
               """;

        // tel: and wa.me links make the customer one tap away from the inbox.
        var digits = new string(order.Phone.Where(char.IsDigit).ToArray());

        return $"""
            <!doctype html>
            <html dir="rtl" lang="ar">
            <body style="margin:0;padding:24px;background:#F3EEE4;font-family:'Segoe UI',Tahoma,Arial,sans-serif;color:#2A1D12;">
              <table role="presentation" cellpadding="0" cellspacing="0" style="max-width:640px;margin:0 auto;background:#FFFFFF;border-radius:6px;overflow:hidden;">
                <tr>
                  <td style="background:#2A1D12;color:#E4C06A;padding:20px 24px;">
                    <div style="font-size:20px;font-weight:700;">🍯 طلب جديد / New order</div>
                    <div style="font-size:14px;color:#E3D3AE;margin-top:4px;">{E(order.OrderNumber)}</div>
                  </td>
                </tr>
                <tr>
                  <td style="padding:24px;">
                    <table role="presentation" cellpadding="0" cellspacing="0" style="width:100%;font-size:15px;">
                      <tr>
                        <td style="padding:4px 0;color:#6B5A47;width:130px;">الاسم / Name</td>
                        <td style="padding:4px 0;font-weight:700;">{E(order.CustomerName)}</td>
                      </tr>
                      <tr>
                        <td style="padding:4px 0;color:#6B5A47;">الهاتف / Phone</td>
                        <td style="padding:4px 0;font-weight:700;" dir="ltr">
                          <a href="tel:{E(order.Phone)}" style="color:#A5761B;">{E(order.Phone)}</a>
                          &nbsp;·&nbsp;
                          <a href="https://wa.me/{digits}" style="color:#A5761B;">WhatsApp</a>
                        </td>
                      </tr>
                      <tr>
                        <td style="padding:4px 0;color:#6B5A47;">الاستلام / Pickup</td>
                        <td style="padding:4px 0;font-weight:700;">{E(order.PickupLocation?.NameAr)} ({E(order.PickupLocation?.NameEn)})</td>
                      </tr>
                    </table>

                    <table role="presentation" cellpadding="0" cellspacing="0" style="width:100%;margin-top:20px;border-collapse:collapse;font-size:15px;">
                      <tr style="background:#FAF4E8;">
                        <th align="start" style="padding:10px 12px;font-size:12px;letter-spacing:.05em;text-transform:uppercase;color:#6B5A47;">المنتج / Item</th>
                        <th align="start" style="padding:10px 12px;font-size:12px;letter-spacing:.05em;text-transform:uppercase;color:#6B5A47;">الحجم / Size</th>
                        <th style="padding:10px 12px;font-size:12px;letter-spacing:.05em;text-transform:uppercase;color:#6B5A47;">الكمية / Qty</th>
                        <th align="end" style="padding:10px 12px;font-size:12px;letter-spacing:.05em;text-transform:uppercase;color:#6B5A47;">المجموع / Total</th>
                      </tr>
                      {rows}
                      {totalBlock}
                    </table>

                    {notesBlock}
                  </td>
                </tr>
                <tr>
                  <td style="padding:14px 24px;background:#FAF4E8;font-size:12px;color:#6B5A47;">
                    أُرسلت تلقائيًا من متجر عسل النحل · Sent automatically by HoneyBee Shop
                  </td>
                </tr>
              </table>
            </body>
            </html>
            """;
    }

    /// <summary>
    /// A wa.me link that opens WhatsApp with the order pre-filled, addressed to
    /// the shop. Not the Business API — nothing sends on its own; the customer
    /// taps send. That needs no Meta approval and works today.
    /// </summary>
    public string BuildWhatsAppLink(Order order)
    {
        var number = new string(_shop.WhatsAppNumber.Where(char.IsDigit).ToArray());
        return $"https://wa.me/{number}?text={Uri.EscapeDataString(BuildSummary(order))}";
    }

    /// <summary>
    /// Sends the order email without making the customer wait for it.
    ///
    /// The body is built here, on the request thread, while the order and its
    /// navigation properties are still loaded — only the SMTP conversation is
    /// handed to the background. Checkout redirects to WhatsApp immediately,
    /// and a slow mail server can no longer delay it.
    /// </summary>
    public void QueueOrderEmail(Order order)
    {
        if (!_smtp.IsConfigured)
        {
            _log.LogInformation(
                "SMTP not configured — order {OrderNumber} was not emailed. It is saved, " +
                "and the customer still gets the WhatsApp message.",
                order.OrderNumber);
            return;
        }

        var subject = $"طلب جديد {order.OrderNumber} — {order.CustomerName}";
        var text = BuildSummary(order);
        var html = BuildEmailHtml(order);
        var reference = order.OrderNumber;

        _ = Task.Run(async () =>
        {
            try
            {
                await SendAsync(_shop.OrderEmail, subject, text, html);
                _log.LogInformation("Emailed order {OrderNumber}.", reference);
            }
            catch (Exception ex)
            {
                // The order is already saved and the shop still gets WhatsApp,
                // so a mail failure is logged rather than surfaced.
                _log.LogError(ex, "Could not email order {OrderNumber}. The order is still saved.",
                    reference);
            }
        });
    }

    /// <summary>
    /// Sends a message and lets failures propagate. Used by the admin's test
    /// button, which needs to show the real error rather than swallow it.
    /// </summary>
    public async Task SendAsync(string to, string subject, string textBody, string? htmlBody = null)
    {
        using var message = new MailMessage
        {
            From = new MailAddress(_smtp.From!, "HoneyBee Shop"),
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

        using var client = new SmtpClient(_smtp.Host, _smtp.Port)
        {
            EnableSsl = true,
            Timeout = _smtp.TimeoutSeconds * 1000,
            Credentials = string.IsNullOrWhiteSpace(_smtp.User)
                ? null
                : new NetworkCredential(_smtp.User, _smtp.Password)
        };

        await client.SendMailAsync(message);
    }

    /// <summary>True when the shop has working mail settings.</summary>
    public bool IsConfigured => _smtp.IsConfigured;

    /// <summary>Where a test message would go, for the admin screen.</summary>
    public string OrderEmail => _shop.OrderEmail;
}
