using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace backend.Services;

public class EmailService : IEmailService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<EmailService> _logger;

    public EmailService(IConfiguration configuration, ILogger<EmailService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task SendVerificationOtpAsync(string toEmail, string recipientName, string otpCode)
    {
        var host = Environment.GetEnvironmentVariable("SMTP_HOST") 
                   ?? _configuration["Smtp:Host"] 
                   ?? "smtp.gmail.com";
                   
        var portStr = Environment.GetEnvironmentVariable("SMTP_PORT") 
                      ?? _configuration["Smtp:Port"] 
                      ?? "587";
        if (!int.TryParse(portStr, out int port)) port = 587;

        var user = Environment.GetEnvironmentVariable("SMTP_USER") 
                   ?? _configuration["Smtp:User"] 
                   ?? "wavedev13@gmail.com";
                   
        var pass = Environment.GetEnvironmentVariable("SMTP_PASS") 
                   ?? _configuration["Smtp:Pass"] 
                   ?? "kzjl otxa ankt igew";

        // Remove spaces from app password
        pass = pass.Replace(" ", "");

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("OpenSocial Security", user));
        message.To.Add(new MailboxAddress(string.IsNullOrWhiteSpace(recipientName) ? toEmail : recipientName, toEmail));
        message.Subject = $"{otpCode} is your OpenSocial verification code";

        var bodyBuilder = new BodyBuilder
        {
            HtmlBody = BuildHtmlEmail(recipientName, otpCode)
        };
        message.Body = bodyBuilder.ToMessageBody();

        using var client = new SmtpClient();
        client.Timeout = 15000;
        client.CheckCertificateRevocation = false;
        await client.ConnectAsync(host, port, SecureSocketOptions.StartTls);
        await client.AuthenticateAsync(user, pass);
        await client.SendAsync(message);
        await client.DisconnectAsync(true);

        _logger.LogInformation("Verification OTP {Otp} sent to {Email}", otpCode, toEmail);
    }

    private string BuildHtmlEmail(string recipientName, string otpCode)
    {
        var displayName = string.IsNullOrWhiteSpace(recipientName) ? "there" : recipientName;
        return $$"""
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">
  <title>OpenSocial Email Verification</title>
</head>
<body style="margin: 0; padding: 0; background-color: #f1f5f9; font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; color: #1e293b;">
  <table width="100%" cellpadding="0" cellspacing="0" style="padding: 40px 16px; background-color: #f1f5f9;">
    <tr>
      <td align="center">
        <table width="100%" cellpadding="0" cellspacing="0" style="max-width: 540px; background-color: #ffffff; border-radius: 24px; border: 1px solid #e2e8f0; overflow: hidden; box-shadow: 0 10px 25px -5px rgba(0, 0, 0, 0.05);">
          
          <!-- Top Emerald Gradient Banner -->
          <tr>
            <td style="background: linear-gradient(135deg, #059669 0%, #10b981 100%); padding: 36px 32px; text-align: center;">
              <div style="display: inline-block; width: 48px; height: 48px; line-height: 48px; border-radius: 50%; background-color: #ffffff; color: #059669; font-size: 26px; font-weight: 900; margin-bottom: 12px; box-shadow: 0 4px 12px rgba(0,0,0,0.15);">
                O
              </div>
              <h1 style="margin: 0; color: #ffffff; font-size: 24px; font-weight: 800; letter-spacing: -0.5px;">OpenSocial</h1>
              <p style="margin: 4px 0 0 0; color: #d1fae5; font-size: 13px; font-weight: 500;">Account Security &amp; Verification</p>
            </td>
          </tr>

          <!-- Main Body Content -->
          <tr>
            <td style="padding: 36px 32px 28px 32px;">
              <h2 style="margin: 0 0 12px 0; color: #0f172a; font-size: 19px; font-weight: 700;">Verify Your Email Address</h2>
              <p style="margin: 0 0 20px 0; font-size: 14px; line-height: 1.6; color: #475569;">
                Hi <strong>{{displayName}}</strong>,<br/>
                We received a request to add and verify this email address for your OpenSocial account. Enter the 6-digit confirmation code below to complete verification:
              </p>

              <!-- OTP Code Display Card -->
              <div style="background: #f0fdf4; border: 2px dashed #86efac; border-radius: 18px; padding: 22px 16px; text-align: center; margin: 28px 0;">
                <span style="display: block; font-size: 11px; text-transform: uppercase; font-weight: 700; letter-spacing: 1.5px; color: #15803d; margin-bottom: 8px;">
                  Your One-Time Code
                </span>
                <span style="font-family: 'Courier New', Courier, monospace; font-size: 38px; font-weight: 800; letter-spacing: 10px; color: #047857; display: inline-block; padding-left: 10px;">
                  {{otpCode}}
                </span>
              </div>

              <!-- Security Information Notice -->
              <table width="100%" cellpadding="0" cellspacing="0" style="background-color: #f8fafc; border: 1px solid #e2e8f0; border-radius: 14px; padding: 14px 18px; margin-bottom: 24px;">
                <tr>
                  <td style="font-size: 12px; line-height: 1.5; color: #64748b;">
                    &#9201;&#65039; <strong>Security Notice:</strong> This code expires in <strong>10 minutes</strong> and can only be used once. If you did not make this request, you can safely ignore this email.
                  </td>
                </tr>
              </table>

              <p style="margin: 0; font-size: 13px; color: #64748b; line-height: 1.5;">
                Warm regards,<br/>
                <strong>The OpenSocial Team</strong>
              </p>
            </td>
          </tr>

          <!-- Footer -->
          <tr>
            <td style="background-color: #f8fafc; border-top: 1px solid #e2e8f0; padding: 20px 32px; text-align: center; font-size: 11px; color: #94a3b8; line-height: 1.6;">
              This is an automated security transmission from OpenSocial.<br/>
              Please do not reply directly to this email. For help, visit Settings &amp; Privacy in your app.
            </td>
          </tr>

        </table>
      </td>
    </tr>
  </table>
</body>
</html>
""";
    }
}
