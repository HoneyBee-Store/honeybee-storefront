using System.Net;
using System.Net.Mail;
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

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(From);
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
    /// Plain-text summary of an order, used for both the email body and the
    /// WhatsApp message so the two always say the same thing.
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
    /// Emails the shop. Never throws: an SMTP outage must not lose an order
    /// that is already saved, and the customer still has the WhatsApp button.
    /// </summary>
    public async Task TryEmailAsync(Order order)
    {
        if (!_smtp.IsConfigured)
        {
            _log.LogInformation(
                "SMTP not configured — order {OrderNumber} was not emailed. " +
                "It is saved, and the customer can still send it on WhatsApp.",
                order.OrderNumber);
            return;
        }

        try
        {
            using var message = new MailMessage
            {
                From = new MailAddress(_smtp.From!, "HoneyBee Shop"),
                Subject = $"New order {order.OrderNumber} — {order.CustomerName}",
                Body = BuildSummary(order),
                BodyEncoding = Encoding.UTF8,
                SubjectEncoding = Encoding.UTF8
            };
            message.To.Add(_shop.OrderEmail);

            using var client = new SmtpClient(_smtp.Host, _smtp.Port)
            {
                EnableSsl = true,
                Credentials = string.IsNullOrWhiteSpace(_smtp.User)
                    ? null
                    : new NetworkCredential(_smtp.User, _smtp.Password)
            };

            await client.SendMailAsync(message);
            _log.LogInformation("Emailed order {OrderNumber}.", order.OrderNumber);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Could not email order {OrderNumber}. The order is still saved.",
                order.OrderNumber);
        }
    }
}
